module Program
open System

// expect: FS3261
let typeName (t: Type) : string = t.FullName

[<EntryPoint>]
let main _ = 0
