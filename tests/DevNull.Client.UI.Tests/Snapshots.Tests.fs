module DevNull.Client.UI.Tests.Snapshots

// The camera tests: a feature's visual states saved as PNGs a review
// pass — or the user — can open. The harness session renders through
// Skia, so a capture is the frame a user sees. artifacts/snapshots is
// gitignored: captures are evidence for a review round, never fixtures.

open System
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Headless
open Avalonia.Input
open Avalonia.VisualTree
open Expecto

open DevNull.Client.Core.Http
open DevNull.Client.UI.Tests.App
open DevNull.Client.UI.Tests.Composer
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

/// A thread with enough replies to scroll one page, so the capture
/// shows content under the band after a scroll.
let private longThreadApi() : FakeApi =
  let replies =
    let rows = [
      for index in 1..12 ->
        replyJson.Replace(
          "018f4c1e-7777-7c3a-9b2d-5e6f7a8b9c0d",
          $"018f4c1e-7777-7c3a-9b2d-5e6f7a8b{index:D4}"
        )
    ]

    sprintf
      "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
      (String.concat "," rows)

  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif
      request.Method = "GET"
      && request.Target.StartsWith(
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/replies"
      )
    then
      ok replies
    elif
      request.Target.StartsWith
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
    then
      ok threadPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// Opens the first thread and waits for its back line.
let private openThreadAsync() : Async<Window> = async {
  let! window = startShell(signedInStore(), longThreadApi() :> IHttp)
  let! shown = showsText(window, "It was DNS.")
  Expect.isTrue shown "the log never rendered"
  do! clickWhenShown(window, "It was DNS.")
  let! thread = showsText(window, "Back to the log")
  Expect.isTrue thread "the thread never rendered"
  do! settleAsync()
  return window
}

/// Opens the search results page through the palette, the way a reader
/// reaches it.
let private openSearchPageAsync() : Async<Window> = async {
  let! window = startShell(signedInStore(), searchApi(false, false) :> IHttp)
  let! shown = showsText(window, "The Octocat")
  Expect.isTrue shown "the log never rendered"
  do! clickWhenShown(window, "Search")

  let! input =
    waitForAsync(
      5000,
      (fun () -> controlByName(window, "SearchInput")),
      Option.isSome
    )

  Expect.isTrue input "the search palette never opened"

  do!
    onUiAsync(fun () ->
      controlByName(window, "SearchInput")
      |> Option.iter(fun control -> (control :?> TextBox).Text <- "shippin"))

  let! settled = seesVisible(window, "@kai-kou")
  Expect.isTrue settled "the search never settled"
  do! clickWhenShown(window, "See all matching commits")

  let! boxShown =
    waitForAsync(
      5000,
      (fun () -> controlByName(window, "SearchBox")),
      Option.isSome
    )

  Expect.isTrue boxShown "the results page never opened"

  let! counted = seesVisible(window, "1 match")
  Expect.isTrue counted "the results page never counted its match"

  do! settleAsync()
  return window
}

/// Opens Settings over the given api and waits for the devices section.
let private openSettingsAsync(api: IHttp) : Async<Window> = async {
  let! window = startShell(signedInStore(), api)
  let! shown = showsText(window, "The Octocat")
  Expect.isTrue shown "the log never rendered"
  do! clickWhenShown(window, "Settings")
  let! settings = showsText(window, "Devices")
  Expect.isTrue settings "the settings page never rendered"
  do! settleAsync()
  return window
}

/// The Settings page's own scroller, found above the devices heading.
let private scrollSettingsToEndAsync(window: Window) : Async<unit> =
  onUiAsync(fun () ->
    window.GetVisualDescendants()
    |> Seq.choose(fun visual ->
      match visual with
      | :? TextBlock as block when block.Text = "Devices" -> Some block
      | _ -> None)
    |> Seq.tryHead
    |> Option.bind(fun block ->
      block.GetVisualAncestors()
      |> Seq.tryPick(function
        | :? ScrollViewer as scroller -> Some scroller
        | _ -> None))
    |> Option.iter _.ScrollToEnd())

/// Opens Settings with the device list loaded and scrolled into view.
let private openDevicesAsync(api: IHttp) : Async<Window> = async {
  let! window = openSettingsAsync api
  let! listed = showsText(window, "on A Mac laptop")
  Expect.isTrue listed "the devices never listed"
  do! scrollSettingsToEndAsync window
  do! settleAsync()
  return window
}

/// One api whose log row carries no image, so the reply composer and
/// its toolbar fit the frame.
let imagelessLogApi() =
  let page =
    Text.RegularExpressions.Regex.Replace(
      logPageJson,
      "\"images\":\[[^\]]*\]",
      "\"images\":[]"
    )

  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok page
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// One api that answers the quote route's commit read with the canned
/// thread, so the quote composer stands over a commit.
type QuoteApi(inner: IHttp) =
  interface IHttp with
    member _.Send(request) =
      if request.Method = "GET" && request.Target.Contains "/replies" then
        async { return Ok(ok repliesPageJson) }
      elif request.Method = "GET" && request.Target.Contains "/related" then
        async { return Ok(ok """{"data":[]}""") }
      elif
        request.Method = "GET" && request.Target.StartsWith "/api/v1/commits/"
      then
        async { return Ok(ok threadPageJson) }
      else
        inner.Send request

    member _.GetBytes(target) = inner.GetBytes target

/// One api whose device list waits at a gate, so the page can be
/// captured in its loading state.
type private HeldDevicesApi(inner: IHttp, gate: TaskCompletionSource<unit>) =
  interface IHttp with
    member _.Send(request) =
      if request.Method = "GET" && request.Target = "/api/v1/me/devices" then
        async {
          do! gate.Task |> Async.AwaitTask
          return! inner.Send request
        }
      else
        inner.Send request

    member _.GetBytes(target) = inner.GetBytes target

/// The thread page's own scroller, found above the back line.
let private threadOffset(window: Window) : float =
  buttonWithText(window, "Back to the log")
  |> Option.bind(fun button ->
    button.GetVisualAncestors()
    |> Seq.tryPick(function
      | :? ScrollViewer as scroller -> Some scroller.Offset.Y
      | _ -> None))
  |> Option.defaultValue 0.0

/// Scrolls the thread page to its end through its own scroller.
let private scrollThreadToEndAsync(window: Window) : Async<unit> =
  onUiAsync(fun () ->
    buttonWithText(window, "Back to the log")
    |> Option.iter(fun button ->
      button.GetVisualAncestors()
      |> Seq.tryPick(function
        | :? ScrollViewer as scroller -> Some scroller
        | _ -> None)
      |> Option.iter _.ScrollToEnd()))

/// The proof of the pipeline itself: one signed-in shell over the canned
/// timeline page, rendered and saved. Every feature's states ride the
/// same captureAsync.
[<Tests>]
let captureTests =
  testList "snapshots" [
    testAsync "the shell renders a frame the camera can save" {
      let api =
        FakeApi(fun request ->
          if request.Target.StartsWith "/api/v1/log/" then
            ok logPageJson
          else
            (match accountAnswer request with
             | ValueSome answer -> answer
             | ValueNone -> ok "{}"))

      let! window = startShell(signedInStore(), api :> IHttp)

      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the shell never rendered its content"

      let! path = captureAsync("spike-shell", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the thread clears the band at the wide width" {
      let! window = openThreadAsync()

      let! path = captureAsync("F15/f15-commit-top-wide", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the thread clears the band at the narrow width" {
      let! window = openThreadAsync()

      do! onUiAsync(fun () -> window.Width <- 380.0)
      do! settleAsync()

      let! path = captureAsync("F15/f15-commit-top-narrow", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "scrolled thread content stays under the band" {
      let! window = openThreadAsync()

      do! scrollThreadToEndAsync window
      do! settleAsync()

      let! offset = probeUiAsync(fun () -> threadOffset window)
      Expect.isGreaterThan offset 0.0 "the thread never scrolled"

      let! path = captureAsync("F15/f15-commit-scrolled", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the search page clears the band" {
      let! window = openSearchPageAsync()

      let! path = captureAsync("F15/f15-search-top", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the devices section names the app first" {
      let! window = openDevicesAsync(devicesApi() :> IHttp)

      let! appNamed = seesVisible(window, "DevNull Client")
      Expect.isTrue appNamed "the app-named row never showed"

      let! machine = seesVisible(window, "on This Fedora desktop")
      Expect.isTrue machine "the own row never named its machine"

      let! path = captureAsync("F8/f8-devices-wide", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the devices section wraps a long app name at a fitting width" {
      Expect.isLessThanOrEqual
        longAppName.Length
        100
        "the fixture stays inside the wire cap"

      let! window = openDevicesAsync(longAppNameDevicesApi() :> IHttp)

      do! onUiAsync(fun () -> window.Width <- 720.0)
      do! settleAsync()
      do! scrollSettingsToEndAsync window
      do! settleAsync()

      let fingerprint = "SHA256:AnotherDeviceFingerprint0123456789012"

      do!
        onUiAsync(fun () ->
          let blocks =
            window.GetVisualDescendants()
            |> Seq.choose(fun visual ->
              match visual with
              | :? TextBlock as block -> Some block
              | _ -> None)
            |> List.ofSeq

          let app =
            blocks |> List.tryFind(fun block -> block.Text = longAppName)

          let print =
            blocks |> List.tryFind(fun block -> block.Text = fingerprint)

          let wrapped =
            app
            |> Option.exists(fun block ->
              block.Bounds.Height > block.FontSize * 1.5)

          let inside =
            print
            |> Option.exists(fun block ->
              let at = block.TranslatePoint(Point(0.0, 0.0), window)

              at.HasValue
              && at.Value.X >= 0.0
              && at.Value.Y >= 0.0
              && at.Value.X + block.Bounds.Width <= window.Bounds.Width + 0.5
              && at.Value.Y + block.Bounds.Height
                 <= window.Bounds.Height + 0.5)

          Expect.isTrue wrapped "the long app name did not wrap"
          Expect.isTrue inside "the fingerprint left the window")

      let! path = captureAsync("F8/f8-devices-narrow", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the devices section shows its empty line" {
      let! window = openSettingsAsync(emptyDevicesApi() :> IHttp)

      do! scrollSettingsToEndAsync window
      do! settleAsync()

      let! empty = seesVisible(window, "No devices.")
      Expect.isTrue empty "the empty line never showed"

      let! path = captureAsync("F8/f8-devices-empty", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the devices section shows its loading line" {
      let gate = TaskCompletionSource<unit>()
      let api = HeldDevicesApi(devicesApi() :> IHttp, gate)

      let! window = openSettingsAsync(api :> IHttp)

      do! scrollSettingsToEndAsync window
      do! settleAsync()

      let! loading = seesVisible(window, "Loading the devices…")
      Expect.isTrue loading "the loading line never showed"

      let! path = captureAsync("F8/f8-devices-loading", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      gate.SetResult(())
      do! settleAsync()
    }

    testAsync "the fresh composer carries the emoji trigger" {
      let! window = startShell(signedInStore(), logApi() :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      let! path = captureAsync("F13/f13-trigger-fresh", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the reply composer carries the emoji trigger" {
      let! window = startShell(signedInStore(), imagelessLogApi() :> IHttp)
      let! _ = showsText(window, "The Octocat")

      do!
        onUiAsync(fun () ->
          controlByName(window, "ReplyButton")
          |> Option.iter(fun control ->
            match control with
            | :? Button as button -> click button
            | _ -> ()))

      let! reply =
        waitForAsync(
          5000,
          (fun () -> visibleBoxWithWatermark(window, "Write your reply…")),
          Option.isSome
        )

      Expect.isTrue reply "the reply composer never opened"

      do!
        onUiAsync(fun () ->
          visibleBoxWithWatermark(window, "Write your reply…")
          |> Option.bind(fun box ->
            box.GetVisualAncestors()
            |> Seq.tryPick(function
              | :? ScrollViewer as scroller -> Some scroller
              | _ -> None))
          |> Option.iter _.ScrollToEnd())

      do! settleAsync()

      let! path = captureAsync("F13/f13-trigger-reply", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the quote composer carries the emoji trigger" {
      let! window =
        startShell(signedInStore(), QuoteApi(logApi() :> IHttp) :> IHttp)

      let! _ = showsText(window, "The Octocat")

      do!
        onUiAsync(fun () ->
          controlByName(window, "QuoteButton")
          |> Option.iter(fun control ->
            match control with
            | :? Button as button -> click button
            | _ -> ()))

      let! quote =
        waitForAsync(
          5000,
          (fun () -> visibleBoxWithWatermark(window, "Add your take")),
          Option.isSome
        )

      Expect.isTrue quote "the quote composer never opened"

      let! path = captureAsync("F13/f13-trigger-quote", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the emoji trigger waits while a post is in flight" {
      let gate = TaskCompletionSource<unit>()
      let api = HeldCommitApi(logApi() :> IHttp, gate)
      let! window = startShell(signedInStore(), api :> IHttp)
      let! _ = showsText(window, "The Octocat")

      do! setComposerSelection(window, "A slow one", 10, 10)
      do! settleAsync()

      do!
        onUiAsync(fun () ->
          textBoxWithWatermark(window, "What did you ship?")
          |> Option.iter(fun box ->
            buttonNear(box, "Commit") |> Option.iter click))

      let! disabled =
        waitForAsync(
          5000,
          (fun () ->
            visibleEmojiButtons window
            |> List.tryHead
            |> Option.map _.IsEnabled),
          fun state -> state = Some false
        )

      Expect.isTrue disabled "the trigger stayed on"

      let! path = captureAsync("F13/f13-trigger-busy", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      gate.SetResult(())
      do! settleAsync()
    }

    testAsync "the picker panel shows each query state" {
      let! window = startShell(signedInStore(), logApi() :> IHttp)
      let! _ = showsText(window, "The Octocat")

      do! openEmojiPicker window

      let! openPath = captureAsync("F13/f13-trigger-open", window)
      let openBytes = IO.File.ReadAllBytes(openPath)
      Expect.isTrue (openBytes.Length > 1000) "the frame saved real pixels"

      let! emptyPath = captureAsync("F13/f13-panel-empty", window)
      let emptyBytes = IO.File.ReadAllBytes(emptyPath)
      Expect.isTrue (emptyBytes.Length > 1000) "the frame saved real pixels"

      do! typeSearch(window, "smi")

      let! known =
        waitForAsync(
          5000,
          (fun () -> cellLabel(window, 0)),
          fun label -> label = Some ":smile:"
        )

      Expect.isTrue known "the ranked results never stood"

      let! knownPath = captureAsync("F13/f13-panel-known", window)
      let knownBytes = IO.File.ReadAllBytes(knownPath)
      Expect.isTrue (knownBytes.Length > 1000) "the frame saved real pixels"

      do! typeSearch(window, "zzqq")

      let! missing =
        waitForAsync(
          5000,
          (fun () -> textOf(window, "EmojiFooterName")),
          fun text -> text = ""
        )

      Expect.isTrue missing "the empty footer never stood"

      let! unknownPath = captureAsync("F13/f13-panel-unknown", window)
      let unknownBytes = IO.File.ReadAllBytes(unknownPath)
      Expect.isTrue (unknownBytes.Length > 1000) "the frame saved real pixels"

      do! typeSearch(window, "")
      do! settleAsync()

      let! hovered =
        probeUiAsync(fun () ->
          controlByName(window, "EmojiCell3")
          |> Option.bind(fun control ->
            let at =
              control.TranslatePoint(
                Point(control.Bounds.Width / 2.0, control.Bounds.Height / 2.0),
                window
              )

            if at.HasValue then Some at.Value else None))

      do!
        onUiAsync(fun () ->
          hovered
          |> Option.iter(fun at ->
            window.MouseMove(at, RawInputModifiers.None)))

      let! armed =
        waitForAsync(
          5000,
          (fun () -> activeCell window),
          fun active -> active = Some "EmojiCellShell3"
        )

      Expect.isTrue armed "the pointer never armed a cell"

      let! hoverPath = captureAsync("F13/f13-panel-hover", window)
      let hoverBytes = IO.File.ReadAllBytes(hoverPath)
      Expect.isTrue (hoverBytes.Length > 1000) "the frame saved real pixels"

      do! pressSearch(window, Key.Down)

      let! walked =
        waitForAsync(
          5000,
          (fun () -> activeCell window),
          fun active -> active = Some "EmojiCellShell11"
        )

      Expect.isTrue walked "the keys never walked a row"

      let! walkPath = captureAsync("F13/f13-panel-key-walk", window)
      let walkBytes = IO.File.ReadAllBytes(walkPath)
      Expect.isTrue (walkBytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "a refused pick closes silently" {
      let! window = startShell(signedInStore(), logApi() :> IHttp)
      let! _ = showsText(window, "The Octocat")

      let full = String.replicate 40000 "a"

      do! setComposerSelection(window, full, full.Length, full.Length)
      do! openEmojiPicker window
      do! typeSearch(window, "rocket")

      let! rocket =
        waitForAsync(
          5000,
          (fun () -> cellLabel(window, 0)),
          fun label -> label = Some ":rocket:"
        )

      Expect.isTrue rocket "the rocket result never stood"

      do!
        onUiAsync(fun () ->
          controlByName(window, "EmojiCell0")
          |> Option.iter(fun control ->
            match control with
            | :? Button as button -> click button
            | _ -> ()))

      let! closed =
        waitForAsync(
          5000,
          (fun () -> emojiSearch window |> Option.map effectiveVisible),
          fun visible -> visible <> Some true
        )

      Expect.isTrue closed "the panel stayed open"

      let! panelPath = captureAsync("F13/f13-panel-refused", window)
      let panelBytes = IO.File.ReadAllBytes(panelPath)
      Expect.isTrue (panelBytes.Length > 1000) "the frame saved real pixels"

      let! bodyPath = captureAsync("F13/f13-body-refused", window)
      let bodyBytes = IO.File.ReadAllBytes(bodyPath)
      Expect.isTrue (bodyBytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "a pick leaves the emoji at the caret" {
      let! window = startShell(signedInStore(), logApi() :> IHttp)
      let! _ = showsText(window, "The Octocat")

      do! setComposerSelection(window, "shipped it", 8, 8)
      do! openEmojiPicker window
      do! typeSearch(window, "rocket")

      let! rocket =
        waitForAsync(
          5000,
          (fun () -> cellLabel(window, 0)),
          fun label -> label = Some ":rocket:"
        )

      Expect.isTrue rocket "the rocket result never stood"

      do!
        onUiAsync(fun () ->
          controlByName(window, "EmojiCell0")
          |> Option.iter(fun control ->
            match control with
            | :? Button as button -> click button
            | _ -> ()))

      let! inserted =
        waitForAsync(
          5000,
          (fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.map(fun box -> box.Text, box.CaretIndex)),
          fun state -> state = Some("shipped 🚀it", 10)
        )

      Expect.isTrue inserted "the emoji never landed"

      let! path = captureAsync("F13/f13-body-inserted", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "Escape closes the picker and leaves the body" {
      let! window = startShell(signedInStore(), logApi() :> IHttp)
      let! _ = showsText(window, "The Octocat")

      do! setComposerSelection(window, "kept", 4, 4)
      do! openEmojiPicker window
      do! pressSearch(window, Key.Escape)

      let! closed =
        waitForAsync(
          5000,
          (fun () -> emojiSearch window |> Option.map effectiveVisible),
          fun visible -> visible <> Some true
        )

      Expect.isTrue closed "the panel stayed open"

      let! path = captureAsync("F13/f13-body-cancelled", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the open panel pins the group header when scrolled" {
      let! window = startShell(signedInStore(), logApi() :> IHttp)
      let! _ = showsText(window, "The Octocat")

      do! openEmojiPicker window

      do!
        onUiAsync(fun () ->
          controlByName(window, "EmojiScroller")
          |> Option.iter(fun control ->
            match control with
            | :? ScrollViewer as scroller ->
              scroller.Offset <- Vector(0.0, 2500.0)
            | _ -> ()))

      let! pinned =
        waitForAsync(
          5000,
          (fun () -> textOf(window, "EmojiPinnedHeaderName")),
          fun text -> text = "Animals & nature"
        )

      Expect.isTrue pinned "the pinned header never followed the scroll"

      let! path = captureAsync("F13/f13-panel-scrolled", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the picker stays inside a narrow window" {
      let! window = startShell(signedInStore(), logApi() :> IHttp)
      let! _ = showsText(window, "The Octocat")

      do!
        onUiAsync(fun () ->
          window.MinHeight <- 0.0
          window.Width <- 380.0
          window.Height <- 360.0)

      do! settleAsync()

      do! openEmojiPicker window
      do! settleAsync()

      let! stand =
        probeUiAsync(fun () ->
          let body = textBoxWithWatermark(window, "What did you ship?")

          let room =
            body
            |> Option.bind(fun box ->
              let head: Point option =
                box.TranslatePoint(Point(0.0, 0.0), window)
                |> Option.ofNullable

              let tail: Point option =
                box.TranslatePoint(Point(0.0, box.Bounds.Height), window)
                |> Option.ofNullable

              Option.map2
                (fun (head: Point) (tail: Point) ->
                  head.Y,
                  max 0.0 ((window :> TopLevel).ClientSize.Height - tail.Y))
                head
                tail)

          match room, emojiPopup window, emojiPanelBody window with
          | Some(above, below), Some popup, Some panel ->
            let targeted =
              body
              |> Option.exists(fun box ->
                popup.PlacementTarget
                |> Option.ofObj
                |> Option.exists(fun target ->
                  obj.ReferenceEquals(target, box)))

            Some(
              popup.Placement,
              targeted,
              panel.MaxHeight,
              panel.Bounds.Height,
              above,
              below
            )
          | _ -> None)

      match stand with
      | Some(mode, targeted, maxHeight, boundsHeight, above, below) ->
        Expect.isTrue targeted "the panel never attached to the body"
        Expect.isTrue (maxHeight <= 400.0) "the panel passed the height cap"

        let room =
          match mode with
          | PlacementMode.TopEdgeAlignedRight -> above
          | _ -> below

        Expect.isTrue
          (maxHeight <= room + 0.5)
          "the panel passed the room on its side"

        Expect.isTrue
          (boundsHeight <= maxHeight + 0.5)
          "the panel grew past its cap"
      | None -> failtest "the panel never stood in the window"

      let! path = captureAsync("F13/f13-panel-narrow", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the picker flips above a composer near the window bottom" {
      let! window =
        startShell(signedInStore(), QuoteApi(logApi() :> IHttp) :> IHttp)

      let! _ = showsText(window, "The Octocat")

      do!
        onUiAsync(fun () ->
          controlByName(window, "QuoteButton")
          |> Option.iter(fun control ->
            match control with
            | :? Button as button -> click button
            | _ -> ()))

      let! quote =
        waitForAsync(
          5000,
          (fun () -> visibleBoxWithWatermark(window, "Add your take")),
          Option.isSome
        )

      Expect.isTrue quote "the quote composer never opened"

      do!
        onUiAsync(fun () ->
          window.MinHeight <- 0.0
          window.Width <- 900.0
          window.Height <- 600.0)

      do! settleAsync()
      do! scrollFeedToEndAsync window
      do! settleAsync()
      do! openBottomEmojiPicker window
      do! settleAsync()

      let! stand =
        probeUiAsync(fun () ->
          let body = visibleBoxWithWatermark(window, "Add your take")

          let below =
            body
            |> Option.bind(fun box ->
              box.TranslatePoint(Point(0.0, box.Bounds.Height), window)
              |> Option.ofNullable
              |> Option.map(fun tail ->
                max 0.0 ((window :> TopLevel).ClientSize.Height - tail.Y)))

          match body, below with
          | Some box, Some below ->
            let popup = emojiPopupFor(window, box)

            let panel =
              popup
              |> Option.bind(fun popup ->
                match popup.Child with
                | :? Border as panel -> Some panel
                | _ -> None)

            popup
            |> Option.bind(fun popup ->
              panel
              |> Option.map(fun panel ->
                popup.Placement, panel.MaxHeight, panel.Bounds.Height, below))
          | _ -> None)

      match stand with
      | Some(mode, maxHeight, boundsHeight, below) ->
        Expect.isTrue
          (below < 200.0)
          "the quote body never sat near the window bottom"

        Expect.equal
          mode
          PlacementMode.TopEdgeAlignedRight
          "the panel stayed below the body"

        Expect.isTrue
          (maxHeight >= 200.0)
          "the flipped panel stayed under the usable floor"

        Expect.isTrue
          (boundsHeight >= 200.0)
          "the flipped panel rendered under the usable floor"
      | None -> failtest "the panel never stood"

      let! path = captureAsync("F13/f13-panel-bottom-anchored", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }
  ]
