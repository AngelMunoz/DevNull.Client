module DevNull.Client.UI.Tests.Program

open Expecto

open DevNull.Client.UI.Tests

[<EntryPoint>]
let main argv =
  let tests = testList "devnull client ui" [ App.tests; Unread.tests ]

  runTestsWithCLIArgs [] argv tests
