module DevNull.Client.Linux.Audio

open System
open System.IO
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open IcedTasks
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
let openStream(direction: int, name: string) : Result<nativeint, AudioFailure> =
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
    Ok stream
  else
    match error with
    | Pulse.AccessDenied -> Error Denied
    | Pulse.NoEntity -> Error NoDevice
    | code -> Error(AudioFailed(Pulse.pa_strerror code))

/// The loudest sample in one chunk, 0 to 1, for the recording meter.
let loudestOf(chunk: byte[]) =
  seq { 0..2 .. chunk.Length - 2 }
  |> Seq.map(fun offset ->
    float(abs(int(BitConverter.ToInt16(chunk, offset)))) / 32768.0)
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
/// stream, the samples it took, and the meter's peak.
type Capture(stream: nativeint) =

  let gate = obj()
  let taken = ResizeArray<byte>()
  let running = ref true
  let peak = ref 0.0
  let chunk: byte[] = Array.zeroCreate 1600

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

          Array.append (wavHeader voice.Length) voice |> Ok)
    }

    member _.Cancel() =
      running.Value <- false
      reader.Join()
      Pulse.pa_simple_free stream

/// One playback: a background writer the reader can stop early, and the
/// event that fires when the samples run out. Volume scales each chunk
/// as it is written, so a move lands on the note while it sounds.
type Playback(stream: nativeint, data: byte[], volume: float) =

  let running = ref true
  let written = ref 0
  let loudness = ref volume
  let finished = Event<unit>()

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
        finished.Trigger()

      Pulse.pa_simple_free stream

    Thread(ThreadStart work, IsBackground = true)

  do writer.Start()

  interface IAudioPlayback with
    member _.Stop() =
      running.Value <- false
      writer.Join()

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
/// the voice-note format: 16 kHz mono PCM, 16-bit little-endian.
type PulseEngine() =

  interface IAudioEngine with

    member _.StartCapture() = asyncEx {
      return!
        Task.Run(fun () ->
          match openStream(Pulse.Record, "Voice note") with
          | Ok stream -> Ok(Capture stream :> IMicrophoneCapture)
          | Error failure -> Error failure)
    }

    member _.Play(media, speed, volume) = asyncEx {
      return!
        Task.Run(fun () ->
          // PulseAudio's simple API takes PCM only; a WAV is already
          // that, and any other container names its refusal rather
          // than playing noise.
          match VoiceMedia.samples media with
          | Some samples ->
            match openStream(Pulse.Playback, "Voice note") with
            | Ok stream ->
              Ok(
                Playback(stream, rendered(samples, speed), volume)
                :> IAudioPlayback
              )
            | Error failure -> Error failure
          | None -> Error(AudioFailed "this build plays WAV voice notes only"))
    }

/// The desktop's own sounds through libcanberra: the message event of
/// the installed XDG sound theme, the same sound the desktop's
/// notifications play.
type SystemSounds() =

  // play_full reads the property list after it returns, so the list
  // lives as long as the context does: one per process, played per
  // chime.
  let context, properties, ready =
    let mutable created = 0n
    let mutable list = 0n

    let ready =
      Canberra.ca_context_create(&created) = 0
      && Canberra.ca_proplist_create(&list) = 0
      && Canberra.ca_proplist_sets(list, "event.id", "message-new-instant") = 0

    created, list, ready

  interface ISystemSounds with
    member _.Notify() =
      if ready then
        Canberra.ca_context_play_full(context, 0u, properties, 0n, 0n) |> ignore
