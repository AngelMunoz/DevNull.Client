module DevNull.Client.Linux.Program

open System
open DevNull.Client.Startup

[<EntryPoint; STAThread>]
let main argv =
  let logging = createLogging()

  start(logging, Natives.services logging, argv)
