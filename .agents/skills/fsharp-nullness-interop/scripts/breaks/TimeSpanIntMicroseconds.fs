module Program
open System

// expect: FS0041
let tick (us: int) = TimeSpan.FromMicroseconds us

[<EntryPoint>]
let main _ = 0
