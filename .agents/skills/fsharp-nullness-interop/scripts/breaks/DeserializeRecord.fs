module Program
open System.Text.Json

type Order = { Id: int }

// expect: FS3261
let load (json: string) : Order = JsonSerializer.Deserialize<Order> json

[<EntryPoint>]
let main _ = 0
