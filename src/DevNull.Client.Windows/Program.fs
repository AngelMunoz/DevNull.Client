module DevNull.Client.Windows.Program

open System
open DevNull.Client.Natives
open DevNull.Client.Startup

[<EntryPoint; STAThread>]
let main argv =
  // The player lives until the app exits: `use` disposes it on the
  // way out of main, after the desktop lifetime ends.
  use sounds = Audio.SystemSounds()

  start(Natives.services sounds, argv)
