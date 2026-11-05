module DevNull.Client.UI.Tests.Snapshots

// The camera tests: a feature's visual states saved as PNGs a review
// pass — or the user — can open. The harness session renders through
// Skia, so a capture is the frame a user sees. artifacts/snapshots is
// gitignored: captures are evidence for a review round, never fixtures.

open System
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto

open DevNull.Client.Core.Http
open DevNull.Client.UI.Tests.App
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
  ]
