module DevNull.Client.UI.Tests.Program

open Expecto

open DevNull.Client.UI.Tests
open DevNull.Client.UI.Tests.CommitMenu
open DevNull.Client.UI.Tests.Composer
open DevNull.Client.UI.Tests.FeedTests
open DevNull.Client.UI.Tests.Natives
open DevNull.Client.UI.Tests.OrganizationView
open DevNull.Client.UI.Tests.Report
open DevNull.Client.UI.Tests.Remotes
open DevNull.Client.UI.Tests.RemotesView
open DevNull.Client.UI.Tests.Scheduled
open DevNull.Client.UI.Tests.Search
open DevNull.Client.UI.Tests.Share
open DevNull.Client.UI.Tests.Snapshots
open DevNull.Client.UI.Tests.Translation
open DevNull.Client.UI.Tests.WhoToFollow

[<EntryPoint>]
let main argv =
  let tests =
    testList "devnull client ui" [
      Natives.tests
      // The memory budgets and the keyboard checks read process-global
      // state (the heap, focus), so Expecto runs them alone:
      // testSequenced drains the parallel pool, then runs the suite one
      // test at a time.
      testSequenced Leaks.tests
      FeedTests.tests
      App.tests
      App.notificationDayTests
      Search.tests
      Search.completionTests
      Composer.tests
      Scheduled.tests
      // Focus and key routing are process-global too; the suite runs
      // alone like the memory budgets.
      testSequenced Shortcuts.tests
      WhoToFollow.tests
      Translation.tests
      Unread.tests
      Remotes.tests
      RemotesView.tests
      RemotesView.receiptTests
      RemotesView.threadTests
      RemotesView.composerTests
      RemotesView.manageTests
      RemotesView.reactTests
      RemotesView.voiceTests
      RemotesView.pingTests
      RemotesView.pinTests
      OrganizationView.tests
      Share.tests
      Report.tests
      CommitMenu.tests
      Snapshots.captureTests
    ]

  // Every UI test shares the one headless session thread, so a pool the
  // size of the processor count interleaves them past their real-time
  // waits. Four workers keep the pool while the waits hold;
  // `--parallel-workers` overrides the cap.
  runTestsWithCLIArgs [ CLIArguments.Parallel_Workers 4 ] argv tests
