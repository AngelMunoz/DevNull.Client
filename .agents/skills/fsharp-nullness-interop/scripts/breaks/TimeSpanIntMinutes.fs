module Program
open System

// expect: FS0041
let timeout (minutes: int) = TimeSpan.FromMinutes minutes

[<EntryPoint>]
let main _ = 0
