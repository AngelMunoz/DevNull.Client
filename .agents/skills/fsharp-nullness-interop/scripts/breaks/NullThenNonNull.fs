module Program
open System.Text.Json

// expect: FS3262
// expect: FS0025
let name (e: JsonElement) : string =
    match e.GetString() with
    | null -> ""
    | NonNull v -> v

[<EntryPoint>]
let main _ = 0
