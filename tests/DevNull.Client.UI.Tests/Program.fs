module DevNull.Client.UI.Tests.Program

open Expecto

open DevNull.Client.UI.Tests
open DevNull.Client.UI.Tests.FeedTests
open DevNull.Client.UI.Tests.EditorSettings
open DevNull.Client.UI.Tests.EditorTypes
open DevNull.Client.UI.Tests.EditorFrame
open DevNull.Client.UI.Tests.EditorChurn
open DevNull.Client.UI.Tests.EditorPanel
open DevNull.Client.UI.Tests.RepoFrame
open DevNull.Client.UI.Tests.RemotesMountTests
open DevNull.Client.UI.Tests.WorkbenchRemotesTests
open DevNull.Client.UI.Tests.EditorFlyout
open DevNull.Client.UI.Tests.PaletteFrame
open DevNull.Client.UI.Tests.EditorHardening
open DevNull.Client.UI.Tests.EditorGates
open DevNull.Client.UI.Tests.Natives
open DevNull.Client.UI.Tests.WorkbenchGateTests
open DevNull.Client.UI.Tests.WorkbenchHardening
open DevNull.Client.UI.Tests.WorkbenchHostViewTests
open DevNull.Client.UI.Tests.WorkbenchSettingsTests

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
      EditorSettings.tests
      EditorTypes.tests
      EditorFrame.tests
      EditorGates.tests
      WorkbenchSettingsTests.tests
      WorkbenchGateTests.tests
      WorkbenchGateTests.columnTests
      WorkbenchGateTests.inspectorViewTests
      WorkbenchHostViewTests.tests
      WorkbenchHardening.tests
      App.tests
      App.notificationDayTests
      // Focus and key routing are process-global too; the suite runs
      // alone like the memory budgets.
      testSequenced Shortcuts.tests
      testSequenced EditorChurn.tests
      testSequenced EditorPanel.tests
      testSequenced RepoFrame.tests
      RemotesMountTests.tests
      WorkbenchRemotesTests.tests
      testSequenced EditorFlyout.tests
      testSequenced PaletteFrame.tests
      testSequenced EditorHardening.tests
      Unread.tests
    ]

  // Every UI test shares the one headless session thread, so a pool the
  // size of the processor count interleaves them past their real-time
  // waits. Four workers keep the pool while the waits hold;
  // `--parallel-workers` overrides the cap.
  runTestsWithCLIArgs [ CLIArguments.Parallel_Workers 4 ] argv tests
