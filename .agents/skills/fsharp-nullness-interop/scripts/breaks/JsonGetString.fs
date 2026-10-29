module Program
open System.Text.Json

// expect: FS3261
let name (e: JsonElement) : string = e.GetString()

[<EntryPoint>]
let main _ = 0
