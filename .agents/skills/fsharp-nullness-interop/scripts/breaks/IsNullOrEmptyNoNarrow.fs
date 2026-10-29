module Program
open System

// expect: FS3261
let length (s: string | null) = if String.IsNullOrEmpty s then 0 else s.Length

[<EntryPoint>]
let main _ = 0
