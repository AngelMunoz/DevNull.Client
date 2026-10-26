module DevNull.Client.UI.Tests.Program

open Expecto

open DevNull.Client.UI.Tests
open DevNull.Client.UI.Tests.Remotes

[<EntryPoint>]
let main argv =
  let tests =
    testList "devnull client ui" [
      App.tests
      Shortcuts.tests
      Unread.tests
      Remotes.tests
      Leaks.tests
    ]

  runTestsWithCLIArgs [] argv tests
