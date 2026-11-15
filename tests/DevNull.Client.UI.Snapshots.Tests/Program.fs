module DevNull.Client.UI.Tests.Program

open Expecto

open DevNull.Client.UI.Tests.Snapshots
open DevNull.Client.UI.Tests.WorkbenchSnapshots

[<EntryPoint>]
let main argv =
  let tests =
    testList "devnull client ui" [
      Snapshots.captureTests
      WorkbenchSnapshots.tests
    ]

  // Every UI test shares the one headless session thread, so a pool the
  // size of the processor count interleaves them past their real-time
  // waits. Four workers keep the pool while the waits hold;
  // `--parallel-workers` overrides the cap.
  runTestsWithCLIArgs [ CLIArguments.Parallel_Workers 4 ] argv tests
