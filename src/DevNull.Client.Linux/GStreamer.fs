module DevNull.Client.Linux.GStreamer

open System
open System.Diagnostics
open System.Runtime.InteropServices
open System.Threading
open Microsoft.Extensions.Logging
open DevNull.Client.Natives

module Gst =

  [<Literal>]
  let Library = "libgstreamer-1.0.so.0"

  // The player's states and words, as the framework numbers them: the
  // state enum starts at its own pending word, so NULL is 1.
  [<Literal>]
  let StateNull = 1

  [<Literal>]
  let StatePlaying = 4

  [<Literal>]
  let FormatTime = 3

  [<Literal>]
  let SeekFlush = 1

  [<Literal>]
  let SeekAccurate = 2

  [<Literal>]
  let SeekSet = 1

  [<Literal>]
  let SeekNone = 0

  [<Literal>]
  let EndOfStream = 1

  [<Literal>]
  let ErrorMessage = 2

  // GValue as the C ABI lays it out: a type word, then the two-pointer
  // union every value kind fits in.
  [<Struct; StructLayout(LayoutKind.Sequential)>]
  type GValue =
    val Kind: nativeint
    val First: nativeint
    val Second: nativeint

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern int gst_init_check(nativeint argc, nativeint argv, nativeint& error)

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern string gst_version_string()

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern nativeint gst_element_factory_make(string factory, string name)

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern nativeint gst_element_get_bus(nativeint element)

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern int gst_element_set_state(nativeint element, int state)

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern int gst_element_get_state(
    nativeint element,
    int& state,
    int& pending,
    int64 timeoutNanoseconds
  )

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern nativeint gst_bus_timed_pop_filtered(
    nativeint bus,
    int64 timeoutNanoseconds,
    int types
  )

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern void gst_message_unref(nativeint message)

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern void gst_message_parse_error(
    nativeint message,
    nativeint& error,
    nativeint& debug
  )

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern int gst_element_query_position(
    nativeint element,
    int format,
    int64& position
  )

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern int gst_element_seek(
    nativeint element,
    double rate,
    int format,
    int flags,
    int startType,
    int64 start,
    int stopType,
    int64 stop
  )

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern void gst_object_unref(nativeint item)

module Object =

  [<Literal>]
  let Library = "libgobject-2.0.so.0"

  // The two value kinds the player sets, as glib numbers them: a
  // string for the note's URI, a double for its loudness. A glib type
  // word is its fundamental number shifted two bits, so the string is
  // 16 and the double 15 after the shift.
  [<Literal>]
  let StringKind = 64

  [<Literal>]
  let DoubleKind = 60

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern nativeint g_value_init(Gst.GValue& value, nativeint kind)

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern void g_value_set_string(Gst.GValue& value, string content)

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern void g_value_set_double(Gst.GValue& value, double content)

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern void g_value_unset(Gst.GValue& value)

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern void g_object_set_property(
    nativeint item,
    string name,
    Gst.GValue& value
  )

  [<DllImport(Library, CallingConvention = CallingConvention.Cdecl)>]
  extern void g_error_free(nativeint error)

[<DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)>]
extern void g_free(nativeint memory)

module Mem =

  // The note never touches the disk: it rides a memory file, opened by
  // its descriptor path once the player asks for the URI's bytes.
  [<DllImport("libc", CallingConvention = CallingConvention.Cdecl)>]
  extern int memfd_create(string name, uint32 flags)

  [<DllImport("libc", CallingConvention = CallingConvention.Cdecl)>]
  extern int64 write(int descriptor, byte[] buffer, unativeint count)

  [<DllImport("libc", CallingConvention = CallingConvention.Cdecl)>]
  extern int64 lseek(int descriptor, int64 offset, int whence)

  [<DllImport("libc", CallingConvention = CallingConvention.Cdecl)>]
  extern int close(int descriptor)

let initialize(log: ILogger) : bool =
  let mutable handle = 0n

  let framework =
    NativeLibrary.TryLoad(Gst.Library, &handle)
    && Gst.gst_init_check(0n, 0n, &handle) <> 0

  if framework then
    log.LogDebug(
      "The desktop's media framework serves the voice notes ({Version}).",
      Gst.gst_version_string()
    )
  else
    log.LogWarning(
      "No desktop media framework; voice notes from the web stay silent."
    )

  framework

let setUri(element: nativeint, uri: string) =
  let mutable value = Gst.GValue()

  Object.g_value_init(&value, nativeint Object.StringKind) |> ignore
  Object.g_value_set_string(&value, uri)
  Object.g_object_set_property(element, "uri", &value)
  Object.g_value_unset(&value)

let setVolume(element: nativeint, volume: float) =
  let mutable value = Gst.GValue()

  Object.g_value_init(&value, nativeint Object.DoubleKind) |> ignore
  Object.g_value_set_double(&value, volume)
  Object.g_object_set_property(element, "volume", &value)
  Object.g_value_unset(&value)

let nanosecondsOf(span: TimeSpan) = span.Ticks * 100L

/// One note playing through the desktop's own player. A watcher thread
/// reads the player's bus; the note's end fires `Ended`, a refused note
/// logs its reason the way the sample writer names its own failures,
/// and every native word lives while the note does.
type Player
  (
    element: nativeint,
    bus: nativeint,
    descriptor: int,
    media: byte[],
    speed: float,
    volume: float,
    log: ILogger
  ) =

  let gate = obj()
  let running = ref true
  let rate = ref speed
  let finished = Event<unit>()
  let started = Stopwatch.StartNew()

  let whyOf(message: nativeint) =
    let mutable failure = 0n
    let mutable debug = 0n

    Gst.gst_message_parse_error(message, &failure, &debug)

    // GError on this platform: a domain and a code, then the message
    // pointer; the words are read straight off the struct so no
    // nullable marshaling enters the build.
    let why =
      if failure <> 0n then
        let words = Marshal.ReadInt64(failure, 8) |> nativeint

        if words <> 0n then
          Marshal.PtrToStringAnsi(words)
        else
          "the framework named no reason"
      else
        "the framework named no reason"

    if failure <> 0n then
      Object.g_error_free failure

    if debug <> 0n then
      g_free debug

    why

  let cleanup() =
    lock gate (fun () ->
      Gst.gst_element_set_state(element, Gst.StateNull) |> ignore

      // The NULL change can settle in its own time; the wait keeps the
      // dispose from catching the element still in flight.
      let mutable state = 0
      let mutable pending = 0

      Gst.gst_element_get_state(element, &state, &pending, 1_000_000_000L)
      |> ignore

      Gst.gst_object_unref bus
      Gst.gst_object_unref element
      Mem.close descriptor |> ignore)

  let watch() =
    // The framework publishes the message kinds as macros, not
    // symbols, so the loop never asks a message which it is: a
    // refusal is checked without waiting each round, then the wait
    // stands on the stream's end alone.
    let rec loop() =
      let refusal = Gst.gst_bus_timed_pop_filtered(bus, 0L, Gst.ErrorMessage)

      if refusal <> 0n then
        let why = whyOf refusal

        Gst.gst_message_unref refusal
        running.Value <- false

        // A refused note never fires `Ended`, so the reader would
        // wait forever; the log names why the note stopped short.
        log.LogWarning("The note did not play through: {Why}", why)
      else
        let endOfStream =
          Gst.gst_bus_timed_pop_filtered(bus, 250_000_000L, Gst.EndOfStream)

        if endOfStream <> 0n then
          Gst.gst_message_unref endOfStream
          running.Value <- false

          log.LogDebug(
            "The note played through ({Bytes} bytes, {Seconds} seconds).",
            media.Length,
            started.Elapsed.TotalSeconds
          )

          finished.Trigger()
        elif running.Value then
          loop()

    loop()
    cleanup()

  let watcher = Thread(ThreadStart watch, IsBackground = true)

  do
    log.LogTrace("The note starts through the desktop's player.")
    watcher.Start()

  interface IAudioPlayback with

    member _.Stop() =
      running.Value <- false
      watcher.Join()

      log.LogDebug(
        "The note stopped after {Seconds} seconds.",
        started.Elapsed.TotalSeconds
      )

    member _.SetVolume(volume: float) =
      if running.Value then
        lock gate (fun () -> setVolume(element, volume))

    member _.Ended = finished.Publish

    member _.Position =
      lock gate (fun () ->
        let mutable at = 0L

        if
          running.Value
          && Gst.gst_element_query_position(element, Gst.FormatTime, &at) <> 0
        then
          TimeSpan.FromTicks(at / 100L)
        else
          TimeSpan.Zero)

    member _.Seek(to': TimeSpan) =
      if running.Value then
        lock gate (fun () ->
          Gst.gst_element_seek(
            element,
            !rate,
            Gst.FormatTime,
            Gst.SeekFlush ||| Gst.SeekAccurate,
            Gst.SeekSet,
            nanosecondsOf to',
            Gst.SeekNone,
            0L
          )
          |> ignore)

let stage(media: byte[]) : Result<int, AudioFailure> =
  try
    let descriptor = Mem.memfd_create("devnull-voice", 0u)

    let staged =
      descriptor >= 0
      && Mem.write(descriptor, media, unativeint media.Length) =
        int64 media.Length
      && Mem.lseek(descriptor, 0L, 0) = 0L

    if staged then
      Ok descriptor
    else
      if descriptor >= 0 then
        Mem.close descriptor |> ignore

      Error(AudioFailed "the note could not be staged in memory")
  with :? DllNotFoundException ->
    Error(AudioFailed "this desktop cannot stage a note in memory")

/// Stages the note in memory, hands its descriptor to the desktop's
/// player, and starts it at the asked speed. The note's bytes leave
/// only when the player reads them through the descriptor path.
let play(log: ILogger, media: byte[], speed: float, volume: float) =
  match stage media with
  | Error failure -> Error failure
  | Ok descriptor ->
    let element = Gst.gst_element_factory_make("playbin", "voice-note")

    if element = 0n then
      Mem.close descriptor |> ignore

      Error(AudioFailed "the desktop's player did not open")
    else
      setUri(element, $"file:///proc/self/fd/{descriptor}")
      setVolume(element, volume)
      Gst.gst_element_set_state(element, Gst.StatePlaying) |> ignore

      // The player takes the rate on a seek, and a seek lands only
      // once the pipeline stands; the wait names no failure, it only
      // gives the seek its moment before the note is asked faster
      // than it sounds.
      let mutable state = 0
      let mutable pending = 0

      Gst.gst_element_get_state(element, &state, &pending, 1_000_000_000L)
      |> ignore

      if speed <> 1.0 then
        Gst.gst_element_seek(
          element,
          speed,
          Gst.FormatTime,
          Gst.SeekFlush,
          Gst.SeekSet,
          0L,
          Gst.SeekNone,
          0L
        )
        |> ignore

      let bus = Gst.gst_element_get_bus element

      Ok(
        Player(element, bus, descriptor, media, speed, volume, log)
        :> IAudioPlayback
      )
