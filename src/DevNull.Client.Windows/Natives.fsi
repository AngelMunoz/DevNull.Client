module DevNull.Client.Windows.Natives

open DevNull.Client.Natives

/// The native services this head provides: the registry scheme
/// registrar, the system sounds through WinRT's media player, and the
/// microphone and speaker through WinRT's audio graph and media
/// player.
val services: sounds: ISystemSounds -> NativeServices
