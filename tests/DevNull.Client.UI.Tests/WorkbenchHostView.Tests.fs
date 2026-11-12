module DevNull.Client.UI.Tests.WorkbenchHostViewTests

open System
open System.Reactive.Subjects
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive

open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.Workbench
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let private now = DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)

let private checkoutId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

let private fakeHandle = {
  new IDisposable with
    member _.Dispose() = ()
}

let private queueEntry: ScheduledCommit = {
  Id = "018f5a1e-0001-7c3a-9b2d-5e6f7a8b9c0d"
  Body = Some "Ship the staging queue."
  BodySegments = [ TextSegment "Ship the staging queue." ]
  PreviewSegments = None
  PollOptions = None
  ImageCount = 0
  ScheduledAt = now.AddDays 1.0
  SubmittedAt = None
  Status = StillScheduled
}

let private stagingOf(stagingOpen: bool) : Staging.Model =
  if stagingOpen then
    {
      Draft = "release notes"
      Quote = None
      Queue = {
        Items = [ queueEntry ]
        Total = 1
        Loading = false
        Error = None
      }
    }
  else
    Staging.initial

let private images = {
  new ImageCacheService with
    member _.Get(_url: string) = async { return None }
}

let private sessionOf(env: TestEnv) =
  match (env :> IProvideSession).Session.Current with
  | SignedIn session -> session
  | SignedOut -> failwith "the W3 test needs a signed-in session"

let private threadPage =
  let json =
    threadPageJson.Replace(
      "\"ancestors\":[]",
      "\"ancestors\":[" + secondCommitJson + "]"
    )

  match CommitPage.decode json with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned thread never decoded: {failure.Message}"

let private threadReplies =
  match CommitNode.decodePage repliesPageJson with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned replies never decoded: {failure.Message}"

let private threadViewOf(env: TestEnv) : ThreadMode.View =
  let model: ThreadMode.Model = {
    CommitId = threadPage.Commit.Id
    Loading = false
    Error = None
    Commit = Some threadPage.Commit
    Ancestors = threadPage.Ancestors
    ReplyParticipants = threadPage.ReplyParticipants
    Replies = threadReplies.Items
    RepliesCursor = Some "next"
    RepliesLoading = false
    RepliesError = None
    Now = now
  }

  let props: ThreadMode.Props<TestEnv> = {
    Env = env
    Session = sessionOf env
    CommitId = threadPage.Commit.Id
    KeyFile = "/tmp/devnull-test-id_ed25519"
    ViewerId = "01a151db-0000-0000-0000-000000000000"
    Pasteis = true
    Images = images
    Now = Observable.single now
    Zone = {
      Info = TimeZoneInfo.Utc
      Label = "UTC"
    }
    OnBack = ignore
    OnOpenThread = ignore
    OnOpenExternal = ignore
    OnQuote = ignore
  }

  let subject = new BehaviorSubject<ThreadMode.Model>(model)

  ThreadMode.view
    props
    (subject :> IObservable<ThreadMode.Model>)
    (fun (_: ThreadMode.Msg) -> ())

let private hostProps(env: TestEnv) : WorkbenchHost.Props<TestEnv> = {
  Env = env
  Initial = WorkbenchSettings.initial
  Images = images
  Now = Observable.single now
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  OnOpenAuthor = ignore
  OnOpenExternal = ignore
  OnStateChanged = ignore
}

let private repositoryProfile =
  let json =
    profileWithStatsJson.Replace(
      "\"pinned_commit\":null",
      "\"pinned_commit\":" + pinnedOwnCommitJson
    )

  match ProfileDetails.decode json with
  | Ok details -> details
  | Error failure ->
    failwith $"the canned repository profile never decoded: {failure.Message}"

let private repositoryCommits =
  match CommitNode.decodePage logPageJson with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned repository commits never decoded: {failure.Message}"

let private repositoryFollowers =
  match User.decodePage followersPageJson with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned repository followers never decoded: {failure.Message}"

let private repositoryViewOf(env: TestEnv) : RepositoryHome.View =
  let model: RepositoryHome.Model = {
    Handle = "octocat"
    Loading = false
    Error = None
    Details = Some repositoryProfile
    Commits = repositoryCommits.Items
    CommitsCursor = Some "next"
    CommitsLoading = false
    CommitsError = None
    FollowBusy = false
    FollowError = None
    People =
      Some {
        Kind = RepositoryHome.Followers
        Loading = false
        Error = None
        Items = repositoryFollowers.Items
        Cursor = Some "next"
      }
    Now = now
  }

  let props: RepositoryHome.Props<TestEnv> = {
    Env = env
    Handle = "octocat"
    KeyFile = "/tmp/devnull-test-id_ed25519"
    ViewerId = "01a151db-0000-0000-0000-000000000000"
    Pasteis = true
    Images = images
    Now = Observable.single now
    Zone = {
      Info = TimeZoneInfo.Utc
      Label = "UTC"
    }
    OnOpenThread = ignore
    OnQuote = ignore
    OnOpenExternal = ignore
    OnOpenRepository = ignore
    OnListChanged = ignore
  }

  let subject = new BehaviorSubject<RepositoryHome.Model>(model)

  RepositoryHome.view
    props
    (subject :> IObservable<RepositoryHome.Model>)
    (fun (_: RepositoryHome.Msg) -> ())

let private repositoryRef =
  WorkbenchSettings.parse "repository:octocat" |> WorkbenchState.refOf

let private controlByName(root: Control, name: string) : Control option =
  root.GetVisualDescendants()
  |> Seq.choose(function
    | :? Control as control when control.Name = name -> Some control
    | _ -> None)
  |> Seq.tryHead

let private modelOf(folded: bool, stagingOpen: bool) : WorkbenchHost.Model =
  let selected = WorkbenchSettings.initial |> WorkbenchState.refOf

  let historyView = Border()
  historyView.Name <- "FakeHistoryView"

  let historyLabel = TextBlock()
  historyLabel.Text <- "HISTORY CONTENT"
  historyView.Child <- historyLabel

  let inspectorView = Border()
  inspectorView.Name <- "FakeInspectorView"

  let inspectorLabel = TextBlock()
  inspectorLabel.Text <- "INSPECTOR CONTENT"
  inspectorView.Child <- inspectorLabel

  {
    Selected = selected
    CheckoutId = Some checkoutId
    Places = Map.empty
    StagingOpen = stagingOpen
    Staging = stagingOf stagingOpen
    ThreadId = None
    Thread = None
    Repository = None
    RepositoryListOpen = false
    Handle = "octocat"
    FetchedAt = Some(now.AddMinutes -12.0)
    SavedCount = 4
    UnreadCount = 3
    Folded = folded
    Now = now
    Pasteis = true
    History =
      Some {
        Ref = selected
        Restore = None
        View = historyView
        Handle = fakeHandle
        Refresh = ignore
      }
    Inspector =
      Some {
        CommitId = checkoutId
        View = inspectorView
        Handle = fakeHandle
      }
    LiveHistory = 1
    LiveInspector = 1
    LiveThread = 0
    LiveRepository = 0
  }

let private buildAsync
  (folded: bool, stagingOpen: bool, width: float)
  : Async<Window * BehaviorSubject<WorkbenchHost.Model>> = async {
  return!
    probeUiAsync(fun () ->
      let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)
      let props = hostProps env

      let subject =
        new BehaviorSubject<WorkbenchHost.Model>(modelOf(folded, stagingOpen))

      let view =
        WorkbenchHost.view
          props
          (subject :> IObservable<WorkbenchHost.Model>)
          (fun (_: WorkbenchHost.Msg) -> ())

      let window = Window(Width = width, Height = 800.0, Content = view)

      window.Show()
      window, subject)
}

let private buildThreadAsync
  ()
  : Async<Window * BehaviorSubject<WorkbenchHost.Model>> = async {
  return!
    probeUiAsync(fun () ->
      let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)
      let props = hostProps env
      let threadView = threadViewOf env

      let model = {
        modelOf(false, false) with
            ThreadId = Some threadPage.Commit.Id
            Thread =
              Some {
                CommitId = threadPage.Commit.Id
                View = threadView
                Handle = fakeHandle
              }
            LiveThread = 1
      }

      let subject = new BehaviorSubject<WorkbenchHost.Model>(model)

      let view =
        WorkbenchHost.view
          props
          (subject :> IObservable<WorkbenchHost.Model>)
          (fun (_: WorkbenchHost.Msg) -> ())

      let window = Window(Width = 1600.0, Height = 900.0, Content = view)

      window.Show()
      window, subject)
}

let private buildRepositoryAsync
  ()
  : Async<Window * BehaviorSubject<WorkbenchHost.Model>> = async {
  return!
    probeUiAsync(fun () ->
      let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)
      let props = hostProps env
      let repositoryView = repositoryViewOf env

      let model = {
        modelOf(false, false) with
            Selected = repositoryRef
            CheckoutId = None
            History = None
            Inspector = None
            Repository =
              Some {
                RepositoryHandle = "octocat"
                View = repositoryView
                Handle = fakeHandle
              }
            RepositoryListOpen = true
            LiveHistory = 0
            LiveInspector = 0
            LiveRepository = 1
      }

      let subject = new BehaviorSubject<WorkbenchHost.Model>(model)

      let view =
        WorkbenchHost.view
          props
          (subject :> IObservable<WorkbenchHost.Model>)
          (fun (_: WorkbenchHost.Msg) -> ())

      let window = Window(Width = 1600.0, Height = 900.0, Content = view)

      window.Show()
      window, subject)
}

let private widthOf(window: Window, name: string) : float =
  controlByName(window, name)
  |> Option.map(fun control -> control.Bounds.Width)
  |> Option.defaultValue 0.0

let private hasTextFragment(root: Control, fragment: string) : bool =
  root.GetVisualDescendants()
  |> Seq.exists(function
    | :? TextBlock as block ->
      block.Text <> null && block.Text.Contains fragment
    | _ -> false)

let private shownAsync(window: Window) =
  waitForAsync(
    5000,
    (fun () -> window),
    fun root ->
      hasText(root, "HISTORY CONTENT")
      && hasText(root, "INSPECTOR CONTENT")
      && hasTextFragment(root, "HEAD -> Following")
      && hasTextFragment(root, "@octocat")
      && hasTextFragment(root, "fetched 12m")
      && hasText(root, "Saved")
      && hasText(root, "Notifications")
  )

let private closeAsync
  (window: Window, subject: BehaviorSubject<WorkbenchHost.Model>)
  =
  onUiAsync(fun () ->
    window.Close()
    subject.OnCompleted()
    subject.Dispose())

let tests =
  testList "workbench host view" [
    testCaseAsync
      "the W1 frame at 1600 px keeps the three regions"
      (async {
        let! window, subject = buildAsync(false, false, 1600.0)
        let! shown = shownAsync window

        Expect.isTrue shown "the W1 frame never rendered its regions"

        do! pumpAsync()

        let! refsWidth, historyWidth, inspectorWidth, visible =
          probeUiAsync(fun () ->
            widthOf(window, "RefsRegion"),
            widthOf(window, "HistoryRegion"),
            widthOf(window, "InspectorRegion"),
            controlByName(window, "InspectorRegion")
            |> Option.map _.IsVisible
            |> Option.defaultValue false)

        Expect.isTrue
          (abs(refsWidth - 240.0) < 2.0)
          "the refs region is not 240 px"

        Expect.isTrue
          (abs(inspectorWidth - 380.0) < 2.0)
          "the inspector region is not 380 px"

        Expect.isTrue
          (historyWidth > 900.0 && historyWidth < 1050.0)
          $"the history did not keep the free width: {historyWidth}"

        Expect.isTrue visible "the inspector is not visible at 1600 px"

        do! closeAsync(window, subject)
      })

    testCaseAsync
      "the W1 frame at 1000 px folds the inspector over the history"
      (async {
        let! window, subject = buildAsync(true, false, 1000.0)
        let! shown = shownAsync window

        Expect.isTrue shown "the folded W1 frame never rendered its regions"

        do! pumpAsync()

        let! refsWidth, historyWidth, inspectorWidth, visible =
          probeUiAsync(fun () ->
            widthOf(window, "RefsRegion"),
            widthOf(window, "HistoryRegion"),
            widthOf(window, "InspectorRegion"),
            controlByName(window, "InspectorRegion")
            |> Option.map _.IsVisible
            |> Option.defaultValue false)

        Expect.isTrue
          (abs(refsWidth - 240.0) < 2.0)
          "the refs region is not 240 px"

        Expect.isTrue
          (abs(inspectorWidth - 380.0) < 2.0)
          "the sheet is not 380 px"

        Expect.isTrue
          (historyWidth > 700.0 && historyWidth < 800.0)
          $"the folded history did not take the free width: {historyWidth}"

        Expect.isTrue visible "the sheet is not visible at 1000 px"

        do! closeAsync(window, subject)
      })

    testCaseAsync
      "the W2 frame at 1600 px opens the staging composer and queue"
      (async {
        let! window, subject = buildAsync(false, true, 1600.0)
        let! shown = shownAsync window

        Expect.isTrue shown "the W2 frame never rendered its regions"

        let! composer =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "What did you ship?")),
            Option.isSome
          )

        Expect.isTrue composer "the staging composer never opened"

        let! queue =
          waitForAsync(
            5000,
            (fun () -> hasTextFragment(window, "Ship the staging queue.")),
            id
          )

        Expect.isTrue queue "the scheduled queue row never rendered"

        let! schedule =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ScheduleButton")),
            Option.isSome
          )

        Expect.isTrue schedule "the staging composer offers no schedule action"

        let! counts =
          waitForAsync(
            5000,
            (fun () ->
              hasTextFragment(window, "STAGING: 1 draft · 1 scheduled")),
            id
          )

        Expect.isTrue counts "the staging strip lost its counts"

        do! pumpAsync()

        let! stagingWidth, areaVisible, historyWidth =
          probeUiAsync(fun () ->
            widthOf(window, "StagingArea"),
            controlByName(window, "StagingArea")
            |> Option.map _.IsVisible
            |> Option.defaultValue false,
            widthOf(window, "HistoryRegion"))

        Expect.isTrue areaVisible "the staging area is not visible"

        Expect.isTrue
          (stagingWidth > 1500.0)
          $"the staging area did not span the window: {stagingWidth}"

        Expect.isTrue
          (historyWidth > 900.0 && historyWidth < 1050.0)
          $"the history lost its free width on staging: {historyWidth}"

        do! closeAsync(window, subject)
      })

    testCaseAsync
      "the W3 frame at 1600 px opens the thread, parents, and reply composer"
      (async {
        let! window, subject = buildThreadAsync()

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              hasTextFragment(root, "THREAD")
              && hasText(root, "PARENTS")
              && hasText(root, "It was DNS.")
              && hasText(root, "Second commit.")
              && hasText(root, "A reply.")
          )

        Expect.isTrue shown "the W3 frame never rendered its thread"

        let! composer =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "Write your reply…")),
            Option.isSome
          )

        Expect.isTrue composer "the inline reply composer never opened"

        let! audience =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ReplyingToLine")),
            Option.isSome
          )

        Expect.isTrue audience "the reply composer lost its participants"

        do! pumpAsync()

        let! (centerWidth,
              historyVisible,
              threadVisible,
              parentsVisible,
              inspectorVisible) =
          probeUiAsync(fun () ->
            widthOf(window, "ThreadCenter"),
            controlByName(window, "HistoryRegion")
            |> Option.map _.IsVisible
            |> Option.defaultValue false,
            controlByName(window, "ThreadRegion")
            |> Option.map _.IsVisible
            |> Option.defaultValue false,
            controlByName(window, "ThreadParents")
            |> Option.map _.IsVisible
            |> Option.defaultValue false,
            controlByName(window, "InspectorRegion")
            |> Option.map _.IsVisible
            |> Option.defaultValue false)

        Expect.isTrue
          (centerWidth > 900.0 && centerWidth < 1050.0)
          $"the thread lost the free center width: {centerWidth}"

        Expect.isFalse
          historyVisible
          "the history stayed visible over the thread"

        Expect.isTrue threadVisible "the thread region is not visible"
        Expect.isTrue parentsVisible "the PARENTS chain is not visible"
        Expect.isTrue inspectorVisible "the inspector checkout is not visible"

        do! closeAsync(window, subject)
      })

    testCaseAsync
      "the W4 frame at 1600 px shows the repository home and its people list"
      (async {
        let! window, subject = buildRepositoryAsync()

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              hasTextFragment(root, "README (pinned)")
              && hasText(root, "CONTRIBUTIONS · week")
              && hasText(root, "HISTORY @octocat")
              && hasText(root, "My own commit.")
              && hasText(root, "Dora Default")
              && hasText(root, "FOLLOWERS · @octocat")
              && hasTextFragment(root, "streak 3 · longest 12")
              && hasTextFragment(root, "level 3 · Wizard")
          )

        Expect.isTrue shown "the W4 frame never rendered its repository home"

        let! follow =
          waitForAsync(5000, (fun () -> hasText(window, "Follow")), id)

        Expect.isTrue follow "the repository header lost its follow control"

        let contributionBars(root: Control) =
          root.GetVisualDescendants()
          |> Seq.choose(function
            | :? Border as border when border.Name = "ContributionBar" ->
              Some border
            | _ -> None)
          |> Seq.toList

        let! bars =
          waitForAsync(
            5000,
            (fun () -> contributionBars(window).Length),
            fun count -> count = 3
          )

        Expect.isTrue bars "the contribution graph invented or lost its buckets"

        do! pumpAsync()

        let! (centerWidth,
              historyVisible,
              repositoryVisible,
              listVisible,
              inspectorVisible,
              firstLane,
              firstTip) =
          probeUiAsync(fun () ->
            let bars = contributionBars(window)

            let tip =
              bars
              |> List.tryHead
              |> Option.map(fun bar -> string(ToolTip.GetTip(bar)))
              |> Option.defaultValue ""

            let lane =
              bars
              |> List.tryHead
              |> Option.exists(fun bar -> bar.Classes.Contains "lane-back-3")

            widthOf(window, "RepositoryCenter"),
            controlByName(window, "HistoryRegion")
            |> Option.map _.IsVisible
            |> Option.defaultValue false,
            controlByName(window, "RepositoryRegion")
            |> Option.map _.IsVisible
            |> Option.defaultValue false,
            controlByName(window, "RepositoryPeople")
            |> Option.map _.IsVisible
            |> Option.defaultValue false,
            controlByName(window, "InspectorRegion")
            |> Option.map _.IsVisible
            |> Option.defaultValue false,
            lane,
            tip)

        Expect.isTrue
          (centerWidth > 900.0 && centerWidth < 1050.0)
          $"the repository home lost the free center width: {centerWidth}"

        Expect.isFalse historyVisible "the history stayed visible over the home"
        Expect.isTrue repositoryVisible "the repository region is not visible"

        Expect.isTrue
          listVisible
          "the people list did not take the inspector place"

        Expect.isTrue inspectorVisible "the inspector region is not visible"
        Expect.isTrue firstLane "the contribution bars lost their lane color"

        Expect.isTrue
          (firstTip.Contains "3")
          "the contribution bar lost its count tooltip"

        do! closeAsync(window, subject)
      })
  ]
