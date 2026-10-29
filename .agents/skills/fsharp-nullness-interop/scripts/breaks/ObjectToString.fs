module Program
open System

// expect: FS3261
let text (o: obj) : string = o.ToString()

[<EntryPoint>]
let main _ = 0
