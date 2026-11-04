module DevNull.Client.UI.Tests.Snapshots

// The camera tests: a feature's visual states saved as PNGs a review
// pass — or the user — can open. The harness session renders through
// Skia, so a capture is the frame a user sees. artifacts/snapshots is
// gitignored: captures are evidence for a review round, never fixtures.

open System
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
  ]
