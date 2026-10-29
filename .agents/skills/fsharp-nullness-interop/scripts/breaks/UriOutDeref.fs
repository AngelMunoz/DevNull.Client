module Program
open System

// expect: FS3261
let tryHost (s: string) =
    match Uri.TryCreate(s, UriKind.Absolute) with
    | true, u -> Some u.Host
    | _ -> None

[<EntryPoint>]
let main _ = 0
