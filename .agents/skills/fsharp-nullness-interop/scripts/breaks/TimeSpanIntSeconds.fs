module Program
open System

// expect: FS0041
let delay (attempt: int) = TimeSpan.FromSeconds(pown 2 attempt)

[<EntryPoint>]
let main _ = 0
