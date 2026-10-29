module Program

// expect: FS3261
let inner (ex: exn) : exn = ex.InnerException

[<EntryPoint>]
let main _ = 0
