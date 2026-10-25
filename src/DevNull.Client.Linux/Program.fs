module DevNull.Client.Linux.Program

open System
open DevNull.Client.Startup

[<EntryPoint; STAThread>]
let main argv = start(Natives.services, argv)
