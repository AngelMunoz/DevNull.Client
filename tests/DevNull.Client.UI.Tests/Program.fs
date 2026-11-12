module DevNull.Client.UI.Tests.Program

open Expecto

open DevNull.Client.UI.Tests
open DevNull.Client.UI.Tests.FeedTests
open DevNull.Client.UI.Tests.Natives

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
      // The switch checks read the same process-global state: the heap
      // for the retained roots, focus and keys for the rebind.
      testSequenced LayoutHost.tests
      FeedTests.tests
      App.tests
      App.notificationDayTests
      // Focus and key routing are process-global too; the suite runs
      // alone like the memory budgets.
      testSequenced Shortcuts.tests
      Unread.tests
    ]

  // Every UI test shares the one headless session thread, so a pool the
  // size of the processor count interleaves them past their real-time
  // waits. Four workers keep the pool while the waits hold;
  // `--parallel-workers` overrides the cap.
  runTestsWithCLIArgs [ CLIArguments.Parallel_Workers 4 ] argv tests
