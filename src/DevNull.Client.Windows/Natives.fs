module DevNull.Client.Windows.Natives

open Microsoft.Extensions.Logging
open DevNull.Client.Natives
open DevNull.Client.Windows.Audio

/// The native services this head provides: the registry scheme
/// registrar, the system sounds through WinRT's media player, and the
/// microphone and speaker through WinRT's audio graph and media
/// player. Everything logs through the factory the app boot built.
let services(logging: ILoggerFactory, sounds: ISystemSounds) = {
  Registrar = Registrar.registrar logging
  Audio = ValueSome(WinRtEngine logging :> IAudioEngine)
  Sounds = ValueSome sounds
}
