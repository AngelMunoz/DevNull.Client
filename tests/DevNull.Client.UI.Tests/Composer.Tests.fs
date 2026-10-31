module DevNull.Client.UI.Tests.Composer

// The composer's completion lane through the real shell: typing @
// offers the site's own people and organizations, Enter completes the
// armed mention into the box, the issue card a number names completes
// to its address, a color and a fenced number never ask, and a
// refusal to slow down shows its sentence.

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.VisualTree
open Expecto
open DevNull.Client.Core.Http
open DevNull.Client.UI.Tests.App
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

/// The site's own suggestion answers: one person, one organization,
/// and the issue a number names.
let mentionsJson =
  """{"data":[{"id":"01a15638-ae71-733c-89b3-373a1a0c538a","handle":"octocat","name":"The Octocat","avatar_url":null,"lane":3}],"organizations":[{"handle":"acme","name":"Acme Labs","avatar_url":null}]}"""

let issueJson =
  """{"issue":{"number":12,"title":"It was DNS","url":"https://github.com/dev-null/sh/issues/12"}}"""

/// Nine people to mention, so the flyout's list overflows the scroller.
let manyJson =
  let people =
    [
      for index in 1..9 ->
        $"{{\"id\":\"018f5a1e-000{index}-7c3a-9b2d-5e6f7a8b9c0d\",\"handle\":\"person{index}\",\"name\":\"Person {index}\",\"avatar_url\":null}}"
    ]
    |> String.concat ","

  $"{{\"data\":[{people}],\"organizations\":[]}}"

/// The api of the many-people test: the mentions route answers nine.
let manyApi() =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/search/mentions" then
      ok manyJson
    elif request.Target.StartsWith "/api/v1/search/issues" then
      ok issueJson
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

let refused status body : HttpResponse = {
  Status = status
  Headers = []
  Body = body
}

/// The api of the completion tests: the log and account answer as the
/// other tests' do, and the suggestion routes answer the completion
/// story — a query of zz answered with the given refusal.
let completionApi(refusal: HttpResponse voption) =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/search/mentions" then
      match refusal with
      | ValueSome answer when request.Target.Contains "q=zz" -> answer
      | _ -> ok mentionsJson
    elif request.Target.StartsWith "/api/v1/search/issues" then
      ok issueJson
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// Types into the log's composer box, once the log stands behind it.
let typeIntoComposer(window: Window, text: string) : Async<unit> = async {
  let! typed =
    waitForAsync(
      5000,
      (fun () -> textBoxWithWatermark(window, "What did you ship?")),
      Option.isSome
    )

  Expect.isTrue typed "the composer never appeared"

  // The caret rides along, the way a real keystroke leaves it: a
  // programmatic text set alone keeps the caret where it stood.
  do!
    onUiAsync(fun () ->
      textBoxWithWatermark(window, "What did you ship?")
      |> Option.iter(fun box ->
        box.Text <- text
        box.CaretIndex <- text.Length))
}

/// Presses one key on the composer's box.
let press(window: Window, key: Key) : Async<unit> =
  onUiAsync(fun () ->
    textBoxWithWatermark(window, "What did you ship?")
    |> Option.iter(fun box ->
      box.RaiseEvent(
        KeyEventArgs(RoutedEvent = InputElement.KeyDownEvent, Key = key)
      )))

let tests =
  testList "composer completions" [
    testCaseAsync
      "typing @ offers the site's people and organizations"
      (async {
        let api = completionApi ValueNone
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        do! typeIntoComposer(window, "@oc")

        let! offered =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "@acme")
          )

        Expect.isTrue offered "the organizations never arrived"

        Expect.isTrue
          (api.Requests
           |> List.exists(fun request ->
             request.Method = "GET"
             && request.Target = "/api/v1/search/mentions?q=oc"))
          "the mention ask"
      })

    testCaseAsync
      "Enter completes the armed mention into the box"
      (async {
        let api = completionApi ValueNone
        let! window = startShell(signedInStore(), api :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! typeIntoComposer(window, "@oc")

        let! offered =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "@acme")
          )

        Expect.isTrue offered "the rows never arrived"

        do! press(window, Key.Enter)

        let! completed =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "What did you ship?")),
            (fun box ->
              box
              |> Option.map(fun control -> control.Text = "@octocat ")
              |> Option.defaultValue false)
          )

        Expect.isTrue completed "the completion never landed"
      })

    testCaseAsync
      "the issue card a number names completes to its address"
      (async {
        let api = completionApi ValueNone
        let! window = startShell(signedInStore(), api :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! typeIntoComposer(window, "see #12")

        let! card =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS")
          )

        Expect.isTrue card "the issue card never arrived"

        Expect.isTrue
          (api.Requests
           |> List.exists(fun request ->
             request.Target = "/api/v1/search/issues?q=12"))
          "the issue ask"

        do! press(window, Key.Enter)

        let address = "https://github.com/dev-null/sh/issues/12"

        let! completed =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "What did you ship?")),
            (fun box ->
              box
              |> Option.map(fun control ->
                let text =
                  control.Text |> Option.ofObj |> Option.defaultValue ""

                text = $"see {address} " && control.CaretIndex = text.Length)
              |> Option.defaultValue false)
          )

        Expect.isTrue completed "the address never landed"
      })

    testCaseAsync
      "a color and a fenced number never ask for an issue"
      (async {
        let api = completionApi ValueNone
        let! window = startShell(signedInStore(), api :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! typeIntoComposer(window, "#000")
        do! Async.Sleep 400
        do! pumpAsync()

        do! typeIntoComposer(window, "see `#12`")
        do! Async.Sleep 400
        do! pumpAsync()

        Expect.isFalse
          (api.Requests
           |> List.exists(fun request ->
             request.Target.StartsWith "/api/v1/search/issues"))
          "a color or fenced number never asked"
      })

    testCaseAsync
      "the keys walk the rows past the visible area"
      (async {
        let api = manyApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! typeIntoComposer(window, "@")

        let! offered =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "@person9")
          )

        Expect.isTrue offered "the rows never arrived"

        let scroller() =
          window.GetVisualDescendants()
          |> Seq.tryFind(fun visual -> visual.Name = "CompletionScroller")
          |> Option.bind(fun visual ->
            match visual with
            | :? ScrollViewer as found -> Some found
            | _ -> None)

        let rowVisible(name: string) =
          match scroller() with
          | None -> false
          | Some found ->
            window.GetVisualDescendants()
            |> Seq.tryFind(fun visual -> visual.Name = name)
            |> Option.map(fun row ->
              let point = row.TranslatePoint(Point(0.0, 0.0), found)

              point.HasValue
              && point.Value.Y >= -1.0
              && point.Value.Y + row.Bounds.Height
                 <= found.Viewport.Height + 1.0)
            |> Option.defaultValue false

        let! before =
          probeUiAsync(fun () ->
            scroller() |> Option.map(fun opened -> opened.Offset.Y))

        Expect.isTrue (before.IsSome) "the scroller never stood"

        for _ in 1..7 do
          do! press(window, Key.Down)

        do! Async.Sleep 300
        do! pumpAsync()

        let! after =
          probeUiAsync(fun () ->
            scroller()
            |> Option.map(fun walked ->
              walked.Offset.Y, walked.Extent.Height, walked.Viewport.Height))

        match before, after with
        | Some opened, Some(walked, extent, viewport) ->
          Expect.isGreaterThan extent viewport "the list overflowed"
          Expect.isGreaterThan walked (opened + 1.0) "the list scrolled"
        | Some _, None -> failtest "the scroller vanished"
        | None, _ -> failtest "the scroller never stood"

        let! armedVisible = probeUiAsync(fun () -> rowVisible "CompletionRow7")

        Expect.isTrue armedVisible "the armed row is visible"

        // Two more steps wrap from the last row to row zero. Row zero
        // must be visible, even when the caption above it scrolls out.
        do! press(window, Key.Down)
        do! press(window, Key.Down)
        do! Async.Sleep 300
        do! pumpAsync()

        let! wrapped = probeUiAsync(fun () -> rowVisible "CompletionRow0")

        Expect.isTrue wrapped "row zero is visible after the wrap"

        // A new answer replaces the rows while row zero is armed. The
        // new first row must be visible too.
        do! typeIntoComposer(window, "@person9")
        do! Async.Sleep 400
        do! pumpAsync()

        let! reset = probeUiAsync(fun () -> rowVisible "CompletionRow0")

        Expect.isTrue reset "row zero is visible for a new answer"
      })

    testCaseAsync
      "the slow-down answer shows its sentence"
      (async {
        let api =
          completionApi(ValueSome(refused 429 """{"message":"Slow down."}"""))

        let! window = startShell(signedInStore(), api :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! typeIntoComposer(window, "@zz")

        let! slowed =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              hasText(root, "error: too many searches, give it a few seconds")
          )

        Expect.isTrue slowed "the slow-down sentence never showed"
      })
  ]
