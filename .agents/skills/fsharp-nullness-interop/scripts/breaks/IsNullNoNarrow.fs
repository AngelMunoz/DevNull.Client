module Program
open System

// expect: FS3261
let envOr (name: string) (fallback: string) : string =
    let v = Environment.GetEnvironmentVariable name
    if isNull v then fallback else v

[<EntryPoint>]
let main _ = 0
