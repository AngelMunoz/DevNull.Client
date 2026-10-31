module DevNull.Client.UI.Tests.Program

open Expecto

open DevNull.Client.UI.Tests
open DevNull.Client.UI.Tests.Composer
open DevNull.Client.UI.Tests.Natives
open DevNull.Client.UI.Tests.Remotes
open DevNull.Client.UI.Tests.RemotesView
open DevNull.Client.UI.Tests.Scheduled
open DevNull.Client.UI.Tests.Search
open DevNull.Client.UI.Tests.WhoToFollow

[<EntryPoint>]
let main argv =
  let tests =
    testList "devnull client ui" [
      Natives.tests
      // The memory budgets measure deltas, so the leak suite runs
      // first, before any page test: every mounted shell and store
      // that ran earlier settles into the measured window (12.5 MB
      // with the app suite alone, 16.1 MB once the scheduled pages
      // joined), and the gate must measure the soak's own work, not
      // the order it ran in.
      Leaks.tests
      App.tests
      Search.tests
      Search.completionTests
      Composer.tests
      Scheduled.tests
      Shortcuts.tests
      WhoToFollow.tests
      Unread.tests
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
