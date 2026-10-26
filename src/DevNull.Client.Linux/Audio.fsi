module DevNull.Client.Linux.Audio

open System
open DevNull.Client.Natives

/// The microphone and the speaker through the PulseAudio simple API,
/// which every PipeWire and PulseAudio desktop serves. Streams stay at
/// the voice-note format: 16 kHz mono PCM, 16-bit little-endian.
type PulseEngine =
  new: unit -> PulseEngine
  interface IAudioEngine

/// The desktop's own sounds through libcanberra: the message event of
/// the installed XDG sound theme, the same sound the desktop's
/// notifications play.
type SystemSounds =
  new: unit -> SystemSounds
  interface ISystemSounds
