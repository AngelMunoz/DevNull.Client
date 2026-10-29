module Program
open System

// expect: FS0041
let jitter (ms: int) = TimeSpan.FromMilliseconds ms

[<EntryPoint>]
let main _ = 0
