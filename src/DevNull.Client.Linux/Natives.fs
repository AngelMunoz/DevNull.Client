module DevNull.Client.Linux.Natives

open System
open System.Runtime.InteropServices
open DevNull.Client.Natives

/// A library this machine carries: the probe that decides between a
/// live service and its `ValueNone`.
let private present(library: string) =
  let mutable handle = 0n
  NativeLibrary.TryLoad(library, &handle)

/// The native services this head provides: the .desktop scheme
/// registrar, the audio engine behind the PulseAudio client library
/// (every PipeWire and PulseAudio desktop serves it), and the desktop's
/// own sounds through libcanberra. A library this machine lacks hides
/// its service; the voice UI follows.
let services =
  let audio =
    if present "libpulse-simple.so.0" then
      ValueSome(Audio.PulseEngine() :> IAudioEngine)
    else
      ValueNone

  let sounds =
    if present "libcanberra.so.0" then
      ValueSome(Audio.SystemSounds() :> ISystemSounds)
    else
      ValueNone

  {
    Registrar = Registrar.registrar
    Audio = audio
    Sounds = sounds
  }
