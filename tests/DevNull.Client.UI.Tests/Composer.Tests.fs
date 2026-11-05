module DevNull.Client.UI.Tests.Composer

// The composer's completion lane through the real shell: typing @
// offers the site's own people and organizations, Enter completes the
// armed mention into the box, the issue card a number names completes
// to its address, a color and a fenced number never ask, and a
// refusal to slow down shows its sentence.

open System
open System.Threading.Tasks
open Avalonia
open Avalonia.Automation
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Headless
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

/// The emoji trigger buttons under the window.
let emojiButtons(window: Window) : Button list =
  window.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? Button as button when button.Name = "EmojiButton" -> Some button
    | _ -> None)
  |> List.ofSeq

let visibleEmojiButtons(window: Window) : Button list =
  emojiButtons window |> List.filter effectiveVisible

/// The picker popup attached to one composer body.
let emojiPopupFor(window: Window, body: Control) : Popup option =
  window.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? Popup as popup when popup.Name = "EmojiPanel" -> Some popup
    | _ -> None)
  |> Seq.tryFind(fun popup ->
    popup.PlacementTarget
    |> Option.ofObj
    |> Option.exists(fun target -> obj.ReferenceEquals(target, body)))

/// The picker's popup control, once the view stands.
let emojiPopup(window: Window) : Popup option =
  controlByName(window, "EmojiPanel")
  |> Option.bind(fun control ->
    match control with
    | :? Popup as popup -> Some popup
    | _ -> None)

/// The picker's panel body, once the panel stands.
let emojiPanelBody(window: Window) : Border option =
  controlByName(window, "EmojiPanelBody")
  |> Option.bind(fun control ->
    match control with
    | :? Border as panel -> Some panel
    | _ -> None)

/// The picker's search field, once the panel stands.
let emojiSearch(window: Window) : TextBox option =
  controlByName(window, "EmojiSearch")
  |> Option.bind(fun control ->
    match control with
    | :? TextBox as box -> Some box
    | _ -> None)

/// Opens the composer's emoji picker and waits for its search field.
let openEmojiPicker(window: Window) : Async<unit> = async {
  let! offered =
    waitForAsync(
      5000,
      (fun () -> visibleEmojiButtons window |> List.tryHead),
      Option.isSome
    )

  Expect.isTrue offered "the emoji trigger never appeared"

  do!
    onUiAsync(fun () ->
      visibleEmojiButtons window |> List.tryHead |> Option.iter click)

  let! search =
    waitForAsync(5000, (fun () -> emojiSearch window), Option.isSome)

  Expect.isTrue search "the emoji panel never opened"
}

/// Opens or closes the picker through the trigger's real pointer path:
/// the press hands focus to the button and the click that follows runs.
let pressEmojiTrigger(window: Window) : Async<unit> = async {
  let! trigger =
    probeUiAsync(fun () -> visibleEmojiButtons window |> List.tryHead)

  match trigger with
  | Some button -> do! pressControlAsync(window, button)
  | None -> failwith "the emoji trigger was not offered"
}

/// Opens the picker from the lowest visible trigger: the composer
/// nearest the window's bottom.
let openBottomEmojiPicker(window: Window) : Async<unit> = async {
  let! offered =
    waitForAsync(
      5000,
      (fun () -> visibleEmojiButtons window |> List.length),
      fun count -> count >= 1
    )

  Expect.isTrue offered "the composer never offered a trigger"

  do!
    onUiAsync(fun () ->
      visibleEmojiButtons window
      |> List.sortBy(fun button ->
        button.TranslatePoint(Point(0.0, 0.0), window)
        |> Option.ofNullable
        |> Option.map _.Y
        |> Option.defaultValue 0.0)
      |> List.tryLast
      |> Option.iter click)

  let! search =
    waitForAsync(5000, (fun () -> emojiSearch window), Option.isSome)

  Expect.isTrue search "the emoji panel never opened"
}

/// The label the cell at the index carries.
let cellLabel(window: Window, index: int) : string option =
  controlByName(window, $"EmojiCell{index}")
  |> Option.map(fun control ->
    AutomationProperties.GetName control
    |> Option.ofObj
    |> Option.defaultValue "")

/// The name of the cell the picker has armed.
let activeCell(window: Window) : string option =
  window.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? Border as border when
      border.Name
      |> Option.ofObj
      |> Option.exists(fun name -> name.StartsWith("EmojiCellShell"))
      ->
      Some border
    | _ -> None)
  |> Seq.tryFind(fun border -> border.Classes.Contains "accent-back")
  |> Option.bind(fun border -> border.Name |> Option.ofObj)

/// True when a visible error line stands under the window.
let errorLineShown(window: Window) : bool =
  window.GetVisualDescendants()
  |> Seq.exists(fun visual ->
    match visual with
    | :? TextBlock as block ->
      (block.Text
       |> Option.ofObj
       |> Option.exists(fun text -> text.StartsWith("error:")))
      && effectiveVisible block
    | _ -> false)

/// The text one named TextBlock carries.
let textOf(window: Window, name: string) : string =
  controlByName(window, name)
  |> Option.bind(fun control ->
    match control with
    | :? TextBlock as block -> block.Text |> Option.ofObj
    | _ -> None)
  |> Option.defaultValue ""

/// Sets the composer's text and its selection, the way a person can
/// leave both before reaching for the picker.
let setComposerSelection
  (window: Window, text: string, start: int, finish: int)
  : Async<unit> =
  onUiAsync(fun () ->
    textBoxWithWatermark(window, "What did you ship?")
    |> Option.iter(fun box ->
      box.Text <- text
      box.CaretIndex <- finish
      box.SelectionStart <- start
      box.SelectionEnd <- finish))

/// Presses one key on the picker's search field.
let pressSearch(window: Window, key: Key) : Async<unit> =
  onUiAsync(fun () ->
    emojiSearch window
    |> Option.iter(fun box ->
      box.RaiseEvent(
        KeyEventArgs(RoutedEvent = InputElement.KeyDownEvent, Key = key)
      )))

/// Types into the picker's search field.
let typeSearch(window: Window, text: string) : Async<unit> =
  onUiAsync(fun () ->
    emojiSearch window |> Option.iter(fun box -> box.Text <- text))

/// Tabs from the focused control until the emoji trigger holds focus,
/// at most 20 stops.
[<TailCall>]
let rec tabToEmojiTriggerFrom(window: Window, remaining: int) : Async<unit> =
  let holdsTrigger() =
    match window.FocusManager.GetFocusedElement() with
    | :? Button as button -> button.Name = "EmojiButton"
    | _ -> false

  async {
    let! held = probeUiAsync holdsTrigger

    match held, remaining with
    | true, _ -> return ()
    | false, 0 -> return failwith "the Tab walk never reached the emoji trigger"
    | false, _ ->
      do!
        onUiAsync(fun () ->
          window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None))

      do! pumpAsync()
      return! tabToEmojiTriggerFrom(window, remaining - 1)
  }

let tabToEmojiTrigger(window: Window) : Async<unit> =
  tabToEmojiTriggerFrom(window, 20)

/// The picker's scroller offset.
let emojiOffset(window: Window) : float option =
  controlByName(window, "EmojiScroller")
  |> Option.bind(fun control ->
    match control with
    | :? ScrollViewer as scroller -> Some scroller.Offset.Y
    | _ -> None)

/// An api that holds the commit post until the gate opens.
type HeldCommitApi(inner: IHttp, gate: TaskCompletionSource<unit>) =
  interface IHttp with
    member _.Send(request) =
      if request.Method = "POST" && request.Target = "/api/v1/commits" then
        async {
          do! gate.Task |> Async.AwaitTask
          return! inner.Send request
        }
      else
        inner.Send request

    member _.GetBytes(target) = inner.GetBytes target

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

        let! moved =
          waitForAsync(
            5000,
            (fun () -> scroller() |> Option.map _.Offset.Y),
            fun offset ->
              offset |> Option.exists(fun value -> value > before.Value + 1.0)
          )

        Expect.isTrue moved "the walk never scrolled the list"

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

        let! wrapped =
          waitForAsync(5000, (fun () -> rowVisible "CompletionRow0"), id)

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

    testCaseAsync
      "the fresh composer offers the emoji trigger"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        let! offered =
          waitForAsync(
            5000,
            (fun () -> visibleEmojiButtons window |> List.tryHead),
            Option.isSome
          )

        Expect.isTrue offered "the fresh composer has no emoji trigger"

        let! enabled =
          probeUiAsync(fun () ->
            visibleEmojiButtons window
            |> List.tryHead
            |> Option.map _.IsEnabled)

        Expect.equal (Some true) enabled "the trigger starts enabled"
      })

    testCaseAsync
      "the reply composer offers the emoji trigger"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

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

        let! offered =
          waitForAsync(
            5000,
            (fun () -> visibleEmojiButtons window |> List.length),
            fun count -> count >= 2
          )

        Expect.isTrue offered "the reply composer has no emoji trigger"
      })

    testCaseAsync
      "the emoji trigger is disabled while a post is in flight"
      (async {
        let gate = TaskCompletionSource<unit>()

        let api = HeldCommitApi(logApi(), gate)

        let! window = startShell(signedInStore(), api :> IHttp)
        let! _ = showsText(window, "The Octocat")

        do! typeIntoComposer(window, "A slow one")
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
            fun enabled -> enabled = Some false
          )

        Expect.isTrue disabled "the trigger stayed on while the post was out"

        gate.SetResult(())

        let! enabled =
          waitForAsync(
            8000,
            (fun () ->
              visibleEmojiButtons window
              |> List.tryHead
              |> Option.map _.IsEnabled),
            fun state -> state = Some true
          )

        Expect.isTrue enabled "the trigger never came back"
      })

    testCaseAsync
      "opening the picker shows the search field, the groups, and the first cell"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! openEmojiPicker window

        let! focused =
          waitForAsync(
            5000,
            (fun () -> emojiSearch window),
            fun search -> search |> Option.exists _.IsFocused
          )

        Expect.isTrue focused "the search field never took focus"

        for group in
          [
            "Smileys & people"
            "Animals & nature"
            "Food & drink"
            "Travel & places"
            "Activities"
            "Objects"
            "Symbols"
            "Flags"
          ] do
          let! header = probeUiAsync(fun () -> hasText(window, group))
          Expect.isTrue header $"the {group} header never stood"

        let! active = probeUiAsync(fun () -> activeCell window)

        Expect.equal
          (Some "EmojiCellShell0")
          active
          "the first cell starts armed"

        let! footer = probeUiAsync(fun () -> textOf(window, "EmojiFooterName"))
        Expect.equal ":grinning:" footer "the footer names the armed cell"
      })

    testCaseAsync
      "a query collapses to the ranked Results section"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! openEmojiPicker window

        do!
          onUiAsync(fun () ->
            controlByName(window, "EmojiScroller")
            |> Option.iter(fun control ->
              match control with
              | :? ScrollViewer as scroller ->
                scroller.Offset <- Vector(0.0, 120.0)
              | _ -> ()))

        do! typeSearch(window, "smi")

        let! labelled =
          waitForAsync(
            5000,
            (fun () -> cellLabel(window, 0)),
            fun label -> label = Some ":smile:"
          )

        Expect.isTrue labelled "the first result never settled"

        let! labels =
          probeUiAsync(fun () ->
            [ 0..2 ] |> List.map(fun index -> cellLabel(window, index)))

        Expect.equal
          [ Some ":smile:"; Some ":smirk:"; Some ":smiley:" ]
          labels
          "the prefix block"

        let! named =
          probeUiAsync(fun () ->
            controlByName(window, "EmojiResults")
            |> Option.map(fun control ->
              AutomationProperties.GetName control |> Option.ofObj))

        Expect.equal
          (Some(Some "Results"))
          named
          "the results section lost its automation name"

        let! header = probeUiAsync(fun () -> visibleText(window, "Results"))

        Expect.isFalse header "the results section kept a visible header"

        let! active = probeUiAsync(fun () -> activeCell window)
        Expect.equal (Some "EmojiCellShell0") active "the first result is armed"

        let! top =
          waitForAsync(
            5000,
            (fun () -> emojiOffset window),
            fun offset -> offset = Some 0.0
          )

        Expect.isTrue top "the results did not scroll to the top"
      })

    testCaseAsync
      "an unknown query shows the empty line and an empty footer"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! openEmojiPicker window
        do! typeSearch(window, "zzqq")

        let! empty = showsText(window, "no emoji for “zzqq”")
        Expect.isTrue empty "the empty line never showed"

        let! footer =
          probeUiAsync(fun () ->
            textOf(window, "EmojiFooterName"),
            textOf(window, "EmojiFooterGlyph"))

        Expect.equal ("", "") footer "the footer stayed empty"
      })

    testCaseAsync
      "focus leaving the picker closes it without touching the body"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! setComposerSelection(window, "kept", 4, 4)
        do! openEmojiPicker window

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun box ->
              box.Focus() |> ignore
              box.Text <- "kept it"
              box.CaretIndex <- 7))

        let! closed =
          waitForAsync(
            5000,
            (fun () -> emojiSearch window |> Option.map effectiveVisible),
            fun visible -> visible <> Some true
          )

        Expect.isTrue closed "the panel stayed open after focus left"

        let! body =
          probeUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.bind(fun box ->
              box.Text
              |> Option.ofObj
              |> Option.map(fun text -> text, box.CaretIndex)))

        Expect.equal (Some("kept it", 7)) body "the picker touched the body"
      })

    testCaseAsync
      "the keys walk the grid and Escape leaves the body alone"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! setComposerSelection(window, "kept", 4, 4)
        do! openEmojiPicker window

        do! pressSearch(window, Key.Left)
        do! pressSearch(window, Key.Up)
        do! settleAsync()

        let! held = probeUiAsync(fun () -> activeCell window)

        Expect.equal (Some "EmojiCellShell0") held "the walk left the start"

        do! pressSearch(window, Key.Right)

        let! right =
          waitForAsync(
            3000,
            (fun () -> activeCell window),
            fun active -> active = Some "EmojiCellShell1"
          )

        Expect.isTrue right "Right never moved one cell"

        do! pressSearch(window, Key.Left)

        let! left =
          waitForAsync(
            3000,
            (fun () -> activeCell window),
            fun active -> active = Some "EmojiCellShell0"
          )

        Expect.isTrue left "Left never moved back"

        do! pressSearch(window, Key.Down)

        let! down =
          waitForAsync(
            3000,
            (fun () -> activeCell window),
            fun active -> active = Some "EmojiCellShell8"
          )

        Expect.isTrue down "Down never moved a row"

        do! pressSearch(window, Key.Up)

        let! up =
          waitForAsync(
            3000,
            (fun () -> activeCell window),
            fun active -> active = Some "EmojiCellShell0"
          )

        Expect.isTrue up "Up never moved back a row"

        do! pressSearch(window, Key.Escape)

        let! closed =
          waitForAsync(
            5000,
            (fun () -> emojiSearch window |> Option.map effectiveVisible),
            fun visible -> visible <> Some true
          )

        Expect.isTrue closed "Escape never closed the panel"

        let! body =
          probeUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.bind(fun box ->
              box.Text
              |> Option.ofObj
              |> Option.map(fun text ->
                text, box.SelectionStart, box.SelectionEnd)))

        Expect.equal (Some("kept", 4, 4)) body "Escape touched the body"

        do! openEmojiPicker window
        do! typeSearch(window, "apple")

        let! apple =
          waitForAsync(
            5000,
            (fun () -> cellLabel(window, 0)),
            fun label -> label = Some ":apple:"
          )

        Expect.isTrue apple "the apple result never stood"

        for _ in 1..3 do
          do! pressSearch(window, Key.Right)

        do! settleAsync()

        let! last = probeUiAsync(fun () -> activeCell window)

        Expect.equal
          (Some "EmojiCellShell2")
          last
          "the walk left the last result"

        do! pressSearch(window, Key.Enter)

        let! picked =
          waitForAsync(
            5000,
            (fun () ->
              textBoxWithWatermark(window, "What did you ship?")
              |> Option.map(fun box -> box.Text, box.CaretIndex, box.IsFocused)),
            fun state -> state = Some("kept🍍", 6, true)
          )

        Expect.isTrue picked "Enter never picked the armed cell"

        let! closedAgain =
          waitForAsync(
            5000,
            (fun () -> emojiSearch window |> Option.map effectiveVisible),
            fun visible -> visible <> Some true
          )

        Expect.isTrue closedAgain "Enter never closed the panel"
      })

    testCaseAsync
      "an Escape handled before the picker leaves the panel open"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! openEmojiPicker window

        let marksHandled =
          EventHandler<KeyEventArgs>(fun _ args ->
            if args.Key = Key.Escape then
              args.Handled <- true)

        do!
          onUiAsync(fun () ->
            emojiPanelBody window
            |> Option.iter(fun panel ->
              panel.AddHandler(
                InputElement.KeyDownEvent,
                marksHandled,
                RoutingStrategies.Tunnel
              )))

        do! pressSearch(window, Key.Escape)
        do! settleAsync()

        let! held =
          probeUiAsync(fun () ->
            emojiSearch window |> Option.map effectiveVisible)

        Expect.equal (Some true) held "the handled Escape closed the panel"

        do!
          onUiAsync(fun () ->
            emojiPanelBody window
            |> Option.iter(fun panel ->
              panel.RemoveHandler(InputElement.KeyDownEvent, marksHandled)))

        do! pressSearch(window, Key.Escape)

        let! closed =
          waitForAsync(
            5000,
            (fun () -> emojiSearch window |> Option.map effectiveVisible),
            fun visible -> not(Option.contains true visible)
          )

        Expect.isTrue closed "the plain Escape never closed the panel"
      })

    testCaseAsync
      "a pick inserts at the caret and leaves the caret after the emoji"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

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
              |> Option.map(fun box -> box.Text, box.CaretIndex, box.IsFocused)),
            fun state -> state = Some("shipped 🚀it", 10, true)
          )

        Expect.isTrue inserted "the emoji never landed at the caret"

        let! lane =
          probeUiAsync(fun () ->
            controlByName(window, "CompletionRow0")
            |> Option.map effectiveVisible)

        Expect.isTrue (lane <> Some true) "the completion lane reopened"
      })

    testCaseAsync
      "a pick replaces the selection"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! setComposerSelection(window, "shipped it", 8, 10)
        do! openEmojiPicker window
        do! typeSearch(window, "tada")

        let! tada =
          waitForAsync(
            5000,
            (fun () -> cellLabel(window, 0)),
            fun label -> label = Some ":tada:"
          )

        Expect.isTrue tada "the tada result never stood"

        do!
          onUiAsync(fun () ->
            controlByName(window, "EmojiCell0")
            |> Option.iter(fun control ->
              match control with
              | :? Button as button -> click button
              | _ -> ()))

        let! replaced =
          waitForAsync(
            5000,
            (fun () ->
              textBoxWithWatermark(window, "What did you ship?")
              |> Option.map(fun box -> box.Text, box.CaretIndex)),
            fun state -> state = Some("shipped 🎉", 10)
          )

        Expect.isTrue replaced "the selection was not replaced"
      })

    testCaseAsync
      "a pick replaces a selection dragged backwards"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! setComposerSelection(window, "shipped it", 10, 8)
        do! openEmojiPicker window
        do! typeSearch(window, "tada")

        let! tada =
          waitForAsync(
            5000,
            (fun () -> cellLabel(window, 0)),
            Option.contains ":tada:"
          )

        Expect.isTrue tada "the tada result never stood"

        do!
          onUiAsync(fun () ->
            controlByName(window, "EmojiCell0")
            |> Option.iter(fun control ->
              match control with
              | :? Button as button -> click button
              | _ -> ()))

        let! replaced =
          waitForAsync(
            5000,
            (fun () ->
              textBoxWithWatermark(window, "What did you ship?")
              |> Option.map(fun box -> box.Text, box.CaretIndex)),
            Option.contains("shipped 🎉", 10)
          )

        Expect.isTrue replaced "the right-to-left selection was not replaced"
      })

    testCaseAsync
      "a refused pick restores a standing range"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        let full = String.replicate 40000 "a"

        do! setComposerSelection(window, full, full.Length - 1, full.Length)

        do! openEmojiPicker window
        do! typeSearch(window, "rocket")

        let! rocket =
          waitForAsync(
            5000,
            (fun () -> cellLabel(window, 0)),
            Option.contains ":rocket:"
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
            fun visible -> not(Option.contains true visible)
          )

        Expect.isTrue closed "the panel stayed open after the refusal"

        let! restored =
          probeUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.map(fun box ->
              box.Text = full,
              box.SelectionStart,
              box.SelectionEnd,
              box.CaretIndex,
              box.IsFocused))

        Expect.equal
          (Some(true, full.Length - 1, full.Length, full.Length, true))
          restored
          "the refusal did not restore the standing range"
      })

    testCaseAsync
      "a pick over the cap refuses silently"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

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

        Expect.isTrue closed "the panel stayed open after the refusal"

        let! unchanged =
          probeUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.map(fun box ->
              box.Text = full,
              box.SelectionStart,
              box.SelectionEnd,
              box.CaretIndex,
              box.IsFocused))

        Expect.equal
          (Some(true, full.Length, full.Length, full.Length, true))
          unchanged
          "the refusal changed the body"

        let! errorLine = probeUiAsync(fun () -> errorLineShown window)
        Expect.isFalse errorLine "the refusal wrote an error line"
      })

    testCaseAsync
      "a pick closes the panel and returns focus to the body"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! setComposerSelection(window, "no mention", 10, 10)
        do! openEmojiPicker window
        do! typeSearch(window, "tada")

        let! tada =
          waitForAsync(
            5000,
            (fun () -> cellLabel(window, 0)),
            fun label -> label = Some ":tada:"
          )

        Expect.isTrue tada "the tada result never stood"

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

        Expect.isTrue closed "the panel stayed open after the pick"

        let! focused =
          waitForAsync(
            5000,
            (fun () ->
              textBoxWithWatermark(window, "What did you ship?")
              |> Option.map _.IsFocused),
            fun state -> state = Some true
          )

        Expect.isTrue focused "the body never took focus back"

        let! lane =
          probeUiAsync(fun () ->
            controlByName(window, "CompletionRow0")
            |> Option.map effectiveVisible)

        Expect.isTrue (lane <> Some true) "the completion lane reopened"
      })

    testCaseAsync
      "a pick that leaves the body unchanged still focuses it at the caret"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! setComposerSelection(window, "🚀", 0, 2)
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

        let! state =
          waitForAsync(
            5000,
            (fun () ->
              textBoxWithWatermark(window, "What did you ship?")
              |> Option.map(fun box -> box.Text, box.CaretIndex, box.IsFocused)),
            Option.contains("🚀", 2, true)
          )

        Expect.isTrue state "the unchanged pick lost the caret or the focus"
      })

    testCaseAsync
      "the trigger toggles the panel and a reopen starts from the catalog top"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! setComposerSelection(window, "kept", 4, 4)
        do! openEmojiPicker window
        do! typeSearch(window, "smi")

        let! ranked =
          waitForAsync(
            5000,
            (fun () -> cellLabel(window, 0)),
            Option.contains ":smile:"
          )

        Expect.isTrue ranked "the smi results never stood"

        do!
          onUiAsync(fun () ->
            visibleEmojiButtons window |> List.tryHead |> Option.iter click)

        do! settleAsync()

        let! panel =
          probeUiAsync(fun () ->
            emojiSearch window |> Option.map effectiveVisible)

        match panel with
        | Some true -> do! pressSearch(window, Key.Enter)
        | _ -> ()

        let! bodyAfter =
          probeUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.map(fun box ->
              box.Text |> Option.ofObj |> Option.defaultValue "",
              box.CaretIndex))

        Expect.isFalse
          (Option.contains true panel)
          "the second click left the panel open"

        Expect.equal
          (Some("kept", 4))
          bodyAfter
          "the second click changed the body"

        do! openEmojiPicker window

        let! reopened =
          waitForAsync(
            5000,
            (fun () ->
              emojiSearch window
              |> Option.bind(fun box -> box.Text |> Option.ofObj),
              activeCell window,
              textOf(window, "EmojiFooterName")),
            fun (query, active, footer) ->
              Option.contains "" query
              && Option.contains "EmojiCellShell0" active
              && footer = ":grinning:"
          )

        Expect.isTrue reopened "the reopened panel kept the stale query"

        do!
          onUiAsync(fun () ->
            controlByName(window, "EmojiScroller")
            |> Option.iter(fun control ->
              match control with
              | :? ScrollViewer as scroller ->
                scroller.Offset <- Vector(0.0, 120.0)
              | _ -> ()))

        let! scrolled =
          waitForAsync(
            5000,
            (fun () -> emojiOffset window),
            fun offset -> offset |> Option.exists(fun value -> value > 0.0)
          )

        Expect.isTrue scrolled "the reopened catalog did not scroll"

        do!
          onUiAsync(fun () ->
            visibleEmojiButtons window |> List.tryHead |> Option.iter click)

        do! settleAsync()

        let! closedAgain =
          probeUiAsync(fun () ->
            emojiSearch window |> Option.map effectiveVisible)

        Expect.isFalse
          (Option.contains true closedAgain)
          "the click did not close the scrolled panel"

        do! openEmojiPicker window

        let! topped =
          waitForAsync(
            5000,
            (fun () ->
              emojiSearch window
              |> Option.bind(fun box -> box.Text |> Option.ofObj),
              activeCell window,
              textOf(window, "EmojiFooterName"),
              emojiOffset window),
            fun (query, active, footer, offset) ->
              Option.contains "" query
              && Option.contains "EmojiCellShell0" active
              && footer = ":grinning:"
              && Option.contains 0.0 offset
          )

        Expect.isTrue
          topped
          "the reopen after a scroll did not start at the catalog top"

        do! pressSearch(window, Key.Enter)

        let! inserted =
          waitForAsync(
            5000,
            (fun () ->
              textBoxWithWatermark(window, "What did you ship?")
              |> Option.map(fun box -> box.Text, box.CaretIndex, box.IsFocused)),
            Option.contains("kept😀", 6, true)
          )

        Expect.isTrue inserted "Enter did not insert the armed first cell"
      })

    testCaseAsync
      "the trigger opens the picker from the keyboard"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do! setComposerSelection(window, "shipped it", 8, 8)

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun box -> ignore(box.Focus())))

        do! tabToEmojiTrigger window

        let! focused =
          probeUiAsync(fun () ->
            match window.FocusManager.GetFocusedElement() with
            | :? Button as button ->
              button.Name |> Option.ofObj |> Option.defaultValue ""
            | _ -> "")

        Expect.equal "EmojiButton" focused "the Tab walk landed elsewhere"

        let! named =
          probeUiAsync(fun () ->
            visibleEmojiButtons window
            |> List.tryHead
            |> Option.bind(fun button ->
              AutomationProperties.GetName button |> Option.ofObj))

        Expect.equal (Some "Add emoji") named "the trigger carries no name"

        do!
          onUiAsync(fun () ->
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None))

        let! opened =
          waitForAsync(
            5000,
            (fun () -> emojiSearch window),
            fun search -> search |> Option.exists _.IsFocused
          )

        Expect.isTrue opened "Enter on the trigger never opened the picker"

        do! pressSearch(window, Key.Enter)

        let! inserted =
          waitForAsync(
            5000,
            (fun () ->
              textBoxWithWatermark(window, "What did you ship?")
              |> Option.map(fun box ->
                box.Text |> Option.ofObj |> Option.defaultValue "",
                box.CaretIndex,
                box.IsFocused)),
            Option.contains("shipped 😀it", 10, true)
          )

        Expect.isTrue
          inserted
          "the pick ignored the caret the body held before Tab"
      })

    testCaseAsync
      "a keyboard activation after an abandoned pointer press uses the live selection"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun box -> ignore(box.Focus())))

        do! setComposerSelection(window, "shipped it", 8, 10)

        let! trigger =
          probeUiAsync(fun () -> visibleEmojiButtons window |> List.tryHead)

        let! body =
          probeUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?"))

        match trigger, body with
        | Some button, Some box ->
          do! pressOutsideAsync(window, button, box)
          do! settleAsync()

          let! opened =
            probeUiAsync(fun () ->
              emojiSearch window |> Option.map effectiveVisible)

          Expect.isFalse
            (Option.contains true opened)
            "the abandoned press opened the picker"

          do! pressControlAsync(window, box)

          let! bodyFocused = probeUiAsync(fun () -> box.IsFocused)

          Expect.isTrue bodyFocused "the body never took pointer focus"

          do! setComposerSelection(window, "fresh text", 0, 5)

          let! selected =
            probeUiAsync(fun () ->
              box.Text |> Option.ofObj |> Option.defaultValue "",
              box.SelectionStart,
              box.SelectionEnd)

          Expect.equal
            selected
            ("fresh text", 0, 5)
            "the body did not hold the fresh selection"

          do! onUiAsync(fun () -> ignore(button.Focus()))

          let! afterFocus =
            probeUiAsync(fun () -> box.SelectionStart, box.SelectionEnd)

          Expect.equal
            afterFocus
            (0, 5)
            "the trigger focus collapsed the body selection"

          let! focused = probeUiAsync(fun () -> button.IsFocused)

          Expect.isTrue focused "the trigger never took keyboard focus"

          do!
            onUiAsync(fun () ->
              window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None))

          let! search =
            waitForAsync(
              5000,
              (fun () -> emojiSearch window),
              fun search -> search |> Option.exists _.IsFocused
            )

          Expect.isTrue search "Enter on the trigger never opened the picker"

          do! typeSearch(window, "tada")

          let! tada =
            waitForAsync(
              5000,
              (fun () -> cellLabel(window, 0)),
              Option.contains ":tada:"
            )

          Expect.isTrue tada "the tada result never stood"

          do! pressSearch(window, Key.Enter)
          do! settleAsync()

          let! inserted =
            probeUiAsync(fun () ->
              textBoxWithWatermark(window, "What did you ship?")
              |> Option.map(fun box ->
                box.Text |> Option.ofObj |> Option.defaultValue "",
                box.CaretIndex))

          Expect.equal
            inserted
            (Some("🎉 text", 2))
            "the pick used the selection the abandoned press recorded"
        | _ -> failwith "the composer or its emoji trigger never stood"
      })

    testCaseAsync
      "a pointer pick replaces the selection the body held"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun box -> ignore(box.Focus())))

        do! setComposerSelection(window, "shipped it", 8, 10)
        do! pressEmojiTrigger window

        let! opened =
          waitForAsync(
            5000,
            (fun () -> emojiSearch window),
            fun search -> search |> Option.exists _.IsFocused
          )

        Expect.isTrue opened "the pointer never opened the picker"

        do! typeSearch(window, "tada")

        let! tada =
          waitForAsync(
            5000,
            (fun () -> cellLabel(window, 0)),
            Option.contains ":tada:"
          )

        Expect.isTrue tada "the tada result never stood"

        do! pressSearch(window, Key.Enter)

        let! replaced =
          waitForAsync(
            5000,
            (fun () ->
              textBoxWithWatermark(window, "What did you ship?")
              |> Option.map(fun box -> box.Text, box.CaretIndex)),
            Option.contains("shipped 🎉", 10)
          )

        Expect.isTrue
          replaced
          "the pointer press collapsed the selection before the pick"
      })

    testCaseAsync
      "a pointer re-click closes the panel and the reopen starts from the catalog top"
      (async {
        let! window =
          startShell(signedInStore(), completionApi ValueNone :> IHttp)

        let! _ = showsText(window, "The Octocat")

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun box -> ignore(box.Focus())))

        do! setComposerSelection(window, "kept", 4, 4)
        do! pressEmojiTrigger window

        let! opened =
          waitForAsync(
            5000,
            (fun () -> emojiSearch window),
            fun search -> search |> Option.exists _.IsFocused
          )

        Expect.isTrue opened "the pointer never opened the picker"

        do! typeSearch(window, "smi")

        let! ranked =
          waitForAsync(
            5000,
            (fun () -> cellLabel(window, 0)),
            Option.contains ":smile:"
          )

        Expect.isTrue ranked "the smi results never stood"

        do! pressEmojiTrigger window
        do! settleAsync()

        let! closed =
          probeUiAsync(fun () ->
            emojiSearch window |> Option.map effectiveVisible)

        Expect.isFalse
          (Option.contains true closed)
          "the pointer re-click did not close the panel"

        let! body =
          probeUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.map(fun box ->
              box.Text |> Option.ofObj |> Option.defaultValue "",
              box.CaretIndex,
              box.IsFocused))

        Expect.equal
          (Some("kept", 4, true))
          body
          "the pointer re-click changed the body or lost its focus"

        do! pressEmojiTrigger window

        let! reopened =
          waitForAsync(
            5000,
            (fun () ->
              emojiSearch window
              |> Option.bind(fun box -> box.Text |> Option.ofObj),
              activeCell window,
              textOf(window, "EmojiFooterName")),
            fun (query, active, footer) ->
              Option.contains "" query
              && Option.contains "EmojiCellShell0" active
              && footer = ":grinning:"
          )

        Expect.isTrue reopened "the reopened panel kept the stale query"

        do! pressSearch(window, Key.Enter)

        let! inserted =
          waitForAsync(
            5000,
            (fun () ->
              textBoxWithWatermark(window, "What did you ship?")
              |> Option.map(fun box -> box.Text, box.CaretIndex, box.IsFocused)),
            Option.contains("kept😀", 6, true)
          )

        Expect.isTrue inserted "Enter did not insert the armed first cell"
      })

  ]
