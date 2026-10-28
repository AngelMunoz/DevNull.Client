module DevNull.Client.UI.Tests.Shortcuts

// The key table checks: the window answers the site's shortcuts, the
// guards stop plain keys at a caret, and the focused card answers its
// own quiet keys.

open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.VisualTree

open Expecto

open DevNull.Client
open DevNull.Client.Core.Http
open DevNull.Client.Shell
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

/// Presses one bare key through the routed event, the way the platform
/// delivers it into the focused element's tree.
let pressKey(source: Interactive, key: Key) : unit =
  source.RaiseEvent(
    KeyEventArgs(RoutedEvent = InputElement.KeyDownEvent, Key = key)
  )

/// Presses one modified key the same way.
let pressModified
  (source: Interactive, key: Key, modifiers: KeyModifiers)
  : unit =
  source.RaiseEvent(
    KeyEventArgs(
      RoutedEvent = InputElement.KeyDownEvent,
      Key = key,
      KeyModifiers = modifiers
    )
  )

/// Builds and shows the shell over one test env.
let startShell(store: SessionStore, http: IHttp) : Async<Window> =
  let env = TestEnv(store, http)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

/// The commit cards under the root, in reading order.
let commitCards(root: Control) : Button list =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? Button as button when
      button.Classes.Contains(Theme.name Theme.CommitRow)
      ->
      Some button
    | _ -> None)
  |> List.ofSeq

/// The card holding keyboard focus, when one does.
let focusedCard(window: Window) : Button option =
  match window.FocusManager.GetFocusedElement() with
  | :? Button as button when button.Classes.Contains(Theme.name Theme.CommitRow) ->
    Some button
  | _ -> None

/// True when the focused card also carries the focus pseudo-class: the
/// hook the focused-row style rides on.
let focusedCardPaints(window: Window) : bool =
  focusedCard window
  |> Option.map(fun card -> card.Classes.Contains ":focus")
  |> Option.defaultValue false

/// The body text of the focused card, so walking asserts on what the
/// reader sees, not on row containers the feed recycles.
let focusedCardText(window: Window) : string =
  match focusedCard window with
  | Some card ->
    card.GetVisualDescendants()
    |> Seq.choose(fun visual ->
      match visual with
      | :? TextBlock as block ->
        block.Text
        |> Option.ofObj
        |> Option.filter(fun text -> text.StartsWith "Card ")
      | _ -> None)
    |> Seq.tryHead
    |> Option.defaultValue ""
  | None -> ""

/// Waits until a TextBlock under the window carries the text.
let showsText(window: Window, text: string) : Async<bool> =
  waitForAsync(5000, (fun () -> window), (fun root -> hasText(root, text)))

let tests =
  testList "keyboard shortcuts" [
    testCaseAsync
      "c opens the compose dialog"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! onUiAsync(fun () -> pressKey(window, Key.C))

        let! opened = showsText(window, "Write a commit")

        Expect.isTrue opened "the compose dialog never opened"
      })

    testCaseAsync
      "the question mark opens the bindings list and escape closes it"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! onUiAsync(fun () -> pressKey(window, Key.OemQuestion))

        let! opened = showsText(window, "Keyboard shortcuts")

        Expect.isTrue opened "the bindings list never opened"

        do! onUiAsync(fun () -> pressKey(window, Key.Escape))

        let! closed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Keyboard shortcuts")))
          )

        Expect.isTrue closed "the bindings list never closed"
      })

    testCaseAsync
      "g then s lands on settings"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            pressKey(window, Key.G)
            pressKey(window, Key.S))

        let! landed =
          showsText(window, "Keep running in the tray when the window closes")

        Expect.isTrue landed "the go-to pair never navigated"
      })

    testCaseAsync
      "a stale g pair navigates nowhere"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! onUiAsync(fun () -> pressKey(window, Key.G))

        // The site's one-second window passes before the pair lands.
        do! Async.Sleep 1200
        do! onUiAsync(fun () -> pressKey(window, Key.S))
        do! settleAsync()

        let! settings =
          probeUiAsync(fun () ->
            hasText(window, "Keep running in the tray when the window closes"))

        let! log = probeUiAsync(fun () -> hasText(window, "The Octocat"))

        Expect.isFalse settings "a stale pair still navigated"
        Expect.isTrue log "the log lost its place"
      })

    testCaseAsync
      "j and k walk the cards"
      (async {
        let! window = startShell(signedInStore(), manyCardsApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        let! dealt = probeUiAsync(fun () -> commitCards window)

        Expect.isGreaterThan dealt.Length 1 "the log carried one card only"

        do! onUiAsync(fun () -> pressKey(window, Key.J))

        let! first =
          waitForAsync(
            5000,
            (fun () -> focusedCardText window),
            (fun text -> text = "Card 1.")
          )

        Expect.isTrue first "j never entered the first card"

        let! painted = probeUiAsync(fun () -> focusedCardPaints window)

        Expect.isTrue painted "the focused card never showed its focus"

        do! onUiAsync(fun () -> pressKey(window, Key.J))

        let! second =
          waitForAsync(
            5000,
            (fun () -> focusedCardText window),
            (fun text -> text = "Card 2.")
          )

        Expect.isTrue second "j never moved down"

        do! onUiAsync(fun () -> pressKey(window, Key.K))

        let! back =
          waitForAsync(
            5000,
            (fun () -> focusedCardText window),
            (fun text -> text = "Card 1.")
          )

        Expect.isTrue back "k never moved up"
      })

    testCaseAsync
      "plain keys stop at the composer's caret"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! typed =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "What did you ship?")),
            Option.isSome
          )

        Expect.isTrue typed "the composer never appeared"

        let! caret =
          probeUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?"))

        do!
          onUiAsync(fun () ->
            caret |> Option.iter(fun box -> box.Focus() |> ignore)

            pressKey(window, Key.J))

        let! focused = probeUiAsync(fun () -> focusedCard window)

        Expect.isNone focused "j walked the cards from inside a sentence"
      })

    testCaseAsync
      "r on the focused card opens the reply composer"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! onUiAsync(fun () -> pressKey(window, Key.J))

        let! card = probeUiAsync(fun () -> focusedCard window)

        Expect.isSome card "j never entered a card"

        do!
          onUiAsync(fun () -> card |> Option.iter(fun c -> pressKey(c, Key.R)))

        let! opened =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "Write your reply…")),
            Option.isSome
          )

        Expect.isTrue opened "r never opened the reply composer"
      })

    testCaseAsync
      "q on the focused card opens the quote page"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! onUiAsync(fun () -> pressKey(window, Key.J))

        let! card = probeUiAsync(fun () -> focusedCard window)

        Expect.isSome card "j never entered a card"

        do!
          onUiAsync(fun () -> card |> Option.iter(fun c -> pressKey(c, Key.Q)))

        let! opened = showsText(window, "Add your take")

        Expect.isTrue opened "q never opened the quote page"
      })

    testCaseAsync
      "ctrl and enter posts the composer's body"
      (async {
        let api = logApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! typed =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "What did you ship?")),
            Option.isSome
          )

        Expect.isTrue typed "the composer never appeared"

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun box ->
              box.Text <- "Committed with the keys"

              pressModified(box, Key.Enter, KeyModifiers.Control)))

        let! posted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST" && request.Target = "/api/v1/commits"))
          )

        Expect.isTrue posted "the commit pair never posted"

        let post =
          api.Requests
          |> List.find(fun request ->
            request.Method = "POST" && request.Target = "/api/v1/commits")

        let postBody =
          match post.Body with
          | HttpBody.Text text -> text
          | _ -> ""

        Expect.stringContains
          postBody
          "Committed with the keys"
          "the typed body"
      })
  ]
