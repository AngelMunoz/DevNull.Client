module DevNull.Client.Linux.Audio

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open IcedTasks
open Microsoft.Extensions.Logging
open DevNull.Client.Errors
open DevNull.Client.Natives

module Pulse =

  [<Literal>]
  let Library = "libpulse-simple.so.0"

  [<Literal>]
  let S16Le = 3

  [<Literal>]
  let Playback = 1

  [<Literal>]
  let Record = 2

  [<Literal>]
  let Rate = 16000

  // The two error codes the open path maps onto the contract's own
  // failure cases; every other code goes out with the library's text.
  [<Literal>]
  let AccessDenied = 1

  [<Literal>]
  let NoEntity = 5

  // pa_sample_spec as the C ABI lays it out: a format enum, a 32-bit
  // rate, and a channel count.
  [<Struct; StructLayout(LayoutKind.Sequential)>]
  type SampleSpec =
    val Format: int
    val Rate: uint32
    val Channels: byte

    new(format: int, rate: uint32, channels: byte) =
      {
        Format = format
        Rate = rate
        Channels = channels
      }

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern nativeint pa_simple_new(
    string | null server,
    string name,
    int direction,
    string | null device,
    string streamName,
    SampleSpec& spec,
    nativeint map,
    nativeint attributes,
    int& error
  )

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern int pa_simple_read(
    nativeint stream,
    byte[] data,
    unativeint bytes,
    int& error
  )

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern int pa_simple_write(
    nativeint stream,
    byte[] data,
    unativeint bytes,
    int& error
  )

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern int pa_simple_drain(nativeint stream, int& error)

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern void pa_simple_free(nativeint stream)

  [<DllImport("libpulse.so.0", CallingConvention = CallingConvention.Cdecl)>]
  extern string pa_strerror(int error)

module Canberra =

  [<Literal>]
  let Library = "libcanberra.so.0"

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern int ca_context_create(nativeint& context)

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern int ca_proplist_create(nativeint& properties)

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern int ca_proplist_sets(nativeint properties, string key, string value)

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern int ca_context_play_full(
    nativeint context,
    uint32 id,
    nativeint properties,
    nativeint callback,
    nativeint userdata
  )

/// Opens one stream and names the failure the contract expects.
let openStream
  (log: ILogger, direction: int, name: string)
  : Result<nativeint, AudioFailure> =
  let mutable error = 0
  let mutable spec = Pulse.SampleSpec(Pulse.S16Le, uint32 Pulse.Rate, 1uy)

  let stream =
    Pulse.pa_simple_new(
      null,
      "DevNull.Client",
      direction,
      null,
      name,
      &spec,
      0n,
      0n,
      &error
    )

  if stream <> 0n then
    log.LogDebug("The {Name} stream opened.", name)

    Ok stream
  else
    let failure =
      match error with
      | Pulse.AccessDenied -> Denied
      | Pulse.NoEntity -> NoDevice
      | code -> AudioFailed(Pulse.pa_strerror code)

    log.LogWarning(
      "The {Name} stream did not open: {Why}",
      name,
      describe(CaptureFailed failure)
    )

    Error failure

/// The loudest sample in one chunk, 0 to 1, for the recording meter.
/// The floor sample keeps an empty chunk from throwing.
let loudestOf(chunk: byte[]) =
  seq { 0..2 .. chunk.Length - 2 }
  |> Seq.map(fun offset ->
    float(abs(int(BitConverter.ToInt16(chunk, offset)))) / 32768.0)
  |> Seq.append(Seq.singleton 0.0)
  |> Seq.max

/// The canonical 44-byte header of a 16 kHz mono S16LE WAV, the format
/// every capture already carries.
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
  writer.Write(uint32 Pulse.Rate)
  writer.Write(uint32(Pulse.Rate * 2))
  writer.Write(uint16 2)
  writer.Write(uint16 16)
  writer.Write("data"B)
  writer.Write(uint32 dataBytes)
  writer.Flush()

  bytes.ToArray()

/// One live microphone capture: a background reader that blocks on the
/// stream, the samples it took, and the meter's peak. A failed read
/// stops the loop and names itself, so Stop can report it instead of
/// handing back a truncated take as if it were whole.
type Capture(stream: nativeint, log: ILogger) =

  let gate = obj()
  let taken = ResizeArray<byte>()
  let running = ref true
  let peak = ref 0.0
  let failedWith: string option ref = ref None
  let chunk: byte[] = Array.zeroCreate 1600
  let started = Stopwatch.StartNew()

  let rec step() =
    let mutable error = 0

    let heard =
      running.Value
      && Pulse.pa_simple_read(stream, chunk, unativeint chunk.Length, &error)
         >= 0

    if heard then
      lock gate (fun () ->
        taken.AddRange chunk

        let loudest = loudestOf chunk

        if loudest > peak.Value then
          peak.Value <- loudest)

      step()
    elif running.Value then
      failedWith.Value <- Some(Pulse.pa_strerror error)

  let reader = Thread(ThreadStart step, IsBackground = true)

  do reader.Start()

  interface IMicrophoneCapture with

    member _.Level() =
      lock gate (fun () ->
        let current = peak.Value
        peak.Value <- 0.0
        current)

    member _.Stop() = asyncEx {
      return!
        Task.Run(fun () ->
          running.Value <- false
          reader.Join()

          let voice =
            lock gate (fun () ->
              let copy = taken.ToArray()
              taken.Clear()
              copy)

          Pulse.pa_simple_free stream

          match failedWith.Value with
          | Some why ->
            log.LogWarning(
              "The recording stopped early after {Seconds} seconds: {Why}",
              started.Elapsed.TotalSeconds,
              why
            )

            Error(AudioFailed why)
          | None ->
            log.LogDebug(
              "The recording took {Bytes} bytes over {Seconds} seconds.",
              voice.Length,
              started.Elapsed.TotalSeconds
            )

            Ok(Array.append (wavHeader voice.Length) voice))
    }

    member _.Cancel() =
      running.Value <- false
      reader.Join()
      Pulse.pa_simple_free stream

      log.LogDebug(
        "The recording was cancelled after {Seconds} seconds.",
        started.Elapsed.TotalSeconds
      )

      lock gate (fun () -> taken.Clear()) |> ignore

/// One playback: a background writer the reader can stop early, and the
/// event that fires when the samples run out. Volume scales each chunk
/// as it is written, so a move lands on the note while it sounds.
type Playback(stream: nativeint, data: byte[], volume: float, log: ILogger) =

  let running = ref true
  let written = ref 0
  let loudness = ref volume
  let finished = Event<unit>()
  let started = Stopwatch.StartNew()

  let scaled(chunk: byte[]) : byte[] =
    let samples: int16 array = Array.zeroCreate(chunk.Length / 2)

    Buffer.BlockCopy(chunk, 0, samples, 0, samples.Length * 2)

    let out =
      samples
      |> Array.map(fun sample ->
        Math.Clamp(int(float sample * loudness.Value), -32768, 32767) |> int16)

    let bytes: byte[] = Array.zeroCreate(out.Length * 2)
    Buffer.BlockCopy(out, 0, bytes, 0, bytes.Length)
    bytes

  let rec writeAt(offset: int) =
    let mutable error = 0

    let take = min 3200 (data.Length - offset)
    let piece = scaled data[offset .. offset + take - 1]

    if
      running.Value
      && offset < data.Length
      && Pulse.pa_simple_write(stream, piece, unativeint piece.Length, &error)
         >= 0
    then
      written.Value <- offset + take
      writeAt(offset + take)

  let writer =
    let work() =
      writeAt 0

      let mutable error = 0

      if running.Value && Pulse.pa_simple_drain(stream, &error) >= 0 then
        log.LogDebug(
          "The note played through ({Bytes} bytes, {Seconds} seconds).",
          data.Length,
          started.Elapsed.TotalSeconds
        )

        finished.Trigger()
      elif running.Value then
        // A drain that fails never fires `Ended`, so the reader would
        // wait forever; the log names why the note stopped short.
        log.LogWarning(
          "The note did not play through: {Why}",
          Pulse.pa_strerror error
        )

      Pulse.pa_simple_free stream

    Thread(ThreadStart work, IsBackground = true)

  do
    log.LogTrace("The note starts ({Bytes} bytes).", data.Length)
    writer.Start()

  interface IAudioPlayback with
    member _.Stop() =
      running.Value <- false
      writer.Join()

      log.LogDebug(
        "The note stopped after {Seconds} seconds.",
        started.Elapsed.TotalSeconds
      )

    member _.SetVolume(volume: float) = loudness.Value <- volume

    member _.Ended = finished.Publish

    // 16 kHz mono 16-bit stands at 32,000 bytes a second; the writer
    // counts what it has pushed.
    member _.Position = TimeSpan.FromSeconds(float written.Value / 32000.0)

    // The simple API streams forward only; a scrub asks and the note
    // plays on from where it stands.
    member _.Seek(_to': TimeSpan) = ()

/// The samples as played: speed applied by nearest-neighbor resampling
/// in the voice-note format. Volume stays out — the writer scales each
/// chunk as it goes, so the fader lands while the note sounds.
let rendered(samples: int16 array, speed: float) : byte[] =
  let played =
    if speed <= 1.0 then
      samples
    else
      let count = max 1 (int(float samples.Length / speed))

      Array.init count (fun index -> samples[int(float index * speed)])

  let bytes: byte[] = Array.zeroCreate(played.Length * 2)
  Buffer.BlockCopy(played, 0, bytes, 0, bytes.Length)
  bytes

/// The microphone and the speaker through the PulseAudio simple API,
/// which every PipeWire and PulseAudio desktop serves. Streams stay at
/// the voice-note format: 16 kHz mono PCM, 16-bit little-endian. A
/// note in a compressed container rides the desktop's media framework
/// instead, the way the Windows head hands its notes to the OS player.
type PulseEngine(log: ILogger) =

  let mediaFramework = GStreamer.initialize log

  interface IAudioEngine with

    member _.StartCapture() = asyncEx {
      return!
        Task.Run(fun () ->
          match openStream(log, Pulse.Record, "Voice note") with
          | Ok stream -> Ok(Capture(stream, log) :> IMicrophoneCapture)
          | Error failure -> Error failure)
    }

    member _.Play(media, speed, volume) = asyncEx {
      return!
        Task.Run(fun () ->
          // The simple API takes PCM only; a WAV is already that, and
          // every other note rides the desktop's media framework the
          // way the Windows head hands its notes to the OS player —
          // the platform names a format it cannot take.
          match VoiceMedia.contentType media with
          | "audio/wav" ->
            match VoiceMedia.samples media with
            | Some samples ->
              match openStream(log, Pulse.Playback, "Voice note") with
              | Ok stream ->
                Ok(
                  Playback(stream, rendered(samples, speed), volume, log)
                  :> IAudioPlayback
                )
              | Error failure -> Error failure
            | None ->
              log.LogWarning("The note carries a WAV this build cannot read.")

              Error(AudioFailed "the note's WAV could not be read")
          | _ ->
            if mediaFramework then
              GStreamer.play(log, media, speed, volume)
            else
              log.LogWarning(
                "The web note cannot play: the desktop's media framework is missing."
              )

              Error(
                AudioFailed
                  "web voice notes need the desktop's media framework"
              ))
    }

/// The desktop's own sounds through libcanberra: the message event of
/// the installed XDG sound theme, the same sound the desktop's
/// notifications play.
type SystemSounds(log: ILogger) =

  // play_full reads the property list after it returns, so the list
  // lives as long as the context does: one per process, played per
  // chime. A desktop without the library keeps the whole service
  // quiet — the probe fails once and Notify does nothing, instead of
  // the head dying at startup.
  let context, properties, ready =
    try
      let mutable created = 0n
      let mutable list = 0n

      let ready =
        Canberra.ca_context_create(&created) = 0
        && Canberra.ca_proplist_create(&list) = 0
        && Canberra.ca_proplist_sets(list, "event.id", "message-new-instant") =
          0

      created, list, ready
    with :? DllNotFoundException ->
      0n, 0n, false

  do
    if ready then
      log.LogDebug("The desktop's sound theme serves the notifications.")
    else
      log.LogWarning("The desktop's sounds did not open; alerts stay silent.")

  interface ISystemSounds with
    member _.Notify() =
      if ready then
        let played =
          Canberra.ca_context_play_full(context, 0u, properties, 0n, 0n)

        if played <> 0 then
          log.LogWarning(
            "The notification sound did not play (error {Error}).",
            played
          )
        else
          log.LogTrace("The notification sound played.")
      else
        log.LogTrace("The notification sound skips; no sound service.")
