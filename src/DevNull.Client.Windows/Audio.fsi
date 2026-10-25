module DevNull.Client.Windows.Audio

open System
open DevNull.Client.Natives

/// The system's sounds, played through WinRT's media player. One
/// player serves every chime for the process's lifetime; the player
/// is disposable, so dispose releases it at exit.
type SystemSounds =
  new: unit -> SystemSounds
  interface ISystemSounds
  interface IDisposable
