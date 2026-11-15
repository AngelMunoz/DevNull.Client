module DevNull.Client.UI.Tests.EditorFrame

open System
open Avalonia
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Formatting
open DevNull.Client.Editor
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
    Active = Observable.single false
    Now = fun () -> DateTimeOffset.UtcNow
    OnUseDefaultLayout = ignore
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

let tabCount(window: Window) =
  window.GetVisualDescendants()
  |> Seq.filter(fun visual ->
    match visual with
    | :? Button as button ->
      button.Classes.Contains(Theme.name Theme.TabButton)
    | _ -> false)
  |> Seq.length

let feedScroller(window: Window) =
  window.GetVisualDescendants()
  |> Seq.choose(function
    | :? ScrollViewer as scroller -> Some scroller
    | _ -> None)
  |> Seq.filter(fun scroller ->
    scroller.Extent.Height > scroller.Viewport.Height + 1.0)
  |> Seq.sortByDescending(fun scroller -> scroller.Extent.Height)
  |> Seq.tryHead

let longCommit(index: int) =
  let moment =
    DateTimeOffset(2026, 10, 20, 4, 17, 19, TimeSpan.Zero)
      .AddMinutes(float index)
      .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")

  commitJson
    .Replace(
      "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d",
      $"018f4c1e-{index:D4}-7c3a-9b2d-5e6f7a8b9c0d"
    )
    .Replace("It was DNS.", $"Commit {index}.")
    .Replace("2026-10-20T04:17:19Z", moment)

let longLogPageJson =
  let rows = [ for index in 0..29 -> longCommit index ] |> String.concat ","

  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"following\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
    rows

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

        let! topInset =
          probeUiAsync(fun () -> (window.Content :?> Control).Margin.Top)

        Expect.equal
          topInset
          Theme.bandHeight
          "the band still covers the tab strip"

        let! centered =
          probeUiAsync(fun () ->
            let named(name: string) =
              window.GetVisualDescendants()
              |> Seq.tryFind(fun visual -> visual.Name = name)
              |> Option.map(fun visual -> visual :?> Control)

            match
              named "EditorActivityBar", named "EditorActivity-History"
            with
            | Some bar, Some button ->
              let point =
                button.TranslatePoint(
                  Point(button.Bounds.Width / 2.0, button.Bounds.Height / 2.0),
                  bar
                )

              point.HasValue
              && abs(point.Value.X - bar.Bounds.Width / 2.0) < 0.5
            | _ -> false)

        Expect.isTrue centered "the activity icons left-aligned inside the bar"

        let! headSpacing =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryFind(fun visual ->
              match visual with
              | :? TextBlock as text -> text.Text = "HEAD"
              | _ -> false)
            |> Option.map(fun visual -> visual :?> TextBlock)
            |> Option.exists(fun tag -> tag.Padding.Left >= 4.0))

        Expect.isTrue headSpacing "the HEAD tag kept its old spacing"

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

        let! tabsBefore = probeUiAsync(fun () -> tabCount window)

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Latest") |> Option.iter click)

        let! switched =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "GET"
                && request.Target.StartsWith "/api/v1/log/latest")
          )

        Expect.isTrue switched "the Latest ref never read its log"

        do! settleAsync()

        let! tabsAfter, switchedStatus =
          probeUiAsync(fun () -> tabCount window, statusText window)

        Expect.equal tabsAfter tabsBefore "the ref switch churned the tab strip"

        Expect.isTrue
          (switchedStatus
           |> Option.bind(fun text -> Option.ofObj text)
           |> Option.exists(fun text -> text.Contains "Latest"))
          "the status strip kept the old ref"

        do! onUiAsync(fun () -> window.Width <- 380.0)
        do! settleAsync()

        let! trimmed =
          waitForAsync(
            5000,
            (fun () -> statusText window),
            fun text -> text |> Option.exists(fun value -> value = "⎇ Latest")
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
      "a ref switch restores the place with one first-page fetch"
      (async {
        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/log/" then
              ok longLogPageJson
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

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "Commit 0.")
          )

        Expect.isTrue shown "the long history never rendered"

        do! settleAsync()

        do!
          onUiAsync(fun () ->
            feedScroller window
            |> Option.iter(fun scroller ->
              scroller.Offset <- Vector(0.0, 240.0)))

        do! settleAsync()

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Latest") |> Option.iter click)

        let! latest =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "GET"
                && request.Target.StartsWith "/api/v1/log/latest")
          )

        Expect.isTrue latest "the Latest ref never read"

        do! settleAsync()

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Following") |> Option.iter click)

        let! returned =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            fun requests ->
              requests
              |> List.filter(fun request ->
                request.Method = "GET"
                && request.Target.StartsWith "/api/v1/log/following")
              |> List.length
              >= 2
          )

        Expect.isTrue returned "the Following ref never read again"

        let! restored =
          waitForAsync(
            8000,
            (fun () ->
              feedScroller window
              |> Option.map(fun scroller -> scroller.Offset.Y)
              |> Option.defaultValue -1.0),
            fun offset -> abs(offset - 240.0) < 4.0
          )

        Expect.isTrue restored "the scroll place never returned"

        let following =
          api.Requests
          |> List.filter(fun request ->
            request.Method = "GET"
            && request.Target.StartsWith "/api/v1/log/following")
          |> List.length

        Expect.equal following 2 "one first-page fetch per return"

        do!
          onUiAsync(fun () ->
            window.Close()
            handle.Dispose())
      })

    testCaseAsync
      "a thread tab opens beside the pinned tab at 1600 and trims at 380"
      (async {
        let api = threadApi()
        let env = TestEnv(signedInStore(), api :> IHttp)

        let! window, handle =
          probeUiAsync(fun () ->
            let control, handle = EditorRoot.mount(ui, props env)

            let window =
              Window(Width = 1600.0, Height = 900.0, Content = control)

            window.Show()
            window, handle)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history card never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "A reply.")
          )

        Expect.isTrue thread "the thread tab never opened"

        let! tabs = probeUiAsync(fun () -> tabCount window)

        Expect.equal tabs 2 "the pinned tab and the thread tab"

        do! onUiAsync(fun () -> window.Width <- 380.0)
        do! settleAsync()

        let! kept =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "A reply.")
          )

        Expect.isTrue kept "the thread left at 380 px"

        let! trimmed =
          waitForAsync(
            5000,
            (fun () -> statusText window),
            fun text ->
              text |> Option.exists(fun value -> value = "⎇ Following")
          )

        Expect.isTrue trimmed "the status strip never trimmed at 380 px"

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
