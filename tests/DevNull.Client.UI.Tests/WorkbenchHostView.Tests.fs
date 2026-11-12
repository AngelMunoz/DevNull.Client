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
open DevNull.Client.Layouts.Workbench
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let private now = DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)

let private checkoutId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

let private fakeHandle = {
  new IDisposable with
    member _.Dispose() = ()
}

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
  OnQuote = ignore
  OnOpenExternal = ignore
  OnStateChanged = ignore
}

let private controlByName(root: Control, name: string) : Control option =
  root.GetVisualDescendants()
  |> Seq.choose(function
    | :? Control as control when control.Name = name -> Some control
    | _ -> None)
  |> Seq.tryHead

let private modelOf(folded: bool) : WorkbenchHost.Model =
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
    StagingOpen = false
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
  (folded: bool, width: float)
  : Async<Window * BehaviorSubject<WorkbenchHost.Model>> = async {
  return!
    probeUiAsync(fun () ->
      let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)
      let props = hostProps env
      let subject = new BehaviorSubject<WorkbenchHost.Model>(modelOf folded)

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
        let! window, subject = buildAsync(false, 1600.0)
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
        let! window, subject = buildAsync(true, 1000.0)
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
  ]
