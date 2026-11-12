module DevNull.Client.UI.Tests.Program

open Expecto

open DevNull.Client.UI.Tests.CommitMenu
open DevNull.Client.UI.Tests.Composer
open DevNull.Client.UI.Tests.OrganizationView
open DevNull.Client.UI.Tests.Report
open DevNull.Client.UI.Tests.Scheduled
open DevNull.Client.UI.Tests.Search
open DevNull.Client.UI.Tests.Share
open DevNull.Client.UI.Tests.Stargazers
open DevNull.Client.UI.Tests.Formatting
open DevNull.Client.UI.Tests.ProfilePictures
open DevNull.Client.UI.Tests.SearchLatest
open DevNull.Client.UI.Tests.HistoryTab
open DevNull.Client.UI.Tests.ThreadTab
open DevNull.Client.UI.Tests.EditorPanel
open DevNull.Client.UI.Tests.Translation
open DevNull.Client.UI.Tests.WhoToFollow

[<EntryPoint>]
let main argv =
  let tests =
    testList "devnull client ui" [
      Search.tests
      Search.completionTests
      Composer.tests
      Scheduled.tests
      WhoToFollow.tests
      Translation.tests
      OrganizationView.tests
      Share.tests
      Report.tests
      CommitMenu.tests
      Stargazers.tests
      Formatting.tests
      ProfilePictures.tests
      SearchLatest.tests
      HistoryTab.tests
      ThreadTab.tests
      EditorPanel.tests
    ]

  // Every UI test shares the one headless session thread, so a pool the
  // size of the processor count interleaves them past their real-time
  // waits. Four workers keep the pool while the waits hold;
  // `--parallel-workers` overrides the cap.
  runTestsWithCLIArgs [ CLIArguments.Parallel_Workers 4 ] argv tests
