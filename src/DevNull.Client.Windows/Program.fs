module DevNull.Client.Windows.Program

open System
open DevNull.Client.Startup

[<EntryPoint; STAThread>]
let main argv =
  let logging = createLogging()

  // The player lives until the app exits: `use` disposes it on the
  // way out of main, after the desktop lifetime ends.
  use sounds =
    new Audio.SystemSounds(logging.CreateLogger "DevNull.Client.Windows.Audio")

  start(logging, Natives.services(logging, sounds), argv)
