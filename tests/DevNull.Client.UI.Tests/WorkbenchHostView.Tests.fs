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
  ]
