module DevNull.Client.UI.Tests.RemotesView

// The remotes page over the real shell: the inbox lists the store's
// rows, the fetched tab follows the account's flag, search narrows the
// rows to the answer, the list's end asks for the next page, the
// sidebar badge follows the store's unread, a merge settles a fetched
// request, and leaving the page releases its rows.

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.VisualTree
open Expecto
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.DeviceAuth
open DevNull.Client.RemotesState
open DevNull.Client.Shell
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness
open DevNull.Client.UI.Tests.Leaks
open DevNull.Client.UI.Tests.Remotes

/// One signed-in session with the direct messages on.
let remotesOnStore(requests: bool, unread: int) : SessionStore =
  SessionStore(
    Some(
      {
        sessionOf "octocat" with
            Remotes =
              Some {
                From = FromEveryone
                PullReceipts = true
                Requests = requests
                Everyone = true
                UnreadCount = unread
              }
      }
    ),
    devicePrivateKeyPath,
    ignore
  )

/// One conversation row: the partner, the moment of its last message,
/// and how many messages sit ahead.
let rowAt(partner: string, at: string, ahead: int) : string =
  Remotes.conversationJson(partner, ahead, at)

/// One distinct developer, so many rows never share one key.
let devJson(index: int) : string =
  Remotes.userJsonOf(
    $"01a151db-{index:D4}-727b-a7ce-c911a7556243",
    $"dev{index}",
    $"Dev {index}"
  )

/// The many-row first page: `count` distinct partners, tall enough to
/// overflow the inbox column's viewport.
let manyRowsJson(count: int) : string =
  Remotes.inboxPageJson(
    [
      for index in 1..count ->
        rowAt(
          devJson index,
          $"2026-10-2{1 + (index % 8)}T04:00:0{index % 10}Z",
          0
        )
    ],
    true,
    3,
    0
  )

/// The api of the page tests: the open tab answers from cans, the
/// search answers a one-row match, the next page answers its last row,
/// and the candidates answer one person.
let pageApi(firstPage: string, nextPage: string, search: string) : FakeApi =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/remotes/candidates" then
      ok($"{{\"candidates\":[{{\"user\":{hubotJson},\"reason\":null}}]}}")
    elif
      request.Target.StartsWith "/api/v1/remotes"
      && request.Target.Contains "q=hub"
    then
      ok(search)
    elif
      request.Target.StartsWith "/api/v1/remotes"
      && request.Target.Contains "before="
    then
      ok nextPage
    elif
      request.Target.StartsWith "/api/v1/remotes"
      && request.Target.Contains "tab=open"
    then
      ok firstPage
    elif request.Target.StartsWith "/api/v1/remotes" then
      ok(Remotes.inboxPageJson([], false, 3, 0))
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// Builds and shows the shell over one test env.
let startShell(store: SessionStore, http: IHttp) : Async<Window> =
  let env = TestEnv(store, http)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

/// Builds and shows the shell over one test env whose attachment
/// sources answer the cans the test set.
let startAttachedShell
  (store: SessionStore, http: IHttp, attachments: FakeAttachments)
  : Async<Window> =
  let env = TestEnv(store, http, attachments = attachments)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

/// Opens the remotes page through the sidebar.
let openRemotesAsync(window: Window) : Async<unit> = async {
  do! clickWhenShown(window, "Remotes")

  let! entered =
    waitForAsync(
      5000,
      (fun () -> window),
      (fun root ->
        root.GetVisualDescendants()
        |> Seq.exists(fun visual -> visual.Name = "RemotesSearch"))
    )

  Expect.isTrue entered "the remotes page never rendered"
}

/// The first text block under the root carrying the text.
let textBlockWith(root: Control, text: string) : TextBlock option =
  root.GetVisualDescendants()
  |> Seq.tryPick(fun visual ->
    match visual with
    | :? TextBlock as block when block.Text = text -> Some block
    | _ -> None)

/// True when the named control and every ancestor stand visible.
let controlShown(root: Control, name: string) : bool =
  root.GetVisualDescendants()
  |> Seq.exists(fun visual ->
    match visual with
    | :? Control as control when visual.Name = name ->
      control.IsVisible
      && control.GetVisualAncestors()
         |> Seq.forall(fun ancestor ->
           match ancestor with
           | :? Control as up -> up.IsVisible
           | _ -> true)
    | _ -> false)

/// The first button whose tooltip equals the tip: the hover actions
/// carry no text, only their tips.
let buttonTipped(root: Control, tip: string) : Button option =
  root.GetVisualDescendants()
  |> Seq.tryPick(fun visual ->
    match visual with
    | :? Button as button when string(ToolTip.GetTip button) = tip ->
      Some button
    | _ -> None)

/// Clicks one icon button once its tooltip shows, the hover actions'
/// own finder.
let clickWhenShownByTip(window: Window, tip: string) : Async<unit> = async {
  let! shown =
    waitForAsync(
      5000,
      (fun () -> window),
      fun root -> buttonTipped(root, tip) |> Option.isSome
    )

  Expect.isTrue shown $"the {tip} button never appeared"

  return! onUiAsync(fun () -> buttonTipped(window, tip) |> Option.iter click)
}

/// Hovers the reader's pointer over one message row, the way the
/// hover actions surface outside a test's pointer: the group's
/// pointer flag never moves headless, so the actions show directly.
let hoverMessage(window: Window, id: int, mine: bool) : unit =
  let name = if mine then "BubbleActionsMine" else "BubbleActionsTheirs"

  window.GetVisualDescendants()
  |> Seq.iter(fun visual ->
    match visual with
    | :? Control as control when visual.Name = name ->
      control.IsVisible <- true
    | _ -> ())

/// The text the Remotes sidebar entry's badge shows, when it shows one.
let badgeText(window: Window) : string option =
  buttonWithText(window, "Remotes")
  |> Option.bind(fun button ->
    button.GetVisualDescendants()
    |> Seq.tryPick(fun visual ->
      match visual with
      | :? TextBlock as block ->
        match block.Text with
        | NonNull text when
          text <> "Remotes" && not(String.IsNullOrEmpty text)
          ->
          Some text
        | _ -> None
      | _ -> None))

let tests =
  testList "remotes page" [
    testCaseAsync
      "the inbox lists the open conversations"
      (async {
        let page =
          Remotes.inboxPageJson(
            [
              rowAt(hubotJson, "2026-10-21T04:00:00Z", 2)
              rowAt(bobJson, "2026-10-20T04:00:00Z", 0)
            ],
            false,
            3,
            0
          )

        let api = pageApi(page, page, page)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openRemotesAsync window

        let! listed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "@hubot") && hasText(root, "@bob"))
          )

        Expect.isTrue listed "the conversations never listed"

        // The ahead pill shows its count; the quiet row shows its time.
        let! ahead =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "2 ahead"))
          )

        Expect.isTrue ahead "the ahead pill never showed"
      })

    testCaseAsync
      "the fetched tab follows the account's flag"
      (async {
        let page = Remotes.inboxPageJson([], false, 0, 0)
        let api = pageApi(page, page, page)

        let! quiet = startShell(remotesOnStore(false, 0), api)

        do! openRemotesAsync quiet

        let! settled =
          waitForAsync(
            5000,
            (fun () -> quiet: Control),
            (fun root -> hasText(root, "Open") && hasText(root, "Stashed"))
          )

        Expect.isTrue settled "the tabs never rendered"

        let! fetched =
          probeUiAsync(fun () ->
            buttonWithText(quiet, "Fetched") |> Option.isSome)

        Expect.isFalse fetched "the fetched tab showed while off"

        let! taking = startShell(remotesOnStore(true, 0), api)

        do! openRemotesAsync taking

        let! fetchedOn =
          waitForAsync(
            5000,
            (fun () -> taking),
            (fun root -> buttonWithText(root, "Fetched") |> Option.isSome)
          )

        Expect.isTrue fetchedOn "the fetched tab never showed"
      })

    testCaseAsync
      "the fetched tab carries its waiting count"
      (async {
        let page = Remotes.inboxPageJson([], false, 0, 2)
        let api = pageApi(page, page, page)
        let! window = startShell(remotesOnStore(true, 0), api)

        do! openRemotesAsync window

        let! badged =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              buttonWithText(root, "Fetched")
              |> Option.map(fun tab -> hasText(tab, "2"))
              |> Option.defaultValue false)
          )

        Expect.isTrue badged "the waiting count never showed"
      })

    testCaseAsync
      "search narrows the rows to the answer"
      (async {
        let page =
          Remotes.inboxPageJson(
            [
              rowAt(hubotJson, "2026-10-21T04:00:00Z", 0)
              rowAt(bobJson, "2026-10-20T04:00:00Z", 0)
            ],
            false,
            0,
            0
          )

        let found =
          Remotes.inboxPageJson(
            [ rowAt(hubotJson, "2026-10-21T04:00:00Z", 0) ],
            false,
            0,
            0
          )

        let api = pageApi(page, page, found)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openRemotesAsync window

        let! listed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "@bob"))
          )

        Expect.isTrue listed "the conversations never listed"

        do!
          onUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? TextBox as box when box.Name = "RemotesSearch" -> Some box
              | _ -> None)
            |> Option.iter(fun box -> box.Text <- "hub"))

        let! narrowed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "@bob")))
          )

        Expect.isTrue narrowed "the search never narrowed the rows"

        let asked =
          api.Requests
          |> List.exists(fun request ->
            request.Target.StartsWith "/api/v1/remotes"
            && request.Target.Contains "q=hub")

        Expect.isTrue asked "the search never asked the api"
      })

    testCaseAsync
      "the end of the list asks for the next page"
      (async {
        let page = manyRowsJson 30
        let nextPage = Remotes.inboxPageJson([], false, 3, 0)
        let api = pageApi(page, nextPage, page)

        let! window = startShell(remotesOnStore(false, 0), api)

        do! openRemotesAsync window

        let! loaded =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "@dev7"))
          )

        Expect.isTrue loaded "the first page never listed"

        do! shrinkAsync window

        // The inbox's own scroller, driven to its end so the probe
        // there asks the page for the next page.
        let! inbox =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? ScrollViewer as scroller when
                scroller.Name = "RemotesInbox"
                ->
                Some scroller
              | _ -> None))

        Expect.isSome inbox "the inbox scroller never rendered"

        do! onUiAsync(fun () -> inbox.Value.ScrollToEnd())
        do! settleAsync()

        // The rows must overflow: the end is a place the scroll can
        // reach, not the whole list already on screen.
        let! placed =
          probeUiAsync(fun () ->
            inbox.Value.Extent.Height, inbox.Value.Viewport.Height)

        let extent, viewport = placed

        Expect.isGreaterThan
          extent
          (viewport + 240.0)
          "the rows never overflowed the inbox"

        let! paged =
          waitForAsync(
            5000,
            (fun () ->
              api.Requests
              |> List.exists(fun request ->
                request.Target.StartsWith "/api/v1/remotes"
                && request.Target.Contains "before=")),
            id
          )

        Expect.isTrue paged "the scroll end never asked for the next page"
      })

    testCaseAsync
      "the sidebar badge follows the store's unread"
      (async {
        let page = Remotes.inboxPageJson([], false, 3, 0)
        let api = pageApi(page, page, page)

        let! window = startShell(remotesOnStore(false, 3), api)

        do! openRemotesAsync window

        let! badged =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> badgeText root = Some "3")
          )

        Expect.isTrue badged "the badge never showed the unread count"
      })

    testCaseAsync
      "a merge settles a fetched request"
      (async {
        let fetched =
          Remotes.inboxPageJson(
            [ rowAt(hubotJson, "2026-10-21T04:00:00Z", 0) ],
            false,
            0,
            1
          )

        let api =
          FakeApi(fun request ->
            if
              request.Method = "POST"
              && request.Target = "/api/v1/remotes/@hubot/merge"
            then
              ok "{}"
            elif request.Target.Contains "tab=fetched" then
              ok fetched
            elif request.Target.StartsWith "/api/v1/remotes" then
              ok(Remotes.inboxPageJson([], false, 0, 1))
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let! window = startShell(remotesOnStore(true, 0), api)

        do! openRemotesAsync window

        do! clickWhenShown(window, "Fetched")

        let! rowShown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "@hubot"))
          )

        Expect.isTrue rowShown "the fetched row never listed"

        do! clickWhenShown(window, "merge")

        let! merged =
          waitForAsync(
            5000,
            (fun () ->
              api.Requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target = "/api/v1/remotes/@hubot/merge")),
            id
          )

        Expect.isTrue merged "the merge never reached the api"
      })

    testCaseAsync
      "leaving the page releases its rows"
      (async {
        let page = manyRowsJson 8
        let api = pageApi(page, page, page)

        let! window = startShell(remotesOnStore(false, 0), api)

        do! openRemotesAsync window

        let! listed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "@dev7"))
          )

        Expect.isTrue listed "the conversations never listed"

        let! weak =
          probeUiAsync(fun () ->
            textBlockWith(window, "@dev7")
            |> Option.map(fun block -> WeakReference(block)))

        Expect.isSome weak "the row never rendered"

        do! clickWhenShown(window, "Settings")
        do! settleAsync()
        collect()

        Expect.isFalse weak.Value.IsAlive "the row outlived its page"
      })
  ]

/// The api of the thread tests: the inbox lists one conversation, and
/// the thread answers from cans, with its older page behind a before
/// cursor.
let threadApi(threadBody: string, olderBody: string) : FakeApi =
  FakeApi(fun request ->
    if
      request.Target.StartsWith "/api/v1/remotes/@hubot/messages"
      && request.Target.Contains "before="
    then
      ok olderBody
    elif request.Target.StartsWith "/api/v1/remotes/@hubot/messages" then
      ok threadBody
    elif request.Target.StartsWith "/api/v1/remotes" then
      ok(
        Remotes.inboxPageJson(
          [ rowAt(hubotJson, "2026-10-21T04:00:00Z", 0) ],
          false,
          0,
          0
        )
      )
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// Opens the one conversation the thread api serves.
let openConversationAsync(window: Window) : Async<unit> = async {
  do! openRemotesAsync window

  let! row =
    waitForAsync(
      5000,
      (fun () -> window),
      (fun root -> buttonWithText(root, "@hubot") |> Option.isSome)
    )

  Expect.isTrue row "the conversation row never listed"

  do! onUiAsync(fun () -> buttonWithText(window, "@hubot") |> Option.iter click)
}

let threadTests =
  testList "remotes thread" [
    testCaseAsync
      "a narrow window shows one pane and the back row returns"
      (async {
        let thread =
          Remotes.threadJson(
            [ Remotes.messageJson(41, partnerId, "one message", "c-41") ],
            41,
            "",
            false,
            0
          )

        let api = threadApi(thread, thread)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window
        do! settleAsync()

        // Wide: both panes, no back row.
        let! wideBack =
          probeUiAsync(fun () -> controlShown(window, "BackToInbox"))

        Expect.isFalse wideBack "the back row showed on a wide window"

        do! shrinkAsync window
        do! settleAsync()

        // Narrow: the thread owns the strip, the inbox hides, and the
        // back row shows.
        let! back =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> controlShown(root, "BackToInbox")
          )

        Expect.isTrue back "the back row never showed"

        let! inboxHidden =
          probeUiAsync(fun () -> controlShown(window, "RemotesSearch"))

        Expect.isFalse inboxHidden "the inbox stayed on the narrow window"

        let! threadShown =
          probeUiAsync(fun () -> controlShown(window, "RemotesThread"))

        Expect.isTrue threadShown "the thread lost the narrow window"

        do!
          onUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? Button as backRow when visual.Name = "BackToInbox" ->
                Some backRow
              | _ -> None)
            |> Option.iter click)

        let! returned =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> controlShown(root, "RemotesSearch")
          )

        Expect.isTrue returned "the inbox never came back"

        let! backGone =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(controlShown(root, "BackToInbox"))
          )

        Expect.isTrue backGone "the back row stayed after the return"
      })

    testCaseAsync
      "every run ends in its clock time"
      (async {
        // Two of mine with one of theirs between: my older run keeps
        // a right-aligned clock, theirs keeps its left one, and only
        // the newest of mine swaps its line for the receipt.
        let thread =
          Remotes.threadJson(
            [
              Remotes.messageJson(41, partnerId, "hello there", "c-41")
              Remotes.messageJson(42, viewerId, "hi back", "c-42")
              Remotes.messageJson(43, partnerId, "still there?", "c-43")
              Remotes.messageJson(44, viewerId, "yes still here", "c-44")
            ],
            44,
            "",
            false,
            0
          )

        let api = threadApi(thread, thread)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        let at =
          DateTimeOffset
            .Parse("2026-10-20T04:00:00Z")
            .ToLocalTime()
            .ToString("h:mm tt")

        let! mine =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual ->
                match visual with
                | :? TextBlock as block ->
                  block.Text = at
                  && block.HorizontalAlignment =
                    Avalonia.Layout.HorizontalAlignment.Right
                | _ -> false)
          )

        Expect.isTrue mine "my older run never showed its clock time"

        let! theirs =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.exists(fun visual ->
              match visual with
              | :? TextBlock as block ->
                block.Text = at
                && block.HorizontalAlignment =
                  Avalonia.Layout.HorizontalAlignment.Left
              | _ -> false))

        Expect.isTrue theirs "their run never showed its clock time"
      })

    testCaseAsync
      "a conversation opens with its messages"
      (async {
        let thread =
          Remotes.threadJson(
            [
              Remotes.messageJson(41, partnerId, "hello there", "c-41")
              Remotes.messageJson(42, viewerId, "hi back", "c-42")
              Remotes.messageJson(43, partnerId, "still there?", "c-43")
            ],
            43,
            "",
            false,
            0
          )

        let api = threadApi(thread, thread)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        let! opened =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual -> visual.Name = "RemotesThread"))
          )

        Expect.isTrue opened "the thread pane never rendered"

        let! listed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              hasText(root, "hello there") && hasText(root, "hi back"))
          )

        Expect.isTrue listed "the messages never listed"

        // My newest message carries its receipt — the pull's word with
        // its own clock time, or the push's.
        let! receipt =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual ->
                match visual with
                | :? TextBlock as block ->
                  let text =
                    block.Text |> Option.ofObj |> Option.defaultValue ""

                  text = "pushed" || text.StartsWith "pulled"
                | _ -> false))
          )

        Expect.isTrue receipt "the receipt never showed"

        // The partner's runs end in their own clock time, the site's
        // meta line under every run that is not the viewer's.
        let at =
          DateTimeOffset
            .Parse("2026-10-20T04:00:00Z")
            .ToLocalTime()
            .ToString("h:mm tt")

        let! stamped =
          waitForAsync(5000, (fun () -> window), fun root -> hasText(root, at))

        Expect.isTrue stamped "the partner's clock time never showed"
      })

    testCaseAsync
      "the unread divider marks the messages not seen"
      (async {
        let thread =
          Remotes.threadJson(
            [
              Remotes.messageJson(41, partnerId, "seen already", "c-41")
              Remotes.messageJson(42, partnerId, "new one", "c-42")
              Remotes.messageJson(43, partnerId, "new two", "c-43")
            ],
            41,
            "",
            false,
            0
          )

        let api = threadApi(thread, thread)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        let! divided =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "HEAD · 2 new"))
          )

        Expect.isTrue divided "the unread divider never showed"
      })

    testCaseAsync
      "scrolling to the top loads the older page"
      (async {
        // Enough messages to overflow the pane, so the scroller can
        // leave the top and reach it again.
        let messages = [
          for index in 1..20 ->
            Remotes.messageJson(
              20 + index,
              partnerId,
              $"message {index}",
              $"c-{20 + index}"
            )
        ]

        let thread =
          Remotes.threadJson(messages, 40, "", false, 0)
          |> fun body ->
            body.Replace("\"has_older\":false", "\"has_older\":true")

        let older =
          Remotes.threadJson(
            [
              Remotes.messageJson(19, partnerId, "the older page", "c-19")
              Remotes.messageJson(20, partnerId, "between pages", "c-20")
            ],
            40,
            "",
            false,
            0
          )

        let api = threadApi(thread, older)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        let! rendered =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual -> visual.Name = "RemotesThread"))
          )

        Expect.isTrue rendered "the thread pane never rendered"

        let! scroller =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? ScrollViewer as viewer when viewer.Name = "RemotesThread" ->
                Some viewer
              | _ -> None))

        Expect.isSome scroller "the thread scroller never rendered"

        let! overflows =
          waitForAsync(
            5000,
            (fun () -> scroller.Value),
            (fun viewer -> viewer.Extent.Height > viewer.Viewport.Height + 48.0)
          )

        Expect.isTrue overflows "the messages never overflowed the pane"

        do!
          onUiAsync(fun () ->
            scroller.Value.ScrollToEnd()
            scroller.Value.Offset <- Vector(0.0, 0.0))

        do! settleAsync()

        let! paged =
          waitForAsync(
            5000,
            (fun () ->
              api.Requests
              |> List.exists(fun request ->
                request.Target.StartsWith "/api/v1/remotes/@hubot/messages"
                && request.Target.Contains "before=")),
            id
          )

        Expect.isTrue paged "the scroll top never asked for the older page"

        let! olderListed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "the older page"))
          )

        Expect.isTrue olderListed "the older page never listed"
      })

    testCaseAsync
      "a returning page reopens the same conversation"
      (async {
        let thread =
          Remotes.threadJson(
            [ Remotes.messageJson(41, partnerId, "one message", "c-41") ],
            41,
            "",
            false,
            0
          )

        let api = threadApi(thread, thread)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        let! opened =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual -> visual.Name = "RemotesThread"))
          )

        Expect.isTrue opened "the thread pane never rendered"

        do! clickWhenShown(window, "Settings")
        do! openRemotesAsync window

        let! reopened =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual -> visual.Name = "RemotesThread"))
          )

        Expect.isTrue reopened "the conversation never reopened"
      })

    testCaseAsync
      "a ghost the thread cannot reach says so"
      (async {
        let thread =
          Remotes.threadJson(
            [ Remotes.messageJson(41, partnerId, "an answer", "c-41") ],
            41,
            "",
            false,
            0
          )
          |> fun body ->
            body.Replace(
              "\"reply_to_id\":null",
              "\"reply_to_id\":99,\"reply_to\":{\"id\":99,\"user_id\":\""
              + partnerId
              + "\",\"body\":\"the original\",\"images_count\":0,\"has_voice\":false}"
            )

        let api = threadApi(thread, thread)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        do! clickWhenShown(window, "the original")

        let! refused =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              hasText(root, "Could not load this message. Try again."))
          )

        Expect.isTrue refused "the unreachable ghost never said so"
      })

    testCaseAsync
      "leaving the page releases the thread rows"
      (async {
        let thread =
          Remotes.threadJson(
            [ Remotes.messageJson(41, partnerId, "held weakly", "c-41") ],
            41,
            "",
            false,
            0
          )

        let api = threadApi(thread, thread)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        let! listed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "held weakly"))
          )

        Expect.isTrue listed "the message never listed"

        let! weak =
          probeUiAsync(fun () ->
            textBlockWith(window, "held weakly")
            |> Option.map(fun block -> WeakReference(block)))

        Expect.isSome weak "the bubble never rendered"

        do! clickWhenShown(window, "Settings")
        do! settleAsync()
        collect()

        Expect.isFalse weak.Value.IsAlive "the bubble outlived its page"
      })

    testCaseAsync
      "the typing line carries the partner's word and its dots"
      (async {
        let thread =
          Remotes.threadJson(
            [ Remotes.messageJson(41, partnerId, "hello there", "c-41") ],
            41,
            "",
            true,
            0
          )

        let api = threadApi(thread, thread)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        let! word =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "@hubot is typing")
          )

        Expect.isTrue word "the typing word never showed"

        let! dots =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.filter(fun visual ->
              match visual with
              | :? Ellipse as dot ->
                dot.Classes
                |> Seq.exists(fun klass -> klass.StartsWith "typing-dot-")
              | _ -> false)
            |> Seq.length)

        Expect.isGreaterThan dots 0 "the typing dots never drew"
      })

    testCaseAsync
      "the open conversation's marker stands at the row's start"
      (async {
        let thread =
          Remotes.threadJson(
            [ Remotes.messageJson(41, partnerId, "hello there", "c-41") ],
            41,
            "",
            false,
            0
          )

        let api = threadApi(thread, thread)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        // The rail owns the row's first dock slot: its left edge is the
        // row's own, and the row's button starts after it. A rail left
        // to a panel's default alignment centers over the row's middle.
        let! placed =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              root.GetVisualDescendants()
              |> Seq.tryPick(fun visual ->
                match visual with
                | :? Border as rail when rail.Name = "SelectedRail" ->
                  match rail.Parent with
                  | :? DockPanel as row ->
                    row.Children
                    |> Seq.tryPick(function
                      | :? Button as button -> Some(rail, button)
                      | _ -> None)
                  | _ -> None
                | _ -> None)
              |> Option.map(fun (rail, button) ->
                rail.Bounds.Width > 0.0
                && rail.Bounds.X < 1.0
                && button.Bounds.Left >= rail.Bounds.Right)
              |> Option.defaultValue false
          )

        Expect.isTrue placed "the marker never stood at the row's start"
      })

    testCaseAsync
      "the gap between the hover actions stays inside the row"
      (async {
        let thread =
          Remotes.threadJson(
            [ Remotes.messageJson(41, partnerId, "hello there", "c-41") ],
            41,
            "",
            false,
            0
          )

        let api = threadApi(thread, thread)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        let! built =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual -> visual.Name = "BubbleActionsTheirs")
          )

        Expect.isTrue built "the actions panel never built"

        do! onUiAsync(fun () -> hoverMessage(window, 41, false))

        // The two 28px icons leave a 2px gap between them; the row must
        // own that stretch, or a pointer crossing it drops the hover and
        // the far icon goes unreachable. The probe hit-tests the row
        // itself at the gap's point: the hit scope is the row's
        // subtree, and the row's background is what holds the pointer.
        let! held =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              root.GetVisualDescendants()
              |> Seq.tryPick(fun visual ->
                match visual with
                | :? Control as panel when
                  visual.Name = "BubbleActionsTheirs"
                  && panel.Bounds.Width >= 58.0
                  ->
                  panel.GetVisualAncestors()
                  |> Seq.tryPick(function
                    | :? DockPanel as row when
                      row.Classes |> Seq.contains "msg-row"
                      ->
                      let gap =
                        panel.Bounds.Position
                        + Point(29.0, panel.Bounds.Height / 2.0)

                      let hit = row.InputHitTest gap

                      match hit with
                      | null -> Some false
                      | hit ->
                        match hit with
                        | :? Visual as struck ->
                          let rowVisual = row :> Visual

                          Some(
                            struck = rowVisual
                            || struck.GetVisualAncestors()
                               |> Seq.exists(fun up -> up = rowVisual)
                          )
                        | _ -> Some false
                    | _ -> None)
                | _ -> None)
              |> Option.defaultValue false
          )

        Expect.isTrue held "the gap between the icons left the row"
      })

    testCaseAsync
      "the present pill carries its word, not its observable"
      (async {
        let thread =
          Remotes.threadJson(
            [ Remotes.messageJson(41, partnerId, "hello there", "c-41") ],
            41,
            "",
            false,
            0
          )

        let api = threadApi(thread, thread)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        // The pill stays hidden while the reading rides the bottom
        // edge, but its content binds regardless: the word arrives,
        // never the observable itself.
        let! word =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual ->
                match visual with
                | :? Button as pill ->
                  match pill.Content with
                  | :? string as text -> text = "back to present"
                  | _ -> false
                | _ -> false)
          )

        Expect.isTrue word "the present pill never carried its word"
      })
  ]

/// How the push route answers in the composer tests.
type PushOutcome =
  | Stored
  | Refused
  | RefusedOnce

/// The client id one push body carried, so the canned answer echoes it
/// and the store can swap its staged copy.
let clientIdOf(body: string) : string =
  let at = body.IndexOf("\"client_id\"")

  if at < 0 then
    failwith "the push carried no client id"
  else
    let tail = body.Substring(at)
    (tail.Split('"'))[3]

/// The api of the composer tests: the thread answers from a can, and
/// the push route answers the canned outcome — the stored message
/// under the sent client id, a refusal, or the first answer refused
/// then the stored one. Byte downloads answer through the given
/// handler when a test needs one.
let private composerApiOf
  (
    threadBody: string,
    outcome: PushOutcome,
    bytes: (string -> Result<byte array, TransportFailure>) option
  ) : FakeApi =
  let served = ResizeArray<string>()
  let answered = ResizeArray<HttpResponse>()

  let refused = {
    Status = 422
    Headers = []
    Body = "{\"message\":\"nope\"}"
  }

  let route(request: HttpRequest) : HttpResponse =
    if
      request.Method = "POST"
      && request.Target.StartsWith "/api/v1/remotes/@hubot/messages"
    then
      let body = Remotes.payloadText request
      served.Add body |> ignore

      let stored =
        ok(
          "{\"data\":"
          + Remotes.messageJson(44, viewerId, "landed", clientIdOf body)
          + "}"
        )

      let answer =
        match outcome with
        | Stored -> stored
        | Refused -> refused
        | RefusedOnce when served.Count = 1 -> refused
        | RefusedOnce -> stored

      answered.Add answer |> ignore
      answer
    elif request.Target.StartsWith "/api/v1/remotes/@hubot/messages" then
      ok threadBody
    elif request.Target.StartsWith "/api/v1/remotes" then
      ok(
        Remotes.inboxPageJson(
          [ rowAt(hubotJson, "2026-10-21T04:00:00Z", 0) ],
          false,
          0,
          0
        )
      )
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}")

  match bytes with
  | Some serve -> FakeApi(route, serve)
  | None -> FakeApi(route)

/// The api of the composer tests, over the default byte downloads.
let composerApi(threadBody: string, outcome: PushOutcome) : FakeApi =
  composerApiOf(threadBody, outcome, None)

/// The api of the voice tests: the composer api over the canned voice
/// bytes.
let voiceApi
  (
    threadBody: string,
    outcome: PushOutcome,
    bytes: string -> Result<byte array, TransportFailure>
  ) : FakeApi =
  composerApiOf(threadBody, outcome, Some bytes)

/// One quiet thread to open the composer against.
let quietThread() : string =
  Remotes.threadJson(
    [ Remotes.messageJson(41, partnerId, "hello there", "c-41") ],
    41,
    "",
    false,
    0
  )

/// One thread whose message already carries a reaction from the
/// partner, so the chip exists before anything is clicked.
let reactedThread() : string =
  Remotes
    .threadJson(
      [ Remotes.messageJson(41, partnerId, "react to me", "c-41") ],
      41,
      "",
      false,
      0
    )
    .Replace(
      "\"reactions\":[]",
      "\"reactions\":[{\"user_id\":\"" + partnerId + "\",\"emoji\":\"😂\"}]"
    )

/// The api of the reaction tests: the thread answers from a can, the
/// reaction routes answer their cans.
let reactApi(threadBody: string) : FakeApi =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/remotes/reactions" then
      ok(Remotes.reactionsJson([ viewerId ], "❤️"))
    elif request.Target.StartsWith "/api/v1/remotes/messages/41/reactions" then
      ok(Remotes.reactionsJson([ viewerId; partnerId ], "❤️"))
    elif request.Target.StartsWith "/api/v1/remotes/@hubot/messages" then
      ok threadBody
    elif request.Target.StartsWith "/api/v1/remotes" then
      ok(
        Remotes.inboxPageJson(
          [ rowAt(hubotJson, "2026-10-21T04:00:00Z", 0) ],
          false,
          0,
          0
        )
      )
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

let reactTests =
  testList "remotes reactions" [
    testCaseAsync
      "a message's reaction chip lists who reacted"
      (async {
        let api = reactApi(reactedThread())
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        let! chipped =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "😂") && hasText(root, "1"))
          )

        Expect.isTrue chipped "the chip never showed"
      })

    testCaseAsync
      "a hover react opens the tray and a pick reacts"
      (async {
        let api = reactApi(quietThread())
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        // The reaction actions live in the hover group beside the
        // bubble: the pointer over the row shows them.
        do! onUiAsync(fun () -> hoverMessage(window, 41, false))
        do! settleAsync()
        do! clickWhenShownByTip(window, "Add reaction")

        // The tray carries the three defaults; a pick lands the chip
        // at once from the optimistic toggle.
        let! picks =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "❤️") |> Option.isSome)
          )

        Expect.isTrue picks "the tray never showed"

        do! clickWhenShown(window, "❤️")

        let! chipped =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "❤️"))
          )

        Expect.isTrue chipped "the optimistic chip never showed"

        let! settled =
          waitForAsync(
            5000,
            (fun () ->
              api.Requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target.StartsWith
                  "/api/v1/remotes/messages/41/reactions")),
            id
          )

        Expect.isTrue settled "the reaction never reached its route"

        let! held =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "2"))
          )

        Expect.isTrue held "the settled reactions never rendered"
      })

    testCaseAsync
      "the reaction tray offers the quick three and the ranked rest"
      (async {
        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/remotes/reactions" then
              ok
                "{\"usage\":[{\"emoji\":\"🎉\",\"count\":9},{\"emoji\":\"😂\",\"count\":0}]}"
            elif
              request.Target.StartsWith "/api/v1/remotes/@hubot/messages"
            then
              ok(quietThread())
            elif request.Target.StartsWith "/api/v1/remotes" then
              ok(
                Remotes.inboxPageJson(
                  [ rowAt(hubotJson, "2026-10-21T04:00:00Z", 0) ],
                  false,
                  0,
                  0
                )
              )
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        // Open the hover group, then the tray.
        do! onUiAsync(fun () -> hoverMessage(window, 41, false))
        do! settleAsync()
        do! clickWhenShownByTip(window, "Add reaction")

        let! ranked =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "🎉") |> Option.isSome)
          )

        Expect.isTrue ranked "the ranked emoji never offered"
      })
  ]

/// Types into the composer's box, the way a user does. The box lands
/// one binding beat after the pane, so the wait comes first.
let typeIntoComposer(window: Window, text: string) : Async<unit> = async {
  let! shown =
    waitForAsync(
      5000,
      (fun () -> window),
      (fun root ->
        root.GetVisualDescendants()
        |> Seq.exists(fun visual -> visual.Name = "RemotesComposer"))
    )

  Expect.isTrue shown "the composer box never rendered"

  do!
    onUiAsync(fun () ->
      window.GetVisualDescendants()
      |> Seq.tryPick(fun visual ->
        match visual with
        | :? TextBox as box when box.Name = "RemotesComposer" -> Some box
        | _ -> None)
      |> Option.iter(fun box -> box.Text <- text))
}

/// True when a text block carrying the text is actually shown: the
/// block and every ancestor stand visible.
let showsText(root: Control, text: string) : bool =
  root.GetVisualDescendants()
  |> Seq.exists(fun visual ->
    match visual with
    | :? TextBlock as block when block.Text = text ->
      block.IsVisible
      && block.GetVisualAncestors()
         |> Seq.forall(fun ancestor ->
           match ancestor with
           | :? Control as control -> control.IsVisible
           | _ -> true)
    | _ -> false)

let composerTests =
  testList "remotes composer" [
    testCaseAsync
      "a push stages at once and swaps in the stored message"
      (async {
        let api = composerApi(quietThread(), Stored)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window
        do! typeIntoComposer(window, "first push")
        do! clickWhenShown(window, "Push")

        // The staged word stands while the server has not answered,
        // then the stored message takes its place.
        let! landed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              showsText(root, "landed") && not(showsText(root, "staged")))
          )

        Expect.isTrue landed "the staged message never swapped"

        let pushed =
          api.Requests
          |> List.exists(fun request ->
            request.Method = "POST"
            && request.Target.StartsWith "/api/v1/remotes/@hubot/messages")

        Expect.isTrue pushed "the push never reached the api"

        // The draft clears with the send.
        let! cleared =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? TextBox as box when box.Name = "RemotesComposer" ->
                Some box.Text
              | _ -> None))

        Expect.equal cleared (Some "") "the draft never cleared"
      })

    testCaseAsync
      "enter pushes and shift-enter keeps the line"
      (async {
        let api = composerApi(quietThread(), Stored)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window
        do! typeIntoComposer(window, "by enter")

        let composer() =
          window.GetVisualDescendants()
          |> Seq.tryPick(fun visual ->
            match visual with
            | :? TextBox as box when box.Name = "RemotesComposer" -> Some box
            | _ -> None)

        let press(key: Key, modifiers: KeyModifiers) =
          composer()
          |> Option.iter(fun box ->
            box.RaiseEvent(
              KeyEventArgs(
                RoutedEvent = InputElement.KeyDownEvent,
                Key = key,
                KeyModifiers = modifiers
              )
            ))

        let pushedOut() =
          api.Requests
          |> List.exists(fun request ->
            request.Method = "POST"
            && request.Target.StartsWith "/api/v1/remotes/@hubot/messages")

        // Shift+Enter only makes the newline: nothing goes out.
        do! onUiAsync(fun () -> press(Key.Enter, KeyModifiers.Shift))
        do! settleAsync()

        let! held = probeUiAsync pushedOut

        Expect.isFalse held "shift-enter pushed"

        do! onUiAsync(fun () -> press(Key.Enter, KeyModifiers.None))

        let! pushed =
          waitForAsync(
            5000,
            (fun () -> api),
            fun fake ->
              fake.Requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target.StartsWith "/api/v1/remotes/@hubot/messages")
          )

        Expect.isTrue pushed "plain enter never pushed"
      })

    testCaseAsync
      "a refused push retries under its own client id"
      (async {
        let api = composerApi(quietThread(), RefusedOnce)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window
        do! typeIntoComposer(window, "try twice")
        do! clickWhenShown(window, "Push")

        let! refused =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "rejected · retry"))
          )

        Expect.isTrue refused "the refusal never showed"

        do! clickWhenShown(window, "retry")

        let! landed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "landed"))
          )

        Expect.isTrue landed "the retry never landed"

        // Both posts carried one client id, so the server stores the
        // message once at most.
        let clientIds =
          api.Requests
          |> List.filter(fun request ->
            request.Method = "POST"
            && request.Target.StartsWith "/api/v1/remotes/@hubot/messages")
          |> List.map Remotes.bodyText
          |> List.map(fun body ->
            let at = body.IndexOf("\"client_id\"")

            if at < 0 then
              failwith "the push carried no client id"
            else
              let tail = body.Substring(at)
              let id = (tail.Split(':'))[1]
              id.Trim('"', ',', '}', ' '))

        Expect.equal
          (List.distinct clientIds |> List.length)
          1
          "the retry changed the client id"

        Expect.isGreaterThan
          (List.length clientIds)
          1
          "the retry never went out"
      })

    testCaseAsync
      "the counter shows past the threshold"
      (async {
        let api = composerApi(quietThread(), Stored)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        let long = String('a', 1700)

        do! typeIntoComposer(window, long)

        let! counted =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "1,700/2,000"))
          )

        Expect.isTrue counted "the counter never showed"
      })

    testCaseAsync
      "the composer wraps its spaceless text"
      (async {
        let api = composerApi(quietThread(), Stored)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window
        do! typeIntoComposer(window, String('a', 100))

        let! wrapped =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual ->
                match visual with
                | :? TextBox as box when
                  box.Name = "RemotesComposer"
                  && box.TextWrapping = Avalonia.Media.TextWrapping.Wrap
                  ->
                  true
                | _ -> false)
          )

        Expect.isTrue wrapped "the composer never wrapped"
      })

    testCaseAsync
      "the box fills its row and Push stays on the pane"
      (async {
        let api = composerApi(quietThread(), Stored)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window
        do! typeIntoComposer(window, String('a', 300))
        do! settleAsync()

        let! push =
          probeUiAsync(fun () ->
            buttonWithText(window, "Push")
            |> Option.map(fun button -> button.Bounds))

        Expect.isSome push "the Push button never rendered"

        let! box =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? TextBox as box when box.Name = "RemotesComposer" ->
                Some box.Bounds
              | _ -> None))

        Expect.isSome box "the composer box never rendered"

        // A stacked row measured the box to its typed length: the box
        // stayed narrow and Push fell past the pane's edge.
        Expect.isGreaterThan
          box.Value.Width
          120.0
          "the box never filled its row"

        Expect.isLessThanOrEqual
          push.Value.Right
          window.ClientSize.Width
          "Push fell off the pane"
      })

    testCaseAsync
      "a pasted clipboard image stages on the composer"
      (async {
        let attachments = FakeAttachments()
        attachments.ClipboardImage <- Some(stagedImage "pasted.png")

        let api = composerApi(quietThread(), Stored)

        let! window =
          startAttachedShell(remotesOnStore(false, 0), api, attachments)

        do! openConversationAsync window
        do! typeIntoComposer(window, "")

        do!
          onUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? TextBox as box when box.Name = "RemotesComposer" ->
                Some box
              | _ -> None)
            |> Option.iter(fun box ->
              box.RaiseEvent(
                KeyEventArgs(
                  RoutedEvent = InputElement.KeyDownEvent,
                  Key = Key.V,
                  KeyModifiers = KeyModifiers.Control
                )
              )))

        let! chipped =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "pasted.png"))
          )

        Expect.isTrue chipped "the pasted image never staged"
      })

    testCaseAsync
      "typing an @ offers the partner's handle and a pick completes it"
      (async {
        let api = composerApi(quietThread(), Stored)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        do! typeIntoComposer(window, "")

        do!
          onUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? TextBox as box when box.Name = "RemotesComposer" ->
                Some box
              | _ -> None)
            |> Option.iter(fun box ->
              box.Text <- "ping @hu"
              box.CaretIndex <- 8))

        let! offered =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual ->
                match visual with
                | :? Button as pick when pick.Name = "MentionPick" ->
                  pick.IsVisible
                | _ -> false))
          )

        Expect.isTrue offered "the mention never offered its handles"

        // The pick carries a panel, not a text button: click it by its
        // own name, not by the row button the inbox shares the handle
        // with.
        let! pickShown =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual ->
                match visual with
                | :? Button as pick when pick.Name = "MentionPick" ->
                  pick.IsVisible
                | _ -> false)
          )

        Expect.isTrue pickShown "the mention pick never showed"

        do!
          onUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? Button as pick when pick.Name = "MentionPick" -> Some pick
              | _ -> None)
            |> Option.iter click)

        let! completed =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? TextBox as box when box.Name = "RemotesComposer" ->
                Some box.Text
              | _ -> None))

        Expect.equal
          completed
          (Some "ping @hubot ")
          "the pick never completed the handle"

        let! closed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              root.GetVisualDescendants()
              |> Seq.forall(fun visual ->
                match visual with
                | :? Button as pick when pick.Name = "MentionPick" ->
                  not pick.IsVisible
                | _ -> true))
          )

        Expect.isTrue closed "the offer stayed open past the handle"
      })

    testCaseAsync
      "the ping pick reaches its route"
      (async {
        let api = composerApi(quietThread(), Stored)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window
        do! clickWhenShownByTip(window, "Ping @hubot")

        let pinged() =
          api.Requests
          |> List.exists(fun request ->
            request.Method = "POST"
            && request.Target = "/api/v1/remotes/@hubot/ping")

        let! pinged' = waitForAsync(5000, (fun () -> pinged()), id)

        Expect.isTrue pinged' "the ping never reached its route"
      })

    testCaseAsync
      "the stash restores an unsent draft"
      (async {
        let api = composerApi(quietThread(), Stored)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window
        do! typeIntoComposer(window, "unsent words")

        // The stash parks once the typing rests.
        do! Async.Sleep 450
        do! settleAsync()

        do! clickWhenShown(window, "Settings")
        do! openRemotesAsync window

        // The restore is silent, the site's own chat behavior: no
        // chip, no word — the draft itself comes back.
        let! landed =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual ->
                match visual with
                | :? TextBox as box when
                  box.Name = "RemotesComposer" && box.Text = "unsent words"
                  ->
                  true
                | _ -> false)
          )

        Expect.isTrue landed "the draft never restored"

        let! chip = probeUiAsync(fun () -> hasText(window, "stash@{0}"))

        Expect.isFalse chip "the restore was not silent"
      })

    testCaseAsync
      "the access bar takes the composer's place"
      (async {
        let closed =
          quietThread().Replace("\"access\":\"open\"", "\"access\":\"closed\"")

        let api = composerApi(closed, Stored)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        let! barred =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "@hubot isn't taking remotes"))
          )

        Expect.isTrue barred "the closed copy never showed"

        let! composerGone =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.forall(fun visual -> visual.Name <> "RemotesComposer"))

        Expect.isTrue composerGone "the composer stayed open"
      })
  ]

/// The api of the management tests: the thread and inbox answer from
/// cans, and the named action routes answer their own canned
/// responses; every other route answers an empty ok.
let manageApi
  (threadBody: string, canned: (string * HttpResponse) list)
  : FakeApi =
  FakeApi(fun request ->
    let route =
      canned
      |> List.tryFind(fun (prefix, _) -> request.Target.StartsWith prefix)

    match route with
    | Some(_, answer) -> answer
    | None ->
      if request.Target.StartsWith "/api/v1/remotes/@hubot/messages" then
        ok threadBody
      elif request.Target.StartsWith "/api/v1/remotes" then
        ok(
          Remotes.inboxPageJson(
            [ rowAt(hubotJson, "2026-10-21T04:00:00Z", 0) ],
            false,
            0,
            0
          )
        )
      else
        (match accountAnswer request with
         | ValueSome answer -> answer
         | ValueNone -> ok "{}"))

/// Opens the conversation and its actions flyout.
let openActionsAsync(window: Window) : Async<unit> = async {
  do! openConversationAsync window
  do! clickWhenShown(window, "···")
}

let manageTests =
  testList "remotes management" [
    testCaseAsync
      "the mute pick reaches its route"
      (async {
        let api =
          manageApi(quietThread(), [ ("/api/v1/remotes/@hubot/mute", ok "{}") ])

        let! window = startShell(remotesOnStore(false, 0), api)

        do! openActionsAsync window
        do! clickWhenShown(window, "mute 1 hour")

        let! muted =
          waitForAsync(
            5000,
            (fun () ->
              api.Requests
              |> List.exists(fun request ->
                request.Method = "PUT"
                && request.Target.StartsWith "/api/v1/remotes/@hubot/mute")),
            id
          )

        Expect.isTrue muted "the mute never reached its route"

        let! body =
          probeUiAsync(fun () ->
            api.Requests
            |> List.tryFind(fun request ->
              request.Target.StartsWith "/api/v1/remotes/@hubot/mute")
            |> Option.map Remotes.bodyText)

        let carried =
          body
          |> Option.map(fun text -> text.Contains "hour")
          |> Option.defaultValue false

        Expect.isTrue carried "the mute carried no duration"
      })

    testCaseAsync
      "the pin pick reaches its route"
      (async {
        let api =
          manageApi(quietThread(), [ ("/api/v1/remotes/@hubot/pin", ok "{}") ])

        let! window = startShell(remotesOnStore(false, 0), api)

        do! openActionsAsync window
        do! clickWhenShown(window, "pin")

        let! pinned =
          waitForAsync(
            5000,
            (fun () ->
              api.Requests
              |> List.exists(fun request ->
                request.Method = "PUT"
                && request.Target.StartsWith "/api/v1/remotes/@hubot/pin")),
            id
          )

        Expect.isTrue pinned "the pin never reached its route"
      })

    testCaseAsync
      "the stash pick moves the thread out"
      (async {
        let api =
          manageApi(
            quietThread(),
            [ ("/api/v1/remotes/@hubot/archive", ok "{}") ]
          )

        let! window = startShell(remotesOnStore(false, 0), api)

        do! openActionsAsync window
        do! clickWhenShown(window, "stash")

        let! stashed =
          waitForAsync(
            5000,
            (fun () ->
              api.Requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target.StartsWith "/api/v1/remotes/@hubot/archive")),
            id
          )

        Expect.isTrue stashed "the stash never reached its route"
      })

    testCaseAsync
      "the block pick asks first, then reaches its route"
      (async {
        let blockedBody =
          "{\"data\":{\"id\":\"01a151db-7fee-727b-a7ce-c911a7556243\",\"handle\":\"hubot\",\"name\":\"The Hubot\",\"avatar_url\":null},\"is_blocking\":true}"

        let api =
          manageApi(
            quietThread(),
            [ ("/api/v1/users/hubot/block", ok blockedBody) ]
          )

        let! window = startShell(remotesOnStore(false, 0), api)

        do! openActionsAsync window

        // The confirm carries the site's own question; Cancel keeps
        // the conversation untouched.
        do! clickWhenShown(window, "block @hubot")

        let! asked =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Block The Hubot?"))
          )

        Expect.isTrue asked "the block never asked first"

        do! clickWhenShown(window, "Cancel")
        do! settleAsync()

        let! heldBack =
          probeUiAsync(fun () ->
            api.Requests
            |> List.exists(fun request ->
              request.Target.StartsWith "/api/v1/users/hubot/block"))

        Expect.isFalse heldBack "the cancel still reached the block route"

        do! clickWhenShown(window, "block @hubot")
        do! clickWhenShown(window, "Block")

        let! blocked =
          waitForAsync(
            5000,
            (fun () ->
              api.Requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target.StartsWith "/api/v1/users/hubot/block")),
            id
          )

        Expect.isTrue blocked "the block never reached its route"

        let! closed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "no conversation checked out"))
          )

        Expect.isTrue closed "the thread never closed"
      })

    testCaseAsync
      "a refusal shows the server's message"
      (async {
        let api =
          manageApi(
            quietThread(),
            [
              ("/api/v1/remotes/@hubot/mute",
               {
                 Status = 422
                 Headers = []
                 Body = "{\"message\":\"no muting today\"}"
               })
            ]
          )

        let! window = startShell(remotesOnStore(false, 0), api)

        do! openActionsAsync window
        do! clickWhenShown(window, "mute 1 hour")

        let! refused =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual ->
                match visual with
                | :? TextBlock as block ->
                  (Option.ofObj block.Text |> Option.defaultValue "")
                    .Contains("no muting today")
                | _ -> false))
          )

        Expect.isTrue refused "the refusal never showed its message"
      })

    testCaseAsync
      "an org starts and opens its thread"
      (async {
        let api =
          manageApi(
            quietThread(),
            [
              ("/api/v1/remotes/orgs/org-9/messages", ok Remotes.orgThreadJson)
              ("/api/v1/remotes/orgs", ok "{\"org\":{\"id\":\"org-9\"}}")
              ("/api/v1/remotes/candidates",
               $"{{\"candidates\":[{{\"user\":{hubotJson},\"reason\":null}}]}}"
               |> ok)
            ]
          )

        let! window = startShell(remotesOnStore(false, 0), api)

        do! openRemotesAsync window

        // Search people, pick the candidate, create the org.
        do!
          onUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? TextBox as box when box.Name = "RemotesSearch" -> Some box
              | _ -> None)
            |> Option.iter(fun box -> box.Text <- "hub"))

        let! pickShown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "@hubot") |> Option.isSome)
          )

        Expect.isTrue pickShown "the candidate never listed"

        do! clickWhenShown(window, "new org")
        do! clickWhenShown(window, "+ @hubot")
        do! clickWhenShown(window, "create org")

        let! created =
          waitForAsync(
            5000,
            (fun () ->
              api.Requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target.StartsWith "/api/v1/remotes/orgs")),
            id
          )

        Expect.isTrue created "the org never reached its route"

        let! body =
          probeUiAsync(fun () ->
            api.Requests
            |> List.tryFind(fun request ->
              request.Target.StartsWith "/api/v1/remotes/orgs")
            |> Option.map Remotes.bodyText)

        let carried =
          body
          |> Option.map(fun text -> text.Contains "hubot")
          |> Option.defaultValue false

        Expect.isTrue carried "the org carried no members"

        // The new org opens as the checked-out thread.
        let! orgThread =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual -> visual.Name = "RemotesThread"))
          )

        Expect.isTrue orgThread "the org thread never opened"
      })

    testCaseAsync
      "the maintainer removes one member"
      (async {
        let api =
          manageApi(
            quietThread(),
            [
              ("/api/v1/remotes/orgs/org-9/members", ok "{}")
              ("/api/v1/remotes/orgs/org-9/messages", ok Remotes.orgThreadJson)
              ("/api/v1/remotes/orgs", ok "{\"org\":{\"id\":\"org-9\"}}")
              ("/api/v1/remotes/candidates",
               $"{{\"candidates\":[{{\"user\":{hubotJson},\"reason\":null}}]}}"
               |> ok)
            ]
          )

        let! window = startShell(remotesOnStore(false, 0), api)

        do! openRemotesAsync window

        do!
          onUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? TextBox as box when box.Name = "RemotesSearch" -> Some box
              | _ -> None)
            |> Option.iter(fun box -> box.Text <- "hub"))

        do! clickWhenShown(window, "new org")
        do! clickWhenShown(window, "+ @hubot")
        do! clickWhenShown(window, "create org")

        let! orgThread =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual -> visual.Name = "RemotesThread"))
          )

        Expect.isTrue orgThread "the org thread never opened"

        do! clickWhenShown(window, "members")
        do! clickWhenShown(window, "Remove from org")

        let! removed =
          waitForAsync(
            5000,
            (fun () ->
              api.Requests
              |> List.exists(fun request ->
                request.Method = "DELETE"
                && request.Target.StartsWith
                  "/api/v1/remotes/orgs/org-9/members/hubot")),
            id
          )

        Expect.isTrue removed "the member removal never reached its route"
      })
  ]

/// The 48 even bars of one canned waveform.
let voiceBars = List.replicate 48 5 |> List.map string |> String.concat ","

/// One thread whose partner message carries a voice note, so the
/// player has its whole surface.
let voicedThread() : string =
  Remotes
    .threadJson(
      [ Remotes.messageJson(41, partnerId, "hear this", "c-41") ],
      41,
      "",
      false,
      0
    )
    .Replace(
      "\"reactions\":[]",
      $"\"reactions\":[],\"voice\":{{\"id\":\"018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d\",\"duration_ms\":1500,\"waveform\":[{voiceBars}],\"url\":\"/api/v1/remotes/voices/018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d\"}}"
    )

/// The byte downloads of the voice api: the voices route serves the
/// canned WAV, anything else refuses.
let voiceBytes(target: string) =
  if target.Contains "/api/v1/remotes/voices/" then
    Ok cannedWav
  else
    Error(
      {
        Message = $"no canned bytes for {target}"
      }
      : TransportFailure
    )

/// Builds and shows the shell over one env whose head passed the fake
/// engine, so the voice surface renders.
let startVoiceShell
  (store: SessionStore, http: IHttp, audio: FakeAudio)
  : Async<Window> =
  let env = TestEnv(store, http, audio = audio)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

/// Builds and shows the shell over one env whose head passed the fake
/// sounds, so a ping's note is countable.
let startSoundsShell
  (store: SessionStore, http: IHttp, sounds: FakeSounds)
  : Async<Window> =
  let env = TestEnv(store, http, sounds = sounds)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

/// True when the mic button shows: it lives in the tree always, so
/// only its visibility tells whether the surface is on.
let micShown(root: Control) : bool =
  buttonTipped(root, "Record a voice note (max. 1 min)")
  |> Option.map(fun button -> button.IsVisible)
  |> Option.defaultValue false

/// True when the recording lane shows.
let laneShown(root: Control) : bool =
  root.GetVisualDescendants()
  |> Seq.tryPick(fun visual ->
    match visual with
    | :? Control as control when control.Name = "RemotesRecord" -> Some control
    | _ -> None)
  |> Option.map(fun control -> control.IsVisible)
  |> Option.defaultValue false

/// Clicks the mic once it shows.
let clickMicAsync(window: Window) : Async<unit> = async {
  let! shown = waitForAsync(5000, (fun () -> window), micShown)

  Expect.isTrue shown "the mic never showed"

  do!
    onUiAsync(fun () ->
      buttonTipped(window, "Record a voice note (max. 1 min)")
      |> Option.iter(fun button ->
        if button.IsVisible then
          click button))
}

let voiceTests =
  testList "remotes voice" [
    testCaseAsync
      "the mic stays hidden without an engine"
      (async {
        let api = composerApi(quietThread(), Stored)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        // The row that would hold the mic is up once Push shows.
        let! row =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Push"))
          )

        Expect.isTrue row "the composer never rendered"

        let! mic = probeUiAsync(fun () -> Some(micShown window))

        Expect.isFalse mic.Value "the mic showed without an engine"
      })

    testCaseAsync
      "a stop past the half second stages the voice on the push"
      (async {
        let audio = FakeAudio()
        let api = composerApi(quietThread(), Stored)
        let! window = startVoiceShell(remotesOnStore(false, 0), api, audio)

        do! openConversationAsync window
        do! clickMicAsync window

        let! lane =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> buttonTipped(root, "Stop recording") |> Option.isSome
          )

        Expect.isTrue lane "the record lane never opened"

        let! capturing =
          waitForAsync(
            5000,
            (fun () -> audio),
            fun engine -> engine.Captures > 0
          )

        Expect.isTrue capturing "the engine never captured"

        do! Async.Sleep 600
        do! clickWhenShownByTip(window, "Stop recording")

        let! staged =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> buttonTipped(root, "Play the note") |> Option.isSome
          )

        Expect.isTrue staged "the voice never staged"

        // A voice note rides alone: the box closes to typing and the
        // pick to staging while the take sits staged, but Push stays.
        let! closed =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? TextBox as box when box.Name = "RemotesComposer" ->
                Some(not box.IsEnabled)
              | _ -> None))

        Expect.equal
          closed
          (Some true)
          "the box stayed writable beside the voice"

        let! pickHeld =
          probeUiAsync(fun () ->
            buttonTipped(window, "Attach images")
            |> Option.map(fun pick -> not pick.IsEnabled))

        Expect.equal
          pickHeld
          (Some true)
          "the pick stayed open beside the voice"

        let! pushLive =
          probeUiAsync(fun () ->
            buttonWithText(window, "Push")
            |> Option.map(fun push -> push.IsEnabled))

        Expect.equal pushLive (Some true) "Push closed with the voice staged"

        do! clickWhenShown(window, "Push")

        let! pushed =
          waitForAsync(
            5000,
            (fun () -> api),
            fun fake ->
              fake.Requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target.StartsWith "/api/v1/remotes/@hubot/messages"
                && Remotes.payloadText(request).Contains "voice_waveform")
          )

        Expect.isTrue pushed "the push carried no voice"
      })

    testCaseAsync
      "a stop while the microphone still opens takes nothing"
      (async {
        let audio = FakeAudio()
        audio.HoldOpens <- true

        let api = composerApi(quietThread(), Stored)
        let! window = startVoiceShell(remotesOnStore(false, 0), api, audio)

        do! openConversationAsync window
        do! clickMicAsync window

        // The lane opens while the microphone itself has not.
        let! lane =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "opening microphone…")
          )

        Expect.isTrue lane "the opening word never showed"

        do! clickWhenShownByTip(window, "Stop recording")
        do! onUiAsync(fun () -> audio.ReleaseOpens())

        // The take cancels the moment the open lands: the lane closes
        // and the capture is released, not resurrected.
        let! closed =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(laneShown root)
          )

        Expect.isTrue closed "the lane stayed after the early stop"

        let! cancelled =
          waitForAsync(
            5000,
            (fun () -> audio),
            fun engine ->
              engine.LastCapture
              |> Option.map(fun capture -> capture.Cancelled)
              |> Option.defaultValue false
          )

        Expect.isTrue cancelled "the late open kept its microphone"
      })

    testCaseAsync
      "a cancelled recording keeps nothing"
      (async {
        let audio = FakeAudio()
        let api = composerApi(quietThread(), Stored)
        let! window = startVoiceShell(remotesOnStore(false, 0), api, audio)

        do! openConversationAsync window
        do! clickMicAsync window
        do! clickWhenShownByTip(window, "Discard voice note (Esc)")

        let! gone =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(laneShown root)
          )

        Expect.isTrue gone "the lane never closed"

        let! staged =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> buttonTipped(root, "Play the note") |> Option.isNone
          )

        Expect.isTrue staged "a cancelled take left its chip"

        let! mic = waitForAsync(5000, (fun () -> window), micShown)

        Expect.isTrue mic "the mic never came back"
      })

    testCaseAsync
      "leaving mid-recording releases the capture"
      (async {
        let audio = FakeAudio()
        let api = composerApi(quietThread(), Stored)
        let! window = startVoiceShell(remotesOnStore(false, 0), api, audio)

        do! openConversationAsync window
        do! clickMicAsync window

        let! capturing =
          waitForAsync(
            5000,
            (fun () -> audio),
            fun engine -> engine.Captures > 0
          )

        Expect.isTrue capturing "the engine never captured"

        let! weak =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              if visual.Name = "RemotesRecord" then Some visual else None)
            |> Option.map(fun control -> WeakReference(control)))

        Expect.isSome weak "the record lane never rendered"

        // The page's end takes the lane with it; the capture the
        // engine handed back must not stay open past it.
        do! clickWhenShown(window, "Settings")
        do! settleAsync()
        collect()

        Expect.isFalse weak.Value.IsAlive "the record lane outlived its page"

        let! released =
          waitForAsync(
            5000,
            (fun () -> audio),
            fun engine ->
              engine.LastCapture
              |> Option.map(fun capture -> capture.Cancelled)
              |> Option.defaultValue false
          )

        Expect.isTrue released "the detached lane kept the microphone"
      })

    testCaseAsync
      "a played note ends back at idle"
      (async {
        let audio = FakeAudio()
        let api = voiceApi(voicedThread(), Stored, voiceBytes)
        let! window = startVoiceShell(remotesOnStore(false, 0), api, audio)

        do! openConversationAsync window
        do! clickWhenShownByTip(window, "Play the note")

        let! played =
          waitForAsync(
            5000,
            (fun () -> audio),
            fun engine -> engine.Playbacks.Length > 0
          )

        Expect.isTrue played "the engine never played"

        let playback = audio.Playbacks.Head

        Expect.isGreaterThan playback.Media.Length 0 "the bytes came back empty"

        Expect.equal playback.Speed 1.0 "the first speed"

        Expect.equal playback.Volume 1.0 "the volume"

        let! sounding =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> buttonTipped(root, "Pause the note") |> Option.isSome
          )

        Expect.isTrue sounding "the player never sounded"

        do! onUiAsync(fun () -> playback.Finish())

        let! idle =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> buttonTipped(root, "Play the note") |> Option.isSome
          )

        Expect.isTrue idle "the player never went idle"
      })

    testCaseAsync
      "the fader lands on the sounding note"
      (async {
        let audio = FakeAudio()
        let api = voiceApi(voicedThread(), Stored, voiceBytes)
        let! window = startVoiceShell(remotesOnStore(false, 0), api, audio)

        do! openConversationAsync window
        do! clickWhenShownByTip(window, "Play the note")

        let! sounded =
          waitForAsync(
            5000,
            (fun () -> audio),
            fun engine -> engine.Playbacks.Length > 0
          )

        Expect.isTrue sounded "the note never sounded"

        do! clickWhenShownByTip(window, "Voice volume")

        let! fader =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? Slider as fader when fader.Name = "VoiceVolume" ->
                Some fader
              | _ -> None))

        Expect.isSome fader "the fader never showed"

        do! onUiAsync(fun () -> fader.Value.Value <- 40.0)

        let! quieter =
          waitForAsync(
            5000,
            (fun () -> audio),
            fun engine -> engine.Playbacks.Head.VolumeNow = 0.4
          )

        Expect.isTrue quieter "the fader never reached the sounding note"
      })

    testCaseAsync
      "the speed cycle restarts the note faster from where it stands"
      (async {
        let audio = FakeAudio()
        let api = voiceApi(voicedThread(), Stored, voiceBytes)
        let! window = startVoiceShell(remotesOnStore(false, 0), api, audio)

        do! openConversationAsync window
        do! clickWhenShownByTip(window, "Play the note")

        let! first =
          waitForAsync(
            5000,
            (fun () -> audio),
            fun engine -> engine.Playbacks.Length > 0
          )

        Expect.isTrue first "the note never sounded"

        let slower = audio.Playbacks.Head

        do! onUiAsync(fun () -> slower.MoveTo(TimeSpan.FromMilliseconds 600.0))

        // The model's position follows the pipeline on the player's own
        // 100 ms tick; give one tick time to land before the press.
        do! Async.Sleep 250
        do! clickWhenShown(window, "1×")

        let! second =
          waitForAsync(
            5000,
            (fun () -> audio),
            fun engine -> engine.Playbacks.Length > 1
          )

        Expect.isTrue second "the speed never restarted the note"

        let faster = audio.Playbacks.Item 1

        Expect.equal faster.Speed 1.5 "the restart ran at the natural pace"

        Expect.isTrue slower.Stopped "the slower note kept sounding"

        let! resumed =
          waitForAsync(
            5000,
            (fun () -> faster),
            fun note -> note.Position >= TimeSpan.FromMilliseconds 600.0
          )

        Expect.isTrue resumed "the faster note lost the playing place"
      })

    testCaseAsync
      "a webm note plays through the engine's own decode"
      (async {
        let audio = FakeAudio()

        let webm =
          Array.append
            [| 0x1Auy; 0x45uy; 0xDFuy; 0xA3uy |]
            (Array.create 4096 0uy)

        let bytes(target: string) =
          if target.Contains "/api/v1/remotes/voices/" then
            Ok webm
          else
            Error(
              {
                Message = $"no canned bytes for {target}"
              }
              : TransportFailure
            )

        let api = voiceApi(voicedThread(), Stored, bytes)
        let! window = startVoiceShell(remotesOnStore(false, 0), api, audio)

        do! openConversationAsync window
        do! clickWhenShownByTip(window, "Play the note")

        // The engine takes the note's bytes whole; the OS pipeline
        // decodes the container, not the player.
        let! sounded =
          waitForAsync(
            5000,
            (fun () -> audio),
            fun engine ->
              engine.Playbacks.Length > 0
              && engine.Playbacks.Head.Media.Length = webm.Length
          )

        Expect.isTrue sounded "the webm bytes never reached the engine"
      })

    testCaseAsync
      "a playback the engine refuses reads as failed"
      (async {
        let audio = FakeAudio()
        audio.RefusePlay <- true

        let api = voiceApi(voicedThread(), Stored, voiceBytes)
        let! window = startVoiceShell(remotesOnStore(false, 0), api, audio)

        do! openConversationAsync window
        do! clickWhenShownByTip(window, "Play the note")

        let! failed =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> buttonTipped(root, "Playback failed") |> Option.isSome
          )

        Expect.isTrue failed "the failed word never showed"
      })

    testCaseAsync
      "a second note stops the first"
      (async {
        let audio = FakeAudio()

        let thread =
          Remotes.threadJson(
            [
              Remotes.messageJson(41, partnerId, "first note", "c-41")
              Remotes.messageJson(42, viewerId, "second note", "c-42")
            ],
            42,
            "",
            false,
            0
          )
          |> fun body ->
            body.Replace(
              "\"reactions\":[]",
              $"\"reactions\":[],\"voice\":{{\"id\":\"018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d\",\"duration_ms\":1500,\"waveform\":[{voiceBars}],\"url\":\"/api/v1/remotes/voices/018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d\"}}"
            )

        let api = voiceApi(thread, Stored, voiceBytes)
        let! window = startVoiceShell(remotesOnStore(false, 0), api, audio)

        do! openConversationAsync window

        do! clickWhenShownByTip(window, "Play the note")

        let! first =
          waitForAsync(
            5000,
            (fun () -> audio),
            fun engine -> engine.Playbacks.Length > 0
          )

        Expect.isTrue first "the first note never sounded"

        // The first player now says pause, so the next play click is
        // the second note's own player.
        do! clickWhenShownByTip(window, "Play the note")

        let! second =
          waitForAsync(
            5000,
            (fun () -> audio),
            fun engine -> engine.Playbacks.Length > 1
          )

        Expect.isTrue second "the second note never sounded"

        Expect.isTrue
          (audio.Playbacks.Head.Stopped)
          "the first note kept sounding"
      })

    testCaseAsync
      "leaving the page releases the voice player"
      (async {
        let api = voiceApi(voicedThread(), Stored, voiceBytes)
        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> buttonTipped(root, "Play the note") |> Option.isSome
          )

        Expect.isTrue shown "the player never rendered"

        let! weak =
          probeUiAsync(fun () ->
            buttonTipped(window, "Play the note")
            |> Option.map(fun button -> WeakReference(button)))

        Expect.isSome weak "the player never rendered"

        do! clickWhenShown(window, "Settings")
        do! settleAsync()
        collect()

        Expect.isFalse weak.Value.IsAlive "the voice player outlived its page"
      })

    testCaseAsync
      "leaving mid-playback stops the note"
      (async {
        let audio = FakeAudio()
        let api = voiceApi(voicedThread(), Stored, voiceBytes)
        let! window = startVoiceShell(remotesOnStore(false, 0), api, audio)

        do! openConversationAsync window
        do! clickWhenShownByTip(window, "Play the note")

        let! played =
          waitForAsync(
            5000,
            (fun () -> audio),
            fun engine -> engine.Playbacks.Length > 0
          )

        Expect.isTrue played "the note never sounded"

        do! clickWhenShown(window, "Settings")
        do! settleAsync()

        Expect.isTrue
          (audio.Playbacks.Head.Stopped)
          "the note kept sounding past its page"
      })

    testCaseAsync
      "no control prints an observable's type name"
      (async {
        let audio = FakeAudio()
        let api = voiceApi(voicedThread(), Stored, voiceBytes)
        let! window = startVoiceShell(remotesOnStore(false, 0), api, audio)

        do! openConversationAsync window

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> buttonTipped(root, "Play the note") |> Option.isSome
          )

        Expect.isTrue shown "the player never rendered"

        let! printed =
          probeUiAsync(fun () ->
            let wordOf(content: obj | null) =
              match content with
              | :? string as text -> text
              | _ -> ""

            window.GetVisualDescendants()
            |> Seq.exists(fun visual ->
              match visual with
              | :? TextBlock as block ->
                (wordOf block.Text).Contains "System.Reactive"
              | :? Button as button ->
                (wordOf button.Content).Contains "System.Reactive"
              | _ -> false))

        Expect.isFalse printed "an observable leaked onto the screen"
      })
  ]

let pingTests =
  testList "remotes ping" [
    testCaseAsync
      "typing that lands on the open thread shows its word"
      (async {
        let can = ResizeArray<string>()
        let quiet = quietThread()

        let typing =
          Remotes.threadJson(
            [ Remotes.messageJson(41, partnerId, "hello there", "c-41") ],
            41,
            "",
            true,
            0
          )

        let api =
          FakeApi(fun request ->
            if
              request.Method = "GET"
              && request.Target.StartsWith "/api/v1/remotes/@hubot/messages"
            then
              ok(if can.Count = 0 then quiet else typing)
            elif request.Target.StartsWith "/api/v1/remotes" then
              ok(
                Remotes.inboxPageJson(
                  [ rowAt(hubotJson, "2026-10-21T04:00:00Z", 0) ],
                  false,
                  0,
                  0
                )
              )
            else
              ok "{}")

        let! window = startShell(remotesOnStore(false, 0), api)

        do! openConversationAsync window

        let! composer =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "Push")
          )

        Expect.isTrue composer "the composer never rendered"

        do! onUiAsync(fun () -> can.Add typing |> ignore)

        let! word =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "@hubot is typing")
          )

        Expect.isTrue word "the typing word never landed"
      })

    testCaseAsync
      "the partner's ping lands as the site's own event word"
      (async {
        let sounds = FakeSounds()

        let thread =
          Remotes.threadJson(
            [
              Remotes.messageJson(41, partnerId, "hello there", "c-41")
              Remotes.pingJson(45, partnerId)
            ],
            41,
            "",
            false,
            0
          )

        let api = threadApi(thread, thread)
        let! window = startSoundsShell(remotesOnStore(false, 0), api, sounds)

        do! openConversationAsync window

        let! landed =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "you got a ping")
          )

        Expect.isTrue landed "the ping word never landed"

        // A ping the thread opened with stays still: the site never
        // rings for what was already there.
        do! Async.Sleep 300
        Expect.equal sounds.Notified 0 "the opened-with ping sounded"
      })

    testCaseAsync
      "a ping that lands on the open thread sounds the system note"
      (async {
        let sounds = FakeSounds()
        let can = ResizeArray<string>()
        let quiet = quietThread()

        let pinged =
          Remotes.threadJson(
            [
              Remotes.messageJson(41, partnerId, "hello there", "c-41")
              Remotes.pingJson(45, partnerId)
            ],
            41,
            "",
            false,
            0
          )

        let api =
          FakeApi(fun request ->
            if
              request.Method = "GET"
              && request.Target.StartsWith "/api/v1/remotes/@hubot/messages"
            then
              ok(if can.Count = 0 then quiet else pinged)
            elif request.Target.StartsWith "/api/v1/remotes" then
              ok(
                Remotes.inboxPageJson(
                  [ rowAt(hubotJson, "2026-10-21T04:00:00Z", 0) ],
                  false,
                  0,
                  0
                )
              )
            else
              ok "{}")

        let! window = startSoundsShell(remotesOnStore(false, 0), api, sounds)

        do! openConversationAsync window

        let! composer =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "Push")
          )

        Expect.isTrue composer "the composer never rendered"

        do! onUiAsync(fun () -> can.Add pinged |> ignore)

        let! sounded =
          waitForAsync(8000, (fun () -> sounds), fun head -> head.Notified > 0)

        Expect.isTrue sounded "the ping never sounded"

        let! word =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "you got a ping")
          )

        Expect.isTrue word "the ping word never landed"
      })
  ]
