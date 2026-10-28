module DevNull.Client.Windows.Audio

open System
open DevNull.Client.Natives

/// The microphone and the speaker through WinRT's own surfaces: the
/// audio graph captures at the voice-note format (16 kHz mono PCM,
/// 16-bit little-endian) with a live level meter, and the media
/// player sounds the notes, decoding whatever container they ride in.
type WinRtEngine =
  new: unit -> WinRtEngine
  interface IAudioEngine

/// The system's sounds, played through WinRT's media player. One
/// player serves every chime for the process's lifetime; the player
/// is disposable, so dispose releases it at exit.
type SystemSounds =
  new: unit -> SystemSounds
  interface ISystemSounds
  interface IDisposable
