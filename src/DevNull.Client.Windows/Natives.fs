module DevNull.Client.Windows.Natives

open DevNull.Client.Natives

/// The native services this head provides: the registry scheme
/// registrar and the given system sounds, played through WinRT. The
/// audio engine comes later; its absence hides the voice UI.
let services(sounds: ISystemSounds) = {
  Registrar = Registrar.registrar
  Audio = ValueNone
  Sounds = ValueSome sounds
}
