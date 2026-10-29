module DevNull.Client.Windows.Audio

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open IcedTasks
open Microsoft.Extensions.Logging
open WinRT
open Windows.Media.Audio
open Windows.Media.Capture
open Windows.Media.MediaProperties
open Windows.Foundation
open DevNull.Client.Errors
open DevNull.Client.Natives

/// The voice-note format the whole engine stays at: 16 kHz mono PCM,
/// 16-bit little-endian.
[<Literal>]
let Rate = 16000

/// The one COM surface WinRT's own byte access rides on: classic COM,
/// not WinRT. NativeAOT ships no built-in COM marshalling — a managed
/// ComImport cast on it throws under PublishAot — so the calls go
/// through the IUnknown vtable, whose layout is stable ABI: QueryInterface
/// at slot 0, Release at slot 2, GetBuffer at slot 3 behind them.
module private ByteAccess =

  /// The interface id of IMemoryBufferByteAccess.
  let iid = Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")

  [<UnmanagedFunctionPointer(CallingConvention.StdCall)>]
  type QueryInterface =
    delegate of nativeint * byref<Guid> * byref<nativeint> -> int32

  [<UnmanagedFunctionPointer(CallingConvention.StdCall)>]
  type Release = delegate of nativeint -> uint32

  [<UnmanagedFunctionPointer(CallingConvention.StdCall)>]
  type GetBuffer =
    delegate of nativeint * byref<nativeint> * byref<uint32> -> int32

  /// One function pointer out of an interface's vtable.
  let slot(self: nativeint, index: int) : nativeint =
    Marshal.ReadIntPtr(Marshal.ReadIntPtr(self), index * IntPtr.Size)

/// The graph's format: 16 kHz mono 16-bit PCM.
let voiceFormat: AudioEncodingProperties =
  AudioEncodingProperties.CreatePcm(16000u, 1u, 16u)

/// The frames arrive as 32-bit IEEE float whatever the encoding
/// properties asked for, so the drain converts them to the voice
/// note's own 16-bit PCM.
let toPcm16(chunk: byte[]) : byte[] =
  let floats =
    Array.init (chunk.Length / 4) (fun at ->
      BitConverter.ToSingle(chunk, at * 4))

  let samples =
    floats
    |> Array.map(fun value ->
      let clipped = Math.Max(-1.0f, Math.Min(1.0f, value))

      int16(clipped * 32767.0f))

  let pcm: byte[] = Array.zeroCreate(samples.Length * 2)
  Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length)
  pcm

/// Reads one locked audio buffer's whole payload. The buffer reference's
/// own native pointer is asked for the byte-access interface through the
/// IUnknown vtable, and the bytes come out through the interface's own
/// slot — every step a function-pointer call, which the published exe
/// supports. Only the asking is caught: a reference that carries no byte
/// access is the one recoverable surprise, and it names itself; anything
/// else the quantum thread may throw belongs in the log, not in silence.
let readBuffer(buffer: Windows.Media.AudioBuffer) : Result<byte[], string> =
  try
    let reference = buffer.CreateReference()

    let self = (reference :?> IWinRTObject).NativeObject.ThisPtr

    let mutable access = 0n
    let mutable wanted = ByteAccess.iid

    let asked =
      Marshal
        .GetDelegateForFunctionPointer<ByteAccess.QueryInterface>(
          ByteAccess.slot(self, 0)
        )
        .Invoke(self, &wanted, &access)

    if asked <> 0 || access = 0n then
      Error("the audio buffer carries no byte access")
    else
      try
        let mutable pointer = 0n
        let mutable capacity = 0u

        let taken =
          Marshal
            .GetDelegateForFunctionPointer<ByteAccess.GetBuffer>(
              ByteAccess.slot(access, 3)
            )
            .Invoke(access, &pointer, &capacity)

        if taken <> 0 || pointer = 0n then
          Error("the audio buffer stayed empty")
        else
          let bytes: byte[] = Array.zeroCreate(int capacity)

          Marshal.Copy(pointer, bytes, 0, bytes.Length)

          Ok bytes
      finally
        Marshal
          .GetDelegateForFunctionPointer<ByteAccess.Release>(
            ByteAccess.slot(access, 2)
          )
          .Invoke(access)
        |> ignore
  with ex ->
    Error(ex.Message)

/// The loudest sample in one chunk, 0 to 1, for the recording meter.
let loudestOf(chunk: byte[]) =
  seq { 0..2 .. chunk.Length - 2 }
  |> Seq.map(fun offset ->
    float(abs(int(BitConverter.ToInt16(chunk, offset)))) / 32768.0)
  |> Seq.append(Seq.singleton 0.0)
  |> Seq.max

/// The canonical 44-byte header of a 16 kHz mono S16LE WAV, the
/// format every capture already carries.
let wavHeader(dataBytes: int) : byte[] =
  use bytes = new MemoryStream(44)
  use writer = new BinaryWriter(bytes)

  writer.Write("RIFF"B)
  writer.Write(uint32(36 + dataBytes))
  writer.Write("WAVE"B)
  writer.Write("fmt "B)
  writer.Write(uint32 16)
  writer.Write(uint16 1)
  writer.Write(uint16 1)
  writer.Write(uint32 Rate)
  writer.Write(uint32(Rate * 2))
  writer.Write(uint16 2)
  writer.Write(uint16 16)
  writer.Write("data"B)
  writer.Write(uint32 dataBytes)
  writer.Flush()

  bytes.ToArray()

/// One live microphone capture: the graph that feeds it, the frames
/// the quantum handler drains, and the meter's peak. Audio only flows
/// along outgoing connections, so the input feeds the frame output;
/// and the teardown runs exactly once whichever path lands first.
type Capture(graph: AudioGraph, input: AudioDeviceInputNode, log: ILogger) =

  let gate = obj()
  let taken = ResizeArray<byte>()
  let mutable peak = 0.0
  let mutable running = true
  let mutable torn = false
  let started = Stopwatch.StartNew()

  // The first reason the quantum drain failed, in the reader's own
  // words; a failed drain stops the capture so Stop can report it.
  let mutable failedWith: string option = None

  let output = graph.CreateFrameOutputNode(voiceFormat)

  do input.AddOutgoingConnection(output)

  let teardown() =
    if not torn then
      torn <- true
      running <- false
      graph.Stop()
      output.Dispose()
      input.Dispose()
      graph.Dispose()

  let quantum =
    TypedEventHandler<AudioGraph, obj>(fun _ _ ->
      if running then
        let frame = output.GetFrame()

        use buffer = frame.LockBuffer(Windows.Media.AudioBufferAccessMode.Read)

        match readBuffer buffer with
        | Ok pcm ->
          lock gate (fun () ->
            taken.AddRange(toPcm16 pcm)

            let loudest = loudestOf(toPcm16 pcm)

            if loudest > peak then
              peak <- loudest)
        | Error why ->
          // The quantum thread has nowhere else to say it; the log is
          // the reader's only word on why the take stopped growing.
          log.LogWarning("The recording stopped early: {Why}", why)

          failedWith <- Some why
          teardown())

  do
    graph.add_QuantumStarted quantum
    graph.Start()

  interface IMicrophoneCapture with

    member _.Level() =
      lock gate (fun () ->
        let current = peak
        peak <- 0.0
        current)

    /// Drains what the quantum thread gathered, then tears the graph
    /// down once. The only stop path; a drain that failed says why.
    member _.Stop() = asyncEx {
      return!
        Task.Run(fun () ->
          graph.remove_QuantumStarted quantum
          teardown()

          match failedWith with
          | Some why -> Error(AudioFailed why)
          | None ->
            let voice =
              lock gate (fun () ->
                let copy = taken.ToArray()
                taken.Clear()
                copy)

            log.LogDebug(
              "The recording took {Bytes} bytes over {Seconds} seconds.",
              voice.Length,
              started.Elapsed.TotalSeconds
            )

            Ok(Array.append (wavHeader voice.Length) voice))
    }

    /// Tears the graph down once and keeps nothing.
    member _.Cancel() =
      graph.remove_QuantumStarted quantum
      teardown()

      log.LogDebug(
        "The recording was cancelled after {Seconds} seconds.",
        started.Elapsed.TotalSeconds
      )

      lock gate (fun () -> taken.Clear()) |> ignore

/// One playback: the OS media player over the note's own bytes. The
/// pipeline decodes whatever container the note rides in, the session
/// reports the real position, and stopping releases the whole chain.
/// Bytes the pipeline cannot decode end the note at once.
type Playback
  (
    player: Windows.Media.Playback.MediaPlayer,
    source: Windows.Media.Core.MediaSource,
    speed: float,
    log: ILogger
  ) =

  let finished = Event<unit>()
  let started = Stopwatch.StartNew()

  let ended =
    TypedEventHandler<Windows.Media.Playback.MediaPlayer, obj>(fun _ _ ->
      log.LogDebug(
        "The note played through after {Seconds} seconds.",
        started.Elapsed.TotalSeconds
      )

      finished.Trigger())

  let failed =
    TypedEventHandler<
      Windows.Media.Playback.MediaPlayer,
      Windows.Media.Playback.MediaPlayerFailedEventArgs
     >
      (fun _ args ->
        // The pipeline's own word on why the note died; without it a
        // refused decode is indistinguishable from a finished note.
        log.LogWarning(
          "The note did not play ({Error}): {Message}",
          args.Error,
          args.ErrorMessage
        )

        finished.Trigger())

  // The session drops the rate a caller set before the source opened;
  // the note reaches its own speed only once the media is open.
  let opened =
    TypedEventHandler<Windows.Media.Playback.MediaPlayer, obj>(fun _ _ ->
      player.PlaybackSession.PlaybackRate <- speed)

  let stop() =
    player.remove_MediaOpened opened
    player.remove_MediaEnded ended
    player.remove_MediaFailed failed
    player.Source <- null
    source.Dispose()
    player.Dispose()

  do
    log.LogTrace("The note starts.")
    player.add_MediaOpened opened
    player.add_MediaEnded ended
    player.add_MediaFailed failed

  interface IAudioPlayback with

    member _.Stop() =
      Task.Run stop |> ignore

      log.LogDebug(
        "The note stopped after {Seconds} seconds.",
        started.Elapsed.TotalSeconds
      )

    member _.SetVolume(volume: float) = player.Volume <- volume

    member _.Ended = finished.Publish

    member _.Position = player.PlaybackSession.Position

    member _.Seek(to': TimeSpan) = player.PlaybackSession.Position <- to'

/// The microphone and the speaker through WinRT's own surfaces: the
/// audio graph feeds the capture (the meter needs frames), and the
/// media player sounds the notes (the OS decodes the container).
module private Engine =

  let graphSettings() =
    let settings =
      AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.Media)

    settings.EncodingProperties <- voiceFormat
    settings

  let openCapture
    (log: ILogger)
    : Async<Result<IMicrophoneCapture, AudioFailure>> = asyncEx {
    let! created = AudioGraph.CreateAsync(graphSettings()).AsTask()

    if created.Status <> AudioGraphCreationStatus.Success then
      log.LogWarning(
        "The audio graph did not start ({Status}).",
        created.Status
      )

      return
        Error(AudioFailed($"the audio graph could not start: {created.Status}"))
    else
      let graph = created.Graph

      let! opened =
        graph
          .CreateDeviceInputNodeAsync(MediaCategory.Speech, voiceFormat)
          .AsTask()

      match opened.Status with
      | AudioDeviceNodeCreationStatus.Success ->
        log.LogDebug("The audio graph opened with the microphone.")

        return
          Ok(Capture(graph, opened.DeviceInputNode, log) :> IMicrophoneCapture)
      | AudioDeviceNodeCreationStatus.AccessDenied ->
        log.LogWarning("The microphone is not allowed to this app.")
        graph.Dispose()
        return Error Denied
      | AudioDeviceNodeCreationStatus.DeviceNotAvailable ->
        log.LogWarning("No microphone is available.")
        graph.Dispose()
        return Error NoDevice
      | other ->
        log.LogWarning("The microphone did not open ({Status}).", other)
        graph.Dispose()
        return Error(AudioFailed($"the microphone could not open: {other}"))
  }

  let openPlayback
    (log: ILogger, media: byte array, speed: float, volume: float)
    : Async<Result<IAudioPlayback, AudioFailure>> = asyncEx {
    let player = new Windows.Media.Playback.MediaPlayer()

    player.Volume <- volume

    let stream = new Windows.Storage.Streams.InMemoryRandomAccessStream()
    let writer = new Windows.Storage.Streams.DataWriter(stream)

    writer.WriteBytes media

    let! _ = writer.StoreAsync().AsTask()
    writer.DetachStream() |> ignore
    stream.Seek(0UL)

    let source =
      Windows.Media.Core.MediaSource.CreateFromStream(
        stream,
        VoiceMedia.contentType media
      )

    log.LogDebug(
      "The note hands to the OS player ({Bytes} bytes, {Type}).",
      media.Length,
      VoiceMedia.contentType media
    )

    player.Source <- source
    player.Play()

    return Ok(Playback(player, source, speed, log) :> IAudioPlayback)
  }

type WinRtEngine(logging: ILoggerFactory) =

  let log = logging.CreateLogger "DevNull.Client.Windows.Audio"

  interface IAudioEngine with

    member _.StartCapture() = Engine.openCapture log

    member _.Play(media, speed, volume) =
      Engine.openPlayback(log, media, speed, volume)

/// The sound Windows itself plays for a system notification, from its
/// own media folder.
let notifySound = Uri @"C:\Windows\Media\Windows Notify System Generic.wav"

/// The system's sounds, played through WinRT's media player. One
/// player serves every chime: a player created per note could be
/// collected while its sound is still playing, and a fresh source on
/// each call restarts the clip cleanly.
type SystemSounds(log: ILogger) =

  let player =
    new Windows.Media.Playback.MediaPlayer(
      AudioCategory =
        Windows.Media.Playback.MediaPlayerAudioCategory.SoundEffects
    )

  let failed =
    TypedEventHandler<
      Windows.Media.Playback.MediaPlayer,
      Windows.Media.Playback.MediaPlayerFailedEventArgs
     >
      (fun _ args ->
        // A missing or unreadable sound file lands here; the alert
        // stays silent either way, so the log is its only word.
        log.LogWarning(
          "The notification sound did not play ({Error}): {Message}",
          args.Error,
          args.ErrorMessage
        ))

  do player.add_MediaFailed failed

  interface ISystemSounds with
    member _.Notify() =
      log.LogTrace("The notification sound plays.")

      player.Source <- Windows.Media.Core.MediaSource.CreateFromUri(notifySound)

      player.Play()

  interface IDisposable with
    member _.Dispose() = player.Dispose()
