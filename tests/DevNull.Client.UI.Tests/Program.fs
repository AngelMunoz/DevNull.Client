module DevNull.Client.UI.Tests.Program

open Expecto

open DevNull.Client.UI.Tests
open DevNull.Client.UI.Tests.Natives
open DevNull.Client.UI.Tests.Remotes
open DevNull.Client.UI.Tests.RemotesView

[<EntryPoint>]
let main argv =
  let tests =
    testList "devnull client ui" [
      Natives.tests
      App.tests
      Shortcuts.tests
      Unread.tests
      // The memory budgets measure deltas, so the leak suite runs
      // before the chat pages: their mounted shells and stores would
      // otherwise settle into the measured window.
      Leaks.tests
      Remotes.tests
      RemotesView.tests
      RemotesView.threadTests
      RemotesView.composerTests
      RemotesView.manageTests
      RemotesView.reactTests
      RemotesView.voiceTests
      RemotesView.pingTests
    ]

  runTestsWithCLIArgs [] argv tests
