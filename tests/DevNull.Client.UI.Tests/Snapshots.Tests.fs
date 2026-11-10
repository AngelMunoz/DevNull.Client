module DevNull.Client.UI.Tests.Snapshots

// The camera tests: a feature's visual states saved as PNGs a review
// pass — or the user — can open. The harness session renders through
// Skia, so a capture is the frame a user sees. artifacts/snapshots is
// gitignored: captures are evidence for a review round, never fixtures.

open System
open System.Reactive.Subjects
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Headless
open Avalonia.Input
open Avalonia.VisualTree
open Expecto

open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.Shell
open DevNull.Client.UI.Tests.App
open DevNull.Client.UI.Tests.Composer
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness
open DevNull.Client.UI.Tests.Remotes
open DevNull.Client.UI.Tests.Report
open DevNull.Client.UI.Tests.Share

module CommitCard = DevNull.Client.Components.CommitCard
module CommitMenu = DevNull.Client.Components.CommitMenu

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

/// Opens the signed-in profile over the given api, so a capture of a
/// list page starts from where the reader opens its counts.
let private openProfileAsync(api: IHttp) : Async<Window> = async {
  let! window = startShell(signedInStore(), api)
  let! shown = showsText(window, "The Octocat")
  Expect.isTrue shown "the log never rendered"
  do! clickWhenShown(window, "Profile")
  let! bio = showsText(window, "Builds things.")
  Expect.isTrue bio "the profile never rendered"
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
    elif
      request.Target.StartsWith
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
    then
      ok threadPageJson
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

/// One api that holds a pushed message until the test opens its
/// gate, so the staged bubble can be captured.
type private HeldMessagesApi(inner: IHttp, gate: TaskCompletionSource<unit>) =
  interface IHttp with
    member _.Send(request) = async {
      if request.Method = "POST" && request.Target.EndsWith "/messages" then
        do! gate.Task |> Async.AwaitTask

      return! inner.Send request
    }

    member _.GetBytes(target: string) = inner.GetBytes target

/// One api that holds a candidate search until the test opens its
/// gate, so the picker's quiet loading can be captured.
type private HeldCandidatesApi(inner: IHttp, gate: TaskCompletionSource<unit>) =
  interface IHttp with
    member _.Send(request) = async {
      if request.Target.StartsWith "/api/v1/remotes/candidates" then
        do! gate.Task |> Async.AwaitTask

      return! inner.Send request
    }

    member _.GetBytes(target: string) = inner.GetBytes target

/// The commit the share log carries, decoded through the wire reader.
let private shareCommit() : Commit =
  match LogPage.decode shareLogJson with
  | Ok page ->
    match page.Page.Items with
    | [ LiveCommit commit ] -> commit
    | _ -> failwith "the share log never carried its commit"
  | Error failure -> failwith $"the share log never decoded: {failure.Message}"

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

    testAsync "the organization page carries its follow and claim header" {
      let! window =
        startShell(
          signedInStore(),
          organizationMentionLogApi(organizationMentionsPageJson) :> IHttp
        )

      let! shown = showsText(window, ", it was DNS.")
      Expect.isTrue shown "the log never rendered"
      do! clickWhenShown(window, "@devnull")

      let! opened = showsText(window, "Organization feed")
      Expect.isTrue opened "the organization page never opened"

      let! follow =
        waitForAsync(
          5000,
          (fun () -> buttonWithText(window, "Follow")),
          Option.isSome
        )

      Expect.isTrue follow "the follow button never rendered"
      do! settleAsync()

      let! path = captureAsync("F11/f11-org-follow", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the org thread mentions bell renders" {
      let mentions = [
        orgMessageJson(43, partnerId, "mention one", None)
        orgMessageJson(44, partnerId, "mention two", None)
      ]

      let thread =
        orgThreadAnswerJsonWithMentions(
          [
            orgMessageJson(41, partnerId, "hold this one", None)
            orgMessageJson(42, viewerId, "mine then", None)
            orgMessageJson(43, partnerId, "mention one", None)
            orgMessageJson(44, partnerId, "mention two", None)
          ],
          "maintainer",
          None,
          0,
          mentions
        )

      let api =
        FakeApi(fun request ->
          if
            request.Target.StartsWith "/api/v1/remotes/orgs/org-1/messages"
          then
            ok thread
          elif request.Target.StartsWith "/api/v1/remotes" then
            ok(
              Remotes.inboxPageJson(
                [ orgRowJson("org-1", "the org", 0, "2026-10-21T04:00:00Z") ],
                false,
                0,
                0
              )
            )
          else
            (match accountAnswer request with
             | ValueSome answer -> answer
             | ValueNone -> ok "{}"))

      let! window = startShell(remotesStoreOn(), api :> IHttp)

      do! clickWhenShown(window, "Remotes")

      do!
        (async {
          let! entered =
            waitForAsync(
              5000,
              (fun () -> window),
              (fun root ->
                root.GetVisualDescendants()
                |> Seq.exists(fun visual -> visual.Name = "RemotesSearch"))
            )

          Expect.isTrue entered "the remotes page never rendered"
        })

      do! clickWhenShown(window, "the org")

      let! bell =
        waitForAsync(
          5000,
          (fun () -> controlByName(window, "MentionsBell")),
          Option.isSome
        )

      Expect.isTrue bell "the mentions bell never showed"
      do! settleAsync()

      let! path = captureAsync("F24/f24-org-mentions-bell", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the organization commits tab renders" {
      let! window =
        startShell(
          signedInStore(),
          organizationMentionLogApi(organizationMentionsPageJson) :> IHttp
        )

      let! shown = showsText(window, ", it was DNS.")
      Expect.isTrue shown "the log never rendered"
      do! clickWhenShown(window, "@devnull")

      let! opened = showsText(window, "Organization feed")
      Expect.isTrue opened "the organization page never opened"

      do! clickWhenShown(window, "Commits")

      let! orgCommit = showsText(window, "Org commit.")
      Expect.isTrue orgCommit "the commits tab never loaded"
      do! settleAsync()

      let! path = captureAsync("F23/f23-org-commits-tab", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "an organization authored commit card renders" {
      let api =
        FakeApi(fun request ->
          if request.Target.StartsWith "/api/v1/log/" then
            ok orgAuthorLogPageJson
          else
            (match accountAnswer request with
             | ValueSome answer -> answer
             | ValueNone -> ok "{}"))

      let! window = startShell(signedInStore(), api :> IHttp)
      let! shown = showsText(window, "It was DNS.")
      Expect.isTrue shown "the organization commit never rendered"
      do! settleAsync()

      let! path = captureAsync("F7/f7-org-author-card", window)
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

    /// Reveals the thread's answer composer and scrolls it into view.
    let openAnswerComposerAsync(window: Window) : Async<unit> = async {
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
    }

    testAsync "the answer composer carries the audience line" {
      let! window = openThreadAsync()

      do! openAnswerComposerAsync window

      let! line = seesVisible(window, "@mona")
      Expect.isTrue line "the audience line never showed"

      let! path = captureAsync("F9/f9-reply-audience-line", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testSequenced(
      testAsync "the audience picker opens over the answer composer" {
        let! window = openThreadAsync()

        do! openAnswerComposerAsync window

        do!
          onUiAsync(fun () ->
            controlByName(window, "ReplyingToLine")
            |> Option.iter(fun control ->
              match control with
              | :? Button as button -> click button
              | _ -> ()))

        let! dialog = seesVisible(window, "Commit author")
        Expect.isTrue dialog "the picker dialog never opened"

        let! path = captureAsync("F9/f9-reply-picker-open", window)
        let bytes = IO.File.ReadAllBytes(path)
        Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
      }
    )

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

    testSequenced(
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
                  Point(
                    control.Bounds.Width / 2.0,
                    control.Bounds.Height / 2.0
                  ),
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
    )

    testSequenced(
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
    )

    testSequenced(
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
    )

    testSequenced(
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
    )

    testSequenced(
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
    )

    testSequenced(
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
    )


    testAsync "the approved card offers share" {
      let api =
        shareApi {
          Log = shareLogJson
          Candidates = (fun _ -> candidatesAnswer [])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! openMenuAsync window

      let! offered =
        waitForAsync(
          5000,
          (fun () -> menuRow(window, "Send in Remotes")),
          Option.isSome
        )

      Expect.isTrue offered "the approved card offered no share control"

      let! path = captureAsync("F12/f12-card-approved", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the card hides share with the remotes off" {
      let api =
        shareApi {
          Log = shareLogJson
          Candidates = (fun _ -> candidatesAnswer [])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(signedInStore(), api :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"
      do! openMenuAsync window
      do! settleAsync()

      let! absent =
        probeUiAsync(fun () ->
          menuRow(window, "Send in Remotes") |> Option.isNone)

      Expect.isTrue absent "the card offered share with the remotes off"

      let! path = captureAsync("F12/f12-card-remotes-off", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the card hides share on a commit that is not approved" {
      for status in [ "pending"; "rejected" ] do
        let api =
          shareApi {
            Log =
              shareLogJson.Replace(
                "\"status\":\"approved\"",
                "\"status\":\"" + status + "\""
              )
            Candidates = (fun _ -> candidatesAnswer [])
            Post = (fun _ -> ok "{}")
            Sent = None
          }

        let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown $"the {status} log never rendered"
        do! openMenuAsync window
        do! settleAsync()

        let! absent =
          probeUiAsync(fun () ->
            menuRow(window, "Send in Remotes") |> Option.isNone)

        Expect.isTrue absent $"the {status} card offered share"

        let! path = captureAsync($"F12/f12-card-{status}", window)
        let bytes = IO.File.ReadAllBytes(path)
        Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the own commit offers the same share control" {
      let api =
        shareApi {
          Log = ownLogJson
          Candidates = (fun _ -> candidatesAnswer [])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "My own commit.")
      Expect.isTrue shown "the own log never rendered"

      do! openMenuAsync window

      let! offered =
        waitForAsync(
          5000,
          (fun () -> menuRow(window, "Send in Remotes")),
          Option.isSome
        )

      Expect.isTrue offered "the own commit offered no share control"

      let! path = captureAsync("F12/f12-card-own", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the card keeps the share control in a narrow window" {
      let api =
        shareApi {
          Log = shareLogJson
          Candidates = (fun _ -> candidatesAnswer [])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! onUiAsync(fun () -> window.Width <- 380.0)
      do! settleAsync()

      do! openMenuAsync window

      let! offered =
        waitForAsync(
          5000,
          (fun () -> menuRow(window, "Send in Remotes")),
          Option.isSome
        )

      Expect.isTrue offered "the narrow card offered no share control"

      let! path = captureAsync("F12/f12-card-narrow", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }


    testAsync "a host that offers no share draws no control" {
      let api =
        shareApi {
          Log = shareLogJson
          Candidates = (fun _ -> candidatesAnswer [])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! clickWhenShown(window, "Profile")

      let! profile = showsText(window, "Builds things.")
      Expect.isTrue profile "the profile page never rendered"

      let! commits = showsText(window, "It was DNS.")
      Expect.isTrue commits "the profile feed never rendered its commit"

      do! openMenuAsync window
      do! settleAsync()

      let! absent =
        probeUiAsync(fun () ->
          menuRow(window, "Send in Remotes") |> Option.isNone)

      Expect.isTrue absent "the unwired card offered share"

      let! path = captureAsync("F12/f12-card-unwired", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the dense card carries the share control" {
      let env = TestEnv(signedInStore(), logApi() :> IHttp)
      let commit = shareCommit()

      let now = new BehaviorSubject<DateTimeOffset>(DateTimeOffset.UtcNow)
      let pastels = new BehaviorSubject<bool>(true)
      let choices = new BehaviorSubject<Map<string, string>>(Map.empty)

      let translation =
        new BehaviorSubject<Commit -> CommitCard.TranslationView voption>(fun _ ->
          ValueNone)

      let relations =
        new BehaviorSubject<Commit -> CommitMenu.AuthorRelation voption>(fun _ ->
          ValueNone)

      let busy = new BehaviorSubject<string option>(None)

      let menu: CommitMenu.MenuSlot voption =
        ValueSome {
          Share = ValueSome ignore
          Report = ValueNone
          Delete = ValueNone
          ToggleFollow = ignore
          ToggleMute = ignore
          Block = ignore
          Relations = relations :> IObservable<_>
          Busy = busy :> IObservable<_>
        }

      let props: CommitCard.RowProps = {
        OnEvent = ignore
        ViewerId = (Author.id commit.Author)
        Pasteis = pastels
        Now = now
        PollChoices = choices
        Dense = true
        Images = (env :> IProvideImageCache).ImageCache
        Flyout = ValueNone
        Pin = ValueNone
        Star = ValueNone
        Vote = ValueNone
        Pastel = ValueNone
        Menu = menu
        Translation = translation
        Zone = zoneOf(None: UserSettings option)
      }

      let! window =
        probeUiAsync(fun () ->
          let window =
            Window(
              Content = CommitCard.feedRow props (LiveCommit commit),
              Width = 720.0,
              Height = 480.0
            )

          window.Show()
          window)

      do! settleAsync()

      let! trigger =
        probeUiAsync(fun () ->
          controlByName(window, "CommitMenuButton") |> Option.isSome)

      Expect.isTrue trigger "the dense card offered no menu trigger"

      let! path = captureAsync("F12/f12-card-dense", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the picker opens empty and lists the reachable first" {
      let api =
        shareApi {
          Log = shareLogJson
          Candidates =
            (fun _ ->
              candidatesAnswer [
                candidateJson(bobJson, Some "doesn't follow you")
                candidateJson(hubotJson, None)
              ])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! openSharePickerAsync window

      let! emptyPath = captureAsync("F12/f12-picker-empty", window)
      let emptyBytes = IO.File.ReadAllBytes(emptyPath)
      Expect.isTrue (emptyBytes.Length > 1000) "the frame saved real pixels"

      do! typeShareTermAsync(window, "hub")

      let! rows =
        waitForAsync(
          5000,
          (fun () -> controlByName(window, "ShareCandidate1")),
          Option.isSome
        )

      Expect.isTrue rows "the mixed rows never listed"

      let! path = captureAsync("F12/f12-picker-rows", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }


    testAsync "the picker shows its quiet loading" {
      let gate = TaskCompletionSource<unit>()

      let inner =
        shareApi {
          Log = shareLogJson
          Candidates =
            (fun _ -> candidatesAnswer [ candidateJson(hubotJson, None) ])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let held = HeldCandidatesApi(inner :> IHttp, gate)
      let! window, _ = startShareShell(remotesStoreOn(), held :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! openSharePickerAsync window
      do! typeShareTermAsync(window, "hub")

      let! loading =
        waitForAsync(
          5000,
          (fun () -> controlByName(window, "ShareLoading")),
          fun control -> control |> Option.exists effectiveVisible
        )

      Expect.isTrue loading "the picker showed no quiet loading"

      let! path = captureAsync("F12/f12-picker-loading", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      gate.SetResult(())
      do! settleAsync()
    }

    testAsync "the picker trims a long developer row" {
      let longUser =
        userJsonOf(
          "01a151db-cccc-727b-a7ce-c911a7556243",
          "a-very-long-handle-that-fills-the-row",
          "A Developer Whose Name Runs Past The Row"
        )

      let api =
        shareApi {
          Log = shareLogJson
          Candidates =
            (fun _ ->
              candidatesAnswer [
                candidateJson(
                  longUser,
                  Some
                    "your request is waiting to be merged after the long name in this row"
                )
              ])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! openSharePickerAsync window
      do! typeShareTermAsync(window, "long")

      let! rows =
        waitForAsync(
          5000,
          (fun () -> controlByName(window, "ShareCandidate0")),
          Option.isSome
        )

      Expect.isTrue rows "the long row never listed"

      let! path = captureAsync("F12/f12-picker-long", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the picker shows a quiet empty answer" {
      let answered = TaskCompletionSource<unit>()

      let api =
        shareApi {
          Log = shareLogJson
          Candidates =
            (fun _ ->
              answered.TrySetResult(()) |> ignore
              candidatesAnswer [])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! openSharePickerAsync window
      do! typeShareTermAsync(window, "nobody")
      do! Async.AwaitTask answered.Task
      do! settleAsync()

      let! quiet =
        waitForAsync(
          5000,
          (fun () -> controlByName(window, "ShareLoading")),
          fun control ->
            control |> Option.exists(fun visual -> not(effectiveVisible visual))
        )

      Expect.isTrue quiet "the empty answer stayed loading"

      let! rows =
        probeUiAsync(fun () -> controlByName(window, "ShareCandidate0"))

      Expect.isNone rows "the empty answer listed a row"

      let! path = captureAsync("F12/f12-picker-none", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the picker shows a failed read" {
      let api =
        shareApi {
          Log = shareLogJson
          Candidates =
            (fun request ->
              if request.Target.Contains "q=hub" then
                candidatesAnswer [ candidateJson(hubotJson, None) ]
              else
                refused(500, "the server said no"))
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! openSharePickerAsync window
      do! typeShareTermAsync(window, "hub")

      let! loaded =
        waitForAsync(
          5000,
          (fun () -> controlByName(window, "ShareCandidate0")),
          Option.isSome
        )

      Expect.isTrue loaded "the picker never listed the first answer"

      do! typeShareTermAsync(window, "other")

      let! failure = showsWords(window, "the server said no")
      Expect.isTrue failure "the picker hid the refusal"

      let! gone =
        probeUiAsync(fun () -> controlByName(window, "ShareCandidate0"))

      Expect.isNone gone "the refused read kept the last rows"

      let! term =
        probeUiAsync(fun () ->
          controlByName(window, "ShareSearch")
          |> Option.bind(fun control ->
            (control :?> TextBox).Text |> Option.ofObj))

      Expect.equal term (Some "other") "the refused read cleared the input"

      let! path = captureAsync("F12/f12-picker-failed", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the picker narrows with the window" {
      let api =
        shareApi {
          Log = shareLogJson
          Candidates =
            (fun _ ->
              candidatesAnswer [
                candidateJson(bobJson, Some "doesn't follow you")
                candidateJson(hubotJson, None)
              ])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! openSharePickerAsync window
      do! typeShareTermAsync(window, "hub")

      let! rows =
        waitForAsync(
          5000,
          (fun () -> controlByName(window, "ShareCandidate1")),
          Option.isSome
        )

      Expect.isTrue rows "the mixed rows never listed"

      do! onUiAsync(fun () -> window.Width <- 380.0)
      do! settleAsync()

      let! path = captureAsync("F12/f12-picker-narrow", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the confirm step names the recipient" {
      let api =
        shareApi {
          Log = shareLogJson
          Candidates =
            (fun _ -> candidatesAnswer [ candidateJson(hubotJson, None) ])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! openSharePickerAsync window
      do! typeShareTermAsync(window, "hub")
      do! pickShareCandidateAsync(window, 0)

      let! titled = showsText(window, "Send commit to @hubot?")
      Expect.isTrue titled "the confirm step never named the recipient"

      let! path = captureAsync("F12/f12-confirm", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the confirm step caps a long preview" {
      let longBody = String.replicate 8 "A long sentence about the commit. "

      let api =
        shareApi {
          Log =
            shareLogJson.Replace(
              "\"body\":\"It was DNS.\"",
              "\"body\":\"" + longBody + "\""
            )
          Candidates =
            (fun _ -> candidatesAnswer [ candidateJson(hubotJson, None) ])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the long log never rendered"

      do! openSharePickerAsync window
      do! typeShareTermAsync(window, "hub")
      do! pickShareCandidateAsync(window, 0)

      let! titled = showsText(window, "Send commit to @hubot?")
      Expect.isTrue titled "the confirm step never named the recipient"

      let! path = captureAsync("F12/f12-confirm-long", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the confirm step keeps its buttons in a narrow window" {
      let api =
        shareApi {
          Log = shareLogJson
          Candidates =
            (fun _ -> candidatesAnswer [ candidateJson(hubotJson, None) ])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! openSharePickerAsync window
      do! typeShareTermAsync(window, "hub")
      do! pickShareCandidateAsync(window, 0)

      let! titled = showsText(window, "Send commit to @hubot?")
      Expect.isTrue titled "the confirm step never named the recipient"

      do! onUiAsync(fun () -> window.Width <- 380.0)
      do! settleAsync()

      let! send =
        waitForAsync(
          5000,
          (fun () -> buttonWithText(window, "Send")),
          Option.isSome
        )

      Expect.isTrue send "the narrow confirm lost its Send"

      let! path = captureAsync("F12/f12-confirm-narrow", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the delivered share shows the link in the thread" {
      let api =
        shareApi {
          Log = shareLogJson
          Candidates =
            (fun _ -> candidatesAnswer [ candidateJson(hubotJson, None) ])
          Post = (fun request -> sharedPushAnswer(request, shareUrl))
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! openSharePickerAsync window
      do! typeShareTermAsync(window, "hub")
      do! pickShareCandidateAsync(window, 0)
      do! clickWhenShown(window, "Send")

      let! landed = showsText(window, shareUrl)
      Expect.isTrue landed "the delivered thread never showed the link"

      let! delivered = seesVisible(window, "pushed")
      Expect.isTrue delivered "the delivered thread kept its staged mark"

      let! path = captureAsync("F12/f12-thread-delivered", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the refused share shows the rejected bubble" {
      let api =
        shareApi {
          Log = shareLogJson
          Candidates =
            (fun _ -> candidatesAnswer [ candidateJson(hubotJson, None) ])
          Post = (fun _ -> refused(403, "the server said no"))
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! openSharePickerAsync window
      do! typeShareTermAsync(window, "hub")
      do! pickShareCandidateAsync(window, 0)
      do! clickWhenShown(window, "Send")

      let! rejected = showsText(window, shareUrl)
      Expect.isTrue rejected "the refused thread never showed the link"

      let! marked = seesVisible(window, "rejected · retry")
      Expect.isTrue marked "the refused thread kept its staged mark"

      let! path = captureAsync("F12/f12-thread-refused", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the staged share shows the link before the server answers" {
      let gate = TaskCompletionSource<unit>()

      let inner =
        shareApi {
          Log = shareLogJson
          Candidates =
            (fun _ -> candidatesAnswer [ candidateJson(hubotJson, None) ])
          Post = (fun request -> sharedPushAnswer(request, shareUrl))
          Sent = None
        }

      let held = HeldMessagesApi(inner :> IHttp, gate)
      let! window, _ = startShareShell(remotesStoreOn(), held :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! openSharePickerAsync window
      do! typeShareTermAsync(window, "hub")
      do! pickShareCandidateAsync(window, 0)
      do! clickWhenShown(window, "Send")

      let! staged = showsText(window, shareUrl)
      Expect.isTrue staged "the staged thread never showed the link"

      let! marked = seesVisible(window, "staged")
      Expect.isTrue marked "the thread did not mark the bubble staged"

      let! path = captureAsync("F12/f12-thread-staged", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      gate.SetResult(())
      do! settleAsync()
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

    let startReportShell
      (anchor: string, api: IHttp, attachments: FakeAttachments)
      : Async<Window> = async {
      let env = TestEnv(signedInStore(), api, attachments = attachments)
      let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
      let! shown = showAsync window
      do! onUiAsync(fun () -> shown.Height <- 900.0)
      do! settleAsync()
      let! _ = showsText(window, anchor)
      return shown
    }

    let scrollReportToEndAsync(window: Window) : Async<unit> = async {
      do!
        onUiAsync(fun () ->
          controlByName(window, "ReportScroller")
          |> Option.iter(function
            | :? ScrollViewer as scroller -> scroller.ScrollToEnd()
            | _ -> ()))

      do! settleAsync()
    }

    testAsync "the report control rides another author's approved card" {
      let api = reportApi(fun _ -> noContent)

      let! window =
        startReportShell("The Hubot", api :> IHttp, FakeAttachments())

      do! openMenuAsync window

      let! offered =
        waitForAsync(
          5000,
          (fun () -> menuRow(window, "Report commit")),
          Option.isSome
        )

      Expect.isTrue offered "the card offered no report control"

      let! path = captureAsync("F10/f10-card-report", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "the own card carries no report control" {
      let api = reportApiWith(ownCommitLog, fun _ -> noContent)

      let! window =
        startReportShell("The Octocat", api :> IHttp, FakeAttachments())

      do! openMenuAsync window
      do! settleAsync()

      let! absent =
        probeUiAsync(fun () ->
          menuRow(window, "Report commit") |> Option.isNone)

      Expect.isTrue absent "the own card offered report"

      let! path = captureAsync("F10/f10-card-own", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "a host without the slot draws no report control" {
      let api = reportApi(fun _ -> noContent)

      let! window =
        startReportShell("The Hubot", api :> IHttp, FakeAttachments())

      do! clickWhenShown(window, "Profile")

      let! profile = showsText(window, "Builds things.")
      Expect.isTrue profile "the profile page never rendered"

      let! commits = showsText(window, "It was DNS.")
      Expect.isTrue commits "the profile feed never rendered its commit"

      do! openMenuAsync window
      do! settleAsync()

      let! absent =
        probeUiAsync(fun () ->
          menuRow(window, "Report commit") |> Option.isNone)

      Expect.isTrue absent "the unwired profile card offered report"

      let! path = captureAsync("F10/f10-card-unwired", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "the dense card carries the report control" {
      let env = TestEnv(signedInStore(), logApi() :> IHttp)
      let commit = shareCommit()

      let now = new BehaviorSubject<DateTimeOffset>(DateTimeOffset.UtcNow)
      let pastels = new BehaviorSubject<bool>(true)
      let choices = new BehaviorSubject<Map<string, string>>(Map.empty)

      let translation =
        new BehaviorSubject<Commit -> CommitCard.TranslationView voption>(fun _ ->
          ValueNone)

      let relations =
        new BehaviorSubject<Commit -> CommitMenu.AuthorRelation voption>(fun _ ->
          ValueNone)

      let busy = new BehaviorSubject<string option>(None)

      let menu: CommitMenu.MenuSlot voption =
        ValueSome {
          Share = ValueNone
          Report = ValueSome ignore
          Delete = ValueNone
          ToggleFollow = ignore
          ToggleMute = ignore
          Block = ignore
          Relations = relations :> IObservable<_>
          Busy = busy :> IObservable<_>
        }

      let props: CommitCard.RowProps = {
        OnEvent = ignore
        ViewerId = viewerId
        Pasteis = pastels
        Now = now
        PollChoices = choices
        Dense = true
        Images = (env :> IProvideImageCache).ImageCache
        Flyout = ValueNone
        Pin = ValueNone
        Star = ValueNone
        Vote = ValueNone
        Pastel = ValueNone
        Menu = menu
        Translation = translation
        Zone = zoneOf(None: UserSettings option)
      }

      let! window =
        probeUiAsync(fun () ->
          let window =
            Window(
              Content = CommitCard.feedRow props (LiveCommit commit),
              Width = 720.0,
              Height = 480.0
            )

          window.Show()
          window)

      do! settleAsync()

      let! trigger =
        probeUiAsync(fun () ->
          controlByName(window, "CommitMenuButton") |> Option.isSome)

      Expect.isTrue trigger "the dense card offered no menu trigger"

      let! path = captureAsync("F10/f10-card-dense", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the report dialog opens with no reason chosen" {
      let api = reportApi(fun _ -> noContent)

      let! window =
        startReportShell("The Hubot", api :> IHttp, FakeAttachments())

      do! openReportDialogAsync window

      let! titled = showsText(window, "Report commit")
      Expect.isTrue titled "the dialog never titled itself"

      let! disabled =
        probeUiAsync(fun () ->
          sendButton window
          |> Option.exists(fun control -> not control.IsEnabled))

      Expect.isTrue disabled "Send report offered itself before a reason"

      let! path = captureAsync("F10/f10-dialog-open", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "a chosen reason highlights its row and arms the send" {
      let api = reportApi(fun _ -> noContent)

      let! window =
        startReportShell("The Hubot", api :> IHttp, FakeAttachments())

      do! openReportDialogAsync window
      do! chooseReasonAsync(window, "ReportReasonHarassment")

      let! armed =
        waitForAsync(
          5000,
          (fun () -> sendButton window),
          fun control -> control |> Option.exists _.IsEnabled
        )

      Expect.isTrue armed "the send stayed off after a reason"

      let! path = captureAsync("F10/f10-dialog-reason", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "the details box caps at five hundred characters" {
      let api = reportApi(fun _ -> noContent)

      let! window =
        startReportShell("The Hubot", api :> IHttp, FakeAttachments())

      do! openReportDialogAsync window
      do! typeDetailsAsync(window, String.replicate 500 "x")

      let! cap =
        probeUiAsync(fun () ->
          controlByName(window, "ReportDetails")
          |> Option.map(fun control ->
            let box = control :?> TextBox

            box.MaxLength, (box.Text |> Option.ofObj |> Option.map _.Length)))

      Expect.equal
        cap
        (Some(500, Some 500))
        "the details box did not cap at five hundred"

      let! path = captureAsync("F10/f10-dialog-details-cap", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "one picked image shows its chip" {
      let api = reportApi(fun _ -> noContent)
      let attachments = FakeAttachments()

      attachments.PickResponse <- [ opaqueStagedImage "one.png" ]

      let! window = startReportShell("The Hubot", api :> IHttp, attachments)

      do! openReportDialogAsync window
      do! attachAsync window

      let! staged =
        waitForAsync(
          5000,
          (fun () -> window),
          (fun root -> hasText(root, "one.png"))
        )

      Expect.isTrue staged "the picked image never staged"

      do! scrollReportToEndAsync window

      let! path = captureAsync("F10/f10-dialog-images-1", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "three picked images fill the strip and disable attach" {
      let api = reportApi(fun _ -> noContent)
      let attachments = FakeAttachments()

      attachments.PickResponse <- [
        opaqueStagedImage "one.png"
        opaqueStagedImage "two.png"
        opaqueStagedImage "three.png"
      ]

      let! window = startReportShell("The Hubot", api :> IHttp, attachments)

      do! openReportDialogAsync window
      do! attachAsync window

      let! staged =
        waitForAsync(
          5000,
          (fun () -> controlByName(window, "ImageDescription2")),
          Option.isSome
        )

      Expect.isTrue staged "the third image never staged"

      let! disabled =
        probeUiAsync(fun () ->
          controlByName(window, "ReportAttachButton")
          |> Option.exists(fun control -> not control.IsEnabled))

      Expect.isTrue disabled "attach stayed on with three images"

      do! scrollReportToEndAsync window

      let! path = captureAsync("F10/f10-dialog-images-3", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "a described chip counts its characters" {
      let api = reportApi(fun _ -> noContent)
      let attachments = FakeAttachments()

      attachments.PickResponse <- [ opaqueStagedImage "one.png" ]

      let! window = startReportShell("The Hubot", api :> IHttp, attachments)

      do! openReportDialogAsync window
      do! attachAsync window

      let! staged =
        waitForAsync(
          5000,
          (fun () -> window),
          (fun root -> hasText(root, "one.png"))
        )

      Expect.isTrue staged "the picked image never staged"

      do!
        onUiAsync(fun () ->
          controlByName(window, "ImageDescription0")
          |> Option.iter(fun control ->
            (control :?> TextBox).Text <- "The error page"))

      let! counted =
        waitForAsync(
          5000,
          (fun () -> window),
          (fun root -> hasText(root, "14/1000"))
        )

      Expect.isTrue counted "the description counter never counted"

      do! scrollReportToEndAsync window

      let! path = captureAsync("F10/f10-dialog-described", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "a refused pick shows its line over an empty stage" {
      let api = reportApi(fun _ -> noContent)
      let attachments = FakeAttachments()

      attachments.PickResponse <- [
        {
          FileName = "notes.txt"
          Bytes = [| 1uy; 2uy; 3uy |]
        }
      ]

      let! window = startReportShell("The Hubot", api :> IHttp, attachments)

      do! openReportDialogAsync window
      do! attachAsync window

      let! refused =
        showsText(window, "notes.txt is not a JPEG, PNG, or WebP image.")

      Expect.isTrue refused "the bad pick said nothing"

      let! path = captureAsync("F10/f10-dialog-refused", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "sending says so and disables the form" {
      let gate = TaskCompletionSource<unit>()
      let inner = reportApi(fun _ -> noContent)
      let held = HeldReportApi(inner :> IHttp, gate)

      let! window =
        startReportShell("The Hubot", held :> IHttp, FakeAttachments())

      do! openReportDialogAsync window
      do! chooseReasonAsync(window, "ReportReasonSpam")
      do! clickSendAsync window

      let! sending =
        waitForAsync(
          5000,
          (fun () -> buttonWithText(window, "Sending…")),
          Option.isSome
        )

      Expect.isTrue sending "the send never said it was sending"

      let! disabled =
        probeUiAsync(fun () ->
          controlByName(window, "ReportCancel")
          |> Option.exists(fun control -> not control.IsEnabled))

      Expect.isTrue disabled "Cancel stayed on while the report sent"

      let! path = captureAsync("F10/f10-dialog-sending", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      gate.SetResult(())
      do! settleAsync()
      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "a rate-limited report asks for a minute" {
      let api = reportApi(fun _ -> refused(429, "Too Many Attempts."))

      let! window =
        startReportShell("The Hubot", api :> IHttp, FakeAttachments())

      do! openReportDialogAsync window
      do! chooseReasonAsync(window, "ReportReasonSpam")
      do! clickSendAsync window

      let! limited =
        showsText(
          window,
          "Too many reports. Please wait a minute and try again."
        )

      Expect.isTrue limited "the rate limit said nothing"

      let! path = captureAsync("F10/f10-dialog-rate-limited", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "a plain refusal says the commit is no longer available" {
      let api = reportApi(fun _ -> refused(500, "the server said no"))

      let! window =
        startReportShell("The Hubot", api :> IHttp, FakeAttachments())

      do! openReportDialogAsync window
      do! chooseReasonAsync(window, "ReportReasonUnsafe")
      do! clickSendAsync window

      let! refusedLine =
        showsText(window, "This commit is no longer available to report.")

      Expect.isTrue refusedLine "the refusal said nothing"

      let! path = captureAsync("F10/f10-dialog-unavailable", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "a lost connection shows the offline line" {
      let api = reportApi(fun _ -> noContent)
      let offline = OfflineReportApi(api :> IHttp)

      let! window =
        startReportShell("The Hubot", offline :> IHttp, FakeAttachments())

      do! openReportDialogAsync window
      do! chooseReasonAsync(window, "ReportReasonSpam")
      do! clickSendAsync window

      let! line =
        showsText(
          window,
          "Could not send your report. Check your connection and try again."
        )

      Expect.isTrue line "the lost connection said nothing"

      let! path = captureAsync("F10/f10-dialog-offline", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "a vanished commit closes the dialog silently" {
      let api = reportApi(fun _ -> refused(404, "This commit isn't available."))

      let! window =
        startReportShell("The Hubot", api :> IHttp, FakeAttachments())

      do! openReportDialogAsync window
      do! chooseReasonAsync(window, "ReportReasonSpam")
      do! clickSendAsync window

      let! closed =
        waitForAsync(
          8000,
          (fun () -> controlByName(window, "ReportDialog")),
          Option.isNone
        )

      Expect.isTrue closed "the vanished commit left the dialog open"

      let! path = captureAsync("F10/f10-dialog-404-closed", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "a sent report closes the dialog silently" {
      let api = reportApi(fun _ -> noContent)

      let! window =
        startReportShell("The Hubot", api :> IHttp, FakeAttachments())

      do! openReportDialogAsync window
      do! chooseReasonAsync(window, "ReportReasonSpam")
      do! clickSendAsync window

      let! closed =
        waitForAsync(
          8000,
          (fun () -> controlByName(window, "ReportDialog")),
          Option.isNone
        )

      Expect.isTrue closed "the sent report left the dialog open"

      let! path = captureAsync("F10/f10-dialog-sent", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "a short narrow window scrolls the report body" {
      let api = reportApi(fun _ -> noContent)
      let attachments = FakeAttachments()

      attachments.PickResponse <- [
        opaqueStagedImage "one.png"
        opaqueStagedImage "two.png"
        opaqueStagedImage "three.png"
      ]

      let! window = startReportShell("The Hubot", api :> IHttp, attachments)

      do! openReportDialogAsync window
      do! attachAsync window

      let! staged =
        waitForAsync(
          5000,
          (fun () -> controlByName(window, "ImageDescription2")),
          Option.isSome
        )

      Expect.isTrue staged "the third image never staged"

      for index in 0..2 do
        do!
          onUiAsync(fun () ->
            controlByName(window, $"ImageDescription{index}")
            |> Option.iter(fun control ->
              (control :?> TextBox).Text <-
                String.replicate 30 "A long description "))

      do!
        onUiAsync(fun () ->
          window.MinHeight <- 0.0
          window.Width <- 380.0
          window.Height <- 640.0)

      do! settleAsync()

      let! scrolls =
        probeUiAsync(fun () ->
          controlByName(window, "ReportScroller")
          |> Option.bind(fun control ->
            match control with
            | :? ScrollViewer as scroller ->
              Some(scroller.Extent.Height > scroller.Viewport.Height)
            | _ -> None))

      Expect.equal
        scrolls
        (Some true)
        "the short window did not scroll the body"

      do! scrollReportToEndAsync window

      let! path = captureAsync("F10/f10-dialog-short", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    // F1 — the commit card's More menu. The popup layer roots its
    // content under the popup root, which never reaches the window's
    // captured frame, so the open-menu and ask captures mount the
    // popup's content control directly in a plain window.

    /// A hand-built slot for the menu captures, with the relation the
    /// capture wants and nothing in flight.
    let menuSlot
      (share: bool, report: bool, delete: bool, relation: bool)
      : CommitMenu.MenuSlot =
      let relations =
        new BehaviorSubject<Commit -> CommitMenu.AuthorRelation voption>(fun _ ->
          if relation then
            ValueSome {
              IsFollowing = false
              IsMuting = false
              IsBlocking = false
            }
          else
            ValueNone)

      let busy = new BehaviorSubject<string option>(None)

      {
        Share = (if share then ValueSome ignore else ValueNone)
        Report = (if report then ValueSome ignore else ValueNone)
        Delete = (if delete then ValueSome ignore else ValueNone)
        ToggleFollow = ignore
        ToggleMute = ignore
        Block = ignore
        Relations = relations :> IObservable<_>
        Busy = busy :> IObservable<_>
      }

    /// Builds the menu over the given commit in memory and lifts the
    /// popup's card out before anything attaches: the popup layer
    /// never reaches the window's captured frame, so the card mounts
    /// on its own for the capture.
    let menuCardOf(slot: CommitMenu.MenuSlot, commit: Commit) : Control =
      let menu =
        CommitMenu.view {
          Commit = commit
          ViewerId = viewerId
          Slot = slot
          OnOpened = ignore
          CopyLink = ignore
        }

      let flyout =
        menu.GetVisualDescendants()
        |> Seq.tryPick(fun visual ->
          match visual with
          | :? Popup as popup when popup.Name = "CommitMenuFlyout" ->
            Some popup
          | _ -> None)

      match flyout with
      | Some popup ->
        let card = popup.Child
        popup.Child <- null
        card
      | None -> failwith "the menu never carried its flyout"

    /// The canned stranger commit.
    let strangerCommit() : Commit =
      match LogPage.decode hubotLog with
      | Ok page ->
        match page.Page.Items with
        | [ LiveCommit commit ] -> commit
        | _ -> failwith "the hubot log never carried its commit"
      | Error failure ->
        failwith $"the hubot log never decoded: {failure.Message}"

    /// The canned viewer's own commit.
    let ownCommit() : Commit =
      match LogPage.decode ownLogJson with
      | Ok page ->
        page.Page.Items
        |> List.tryPick(function
          | LiveCommit commit when (Author.id commit.Author) = viewerId ->
            Some commit
          | _ -> None)
        |> Option.defaultWith(fun () ->
          failwith "the own log never carried its commit")
      | Error failure ->
        failwith $"the own log never decoded: {failure.Message}"

    /// Mounts the menu card in its own window.
    let mountMenuCard
      (slot: CommitMenu.MenuSlot, commit: Commit)
      : Async<Window> = async {
      let! card = probeUiAsync(fun () -> menuCardOf(slot, commit))
      return! mountAsync card
    }

    /// Lifts the one open destructive ask out of the mounted menu card
    /// and mounts it on its own. The popup closes first, so its child
    /// lets go of the popup before it re-parents.
    let mountAskAsync(menuCardWindow: Window) : Async<Window> = async {
      let! ask =
        probeUiAsync(fun () ->
          menuCardWindow.GetVisualDescendants()
          |> Seq.tryPick(fun visual ->
            match visual with
            | :? Popup as popup when
              popup.Name = "ConfirmFlyout" && popup.IsOpen
              ->
              popup.IsOpen <- false
              let card = popup.Child
              popup.Child <- null
              Some card
            | _ -> None))

      Expect.isSome ask "the open ask never carried its card"

      return! mountAsync ask.Value
    }

    testAsync "the own card's toolbar carries the More trigger" {
      let api =
        shareApi {
          Log = ownLogJson
          Candidates = (fun _ -> candidatesAnswer [])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "My own commit.")
      Expect.isTrue shown "the log never rendered"
      do! settleAsync()

      let! path = captureAsync("F1/f1-card-own-menu-closed", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "the stranger card's toolbar carries the More trigger" {
      let api =
        shareApi {
          Log = shareLogJson
          Candidates = (fun _ -> candidatesAnswer [])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "It was DNS.")
      Expect.isTrue shown "the log never rendered"
      do! settleAsync()

      let! path = captureAsync("F1/f1-card-stranger-menu-closed", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "the open menu over a stranger card, relation loaded" {
      let! cardWindow =
        mountMenuCard(menuSlot(true, true, false, true), strangerCommit())

      do! settleAsync()

      let! path = captureAsync("F1/f1-menu-open-stranger", cardWindow)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> cardWindow.Close())
    }

    testAsync "the open menu over an own card" {
      let! cardWindow =
        mountMenuCard(menuSlot(false, false, true, false), ownCommit())

      do! settleAsync()

      let! path = captureAsync("F1/f1-menu-open-own", cardWindow)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> cardWindow.Close())
    }

    testAsync "the block ask" {
      let! cardWindow =
        mountMenuCard(menuSlot(false, true, false, true), strangerCommit())

      do! settleAsync()

      do!
        onUiAsync(fun () ->
          buttonWithText(cardWindow, "Block @hubot") |> Option.iter click)

      let! askWindow = mountAskAsync cardWindow
      do! settleAsync()

      let! path = captureAsync("F1/f1-menu-block-confirm", askWindow)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do!
        onUiAsync(fun () ->
          askWindow.Close()
          cardWindow.Close())
    }

    testAsync "the delete ask" {
      let! cardWindow =
        mountMenuCard(menuSlot(false, false, true, false), ownCommit())

      do! settleAsync()

      do!
        onUiAsync(fun () ->
          buttonWithText(cardWindow, "Delete commit") |> Option.iter click)

      let! askWindow = mountAskAsync cardWindow
      do! settleAsync()

      let! path = captureAsync("F1/f1-menu-delete-confirm", askWindow)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do!
        onUiAsync(fun () ->
          askWindow.Close()
          cardWindow.Close())
    }

    testAsync "a pending commit's card" {
      let api =
        shareApi {
          Log =
            shareLogJson.Replace(
              "\"status\":\"approved\"",
              "\"status\":\"pending\""
            )
          Candidates = (fun _ -> candidatesAnswer [])
          Post = (fun _ -> ok "{}")
          Sent = None
        }

      let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
      let! shown = showsText(window, "It was DNS.")
      Expect.isTrue shown "the log never rendered"
      do! settleAsync()

      let! path = captureAsync("F1/f1-card-pending", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"

      do! onUiAsync(fun () -> window.Close())
    }

    testAsync "the profile header stands full in a tall window" {
      let! window = startShell(signedInStore(), statsProfileApi() :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! onUiAsync(fun () -> window.Height <- 900.0)
      do! settleAsync()

      do! clickWhenShown(window, "Profile")

      let! caption = seesVisible(window, "activity · week")
      Expect.isTrue caption "the full header never rendered"

      let! feed = showsText(window, "It was DNS.")
      Expect.isTrue feed "the feed never rendered"

      do! settleAsync()

      let! path = captureAsync("F17/f17-profile-tall-full", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the profile keeps most of a short window" {
      let! window = startShell(signedInStore(), statsProfileApi() :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do!
        onUiAsync(fun () ->
          window.MinHeight <- 0.0
          window.Height <- 480.0)

      do! settleAsync()

      do! clickWhenShown(window, "Profile")

      let! feed = showsText(window, "It was DNS.")
      Expect.isTrue feed "the feed never rendered"

      let! folded =
        waitForAsync(
          5000,
          (fun () -> window),
          (fun root -> not(visibleText(root, "activity · week")))
        )

      Expect.isTrue folded "the header never folded in the short window"

      do! settleAsync()

      let! path = captureAsync("F17/f17-short-compact", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the reader pins the full header in a short window" {
      let! window = startShell(signedInStore(), statsProfileApi() :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do!
        onUiAsync(fun () ->
          window.MinHeight <- 0.0
          window.Height <- 480.0)

      do! settleAsync()

      do! clickWhenShown(window, "Profile")

      let! feed = showsText(window, "It was DNS.")
      Expect.isTrue feed "the feed never rendered"

      let! folded =
        waitForAsync(
          5000,
          (fun () -> window),
          (fun root -> not(visibleText(root, "activity · week")))
        )

      Expect.isTrue folded "the header never folded in the short window"

      do! clickVisibleHeaderToggle(window)

      let! pinned = seesVisible(window, "activity · week")
      Expect.isTrue pinned "the header never unfolded"

      do! settleAsync()

      let! path = captureAsync("F17/f17-short-pinned-full", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "a scrolled profile rides under the compact bar" {
      let! window = startShell(signedInStore(), longProfileApi() :> IHttp)
      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! onUiAsync(fun () -> window.Height <- 900.0)
      do! settleAsync()

      do! clickWhenShown(window, "Profile")

      let! feed = showsText(window, "It was DNS.")
      Expect.isTrue feed "the feed never rendered"

      do! scrollFeedToEndAsync window

      let! folded =
        waitForAsync(
          5000,
          (fun () -> window),
          (fun root -> not(visibleText(root, "activity · week")))
        )

      Expect.isTrue folded "the scroll never folded the header"

      do! settleAsync()

      let! path = captureAsync("F17/f17-scrolled-compact", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the following list stands with its organizations" {
      let! window = openProfileAsync(connectionsApi() :> IHttp)

      do! clickVisibleNamed(window, "ProfileFollowingCount:octocat")

      let! back = showsText(window, "Follow back")
      Expect.isTrue back "the following list never rendered"

      let! orgs = showsText(window, "DevNull")
      Expect.isTrue orgs "the organizations never rendered"

      let! people = seesVisible(window, "People")
      Expect.isTrue people "the people heading never rendered"

      do! settleAsync()

      let! path = captureAsync("F4/f4-following", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "Show more organizations lands the second page" {
      let! window = openProfileAsync(connectionsApi() :> IHttp)

      do! clickVisibleNamed(window, "ProfileFollowingCount:octocat")

      let! shown = showsText(window, "DevNull")
      Expect.isTrue shown "the organizations never rendered"

      do! clickWhenShown(window, "Show more organizations")

      let! second = showsText(window, "The Octopus")
      Expect.isTrue second "the second page never landed"

      do! settleAsync()

      let! path = captureAsync("F4/f4-following-more-orgs", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "another user's followers carry the chip" {
      let! window = openProfileAsync(connectionsApi() :> IHttp)

      // dora's follower list: a row that follows the viewer shows the
      // chip there, where the viewer's own list would hide it.
      do! clickVisibleNamed(window, "ProfileFollowingCount:octocat")

      let! rows = showsText(window, "Dora Default")
      Expect.isTrue rows "the following rows never rendered"

      do! clickVisibleNamed(window, "OpenConnection:dora")

      let! dora = showsText(window, "@dora")
      Expect.isTrue dora "dora's profile never opened"

      do! clickVisibleNamed(window, "ProfileFollowersCount:dora")

      let! chip = seesVisible(window, "Follows you")
      Expect.isTrue chip "the chip never showed"

      do! settleAsync()

      let! path = captureAsync("F4/f4-followers", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "an empty follower list says its words" {
      let! window = openProfileAsync(emptyFollowersApi() :> IHttp)

      do! clickVisibleNamed(window, "ProfileFollowersCount:octocat")

      let! words = seesVisible(window, "Nobody is following @octocat yet.")
      Expect.isTrue words "the empty words never rendered"

      do! settleAsync()

      let! path = captureAsync("F4/f4-empty-followers", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the search palette lists the organizations below the people" {
      let! window =
        startShell(signedInStore(), searchApi(false, false) :> IHttp)

      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! clickWhenShown(window, "Search")

      // The at keeps the developer reading, so the person stands first
      // and the organizations follow under their heading.
      do!
        onUiAsync(fun () ->
          controlByName(window, "SearchInput")
          |> Option.iter(fun control -> (control :?> TextBox).Text <- "@kai"))

      let! settled = seesVisible(window, "@acme")
      Expect.isTrue settled "the search never settled"

      do! settleAsync()

      let! path = captureAsync("F2/f2-dialog-orgs", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the keys arm an organization row in the search palette" {
      let! window =
        startShell(signedInStore(), searchApi(false, false) :> IHttp)

      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do! clickWhenShown(window, "Search")

      do!
        onUiAsync(fun () ->
          controlByName(window, "SearchInput")
          |> Option.iter(fun control -> (control :?> TextBox).Text <- "@kai"))

      let! settled = seesVisible(window, "@acme")
      Expect.isTrue settled "the search never settled"

      // Down leaves the person row and arms the first organization.
      do!
        onUiAsync(fun () ->
          controlByName(window, "SearchInput")
          |> Option.iter(fun control ->
            (control :?> TextBox)
              .RaiseEvent(
                KeyEventArgs(
                  RoutedEvent = InputElement.KeyDownEvent,
                  Key = Key.Down
                )
              )))

      do! settleAsync()

      let! path = captureAsync("F2/f2-dialog-org-armed", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the search palette stays inside a short window" {
      let! window =
        startShell(signedInStore(), searchApi(false, false) :> IHttp)

      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the log never rendered"

      do!
        onUiAsync(fun () ->
          window.MinHeight <- 0.0
          window.Height <- 480.0)

      do! clickWhenShown(window, "Search")

      do!
        onUiAsync(fun () ->
          controlByName(window, "SearchInput")
          |> Option.iter(fun control ->
            (control :?> TextBox).Text <- "kai kou"))

      let! settled = seesVisible(window, "@acme")
      Expect.isTrue settled "the search never settled"

      do! settleAsync()

      let! path = captureAsync("F2/f2-dialog-short-window", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the results page stands the profile sections above the commits" {
      let! window = openSearchPageAsync()

      let! developers = seesVisible(window, "Developers")
      Expect.isTrue developers "the developers heading never rendered"

      let! orgs = seesVisible(window, "@acme")
      Expect.isTrue orgs "the organizations never rendered"

      do! settleAsync()

      let! path = captureAsync("F2/f2-search-page-profiles", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "a titled code block shows its filename" {
      let! window =
        startShell(signedInStore(), titledCodeBlockLogApi() :> IHttp)

      let! titled = showsText(window, "fig.sh")
      Expect.isTrue titled "the filename never showed over the block"

      do! settleAsync()

      let! path = captureAsync("F30/f30-code-block-title", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "the log shows the caught-up feed in place" {
      let! window = startShell(signedInStore(), catchUpLogApi() :> IHttp)

      let! late = showsText(window, "A late commit.")
      Expect.isTrue late "the caught-up commit never rendered"

      do! settleAsync()

      let! path = captureAsync("F28/f28-log-catch-up", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }

    testAsync "a hashtag renders as a search link" {
      let! window = startShell(signedInStore(), hashtagLogApi() :> IHttp)

      let! tag = showsText(window, "#fsharp")
      Expect.isTrue tag "the hashtag never rendered"

      do! settleAsync()

      let! path = captureAsync("F26/f26-hashtag-links", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }
  ]
