module DevNull.Client.Mac.Program

open System
open DevNull.Client.Startup

[<EntryPoint; STAThread>]
let main argv =
  let logging = createLogging()

  start(logging, Natives.services logging, argv)
