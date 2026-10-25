module DevNull.Client.Natives

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

/// One playback of decoded voice samples.
type IAudioPlayback =
  /// Ends the playback early.
  abstract Stop: unit -> unit

  /// Fires when the samples have all played.
  abstract Ended: IEvent<unit>

/// The microphone and the speaker, behind one engine. Decoding stays
/// with the caller: the engine takes PCM in and gives PCM back.
type IAudioEngine =
  /// Opens the microphone. `Denied` covers a permission the user has
  /// not granted; `NoDevice` covers a machine with no microphone.
  abstract StartCapture: unit -> Async<Result<IMicrophoneCapture, AudioFailure>>

  /// Plays 16 kHz mono samples at the given speed (1.0, 1.5, or 2.0)
  /// and volume (0.0 to 1.0).
  abstract Play:
    samples: int16 array * speed: float * volume: float -> IAudioPlayback

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
val executablePath: unit -> string
