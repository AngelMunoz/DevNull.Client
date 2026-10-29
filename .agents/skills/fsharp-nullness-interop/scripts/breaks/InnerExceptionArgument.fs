module Program

let describe (ex: exn) = ex.Message

// expect: FS3261
// expect-text: The types 'exn' and 'exn | null' do not have compatible nullability
let innerMessage (ex: exn) = describe ex.InnerException

[<EntryPoint>]
let main _ = 0
