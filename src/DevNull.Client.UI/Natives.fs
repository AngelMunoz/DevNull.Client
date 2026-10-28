module DevNull.Client.Natives

open System
open System.IO

/// One operating system's handler for the devnull:// scheme: it points
/// the OS at this executable.
type ISchemeRegistrar =
  /// Registers the handler. The message names why it could not.
  abstract Register: unit -> Result<unit, string>

/// Why a capture or a playback could not start.
type AudioFailure =
  | NoDevice
  | Denied
  | Busy
  | AudioFailed of message: string

/// One live microphone capture at the voice-note format: 16 kHz mono
/// PCM, 16-bit little-endian.
type IMicrophoneCapture =
  /// The loudest recent sample, 0 to 1, for the recording meter.
  abstract Level: unit -> float

  /// Ends the recording and returns its bytes as a complete WAV file.
  abstract Stop: unit -> Async<Result<byte array, AudioFailure>>

  /// Ends the recording and keeps nothing.
  abstract Cancel: unit -> unit

/// One playback of a voice note's bytes.
type IAudioPlayback =
  /// Ends the playback early.
  abstract Stop: unit -> unit

  /// Changes how loud the note sounds while it plays, 0.0 to 1.0.
  abstract SetVolume: volume: float -> unit

  /// Fires when the note has played through.
  abstract Ended: IEvent<unit>

  /// Where the playing stands now; the pipeline's own word, not an
  /// estimate.
  abstract Position: TimeSpan

  /// Moves the playing to a point in the note, the scrub gesture's
  /// own arm.
  abstract Seek: to': TimeSpan -> unit

/// The microphone and the speaker, behind one engine. The platform
/// decodes what it can: playback takes the voice file's bytes as they
/// were downloaded, and any decoding stays inside the head.
type IAudioEngine =
  /// Opens the microphone. `Denied` covers a permission the user has
  /// not granted; `NoDevice` covers a machine with no microphone.
  abstract StartCapture: unit -> Async<Result<IMicrophoneCapture, AudioFailure>>

  /// Plays one voice note's bytes at the given speed (1.0, 1.5, or
  /// 2.0) and volume (0.0 to 1.0). The failure names why the note
  /// could not start.
  abstract Play:
    media: byte array * speed: float * volume: float ->
      Async<Result<IAudioPlayback, AudioFailure>>

/// One voice file's bytes as the engines read them.
module VoiceMedia =

  /// Whether the bytes open as a RIFF/WAVE file.
  let private isWav(media: byte array) =
    media.Length >= 44
    && media[0] = 0x52uy
    && media[1] = 0x49uy
    && media[2] = 0x46uy
    && media[3] = 0x46uy
    && media[8] = 0x57uy
    && media[9] = 0x41uy
    && media[10] = 0x56uy
    && media[11] = 0x45uy

  /// The content type the bytes stand for: `audio/wav` or
  /// `audio/webm`.
  let contentType(media: byte array) =
    if isWav media then "audio/wav" else "audio/webm"

  /// The samples a 16 kHz mono S16LE WAV carries, or none when the
  /// bytes are any other container.
  let samples(media: byte array) =
    if isWav media then
      let pcm: int16 array = Array.zeroCreate((media.Length - 44) / 2)

      Buffer.BlockCopy(media, 44, pcm, 0, pcm.Length * 2)
      Some pcm
    else
      None

/// The operating system's own sounds.
type ISystemSounds =
  /// Plays the system's notification sound: the one its notifications
  /// use, as the user configured it. Fire-and-forget.
  abstract Notify: unit -> unit

/// The services one platform head hands to the shared client. Every
/// entry beyond the registrar may be missing: the interface consumer
/// hides what its head does not provide.
[<Struct>]
type NativeServices = {
  /// The devnull:// scheme handler for the head's operating system.
  Registrar: ISchemeRegistrar

  /// The microphone and speaker. `ValueNone` while the head has no
  /// engine; the voice UI stays hidden.
  Audio: IAudioEngine voption

  /// The system's notification sound. `ValueNone` while the head has
  /// no way to reach it; callers stay silent.
  Sounds: ISystemSounds voption
}

/// Accessor for the native services capability.
type IProvideNatives =
  abstract Natives: NativeServices

/// This executable's path, as a scheme handler's command names it.
let executablePath() =
  match Environment.ProcessPath with
  | null ->
    let name =
      if OperatingSystem.IsWindows() then
        "DevNull.Client.exe"
      else
        "DevNull.Client"

    Path.Combine(AppContext.BaseDirectory, name)
  | path when
    path.EndsWith("DevNull.Client.exe", StringComparison.OrdinalIgnoreCase)
    ->
    path
  | path -> path
