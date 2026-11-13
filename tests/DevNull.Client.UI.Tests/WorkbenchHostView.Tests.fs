module DevNull.Client.UI.Tests.WorkbenchHostViewTests

open System
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Media
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive

open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.Theme
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
    PollChoices = Map.empty
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
    OnDialog = ignore
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
  OnUseDefaultLayout = ignore
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
    PollChoices = Map.empty
    Now = now
  }

  let props: RepositoryHome.Props<TestEnv> = {
    Env = env
    Session = None
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
    OnDialog = ignore
  }

  let subject = new BehaviorSubject<RepositoryHome.Model>(model)

  RepositoryHome.view
    props
    (subject :> IObservable<RepositoryHome.Model>)
    (fun (_: RepositoryHome.Msg) -> ())

let private repositoryRef =
  WorkbenchSettings.parse "repository:octocat" |> WorkbenchState.refOf

let private notificationsRef =
  WorkbenchSettings.parse "notifications" |> WorkbenchState.refOf

let private notificationsPage =
  match NotificationPage.decode groupedInboxJson with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned notifications never decoded: {failure.Message}"

let private unreadNotifications = {
  notificationsPage with
      Page = {
        notificationsPage.Page with
            Items =
              notificationsPage.Page.Items
              |> List.mapi(fun index notification ->
                if index = 0 then
                  { notification with UnreadCount = 2 }
                else
                  notification)
            NextCursor = Some "next"
      }
}

let private notificationsViewOf(env: TestEnv) : Control =
  let model: NotificationsCenter.Model = {
    Loading = false
    More = false
    Error = None
    Items = unreadNotifications.Page.Items
    Cursor = Some "next"
    Now = now
  }

  let props: NotificationsCenter.Props<TestEnv> = {
    Env = env
    KeyFile = "/tmp/devnull-test-id_ed25519"
    Images = images
    Now = Observable.single now
    OnCheckout = ignore
  }

  let subject = new BehaviorSubject<NotificationsCenter.Model>(model)

  NotificationsCenter.view
    props
    (subject :> IObservable<NotificationsCenter.Model>)
    (fun (_: NotificationsCenter.Msg) -> ())

let private inspectorViewOf(env: TestEnv) : Control =
  let model: Inspector.Model = {
    CommitId = threadPage.Commit.Id
    Loading = false
    Error = None
    Commit = Some threadPage.Commit
    Ancestors = []
    ReplyParticipants = []
    Replies = []
    RepliesCursor = None
    RepliesLoading = false
    RepliesError = None
    Busy = None
    Pinning = None
    PollChoices = Map.empty
    Now = now
  }

  let props: Inspector.Props<TestEnv> = {
    Env = env
    Session = None
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
    OnCheckoutParent = ignore
    OnCheckoutChild = ignore
    OnOpenAuthor = ignore
    OnOpenExternal = ignore
    OnQuote = ignore
    OnOpenThread = ignore
    OnClose = ignore
    OnDialog = ignore
  }

  let subject = new BehaviorSubject<Inspector.Model>(model)

  Inspector.view
    props
    (subject :> IObservable<Inspector.Model>)
    (fun (_: Inspector.Msg) -> ())

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
    Notifications = None
    SearchPalette = None
    Search = None
    SearchTerm = None
    ShareOpen = None
    ReportOpen = None
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
    LiveNotifications = 0
    LiveSearchPalette = 0
    LiveSearch = 0
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

let private buildNotificationsAsync
  ()
  : Async<Window * BehaviorSubject<WorkbenchHost.Model>> = async {
  return!
    probeUiAsync(fun () ->
      let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)
      let props = hostProps env
      let notificationsView = notificationsViewOf env

      let model = {
        modelOf(false, false) with
            Selected = notificationsRef
            CheckoutId = None
            History = None
            Inspector = None
            Notifications =
              Some {
                View = notificationsView
                Handle = fakeHandle
              }
            LiveHistory = 0
            LiveInspector = 0
            LiveNotifications = 1
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

let private heightOf(window: Window, name: string) : float =
  controlByName(window, name)
  |> Option.map(fun control -> control.Bounds.Height)
  |> Option.defaultValue 0.0

let private textBoxByName(root: Control, name: string) : TextBox option =
  root.GetVisualDescendants()
  |> Seq.tryPick(function
    | :? TextBox as box when box.Name = name -> Some box
    | _ -> None)

let private buttonWithText(root: Control, text: string) : Button option =
  root.GetVisualDescendants()
  |> Seq.tryPick(function
    | :? Button as button ->
      match button.Content with
      | :? string as label when label = text -> Some button
      | _ -> None
    | _ -> None)

let private menuContentOf(root: Control) : Control option =
  controlByName(root, "CommitMenuFlyout")
  |> Option.bind(function
    | :? Avalonia.Controls.Primitives.Popup as popup -> Option.ofObj popup.Child
    | _ -> None)

let private menuRowOf(window: Window, label: string) : Button option =
  menuContentOf window
  |> Option.bind(fun content -> buttonWithText(content, label))

let private envWithRemotes() : TestEnv =
  let api = workbenchApi()
  let store = signedInStore()

  match store.Current with
  | SignedIn current ->
    store.Refresh {
      current.Session with
          Remotes =
            Some {
              From = FromEveryone
              PullReceipts = true
              Requests = true
              Everyone = true
              UnreadCount = 0
            }
    }
  | SignedOut -> ()

  TestEnv(store, api :> IHttp)

let private openFirstMenu(window: Window) = async {
  let! trigger =
    waitForAsync(
      8000,
      (fun () -> controlByName(window, "CommitMenuButton")),
      Option.isSome
    )

  Expect.isTrue trigger "the card offered no menu trigger"

  do!
    onUiAsync(fun () ->
      controlByName(window, "CommitMenuButton")
      |> Option.iter(function
        | :? Button as button -> click button
        | _ -> ()))
}

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
      "the open staging zone stays under 45 percent of a 900 px window"
      (async {
        let! window, subject =
          probeUiAsync(fun () ->
            let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)

            let entries = [
              for index in 1..12 ->
                {
                  queueEntry with
                      Id = "018f5a1e-0001-7c3a-9b2d-0000000000" + string index
                      Body = Some $"Queued commit {index}."
                      BodySegments = [ TextSegment $"Queued commit {index}." ]
                }
            ]

            let model: Staging.View = {
              Open = true
              Draft = "release notes"
              Quote = None
              Queue = {
                Items = entries
                Total = entries.Length
                Loading = false
                Error = None
              }
            }

            let props: Staging.Props<TestEnv> = {
              Env = env
              Session = Some(sessionOf env)
              OnToggle = ignore
              OnDraftChanged = ignore
              OnPosted = ignore
              OnScheduled = ignore
            }

            let subject = new BehaviorSubject<Staging.View>(model)

            let view =
              Staging.view props (subject :> IObservable<Staging.View>)

            let window = Window(Width = 1600.0, Height = 900.0, Content = view)
            window.Show()
            window, subject)

        do! pumpAsync()

        let! stagingHeight =
          probeUiAsync(fun () -> heightOf(window, "StagingRegion"))

        Expect.isTrue
          (stagingHeight > 0.0 && stagingHeight < 405.0)
          $"the open staging zone took {stagingHeight} of 900 px"

        do!
          onUiAsync(fun () ->
            window.Close()
            subject.OnCompleted()
            subject.Dispose())
      })

    testCaseAsync
      "the inspector close clears the window chrome band and the right edge"
      (async {
        let! window =
          probeUiAsync(fun () ->
            let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)

            let window =
              Window(
                Width = 1600.0,
                Height = 900.0,
                Content = inspectorViewOf env
              )

            window.Show()
            window)

        do! pumpAsync()

        let! top, rightGap =
          probeUiAsync(fun () ->
            match controlByName(window, "InspectorClose") with
            | Some close ->
              let origin = close.TranslatePoint(Point(0.0, 0.0), window)

              origin.Value.Y,
              window.Bounds.Width - (origin.Value.X + close.Bounds.Width)
            | None -> 0.0, 0.0)

        Expect.isGreaterThanOrEqual
          top
          (Theme.bandHeight + 8.0)
          "the inspector close entered the window chrome band"

        Expect.isGreaterThanOrEqual
          rightGap
          40.0
          "the inspector close crowded the window close button"

        do! onUiAsync(fun () -> window.Close())
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

    testCaseAsync
      "the notifications center at 1600 px lists the API's rows"
      (async {
        let! window, subject = buildNotificationsAsync()

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              hasText(root, "NOTIFICATIONS")
              && hasText(root, "replied to your commit")
              && hasText(root, "starred your commit")
              && hasText(root, "3 stars")
              && hasText(root, "It was DNS.")
              && hasText(root, "Read state stays with the server.")
              && (controlByName(root, "SearchEntry") |> Option.isSome)
          )

        Expect.isTrue shown "the notifications center never rendered its rows"

        let! notice =
          probeUiAsync(fun () ->
            controlByName(window, "NotificationsNotice")
            |> Option.map(function
              | :? TextBlock as block -> block.TextWrapping
              | _ -> TextWrapping.NoWrap))

        Expect.equal
          notice
          (Some TextWrapping.Wrap)
          "the notifications note does not wrap"

        do! pumpAsync()

        let! (centerWidth, historyVisible, notificationsVisible, unreadVisible) =
          probeUiAsync(fun () ->
            let unread =
              window.GetVisualDescendants()
              |> Seq.choose(function
                | :? Border as border when
                  border.Name = "NotificationUnreadDot"
                  ->
                  Some border
                | _ -> None)
              |> Seq.exists(fun border -> border.IsVisible)

            widthOf(window, "NotificationsCenter"),
            controlByName(window, "HistoryRegion")
            |> Option.map _.IsVisible
            |> Option.defaultValue false,
            controlByName(window, "NotificationsRegion")
            |> Option.map _.IsVisible
            |> Option.defaultValue false,
            unread)

        Expect.isTrue
          (centerWidth > 1300.0 && centerWidth < 1400.0)
          $"the notifications center lost the free width: {centerWidth}"

        Expect.isFalse
          historyVisible
          "the history stayed visible over the inbox"

        Expect.isTrue
          notificationsVisible
          "the notifications region is not visible"

        Expect.isTrue unreadVisible "no unread dot rendered"

        do! closeAsync(window, subject)
      })

    testCaseAsync
      "the hosted search dialog searches and opens the mapped commit route"
      (async {
        let api = workbenchApi()
        let env = TestEnv(signedInStore(), api :> IHttp)
        let props = hostProps env

        let! window, handle =
          probeUiAsync(fun () ->
            let view, handle = WorkbenchHost.mount(ui, props)

            let window = Window(Width = 1600.0, Height = 900.0, Content = view)

            window.Show()
            window, handle)

        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchEntry")
            |> Option.iter(function
              | :? Button as button -> click button
              | _ -> ()))

        let! input =
          waitForAsync(
            8000,
            (fun () -> textBoxByName(window, "SearchInput")),
            Option.isSome
          )

        Expect.isTrue input "the hosted search dialog never opened"

        do!
          onUiAsync(fun () ->
            textBoxByName(window, "SearchInput")
            |> Option.iter(fun box -> box.Text <- "dns server"))

        let! results =
          waitForAsync(
            8000,
            (fun () -> controlByName(window, "SearchDialogCard")),
            fun card ->
              card
              |> Option.exists(fun root -> hasTextFragment(root, "It was DNS."))
          )

        Expect.isTrue results "the hosted search dialog never rendered results"

        Expect.exists
          api.Requests
          (fun request ->
            request.Target.StartsWith(
              "/api/v1/search/commits?q=dns%20server",
              StringComparison.Ordinal
            ))
          "the hosted search dialog never searched commits"

        do!
          onUiAsync(fun () ->
            textBoxByName(window, "SearchInput")
            |> Option.iter(fun box ->
              box.RaiseEvent(
                KeyEventArgs(
                  RoutedEvent = InputElement.KeyDownEvent,
                  Key = Key.Enter
                )
              )))

        let! opened =
          waitForAsync(
            8000,
            (fun () -> controlByName(window, "ThreadCenter")),
            Option.isSome
          )

        Expect.isTrue opened "Enter did not open the mapped commit thread"

        do!
          onUiAsync(fun () ->
            window.Close()
            (handle :> IDisposable).Dispose())
      })

    testCaseAsync
      "the hosted search center reuses the dialog with the committed term"
      (async {
        let env = TestEnv(signedInStore(), workbenchApi() :> IHttp)
        let props = hostProps env

        let! window, mounted =
          probeUiAsync(fun () ->
            let view, handle = WorkbenchHost.mount(ui, props)

            let window = Window(Width = 1600.0, Height = 900.0, Content = view)

            window.Show()
            window, handle :?> WorkbenchHost.MountedHost)

        do!
          onUiAsync(fun () ->
            mounted.Send(WorkbenchHost.SearchCommitted "dns server"))

        let! center =
          waitForAsync(
            8000,
            (fun () -> controlByName(window, "SearchRegion")),
            fun region ->
              region |> Option.exists(fun control -> control.IsVisible)
              && (controlByName(window, "SearchPaletteRegion")
                  |> Option.exists(fun control -> not control.IsVisible))
          )

        Expect.isTrue center "the search center never opened"

        let! seeded =
          waitForAsync(
            8000,
            (fun () -> textBoxByName(window, "SearchInput")),
            fun input ->
              input
              |> Option.exists(fun box ->
                box.Text = "dns server"
                && hasTextFragment(window, "It was DNS."))
          )

        Expect.isTrue seeded "the search center lost the committed term"

        do!
          onUiAsync(fun () ->
            window.Close()
            (mounted :> IDisposable).Dispose())
      })

    testCaseAsync
      "Escape closes the hosted search palette"
      (async {
        let env = TestEnv(signedInStore(), workbenchApi() :> IHttp)
        let props = hostProps env

        let! window, handle =
          probeUiAsync(fun () ->
            let view, handle = WorkbenchHost.mount(ui, props)

            let window = Window(Width = 1600.0, Height = 900.0, Content = view)

            window.Show()
            window, handle)

        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchEntry")
            |> Option.iter(function
              | :? Button as button -> click button
              | _ -> ()))

        let! input =
          waitForAsync(
            8000,
            (fun () -> textBoxByName(window, "SearchInput")),
            Option.isSome
          )

        Expect.isTrue input "the hosted search dialog never opened"

        do!
          onUiAsync(fun () ->
            textBoxByName(window, "SearchInput")
            |> Option.iter(fun box ->
              box.RaiseEvent(
                KeyEventArgs(
                  RoutedEvent = InputElement.KeyDownEvent,
                  Key = Key.Escape
                )
              )))

        let! closed =
          waitForAsync(
            8000,
            (fun () -> controlByName(window, "SearchDialogCard")),
            Option.isNone
          )

        Expect.isTrue closed "Escape did not close the hosted search palette"

        do!
          onUiAsync(fun () ->
            window.Close()
            (handle :> IDisposable).Dispose())
      })

    testCaseAsync
      "a card menu opens the share dialog when remotes are on"
      (async {
        let env = envWithRemotes()
        do! pumpAsync()
        let props = hostProps env

        let! window, handle =
          probeUiAsync(fun () ->
            let view, handle = WorkbenchHost.mount(ui, props)

            let window = Window(Width = 1600.0, Height = 900.0, Content = view)

            window.Show()
            window, handle)

        do! openFirstMenu window

        let! shareRow =
          waitForAsync(
            5000,
            (fun () -> menuRowOf(window, "Send in Remotes")),
            Option.isSome
          )

        Expect.isTrue shareRow "the card menu offered no Remotes row"

        do!
          onUiAsync(fun () ->
            menuRowOf(window, "Send in Remotes") |> Option.iter click)

        let! dialog =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ShareDialog")),
            Option.isSome
          )

        Expect.isTrue dialog "the share dialog never opened"

        do!
          onUiAsync(fun () ->
            controlByName(window, "ShareDialog")
            |> Option.bind(fun dialog -> buttonWithText(dialog, "Cancel"))
            |> Option.iter click)

        let! closed =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ShareDialog")),
            Option.isNone
          )

        Expect.isTrue closed "the share dialog did not close on Cancel"

        do!
          onUiAsync(fun () ->
            window.Close()
            (handle :> IDisposable).Dispose())
      })

    testCaseAsync
      "a card menu opens the report dialog on another author's commit"
      (async {
        let env = envWithRemotes()
        do! pumpAsync()
        let props = hostProps env

        let! window, handle =
          probeUiAsync(fun () ->
            let view, handle = WorkbenchHost.mount(ui, props)

            let window = Window(Width = 1600.0, Height = 900.0, Content = view)

            window.Show()
            window, handle)

        do! openFirstMenu window

        let! reportRow =
          waitForAsync(
            5000,
            (fun () -> menuRowOf(window, "Report commit")),
            Option.isSome
          )

        Expect.isTrue reportRow "the card menu offered no report row"

        do!
          onUiAsync(fun () ->
            menuRowOf(window, "Report commit") |> Option.iter click)

        let! dialog =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ReportDialog")),
            Option.isSome
          )

        Expect.isTrue dialog "the report dialog never opened"

        do!
          onUiAsync(fun () ->
            controlByName(window, "ReportCancel")
            |> Option.iter(function
              | :? Button as button -> click button
              | _ -> ()))

        let! closed =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ReportDialog")),
            Option.isNone
          )

        Expect.isTrue closed "the report dialog did not close on Cancel"

        do!
          onUiAsync(fun () ->
            window.Close()
            (handle :> IDisposable).Dispose())
      })
  ]
