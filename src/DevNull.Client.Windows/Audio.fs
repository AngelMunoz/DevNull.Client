module DevNull.Client.Windows.Audio

open System
open Windows.Media.Core
open Windows.Media.Playback
open DevNull.Client.Natives

/// The sound Windows itself plays for a system notification, from its
/// own media folder.
let private notifySound =
  Uri @"C:\Windows\Media\Windows Notify System Generic.wav"

/// The system's sounds, played through WinRT's media player. One
/// player serves every chime: a player created per note could be
/// collected while its sound is still playing, and a fresh source on
/// each call restarts the clip cleanly.
type SystemSounds() =
  let player =
    MediaPlayer(AudioCategory = MediaPlayerAudioCategory.SoundEffects)

  interface ISystemSounds with
    member _.Notify() =
      player.Source <- MediaSource.CreateFromUri(notifySound)
      player.Play()

  interface IDisposable with
    member _.Dispose() = player.Dispose()
