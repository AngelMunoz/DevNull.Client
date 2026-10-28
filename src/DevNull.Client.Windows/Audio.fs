module DevNull.Client.Windows.Audio

open System
open System.IO
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open IcedTasks
open Windows.Media.Audio
open Windows.Media.Capture
open Windows.Media.MediaProperties
open Windows.Foundation
open DevNull.Client.Natives

/// The voice-note format the whole engine stays at: 16 kHz mono PCM,
/// 16-bit little-endian.
[<Literal>]
let Rate = 16000

/// The one COM surface WinRT's own byte access rides on: every frame
/// buffer reference hands its raw bytes out through it.
[<ComImport>]
[<Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")>]
[<InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>]
type IMemoryBufferByteAccess =

  abstract GetBuffer: buffer: nativeint byref * capacity: uint32 byref -> unit

/// The graph's format: 16 kHz mono 16-bit PCM.
let voiceFormat: AudioEncodingProperties =
  AudioEncodingProperties.CreatePcm(16000u, 1u, 16u)

/// Reads one locked audio buffer's whole payload. The reference
/// always carries the byte access interface — it is WinRT's own
/// contract — but a reference that somehow does not reads as no
/// bytes rather than crashing the quantum thread.
let readBuffer(buffer: Windows.Media.AudioBuffer) : byte[] =
  match box(buffer.CreateReference()) with
  | :? IMemoryBufferByteAccess as access ->
    let mutable pointer = 0n
    let mutable capacity = 0u

    access.GetBuffer(&pointer, &capacity)

    let bytes: byte[] = Array.zeroCreate(int capacity)
    Marshal.Copy(pointer, bytes, 0, int capacity)
    bytes
  | _ -> [||]

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
/// the quantum handler drained, and the meter's peak.
type Capture(graph: AudioGraph, input: AudioDeviceInputNode) =

  let gate = obj()
  let taken = ResizeArray<byte>()
  let mutable peak = 0.0
  let mutable running = true

  let output = graph.CreateFrameOutputNode(voiceFormat)

  let tearDown() =
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
        let chunk = readBuffer buffer

        lock gate (fun () ->
          taken.AddRange chunk

          let loudest = loudestOf chunk

          if loudest > peak then
            peak <- loudest))

  do
    graph.add_QuantumStarted quantum
    graph.Start()

  interface IMicrophoneCapture with

    member _.Level() =
      lock gate (fun () ->
        let current = peak
        peak <- 0.0
        current)

    member _.Stop() = asyncEx {
      return!
        Task.Run(fun () ->
          let voice =
            lock gate (fun () ->
              let copy = taken.ToArray()
              taken.Clear()
              copy)

          graph.remove_QuantumStarted quantum
          tearDown()

          Array.append (wavHeader voice.Length) voice |> Ok)
    }

    member _.Cancel() =
      lock gate (fun () -> taken.Clear()) |> ignore
      graph.remove_QuantumStarted quantum
      tearDown()

/// One playback: the OS media player over the note's own bytes. The
/// pipeline decodes whatever container the note rides in, the session
/// reports the real position, and stopping releases the whole chain.
/// Bytes the pipeline cannot decode end the note at once.
type Playback
  (
    player: Windows.Media.Playback.MediaPlayer,
    source: Windows.Media.Core.MediaSource
  ) =

  let finished = Event<unit>()

  let ended =
    TypedEventHandler<Windows.Media.Playback.MediaPlayer, obj>(fun _ _ ->
      finished.Trigger())

  let failed =
    TypedEventHandler<
      Windows.Media.Playback.MediaPlayer,
      Windows.Media.Playback.MediaPlayerFailedEventArgs
     >
      (fun _ _ -> finished.Trigger())

  let stop() =
    player.remove_MediaEnded ended
    player.remove_MediaFailed failed
    player.Source <- null
    source.Dispose()
    player.Dispose()

  do
    player.add_MediaEnded ended
    player.add_MediaFailed failed

  interface IAudioPlayback with

    member _.Stop() = Task.Run stop |> ignore

    member _.Ended = finished.Publish

    member _.Position = player.PlaybackSession.Position

/// The microphone and the speaker through WinRT's own surfaces: the
/// audio graph feeds the capture (the meter needs frames), and the
/// media player sounds the notes (the OS decodes the container).
module private Engine =

  let graphSettings() =
    let settings =
      AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.Media)

    settings.EncodingProperties <- voiceFormat
    settings

  let openCapture() : Async<Result<IMicrophoneCapture, AudioFailure>> = asyncEx {
    let! created = AudioGraph.CreateAsync(graphSettings()).AsTask()

    if created.Status <> AudioGraphCreationStatus.Success then
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
        return Ok(Capture(graph, opened.DeviceInputNode) :> IMicrophoneCapture)
      | AudioDeviceNodeCreationStatus.AccessDenied ->
        graph.Dispose()
        return Error Denied
      | AudioDeviceNodeCreationStatus.DeviceNotAvailable ->
        graph.Dispose()
        return Error NoDevice
      | other ->
        graph.Dispose()
        return Error(AudioFailed($"the microphone could not open: {other}"))
  }

  let openPlayback
    (media: byte array, speed: float, volume: float)
    : Async<Result<IAudioPlayback, AudioFailure>> = asyncEx {
    let player = new Windows.Media.Playback.MediaPlayer()

    player.Volume <- volume
    player.PlaybackSession.PlaybackRate <- speed

    let stream = new Windows.Storage.Streams.InMemoryRandomAccessStream()
    let writer = new Windows.Storage.Streams.DataWriter(stream)

    writer.WriteBytes media

    let! _ = writer.StoreAsync().AsTask()
    writer.DetachStream()
    stream.Seek(0UL)

    let source =
      Windows.Media.Core.MediaSource.CreateFromStream(
        stream,
        VoiceMedia.contentType media
      )

    player.Source <- source
    player.Play()

    return Ok(Playback(player, source) :> IAudioPlayback)
  }

type WinRtEngine() =

  interface IAudioEngine with

    member _.StartCapture() = Engine.openCapture()

    member _.Play(media, speed, volume) =
      Engine.openPlayback(media, speed, volume)

/// The sound Windows itself plays for a system notification, from its
/// own media folder.
let notifySound = Uri @"C:\Windows\Media\Windows Notify System Generic.wav"

/// The system's sounds, played through WinRT's media player. One
/// player serves every chime: a player created per note could be
/// collected while its sound is still playing, and a fresh source on
/// each call restarts the clip cleanly.
type SystemSounds() =

  let player =
    new Windows.Media.Playback.MediaPlayer(
      AudioCategory =
        Windows.Media.Playback.MediaPlayerAudioCategory.SoundEffects
    )

  interface ISystemSounds with
    member _.Notify() =
      player.Source <- Windows.Media.Core.MediaSource.CreateFromUri(notifySound)
      player.Play()

  interface IDisposable with
    member _.Dispose() = player.Dispose()
