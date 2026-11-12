module DevNull.Client.UI.Tests.EditorFrame

open System
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Formatting
open DevNull.Client.Layouts.Editor
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let props(env: TestEnv) : EditorRoot.Props<TestEnv> =
  let session =
    match (env :> IProvideSession).Session.SignedIn with
    | ValueSome session -> session
    | ValueNone -> failwith "the test env is signed out"

  {
    Env = env
    Session = session
    Stored = EditorSettings.initial()
    Lane = Some(LaneIndex 2)
    Zone = {
      Info = TimeZoneInfo.Utc
      Label = "UTC"
    }
    Saved = Observable.single 4
    Queue = Observable.single 1
    Active = Observable.single true
    Now = fun () -> DateTimeOffset.UtcNow
  }

let statusText(window: Window) =
  window.GetVisualDescendants()
  |> Seq.tryFind(fun visual -> visual.Name = "EditorStatusBar")
  |> Option.bind(fun visual ->
    match visual with
    | :? Control as bar ->
      bar.GetVisualDescendants()
      |> Seq.tryPick(function
        | :? TextBlock as block -> Some block.Text
        | _ -> None)
    | _ -> None)

let explorerVisible(window: Window) =
  window.GetVisualDescendants()
  |> Seq.exists(fun visual ->
    visual.Name = "EditorExplorer"
    && (match visual with
        | :? Control as control -> control.IsEffectivelyVisible
        | _ -> false))

let tests =
  testList "editor frame" [
    testCaseAsync
      "the frame shows the full anatomy at 1600 and trims at 380"
      (async {
        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/log/" then
              ok emptyLogPageJson
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let env = TestEnv(signedInStore(), api :> IHttp)

        let! window, handle =
          probeUiAsync(fun () ->
            let control, handle = EditorRoot.mount(ui, props env)

            let window =
              Window(Width = 1600.0, Height = 900.0, Content = control)

            window.Show()
            window, handle)

        let! fetched =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "GET"
                && request.Target.StartsWith "/api/v1/log/")
          )

        Expect.isTrue fetched "the history tab never fetched its first page"

        do! settleAsync()

        let! fullStatus = probeUiAsync(fun () -> statusText window)

        Expect.isSome fullStatus "the status strip never rendered"

        Expect.isTrue
          (fullStatus
           |> Option.bind(fun text -> Option.ofObj text)
           |> Option.exists(fun text -> text.Contains "fetched"))
          "the full status strip lost its fetch line"

        let! explorer = probeUiAsync(fun () -> explorerVisible window)

        Expect.isTrue explorer "the explorer never stood at 1600 px"

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "EXPLORER")
          )

        Expect.isTrue shown "the explorer's header never rendered"

        do! onUiAsync(fun () -> window.Width <- 380.0)
        do! settleAsync()

        let! trimmed =
          waitForAsync(
            5000,
            (fun () -> statusText window),
            fun text ->
              text |> Option.exists(fun value -> value = "⎇ Following")
          )

        Expect.isTrue trimmed "the status strip never trimmed at 380 px"

        let! explorerAtFloor = probeUiAsync(fun () -> explorerVisible window)

        Expect.isFalse
          explorerAtFloor
          "the explorer stayed visible at the app floor"

        do!
          onUiAsync(fun () ->
            window.Close()
            handle.Dispose())
      })

    testCaseAsync
      "the trimmed strip keeps the pinned and active tabs and overflows the rest"
      (async {
        let tabs = [
          EditorTab.pinned Following
          EditorTab.ordinary(TabId.ordinary "a1b2", "a1b2 @ana")
          EditorTab.ordinary(TabId.ordinary "55aa", "55aa @cy")
        ]

        let! shape =
          probeUiAsync(fun () ->
            let view =
              TabStrip.view {
                Tabs = tabs
                Active = TabId.ordinary "55aa"
                Band = Trimmed
                OnSelect = ignore
                OnClose = ignore
              }

            match view with
            | :? Border as border ->
              match border.Child with
              | :? StackPanel as panel ->
                let overflow =
                  panel.Children[panel.Children.Count - 1] :?> Button

                panel.Children.Count, string overflow.Content
              | _ -> 0, ""
            | _ -> 0, "")

        Expect.equal (fst shape) 3 "the trimmed strip's controls"

        Expect.equal
          (snd shape)
          "⋯1"
          "the overflow count did not name the hidden tab"
      })
  ]
