module DevNull.Client.Windows.Natives

open DevNull.Client.Natives
open DevNull.Client.Windows.Audio

/// The native services this head provides: the registry scheme
/// registrar, the system sounds through WinRT's media player, and the
/// microphone and speaker through WinRT's audio graph and media
/// player.
let services(sounds: ISystemSounds) = {
  Registrar = Registrar.registrar
  Audio = ValueSome(new WinRtEngine() :> IAudioEngine)
  Sounds = ValueSome sounds
}
