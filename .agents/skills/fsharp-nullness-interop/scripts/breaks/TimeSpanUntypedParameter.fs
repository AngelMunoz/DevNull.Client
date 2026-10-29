module Program
open System

// expect: FS0041
let toSpan x = TimeSpan.FromSeconds x

[<EntryPoint>]
let main _ = 0
