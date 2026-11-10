module DevNull.Client.UI.Tests.App

// The shell checks: the boot route follows the session, the timeline
// renders through the signed API with fakes, the sidebar navigates, and
// the guard sends a signed-out visitor to sign-in.

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Documents
open Avalonia.Controls.Presenters
open Avalonia.Controls.Primitives
open Avalonia.Controls.Shapes
open Avalonia.Headless
open Avalonia.Input
open Avalonia.Styling
open Avalonia.VisualTree

open Expecto
open Microsoft.Extensions.Logging.Abstractions

open DevNull.Client
open DevNull.Client.Components.SearchParts
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.RecentSearchStore
open DevNull.Client.SettingsStore
open DevNull.Client.Shell
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.Timeline
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness
open DevNull.Client.Views

// The two components the mention tests mount: the aliases keep the
// qualified names short without opening the whole Components set.
module CommitCard = DevNull.Client.Components.CommitCard
module UserFlyout = DevNull.Client.Components.UserFlyout

let buttons(root: Control) : Button list =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? Button as button -> Some button
    | _ -> None)
  |> List.ofSeq

/// True when the control and every ancestor are visible.
let effectiveVisible(control: Control) : bool =
  control.IsVisible
  && (control.GetVisualAncestors()
      |> Seq.choose(fun visual ->
        match visual with
        | :? Control as ancestor -> Some ancestor
        | _ -> None)
      |> Seq.forall(fun ancestor -> ancestor.IsVisible))

/// The first control carrying the automation name.
let controlByName(root: Control, name: string) : Control option =
  root.GetVisualDescendants()
  |> Seq.tryFind(fun visual -> visual.Name = name)
  |> Option.map(fun visual -> visual :?> Control)

/// The open pane length of the named split view.
let splitWidth(root: Control, name: string) : float option =
  controlByName(root, name)
  |> Option.bind(fun control ->
    match control with
    | :? SplitView as split -> Some split.OpenPaneLength
    | _ -> None)

/// The visible-state probe for the finder helpers: an option-aware
/// `effectiveVisible`.
let visibleOption(option: Button option) : bool =
  option |> Option.map effectiveVisible |> Option.defaultValue false

/// The first effectively visible text box whose placeholder equals the
/// text: hidden composers — the collapsed log chrome off the log —
/// never match.
let visibleBoxWithWatermark(root: Control, text: string) : TextBox option =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? TextBox as box when box.PlaceholderText = text -> Some box
    | _ -> None)
  |> Seq.tryFind effectiveVisible

/// The feed's scroll offset, so a returning page can prove it kept its
/// place. Small chrome viewers never qualify.
let feedOffset(window: Window) : float =
  window.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? ScrollViewer as scroller -> Some scroller
    | _ -> None)
  |> Seq.filter(fun scroller ->
    scroller.Extent.Height > scroller.Viewport.Height + 1.0)
  |> Seq.tryHead
  |> Option.map(fun scroller -> scroller.Offset.Y)
  |> Option.defaultValue 0.0

/// The first TextBlock under the root that carries the text.
let blockWithText(root: Control, text: string) : TextBlock option =
  root.GetVisualDescendants()
  |> Seq.tryPick(fun visual ->
    match visual with
    | :? TextBlock as block when textBlockShows(block, text) -> Some block
    | _ -> None)

/// The feed's own scroller, the first one above the commit text: the
/// pinned header sits in no scroller.
let scrollerAbove(root: Control, text: string) : ScrollViewer option =
  blockWithText(root, text)
  |> Option.bind(fun block ->
    block.GetVisualAncestors()
    |> Seq.tryPick(function
      | :? ScrollViewer as scroller -> Some scroller
      | _ -> None))

/// A profile feed of several commits, so the places the header tests
/// scroll to stay real: the canned commit returns with its own id per
/// row, and the profile carries the stats fixture.
let longProfileApi() : FakeApi =
  let rows =
    [
      for index in 1..6 ->
        commitJson.Replace(
          "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d",
          $"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b{index:D4}"
        )
    ]
    |> String.concat ","

  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif request.Target = "/api/v1/users/octocat" then
      ok profileWithStatsJson
    elif request.Target.StartsWith "/api/v1/users/octocat/commits" then
      ok(
        sprintf
          "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
          rows
      )
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// Clicks the visible header chevron: both forms carry one under the
/// same name, and the hidden form's copy must not take the click.
let clickVisibleHeaderToggle(window: Window) : Async<unit> = async {
  let named(window: Window) =
    window.GetVisualDescendants()
    |> Seq.choose(fun visual ->
      match visual with
      | :? Button as button when button.Name = "ProfileHeaderToggle" ->
        Some button
      | _ -> None)
    |> Seq.filter effectiveVisible
    |> Seq.tryHead

  let! shown = waitForAsync(5000, (fun () -> named window), Option.isSome)

  Expect.isTrue shown "the header chevron never showed"

  do! onUiAsync(fun () -> named window |> Option.iter click)
}

/// Clicks the first visible button under the given automation name.
let clickVisibleNamed(window: Window, name: string) : Async<unit> = async {
  let named() =
    window.GetVisualDescendants()
    |> Seq.choose(fun visual ->
      match visual with
      | :? Button as button when button.Name = name -> Some button
      | _ -> None)
    |> Seq.filter effectiveVisible
    |> Seq.tryHead

  let! shown = waitForAsync(5000, named, Option.isSome)

  Expect.isTrue shown $"the {name} button never appeared"

  do! onUiAsync(fun () -> named() |> Option.iter click)
}

/// The word a named button carries, read on the UI thread: a flipping
/// word asserts on the row that owns it, never on a page-wide text.
let namedButtonWord(window: Window, name: string) : string option =
  controlByName(window, name)
  |> Option.bind(fun control ->
    match control with
    | :? Button as button ->
      match button.Content with
      | :? string as text -> Some text
      | _ -> None
    | _ -> None)

/// Builds and shows the shell over one test env. The window is born on
/// the UI thread: the headless platform only exists there.
let startShell(store: SessionStore, http: IHttp) : Async<Window> =
  let env = TestEnv(store, http)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

/// Builds and shows the shell over one test env with the given fake
/// attachment sources.
let startShellWith
  (store: SessionStore, http: IHttp, attachments: FakeAttachments)
  : Async<Window> =
  let env = TestEnv(store, http, attachments = attachments)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

/// Builds and shows the shell over one test env with the given fake
/// profile worker.
let startShellWithProfiles
  (store: SessionStore, http: IHttp, profiles: FakeProfiles)
  : Async<Window> =
  let env = TestEnv(store, http, profiles = profiles)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

/// The canned profile as the page's own profile worker answers it.
let profileDetailsOf(json: string) : ProfileDetails =
  match ProfileDetails.decode json with
  | Ok details -> details
  | Error failure ->
    failwith $"the canned profile never decoded: {failure.Message}"

/// Builds and shows the shell over one test env with a pre-seeded
/// last-visit store.
let startShellSeeded
  (store: SessionStore, http: IHttp, lastVisit: LastVisitStore)
  : Async<Window> =
  let env = TestEnv(store, http, lastVisit = lastVisit)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

/// Builds and shows the shell over one test env with a pre-seeded
/// recent-search store.
let startShellWithRecents
  (store: SessionStore, http: IHttp, recents: RecentSearchStore)
  : Async<Window> =
  let env = TestEnv(store, http, recentSearches = recents)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

/// Builds and shows the shell over one test env, handing the env
/// back so the test asserts on the headless browser's recorder.
let startShellWithBrowser
  (store: SessionStore, http: IHttp)
  : Async<Window * TestEnv> =
  let env = TestEnv(store, http)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)

    let! shown = showAsync window

    return shown, env
  }

/// Waits until a TextBlock under the window carries the text.
let showsText(window: Window, text: string) : Async<bool> =
  waitForAsync(5000, (fun () -> window), (fun root -> hasText(root, text)))

/// True when an effectively visible TextBlock under the root carries the
/// text, so a hidden rail mark never counts.
let visibleText(root: Control, text: string) : bool =
  root.GetVisualDescendants()
  |> Seq.exists(fun visual ->
    match visual with
    | :? TextBlock as block ->
      textBlockShows(block, text) && effectiveVisible block
    | _ -> false)

/// Waits until an effectively visible TextBlock under the window
/// carries the text: hidden empty panels and collapsed sections stay
/// in the tree, and visibility is the claim.
let seesVisible(window: Window, text: string) : Async<bool> =
  waitForAsync(5000, (fun () -> window), (fun root -> visibleText(root, text)))

/// Clicks one button by its text once it shows up.
let clickWhenShown(window: Window, text: string) : Async<unit> = async {
  let! appeared =
    waitForAsync(5000, (fun () -> buttonWithText(window, text)), Option.isSome)

  Expect.isTrue appeared $"the {text} button never appeared"

  do! onUiAsync(fun () -> buttonWithText(window, text) |> Option.iter click)
}

/// Clicks one named button once it shows up.
let clickNamed(window: Window, name: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, name)
    |> Option.iter(fun control ->
      match control with
      | :? Button as button -> click button
      | _ -> ()))

/// Reveals the card's reply composer through its named trigger.
let revealReply(window: Window) : Async<unit> = async {
  let! revealed =
    waitForAsync(
      5000,
      (fun () -> controlByName(window, "ReplyButton")),
      (fun control ->
        control
        |> Option.map(fun found -> found :? Button)
        |> Option.defaultValue false)
    )

  Expect.isTrue revealed "the reply button never appeared"

  do! clickNamed(window, "ReplyButton")
}

/// The text the audience trigger line composes, read in inline order:
/// the plain runs and the handle blocks the line carries.
let replyingLineText(window: Window) : string option =
  controlByName(window, "ReplyingToLine")
  |> Option.bind(fun control ->
    match control with
    | :? Button as button ->
      button.GetVisualDescendants()
      |> Seq.tryFind(fun visual ->
        match visual with
        | :? TextBlock as block ->
          (block.Text |> Option.ofObj |> Option.defaultValue "") <> ""
          || block.Inlines <> null
        | _ -> false)
      |> Option.map(fun visual ->
        let block = visual :?> TextBlock

        match block.Text |> Option.ofObj with
        | Some text -> text
        | None ->
          let ofInline(piece: Inline) : string =
            match piece with
            | :? Run as run -> run.Text
            | :? InlineUIContainer as holder ->
              match holder.Child with
              | :? TextBlock as inner ->
                inner.Text |> Option.ofObj |> Option.defaultValue ""
              | _ -> ""
            | _ -> ""

          block.Inlines |> Seq.map ofInline |> String.concat "")
    | _ -> None)

/// A thread whose parent reached one hundred and one people: the
/// audience rides in by default and the post crosses the bound.
let overflowThreadApi() =
  let rows =
    [
      for index in 1..101 ->
        sprintf
          "{\"id\":\"01a15638-%04d-733c-89b3-373a1a0c538a\",\"handle\":\"person%d\"}"
          index
          index
    ]
    |> String.concat ","

  let page =
    sprintf
      "{\"data\":%s,\"ancestors\":[],\"reply_participants\":[%s]}"
      commitJson
      rows

  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif
      request.Method = "GET"
      && request.Target.StartsWith
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/replies"
    then
      ok
        "{\"data\":[],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
    elif
      request.Target.StartsWith
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
    then
      ok page
    elif request.Method = "POST" && request.Target = "/api/v1/commits" then
      ok(sprintf "{\"data\":%s}" replyJson)
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The menu's popup card content, once the menu stood open: the rows
/// live under the popup's child, which roots under the popup layer
/// while the menu stands.
let menuContentOf(root: Control) : Control option =
  controlByName(root, "CommitMenuFlyout")
  |> Option.bind(fun control ->
    match control with
    | :? Popup as popup -> Option.ofObj popup.Child
    | _ -> None)

/// One row of the open menu by its words.
let menuRowOf(window: Window, label: string) : Button option =
  menuContentOf window
  |> Option.bind(fun content -> buttonWithText(content, label))

/// The verb button of the one open destructive ask.
let confirmVerbOf(window: Window, verb: string) : Button option =
  menuContentOf window
  |> Option.bind(fun content ->
    content.GetVisualDescendants()
    |> Seq.tryPick(fun visual ->
      match visual with
      | :? Popup as popup when popup.Name = "ConfirmFlyout" && popup.IsOpen ->
        Some popup
      | _ -> None))
  |> Option.bind(fun popup -> Option.ofObj popup.Child)
  |> Option.bind(fun card -> buttonWithText(card, verb))

/// Opens the first card's More menu through its trigger.
let openOwnMenu(window: Window) : Async<unit> = async {
  let! trigger =
    waitForAsync(
      5000,
      (fun () -> controlByName(window, "CommitMenuButton")),
      Option.isSome
    )

  Expect.isTrue trigger "the card offered no menu trigger"

  do!
    onUiAsync(fun () ->
      controlByName(window, "CommitMenuButton")
      |> Option.iter(fun control -> click(control :?> Button)))
}

let notificationDayTests =
  testList "notification inbox rows" [
    testCaseAsync
      "duplicate groups keep one row and break days at the boundary"
      (async {
        let notificationOf(groupId: string, at: string, body: string) =
          notificationJson
            .Replace("01a15752-d17c-7065-b076-99e5bdad596d", $"{groupId}-{at}")
            .Replace("01a15751-bf69-7261-a486-e524bcb4d8e8", groupId)
            .Replace("2026-10-20T04:17:19Z", at)
            .Replace("It was DNS.", body)

        let firstDay = "2026-10-21T04:17:19Z"
        let secondDay = "2026-10-20T04:17:19Z"

        let kept = notificationOf("group-alpha", firstDay, "Alpha kept.")

        let dropped = notificationOf("group-alpha", secondDay, "Alpha dropped.")

        let beta = notificationOf("group-beta", secondDay, "Beta kept.")

        let inbox =
          sprintf
            """{"data":[%s],"links":{},"meta":{"per_page":20,"next_cursor":null,"prev_cursor":null},"filters":{"type":"all"},"read_at":"2026-10-20T04:20:00Z"}"""
            (String.concat "," [ kept; dropped; beta ])

        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/notifications" then
              ok inbox
            elif request.Target.StartsWith "/api/v1/log/" then
              ok logPageJson
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Notifications")

        let! alphaShown = showsText(window, "Alpha kept.")

        Expect.isTrue alphaShown "the kept group row never rendered"

        let! betaShown = showsText(window, "Beta kept.")

        Expect.isTrue betaShown "the second group row never rendered"

        let dayOne =
          DevNull.Client.Formatting.dayLabel(
            DateTimeOffset.UtcNow,
            DateTimeOffset(2026, 10, 21, 4, 17, 19, TimeSpan.Zero)
          )

        let dayTwo =
          DevNull.Client.Formatting.dayLabel(
            DateTimeOffset.UtcNow,
            DateTimeOffset(2026, 10, 20, 4, 17, 19, TimeSpan.Zero)
          )

        let! texts =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.choose(fun visual ->
              match visual with
              | :? TextBlock as block when effectiveVisible block ->
                Option.ofObj block.Text
              | _ -> None)
            |> Seq.toList)

        let countOf(value: string) =
          texts |> List.filter(fun text -> text = value) |> List.length

        let indexOf(value: string) =
          texts |> List.tryFindIndex(fun text -> text = value)

        Expect.equal (countOf dayOne) 1 "the first day separator"
        Expect.equal (countOf dayTwo) 1 "the second day separator"
        Expect.equal (countOf "Alpha kept.") 1 "the kept group row"
        Expect.equal (countOf "Alpha dropped.") 0 "the duplicate group row"
        Expect.equal (countOf "Beta kept.") 1 "the second group row"

        match
          indexOf dayOne,
          indexOf "Alpha kept.",
          indexOf dayTwo,
          indexOf "Beta kept."
        with
        | Some dayOneAt, Some alphaAt, Some dayTwoAt, Some betaAt ->
          let visible = String.Join(" | ", texts)

          Expect.isTrue
            (dayOneAt < alphaAt && alphaAt < dayTwoAt && dayTwoAt < betaAt)
            $"the rows did not break at the boundary: {visible}"
        | _ -> failtestf "a day separator or group row never rendered: %A" texts
      })
  ]

let tests =
  testList "app shell" [
    testCaseAsync
      "a signed-in boot lands on the latest log and renders commits"
      (async {
        let api = logApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the commit author never rendered"

        Expect.isNonEmpty api.Requests "no request went out"

        Expect.exists
          api.Requests
          (fun request ->
            request.Method = "GET"
            && request.Target.StartsWith "/api/v1/log/latest")
          "the signed log request"
      })

    testCaseAsync
      "a signed-out boot lands on the sign-in page"
      (async {
        let! window = startShell(signedOutStore(), logApi() :> IHttp)

        let! shown = showsText(window, "Sign in to /dev/null")

        Expect.isTrue shown "the sign-in page never rendered"
      })

    testCaseAsync
      "an empty log shows the empty state"
      (async {
        let! window = startShell(signedInStore(), emptyLogApi() :> IHttp)

        let! shown = showsText(window, "Nothing here yet. Commit something.")

        Expect.isTrue shown "the empty state never rendered"
      })

    testCaseAsync
      "a code block shows the characters the highlighter escaped"
      (async {
        let! window = startShell(signedInStore(), codeBlockLogApi() :> IHttp)

        let! decoded =
          showsText(window, "figlet \"HAVE A GREAT DAY GUYS\" | lolcat")

        Expect.isTrue decoded "the block never showed its quotes"

        let! escaped =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.exists(fun visual ->
              match visual with
              | :? TextBlock as block ->
                block.Text
                |> Option.ofObj
                |> Option.exists(fun text ->
                  text.Contains("&quot;", StringComparison.Ordinal))
              | _ -> false))

        Expect.isFalse escaped "the block kept the escaped entity"
      })

    testCaseAsync
      "a titled code block shows its filename"
      (async {
        let! window =
          startShell(signedInStore(), titledCodeBlockLogApi() :> IHttp)

        let! titled = showsText(window, "fig.sh")

        Expect.isTrue titled "the filename never showed over the block"

        let! decoded =
          showsText(window, "figlet \"HAVE A GREAT DAY GUYS\" | lolcat")

        Expect.isTrue decoded "the block never showed its quotes"
      })

    testCaseAsync
      "the log separates the days and rides the rail"
      (async {
        let! window = startShell(signedInStore(), twoDaysLogApi() :> IHttp)

        let! firstDay = showsText(window, "Fri, Apr 3")

        Expect.isTrue firstDay "the first day header never rendered"

        let! secondDay = showsText(window, "Wed, Apr 1")

        Expect.isTrue secondDay "the second day header never rendered"

        let! firstCommit = showsText(window, "First landing.")

        Expect.isTrue firstCommit "the first commit never rendered"

        let! secondCommit = showsText(window, "The week before.")

        Expect.isTrue secondCommit "the second commit never rendered"
      })

    testCaseAsync
      "the composer card fills the column and rides the card rail"
      (async {
        let! window = startShell(signedInStore(), twoDaysLogApi() :> IHttp)

        let! firstCommit = showsText(window, "First landing.")

        Expect.isTrue firstCommit "the first commit never rendered"

        // The reported overflow shows at the narrow end, where the
        // reading column is the whole window.
        do!
          onUiAsync(fun () ->
            window.Width <- 570.0
            window.Height <- 721.0)

        do! settleAsync()

        let! settled =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              hasText(root, "Fri, Apr 3")
              && (controlByName(root, "ComposerRailNode")
                  |> Option.map(fun node -> node.Bounds.X > 5.0)
                  |> Option.defaultValue false))
          )

        Expect.isTrue settled "the feed never laid out at the narrow width"

        let! bounds =
          probeUiAsync(fun () ->
            // The unboxed composer anchors on its text box.
            let composerBox =
              window.GetVisualDescendants()
              |> Seq.tryFind(fun visual -> visual.Name = "ComposerBody")
              |> Option.map(fun visual -> visual :?> Control)

            let dayLabel =
              window.GetVisualDescendants()
              |> Seq.tryFind(fun visual ->
                match visual with
                | :? TextBlock as textBlock -> textBlock.Text = "Fri, Apr 3"
                | _ -> false)
              |> Option.bind(fun textBlock ->
                match textBlock.Parent with
                | :? Border as border -> Some border
                | _ -> None)

            let dayLine =
              dayLabel
              |> Option.bind(fun label ->
                match label.Parent with
                | :? Panel as panel -> panel.Children |> Seq.tryHead
                | _ -> None)

            let cardRow = buttonWithText(window, "First landing.")

            let node = controlByName(window, "ComposerRailNode")

            // Bounds.Position chains parent to parent; summing them gives
            // the window-space edge the renderer draws at. (The transformed
            // bounds helper drops offsets across templated presenters.)
            let absoluteLeft(control: Control) : float =
              let rec walk(visual: Visual, x: float) =
                match visual.GetVisualParent() with
                | null -> x
                | parent ->
                  let offset =
                    match parent with
                    | :? Control as parentControl -> parentControl.Bounds.X
                    | _ -> 0.0

                  walk(parent, x + offset)

              walk(control, control.Bounds.X)

            match composerBox, dayLabel, dayLine, cardRow, node with
            | Some composerBox, Some label, Some line, Some row, Some node ->
              let boxLeft = absoluteLeft composerBox
              let rowLeft = absoluteLeft row
              let labelLeft = absoluteLeft label
              let lineLeft = absoluteLeft line
              let nodeLeft = absoluteLeft node

              boxLeft + composerBox.Bounds.Width,
              rowLeft + row.Bounds.Width,
              boxLeft,
              labelLeft,
              lineLeft + line.Bounds.Width / 2.0,
              nodeLeft + node.Bounds.Width / 2.0,
              $"client={(window :> TopLevel).ClientSize.Width} box=(L={boxLeft} w={composerBox.Bounds.Width}) row=(L={rowLeft} w={row.Bounds.Width})"
            | _ -> failwith "the composer box, day row, or rail never rendered")

        let (boxRight,
             rowRight,
             boxLeft,
             labelLeft,
             lineCenter,
             nodeCenter,
             detail) =
          bounds

        // The box ends one card inset in from the column edge, the same
        // 16px the cards keep on the right.
        Expect.isLessThan
          (abs((rowRight - boxRight) - 16.0))
          0.5
          $"the composer box misses the right inset ({detail})"

        // The box starts on the content column's left edge, the same
        // line the day header labels start on.
        Expect.isLessThan
          (abs(boxLeft - labelLeft))
          0.5
          "the composer box is offset"

        // The staged node sits on the same rail line as the day
        // separator's line.
        Expect.isLessThan
          (abs(lineCenter - nodeCenter))
          0.5
          "the staged node is off-rail"
      })

    testCaseAsync
      "toRows interleaves the days of the log"
      (async {
        let! response =
          (twoDaysLogApi(): IHttp)
            .Send(
              {
                Method = "GET"
                Target = "/api/v1/log/latest"
                Headers = []
                Body = HttpBody.Empty
              }
            )

        let page =
          match response with
          | Ok response ->
            match LogPage.decode response.Body with
            | Ok page -> page
            | Error failure -> failwith $"page never decoded: {failure.Message}"
          | Error failure -> failwith $"request failed: {failure.Message}"

        let rows = LogView.State.toRows(page.Page.Items, page.IsRanked, None)

        let commitOf(node: CommitNode) =
          match node with
          | LiveCommit commit -> commit
          | Tombstone _ -> failwith "the fixture commit is live"

        let first = commitOf page.Page.Items[0]
        let second = commitOf page.Page.Items[1]

        Expect.equal (rows |> List.length) 4 "the row count"

        Expect.equal
          rows[0]
          (LogView.LogRow.Day "Fri, Apr 3")
          "the first separator"

        Expect.equal rows[1] (LogView.LogRow.Node first) "the first node"

        Expect.equal
          rows[2]
          (LogView.LogRow.Day "Wed, Apr 1")
          "the second separator"

        Expect.equal rows[3] (LogView.LogRow.Node second) "the second node"
      })

    testCaseAsync
      "a ranked log rides without day separators"
      (async {
        let page =
          match LogPage.decode rankedLogPageJson with
          | Ok page -> page
          | Error failure ->
            failwith $"the ranked page never decoded: {failure.Message}"

        Expect.isTrue page.IsRanked "the ranked flag never decoded"

        // Ranked order interleaves the days, so separators would repeat
        // their keys: the ranked feed rides without them.
        let ranked = LogView.State.toRows(page.Page.Items, true, None)

        Expect.equal (ranked |> List.length) 3 "the ranked row count"

        Expect.isNone
          (ranked
           |> List.tryFind(function
             | LogView.LogRow.Day _ -> true
             | _ -> false))
          "the ranked feed carried a day separator"

        let keys = ranked |> List.map LogView.State.keyOf

        Expect.equal
          (keys |> List.distinct |> List.length)
          (keys |> List.length)
          "the ranked rows carried a repeated key"

        // The same commits on a newest-first reading interleave their
        // days: separators repeat their keys, which is why ranked rides
        // without them.
        let unranked = LogView.State.toRows(page.Page.Items, false, None)

        let unrankedKeys = unranked |> List.map LogView.State.keyOf

        Expect.isGreaterThan
          (unrankedKeys.Length - (unrankedKeys |> List.distinct |> List.length))
          0
          "the interleaved days never repeated a key"

        // The fetch records the ranked flag, and the ranked log never
        // stages a since-poll.
        let props: LogView.Props<TestEnv> = {
          Env = TestEnv(signedInStore(), twoDaysLogApi() :> IHttp)
          Log = Latest
          Session = signedInSession()
          Nav = Unchecked.defaultof<Navs.INavigable>
          Context = Unchecked.defaultof<Navs.RouteContext>
        }

        let fetched, _ =
          LogView.State.update
            props
            (LogView.State.Fetched(Ok page))
            LogView.State.initial

        Expect.isTrue fetched.Ranked "the fetch never took the ranked flag"

        let polled, _ =
          LogView.State.update props LogView.State.CheckNew fetched

        Expect.equal polled fetched "the ranked log staged a since-poll"
      })

    testCaseAsync
      "the sidebar navigates to the settings page"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Settings")

        let! settings = showsText(window, "@octocat")

        Expect.isTrue settings "the settings page never rendered"
      })

    testCase "the approval URL carries this app's name and id" (fun () ->
      let env = TestEnv(signedInStore(), logApi() :> IHttp)

      let key: DevNull.Client.DeviceAuth.DeviceKey = {
        PrivatePath = "unused"
        PublicPath = "unused"
        Fingerprint = "SHA256:unused"
      }

      let start = DevNull.Client.DeviceAuth.startAuthorization(env, key)

      let query =
        Uri(start.Url)
          .Query.TrimStart('?')
          .Split('&', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map(fun part ->
          match part.Split([| '=' |], 2) with
          | [| name; value |] -> name, Uri.UnescapeDataString value
          | _ -> part, "")
        |> Map.ofArray

      let appName = Map.find "app_name" query
      let appId = Map.find "app_id" query
      let deviceName = Map.find "device_name" query

      Expect.equal "DevNull Client" appName "app name"
      Expect.isLessThanOrEqual appName.Length 100 "app name length"

      Expect.equal
        "A test machine"
        deviceName
        "the device name never came from the env"

      Expect.isTrue
        (System.Text.RegularExpressions.Regex.IsMatch(
          appId,
          "^(windows|linux|macos):me\\.tunaxor\\.devnull\\.client$"
        ))
        "app id shape"

      Expect.isLessThanOrEqual appId.Length 100 "app id length")

    testCaseAsync
      "the settings page lists the devices and revokes one"
      (async {
        let api = devicesApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        do! clickWhenShown(window, "Settings")

        let! listed = showsText(window, "on A Mac laptop")

        Expect.isTrue listed "the other device never listed"

        let! appNamed = showsText(window, "DevNull Client")

        Expect.isTrue appNamed "the app name never listed"

        let! styled =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              let blocks =
                root.GetVisualDescendants()
                |> Seq.choose(fun visual ->
                  match visual with
                  | :? TextBlock as block -> Some block
                  | _ -> None)
                |> List.ofSeq

              let app =
                blocks
                |> List.tryFind(fun block -> block.Text = "DevNull Client")

              let device =
                blocks
                |> List.tryFind(fun block -> block.Text = "on A Mac laptop")

              let panelOf(block: TextBlock) =
                block.GetVisualAncestors()
                |> Seq.tryPick(function
                  | :? WrapPanel as panel -> Some panel
                  | _ -> None)

              match app, device with
              | Some appBlock, Some deviceBlock ->
                match panelOf appBlock, panelOf deviceBlock with
                | Some appPanel, Some devicePanel ->
                  let children =
                    appPanel.Children
                    |> Seq.choose(fun child ->
                      match child with
                      | :? TextBlock as block -> Some block
                      | _ -> None)
                    |> List.ofSeq

                  let indexOf(block: TextBlock) =
                    children
                    |> List.tryFindIndex(fun child ->
                      obj.ReferenceEquals(child, block))

                  let appFirst =
                    children
                    |> List.tryHead
                    |> Option.exists(fun first ->
                      obj.ReferenceEquals(first, appBlock))

                  appBlock.Classes.Contains(Theme.name Theme.Subtitle)
                  && deviceBlock.Classes.Contains(Theme.name Theme.Muted)
                  && obj.ReferenceEquals(appPanel, devicePanel)
                  && appFirst
                  && (match indexOf appBlock, indexOf deviceBlock with
                      | Some appIndex, Some deviceIndex ->
                        appIndex < deviceIndex
                      | _ -> false)
                | _ -> false
              | _ -> false)
          )

        Expect.isTrue
          styled
          "the app name never stood first in its subtitle style"

        let! selfNamed = showsText(window, "on This Fedora desktop")

        Expect.isTrue selfNamed "this device never named its machine"

        let! selfBadge = showsText(window, "This device")

        Expect.isTrue selfBadge "this device never named"

        let! ownOrdered =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              let blocks =
                root.GetVisualDescendants()
                |> Seq.choose(fun visual ->
                  match visual with
                  | :? TextBlock as block -> Some block
                  | _ -> None)
                |> List.ofSeq

              let panelOf(block: TextBlock) =
                block.GetVisualAncestors()
                |> Seq.tryPick(fun visual ->
                  match visual with
                  | :? WrapPanel as panel -> Some panel
                  | _ -> None)

              blocks
              |> List.tryFind(fun block ->
                block.Text = "on This Fedora desktop")
              |> Option.bind(panelOf)
              |> Option.map(fun panel ->
                panel.Children
                |> Seq.choose(fun child ->
                  match child with
                  | :? TextBlock as block -> Some block
                  | _ -> None)
                |> List.ofSeq)
              |> Option.exists(fun nameBlocks ->
                match nameBlocks with
                | [ app; badge; onMachine ] ->
                  app.Text = "DevNull Client"
                  && badge.Text = "This device"
                  && onMachine.Text = "on This Fedora desktop"
                | _ -> false))
          )

        Expect.isTrue
          ownOrdered
          "this device's badge never stood between the app name and the machine"

        do! clickWhenShown(window, "Revoke")

        let revokedWentOut(request: HttpRequest) =
          request.Method = "DELETE"
          && request.Target.EndsWith "018f4c1e-dev1-7c3a-9b2d-5e6f7a8b9c0d"

        let! revoked =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            (fun requests -> List.exists revokedWentOut requests)
          )

        Expect.isTrue revoked "the revoke never went out"

        let! refreshed =
          waitForAsync(
            5000,
            (fun () ->
              api.Requests
              |> List.filter(fun request ->
                request.Method = "GET"
                && request.Target = "/api/v1/me/devices")
              |> List.length),
            (fun count -> count >= 2)
          )

        Expect.isTrue refreshed "the device list never refreshed"
      })

    testCaseAsync
      "revoking this device signs the app out"
      (async {
        let api = devicesApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        do! clickWhenShown(window, "Settings")
        do! clickWhenShown(window, "Sign out on this device")

        let! signedOut = showsText(window, "Sign in to /dev/null")

        Expect.isTrue signedOut "the app never signed out"
      })

    testCaseAsync
      "deleting a commit offers undo and restoring lands it"
      (async {
        let api = undoApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "My own commit.")

        Expect.isTrue shown "the log never rendered"

        do! openOwnMenu window

        let! row =
          waitForAsync(
            5000,
            (fun () -> menuRowOf(window, "Delete commit")),
            Option.isSome
          )

        Expect.isTrue row "the menu never carried the delete row"

        do!
          onUiAsync(fun () ->
            menuRowOf(window, "Delete commit") |> Option.iter click)

        let! ask =
          waitForAsync(
            5000,
            (fun () -> confirmVerbOf(window, "Delete")),
            Option.isSome
          )

        Expect.isTrue ask "the delete ask never stood"

        do!
          onUiAsync(fun () ->
            confirmVerbOf(window, "Delete") |> Option.iter click)

        let! toast = showsText(window, "Commit deleted.")

        Expect.isTrue toast "the undo toast never stood"

        do! clickWhenShown(window, "Undo")

        let restoredWentOut(request: HttpRequest) =
          request.Method = "POST"
          && request.Target =
            "/api/v1/commits/018f4c1e-0aa0-7c3a-9b2d-5e6f7a8b9c0d/restoration"

        let! restored =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            (fun requests -> List.exists restoredWentOut requests)
          )

        Expect.isTrue restored "the restoration never went out"

        let! toastLeft =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Commit deleted.")))
          )

        Expect.isTrue toastLeft "the toast never left"
      })

    testCaseAsync
      "the undo toast leaves when its window ends"
      (async {
        let api = undoApi()
        let env = TestEnv(signedInStore(), api)

        let! window =
          probeUiAsync(fun () ->
            Shell(env, None, undoWindow = TimeSpan.FromSeconds 1.0) :> Window)

        let! _ = showAsync window

        let! shown = showsText(window, "My own commit.")

        Expect.isTrue shown "the log never rendered"

        do! openOwnMenu window

        let! row =
          waitForAsync(
            5000,
            (fun () -> menuRowOf(window, "Delete commit")),
            Option.isSome
          )

        Expect.isTrue row "the menu never carried the delete row"

        do!
          onUiAsync(fun () ->
            menuRowOf(window, "Delete commit") |> Option.iter click)

        let! ask =
          waitForAsync(
            5000,
            (fun () -> confirmVerbOf(window, "Delete")),
            Option.isSome
          )

        Expect.isTrue ask "the delete ask never stood"

        do!
          onUiAsync(fun () ->
            confirmVerbOf(window, "Delete") |> Option.iter click)

        let! toast = showsText(window, "Commit deleted.")

        Expect.isTrue toast "the undo toast never stood"

        let! left =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Commit deleted.")))
          )

        Expect.isTrue left "the toast outlived its window"

        let restoredWentOut(request: HttpRequest) =
          request.Method = "POST" && request.Target.EndsWith "/restoration"

        Expect.isFalse
          (api.Requests |> List.exists restoredWentOut)
          "a restoration went out on its own"
      })

    testCaseAsync
      "the commit button opens the composer dialog off the log"
      (async {
        let api = logApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Notifications")

        let! nav =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "NavCommitButton")),
            (fun control ->
              control
              |> Option.map(fun c -> c :? Button)
              |> Option.defaultValue false)
          )

        Expect.isTrue nav "the nav commit button never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "NavCommitButton")
            |> Option.iter(fun control -> click(control :?> Button)))

        let! dialog = showsText(window, "Write a commit")

        Expect.isTrue dialog "the compose dialog never opened"

        let! typed =
          waitForAsync(
            5000,
            (fun () -> visibleBoxWithWatermark(window, "What did you ship?")),
            Option.isSome
          )

        Expect.isTrue typed "the dialog composer never appeared"

        do!
          onUiAsync(fun () ->
            visibleBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun box ->
              box.Text <- "Committed from the dialog"

              buttonNear(box, "Commit") |> Option.iter click))

        let! posted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST" && request.Target = "/api/v1/commits"))
          )

        Expect.isTrue posted "the dialog commit never posted"

        let! closed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Write a commit")))
          )

        Expect.isTrue closed "the dialog never closed after the commit"
      })

    testCaseAsync
      "the commit button keeps the log on its own composer"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            controlByName(window, "NavCommitButton")
            |> Option.iter(fun control -> click(control :?> Button)))

        // The log page carries its own composer, so the button focuses it
        // instead of opening the dialog: the dialog never shows. (The
        // focus hand-off itself is global state, which parallel windows
        // contend for, so the suite pins the observable half.)
        let! stayed =
          waitForAsync(
            1000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Write a commit")))
          )

        Expect.isTrue stayed "the dialog opened over the log's own composer"

        let! typed =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "What did you ship?")),
            Option.isSome
          )

        Expect.isTrue typed "the log's composer never stayed up"
      })

    testCaseAsync
      "the log tabs request the other log"
      (async {
        let api = logApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Trending")

        let! switched =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Target.StartsWith "/api/v1/log/trending"))
          )

        Expect.isTrue switched "the trending log was never requested"
      })

    testCaseAsync
      "the back line returns to Following, the log the reader left"
      (async {
        let api = threadApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        // The fake's targets name each log, so the boot's latest reads
        // never count for the walk's own.
        let readsOf(target: string) =
          api.Requests
          |> List.filter(fun request ->
            request.Method = "GET" && request.Target.StartsWith target)
          |> List.length

        do! clickWhenShown(window, "Following")

        let! opened =
          waitForAsync(
            5000,
            (fun () -> readsOf "/api/v1/log/following"),
            (fun count -> count > 0)
          )

        Expect.isTrue opened "the following log was never requested"

        do! clickWhenShown(window, "It was DNS.")

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        do! clickWhenShown(window, "Back to the log")

        let! back =
          waitForAsync(
            5000,
            (fun () -> readsOf "/api/v1/log/following"),
            (fun count -> count > 1)
          )

        Expect.isTrue back "the back press never returned to Following"

        // The chrome rides the route, so the tab the reader left is
        // the checked one once the page returns.
        let! checkedTab =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              let tabState(text: string) =
                buttonWithText(root, text)
                |> Option.bind(fun button ->
                  match button with
                  | :? ToggleButton as toggle -> Some toggle.IsChecked
                  | _ -> None)

              match tabState "Following", tabState "Trending" with
              | Some following, Some trending ->
                following.GetValueOrDefault false
                && not(trending.GetValueOrDefault false)
              | _ -> false)
          )

        Expect.isTrue checkedTab "the Following tab never checked again"
      })

    testCaseAsync
      "the back line returns to Trending, the log the reader left"
      (async {
        let api = threadApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        let readsOf(target: string) =
          api.Requests
          |> List.filter(fun request ->
            request.Method = "GET" && request.Target.StartsWith target)
          |> List.length

        do! clickWhenShown(window, "Trending")

        let! opened =
          waitForAsync(
            5000,
            (fun () -> readsOf "/api/v1/log/trending"),
            (fun count -> count > 0)
          )

        Expect.isTrue opened "the trending log was never requested"

        do! clickWhenShown(window, "It was DNS.")

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        do! clickWhenShown(window, "Back to the log")

        let! back =
          waitForAsync(
            5000,
            (fun () -> readsOf "/api/v1/log/trending"),
            (fun count -> count > 1)
          )

        Expect.isTrue back "the back press never returned to Trending"

        let! checkedTab =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              let tabState(text: string) =
                buttonWithText(root, text)
                |> Option.bind(fun button ->
                  match button with
                  | :? ToggleButton as toggle -> Some toggle.IsChecked
                  | _ -> None)

              match tabState "Following", tabState "Trending" with
              | Some following, Some trending ->
                not(following.GetValueOrDefault false)
                && trending.GetValueOrDefault false
              | _ -> false)
          )

        Expect.isTrue checkedTab "the Trending tab never checked again"
      })

    testCase "the back target falls back to Latest without a record"
    <| fun () ->
      let timeline = TimelineService.create()

      Expect.isNone timeline.LastLog "a fresh timeline already knows a log"

      timeline.RememberLog Trending

      Expect.equal timeline.LastLog (Some Trending) "the record never landed"

      Expect.equal
        (CommitDetailView.Parts.backTarget None)
        "/log/latest"
        "the no-record back target left Latest"

      Expect.equal
        (CommitDetailView.Parts.backTarget(Some Trending))
        "/log/trending"
        "the remembered back target left the remembered log"

    testCaseAsync
      "a signed-out visitor is sent to sign-in by the guard"
      (async {
        let store = signedInStore()
        let! window = startShell(store, logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! onUiAsync(fun () -> store.Leave())
        do! clickWhenShown(window, "Settings")

        let! signIn = showsText(window, "Sign in to /dev/null")

        Expect.isTrue signIn "the guard never redirected to sign-in"
      })

    testCaseAsync
      "the composer posts a commit and reloads the log"
      (async {
        let api = logApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

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
              box.Text <- "Committed from the test"

              buttonNear(box, "Commit") |> Option.iter click))

        let! posted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST" && request.Target = "/api/v1/commits"))
          )

        Expect.isTrue posted "the commit was never posted"

        let post =
          api.Requests
          |> List.find(fun request ->
            request.Method = "POST" && request.Target = "/api/v1/commits")

        let postBody =
          match post.Body with
          | Http.HttpBody.Text text -> text
          | _ -> ""

        Expect.stringContains
          postBody
          "Committed from the test"
          "the typed body"

        // A landed commit leaves the box empty: a sent body never lingers
        // to be cut or reposted.
        let! cleared =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "What did you ship?")),
            (fun box ->
              box
              |> Option.map(fun control -> control.Text = "")
              |> Option.defaultValue false)
          )

        Expect.isTrue cleared "the composer kept the sent body"

        // The commit lands through the timeline service, so the reload
        // follows the post by a few dispatcher hops: it is waited for,
        // never read at once.
        let! reloaded =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.filter(fun request ->
                request.Method = "GET"
                && request.Target.StartsWith "/api/v1/log/latest")
              |> List.length
              |> fun count -> count > 1)
          )

        Expect.isTrue reloaded "the log never reloaded"
      })

    testCaseAsync
      "starring a commit posts the star"
      (async {
        let api = logApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        let! starred =
          waitForAsync(
            5000,
            (fun () -> buttonWithText(window, "7")),
            Option.isSome
          )

        Expect.isTrue starred "the star button never appeared"

        do!
          onUiAsync(fun () -> buttonWithText(window, "7") |> Option.iter click)

        let! posted =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target =
                  "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/star"))
          )

        Expect.isTrue posted "the star was never posted"
      })

    testCaseAsync
      "load more appends the next page"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the first page never rendered"

        do! clickWhenShown(window, "Load more")

        do! scrollFeedToEndAsync window

        let! appended = showsText(window, "Second commit.")

        Expect.isTrue appended "the second page never rendered"
      })

    testCaseAsync
      "scrolling recycles rows and realizes them again"
      (async {
        let! window = startShell(signedInStore(), manyCardsApi() :> IHttp)

        let! shown = showsText(window, "Card 1.")

        Expect.isTrue shown "the first card never rendered"

        // The small window makes the six-card feed overflow the viewport
        // plus its cache band, so scrolling must recycle the top rows.
        do!
          onUiAsync(fun () ->
            window.Width <- 420.0
            window.Height <- 480.0)

        do! scrollFeedToEndAsync window

        let! recycled =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Card 1.")))
          )

        Expect.isTrue recycled "the first row was never recycled"

        do! scrollFeedToTopAsync window

        let! returned = showsText(window, "Card 1.")

        Expect.isTrue returned "the recycled row never came back"
      })

    testCaseAsync
      "a card image loads into its reserved box"
      (async {
        let api = logApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        let! loaded =
          waitForAsync(
            8000,
            (fun () ->
              window.GetVisualDescendants()
              |> Seq.tryFind(fun visual -> visual.Name = "CardImage")),
            (fun cell ->
              cell
              |> Option.bind(fun control ->
                match control with
                | :? ContentControl as host ->
                  match host.Content with
                  | :? Avalonia.Controls.Image as image ->
                    Option.ofObj image.Source
                  | _ -> None
                | _ -> None)
              |> Option.isSome)
          )

        Expect.isTrue loaded "the image never landed"

        let fetched =
          api.ByteRequests
          |> List.exists(fun target -> target.Contains "/commit-images/")

        Expect.isTrue fetched "the image bytes were never fetched"
      })

    testCaseAsync
      "an image click opens the gallery over the window"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        let! cell =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "CardImageButton")),
            (fun found ->
              found
              |> Option.map(fun control -> control :? Button)
              |> Option.defaultValue false)
          )

        Expect.isTrue cell "the image cell never became a button"

        do!
          onUiAsync(fun () ->
            controlByName(window, "CardImageButton")
            |> Option.iter(fun control ->
              match control with
              | :? Button as button -> click button
              | _ -> ()))

        let viewer() =
          window.OwnedWindows
          |> Seq.tryFind(fun open_ -> open_.Name = "ImageViewer")

        let! opened = waitForAsync(5000, viewer, Option.isSome)

        Expect.isTrue opened "the gallery never opened"

        let! counted =
          probeUiAsync(fun () ->
            viewer()
            |> Option.map(fun viewer -> hasText(viewer, "1 / 1"))
            |> Option.defaultValue false)

        Expect.isTrue counted "the gallery lost count of its photos"

        do!
          onUiAsync(fun () ->
            viewer()
            |> Option.iter(fun viewer ->
              controlByName(viewer, "ViewerClose")
              |> Option.iter(fun control ->
                match control with
                | :? Button as button -> click button
                | _ -> ())))

        let! closed = waitForAsync(5000, viewer, Option.isNone)

        Expect.isTrue closed "the gallery never closed"
      })

    testCaseAsync
      "an author's own commit carries no star toggle"
      (async {
        let! window = startShell(signedInStore(), ownLogApi() :> IHttp)

        let! own = showsText(window, "My own commit.")

        Expect.isTrue own "the viewer's commit never rendered"

        let! foreign = showsText(window, "It was DNS.")

        Expect.isTrue foreign "the other commit never rendered"

        // The card row of one body: the star affordances live inside
        // it, so the two commits answer separately.
        let rowStarNames(body: string) =
          window.GetVisualDescendants()
          |> Seq.tryFind(function
            | :? TextBlock as block when block.Text = body -> true
            | _ -> false)
          |> Option.bind(fun found ->
            found.GetVisualAncestors()
            |> Seq.tryPick(function
              | :? Button as card when card.Classes.Contains(name CommitRow) ->
                Some card
              | _ -> None))
          |> Option.map(fun card ->
            card.GetVisualDescendants()
            |> List.ofSeq
            |> List.filter(fun visual ->
              visual.Name = "StarButton" || visual.Name = "StarCount")
            |> List.map _.Name)

        let! stars = probeUiAsync(fun () -> rowStarNames "My own commit.")

        Expect.isSome stars "the viewer's commit never drew its row"

        Expect.equal
          stars.Value
          [ "StarCount" ]
          "the own commit drew a star toggle"

        let! theirs = probeUiAsync(fun () -> rowStarNames "It was DNS.")

        Expect.isSome theirs "the other commit never drew its row"

        Expect.equal
          theirs.Value
          [ "StarButton" ]
          "the other commit lost its star toggle"
      })

    testCaseAsync
      "a failed image keeps the placeholder and the app alive"
      (async {
        let offline target =
          Error({ Message = $"offline {target}" }: TransportFailure)

        let api = logApiWithBytes offline
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        let! attempted =
          waitForAsync(
            8000,
            (fun () -> api.ByteRequests),
            (fun targets ->
              targets
              |> List.exists(fun target -> target.Contains "/commit-images/"))
          )

        Expect.isTrue attempted "the image bytes were never requested"

        let! settled =
          waitForAsync(
            2000,
            (fun () ->
              window.GetVisualDescendants()
              |> Seq.tryFind(fun visual -> visual.Name = "CardImage")),
            (fun cell ->
              cell
              |> Option.forall(fun control ->
                match control with
                | :? ContentControl as host ->
                  match host.Content with
                  | :? Avalonia.Controls.Image -> false
                  | _ -> true
                | _ -> true))
          )

        Expect.isTrue settled "the failed load replaced the placeholder"

        let! alive = probeUiAsync(fun () -> hasText(window, "It was DNS."))

        Expect.isTrue alive "the feed died with the failed image"
      })

    testCaseAsync
      "an author avatar loads over the initial disc"
      (async {
        let api = avatarLogApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        let! loaded =
          waitForAsync(
            8000,
            (fun () ->
              window.GetVisualDescendants()
              |> Seq.tryFind(fun visual -> visual.Name = "AvatarImage")),
            (fun avatar ->
              avatar
              |> Option.bind(fun control ->
                match control with
                | :? ContentControl as host ->
                  match host.Content with
                  | :? Avalonia.Controls.Image as image ->
                    Option.ofObj image.Source
                  | _ -> None
                | _ -> None)
              |> Option.isSome)
          )

        Expect.isTrue loaded "the avatar never landed"
      })

    testCaseAsync
      "a staged image's description rides the post"
      (async {
        let api = logApi()
        let attachments = FakeAttachments()

        let env =
          TestEnv(signedInStore(), api :> IHttp, attachments = attachments)

        attachments.PickResponse <- [ stagedImage "one.png" ]

        let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
        let! _ = showAsync window

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            controlByName(window, "AttachButton")
            |> Option.iter(fun visual ->
              match visual with
              | :? Button as button -> click button
              | _ -> ()))

        let! staged =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "one.png"))
          )

        Expect.isTrue staged "the picked files never staged"

        do!
          onUiAsync(fun () ->
            controlByName(window, "ImageDescription0")
            |> Option.iter(fun visual ->
              match visual with
              | :? TextBox as box -> box.Text <- "A diagram"
              | _ -> ()))

        let! counted =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "9/1000"))
          )

        Expect.isTrue counted "the description counter never counted"

        do!
          onUiAsync(fun () ->
            visibleBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun box ->
              box.Text <- "Shipped with a description"

              buttonNear(box, "Commit") |> Option.iter click))

        let! posted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST" && request.Target = "/api/v1/commits"))
          )

        Expect.isTrue posted "the v1 commit was never posted"

        let post =
          api.Requests
          |> List.find(fun request ->
            request.Method = "POST" && request.Target = "/api/v1/commits")

        let form =
          match post.Body with
          | Http.HttpBody.Multipart form -> form
          | _ -> failtest "expected a multipart body"

        let payload = form.Files |> List.find(fun file -> file.Name = "payload")

        let json = System.Text.Encoding.UTF8.GetString payload.Content

        Expect.isTrue
          (json.Contains "\"image_descriptions\":[\"A diagram\"]")
          $"the payload carried the description: {json}"
      })

    testCaseAsync
      "an over-long description refuses before the post"
      (async {
        let api = logApi()
        let attachments = FakeAttachments()

        let env =
          TestEnv(signedInStore(), api :> IHttp, attachments = attachments)

        attachments.PickResponse <- [ stagedImage "one.png" ]

        let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
        let! _ = showAsync window

        let! _ = showsText(window, "It was DNS.")

        do!
          onUiAsync(fun () ->
            controlByName(window, "AttachButton")
            |> Option.iter(fun visual ->
              match visual with
              | :? Button as button -> click button
              | _ -> ()))

        let! staged =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "one.png"))
          )

        Expect.isTrue staged "the picked files never staged"

        do!
          onUiAsync(fun () ->
            controlByName(window, "ImageDescription0")
            |> Option.iter(fun visual ->
              match visual with
              | :? TextBox as box -> box.Text <- String.replicate 1001 "x"
              | _ -> ()))

        do!
          onUiAsync(fun () ->
            visibleBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun box ->
              box.Text <- "Refused description"

              buttonNear(box, "Commit") |> Option.iter click))

        let boundSurfaced(root: Control) =
          root.GetVisualDescendants()
          |> Seq.exists(fun visual ->
            match visual with
            | :? TextBlock as block ->
              block.Text.Contains "1000 characters allowed"
            | _ -> false)

        let! refused = waitForAsync(5000, (fun () -> window), boundSurfaced)

        Expect.isTrue refused "the description bound never surfaced"

        let! settled = settleAsync()

        let posted =
          api.Requests
          |> List.exists(fun request ->
            request.Method = "POST" && request.Target = "/api/v1/commits")

        Expect.isFalse posted "the refused post left anyway"
      })

    testCaseAsync
      "staged images post through the signed v1 route"
      (async {
        let api = logApi()
        let attachments = FakeAttachments()

        let env =
          TestEnv(signedInStore(), api :> IHttp, attachments = attachments)

        attachments.PickResponse <- [
          stagedImage "one.png"
          stagedImage "two.png"
        ]

        let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
        let! _ = showAsync window

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        let! attachable =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "AttachButton")),
            Option.isSome
          )

        Expect.isTrue attachable "the attach button never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "AttachButton")
            |> Option.iter(fun visual ->
              match visual with
              | :? Button as button -> click button
              | _ -> ()))

        let! staged =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "one.png"))
          )

        Expect.isTrue staged "the picked files never staged"

        do!
          onUiAsync(fun () ->
            visibleBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun box ->
              box.Text <- "Shipped with pictures"

              buttonNear(box, "Commit") |> Option.iter click))

        let! posted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST" && request.Target = "/api/v1/commits"))
          )

        Expect.isTrue posted "the v1 commit was never posted"

        // The v1 route signs with the device key under the images
        // namespace; the CLI key lookup never runs.
        let lookup =
          api.Requests
          |> List.exists(fun request -> request.Target = "/api/cli/key-lookups")

        Expect.isFalse lookup "the CLI key lookup still ran"

        Expect.isTrue
          (env.SignedRequests
           |> List.exists(fun request ->
             request.Namespace = "devnull-api-images"))
          "the post signed under the images namespace"

        let post =
          api.Requests
          |> List.find(fun request ->
            request.Method = "POST" && request.Target = "/api/v1/commits")

        let form =
          match post.Body with
          | Http.HttpBody.Multipart form -> form
          | _ -> failtest "expected a multipart body"

        let partNames = form.Files |> List.map _.Name

        Expect.equal
          partNames
          [ "payload"; "images[]"; "images[]" ]
          "the form parts"

        let payload = form.Files |> List.find(fun file -> file.Name = "payload")

        let payloadText = System.Text.Encoding.UTF8.GetString payload.Content

        Expect.stringContains
          payloadText
          "Shipped with pictures"
          "the signed payload"

        // The commit lands through the timeline service, so the reload
        // follows the post by a few dispatcher hops: it is waited for,
        // never read at once.
        let! reloaded =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.filter(fun request ->
                request.Method = "GET"
                && request.Target.StartsWith "/api/v1/log/latest")
              |> List.length
              |> fun count -> count > 1)
          )

        Expect.isTrue reloaded "the log never reloaded"
      })

    testCaseAsync
      "a fourth image is refused at stage time"
      (async {
        let api = logApi()
        let attachments = FakeAttachments()

        attachments.PickResponse <- [
          stagedImage "a.png"
          stagedImage "b.png"
          stagedImage "c.png"
          stagedImage "d.png"
        ]

        let! window = startShellWith(signedInStore(), api :> IHttp, attachments)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryFind(fun visual -> visual.Name = "AttachButton")
            |> Option.iter(fun visual ->
              match visual with
              | :? Button as button -> click button
              | _ -> ()))

        let! refused =
          showsText(window, "A commit allows at most 3 images, got 4.")

        Expect.isTrue refused "the over-limit pick never said so"

        let posted =
          api.Requests
          |> List.exists(fun request -> request.Target = "/api/cli/commits")

        Expect.isFalse posted "a refused staging still posted"
      })

    testCaseAsync
      "pasting a clipboard image stages it"
      (async {
        let api = logApi()
        let attachments = FakeAttachments()

        attachments.ClipboardImage <- Some(stagedImage "paste.png")

        let! window = startShellWith(signedInStore(), api :> IHttp, attachments)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

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
              let args =
                KeyEventArgs(
                  RoutedEvent = InputElement.KeyDownEvent,
                  Key = Key.V,
                  KeyModifiers = KeyModifiers.Control
                )

              box.RaiseEvent args))

        let! staged =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "paste.png"))
          )

        Expect.isTrue staged "the pasted image never staged"
      })

    testCaseAsync
      "a poll waits for a choice and the Vote button, then shows the bars"
      (async {
        let api = voteApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        let! offered =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "PollChoiceo1")),
            Option.isSome
          )

        Expect.isTrue offered "the poll choice never appeared"

        let! waiting = showsText(window, "2d left")

        Expect.isTrue waiting "the time left never rendered"

        do!
          onUiAsync(fun () ->
            controlByName(window, "PollChoiceo1")
            |> Option.iter(fun control ->
              (control :?> ToggleButton).IsChecked <- Nullable true))

        let! postedEarly =
          probeUiAsync(fun () ->
            api.Requests
            |> List.exists(fun request ->
              request.Method = "POST" && request.Target.EndsWith "/poll/votes"))

        Expect.isFalse postedEarly "a choice posted without the Vote button"

        let! ready =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "PollVote")),
            (fun control ->
              control |> Option.exists(fun value -> value.IsEnabled))
          )

        Expect.isTrue ready "the Vote button never armed"

        do!
          onUiAsync(fun () ->
            controlByName(window, "PollVote")
            |> Option.iter(fun control -> click(control :?> Button)))

        let! voted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target.EndsWith "/poll/votes"))
          )

        Expect.isTrue voted "the vote was never posted"

        let body =
          let vote =
            api.Requests
            |> List.find(fun request ->
              request.Method = "POST" && request.Target.EndsWith "/poll/votes")

          match vote.Body with
          | Http.HttpBody.Text text -> text
          | _ -> failtest "the vote had no text body"

        Expect.stringContains body "\"option\":\"o1\"" "the voted option"

        let! summary = showsText(window, "4 votes · 2d left")

        Expect.isTrue summary "the result summary never rendered"

        let! share = showsText(window, "75%")

        Expect.isTrue share "the result share never rendered"

        let! marked =
          probeUiAsync(fun () ->
            controlByName(window, "PollCheckYes") |> Option.isSome)

        Expect.isTrue marked "the voted check never rendered"

        let! voteGone =
          probeUiAsync(fun () ->
            controlByName(window, "PollVote")
            |> Option.forall(fun control -> not(effectiveVisible control)))

        Expect.isTrue voteGone "the Vote button stayed after the vote"
      })

    testCaseAsync
      "a closed poll shows the final results without a Vote button"
      (async {
        let api = pollLogApiWith(strangerPollCommit closedPoll)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        let! summary = showsText(window, "12 votes · Final results")

        Expect.isTrue summary "the final summary never rendered"

        let! share = showsText(window, "75%")

        Expect.isTrue share "the final share never rendered"

        let! voteGone =
          probeUiAsync(fun () ->
            controlByName(window, "PollVote")
            |> Option.forall(fun control -> not(effectiveVisible control)))

        Expect.isTrue voteGone "a closed poll offered a vote"
      })

    testCaseAsync
      "the viewer's own poll shows its results without a Vote button"
      (async {
        let api = pollLogApiWith(ownPollCommit ownPoll)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! summary = showsText(window, "0 votes · 2d left")

        Expect.isTrue summary "the own poll summary never rendered"

        let! voteGone =
          probeUiAsync(fun () ->
            controlByName(window, "PollVote")
            |> Option.forall(fun control -> not(effectiveVisible control)))

        Expect.isTrue voteGone "the author was offered a vote"
      })

    testCaseAsync
      "a poll before approval waits for the commit"
      (async {
        let api = pollLogApiWith(strangerPollCommit pendingPoll)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! status = showsText(window, "Voting opens once approved")

        Expect.isTrue status "the pending poll status never rendered"

        let! voteGone =
          probeUiAsync(fun () ->
            controlByName(window, "PollVote")
            |> Option.forall(fun control -> not(effectiveVisible control)))

        Expect.isTrue voteGone "a pending poll offered a vote"
      })

    testCaseAsync
      "a poll past its close time shows the closed state"
      (async {
        let api = pollLogApiWith(strangerPollCommit expiredPoll)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! status = showsText(window, "Poll closed")

        Expect.isTrue status "the expired poll status never rendered"

        let! voteGone =
          probeUiAsync(fun () ->
            controlByName(window, "PollVote")
            |> Option.forall(fun control -> not(effectiveVisible control)))

        Expect.isTrue voteGone "an expired poll offered a vote"
      })

    testCaseAsync
      "the composer builds a poll and posts it"
      (async {
        let api = logApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        let! pollable =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "PollButton")),
            Option.isSome
          )

        Expect.isTrue pollable "the poll button never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "PollButton")
            |> Option.iter(fun control ->
              match control with
              | :? Button as button -> click button
              | _ -> ()))

        let! firstOption =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "PollOption0")),
            Option.isSome
          )

        Expect.isTrue firstOption "the poll builder never opened"

        let! capped =
          probeUiAsync(fun () ->
            controlByName(window, "PollOption0")
            |> Option.map(fun control -> (control :?> TextBox).MaxLength = 50)
            |> Option.defaultValue false)

        Expect.isTrue capped "the option box has no 50-character cap"

        do!
          onUiAsync(fun () ->
            (controlByName(window, "PollOption0")
             |> Option.iter(fun control -> (control :?> TextBox).Text <- "Yes"))

            (controlByName(window, "PollOption1")
             |> Option.iter(fun control -> (control :?> TextBox).Text <- "No"))

            (controlByName(window, "PollLength")
             |> Option.iter(fun control ->
               (control :?> ComboBox).SelectedIndex <- 2)))

        do!
          onUiAsync(fun () ->
            visibleBoxWithWatermark(window, "Ask a question")
            |> Option.iter(fun box ->
              box.Text <- "Ship it?"

              buttonNear(box, "Commit") |> Option.iter click))

        let! posted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST" && request.Target = "/api/v1/commits"))
          )

        Expect.isTrue posted "the poll commit was never posted"

        let body =
          let post =
            api.Requests
            |> List.find(fun request ->
              request.Method = "POST" && request.Target = "/api/v1/commits")

          match post.Body with
          | Http.HttpBody.Text text -> text
          | _ -> failtest "the poll commit had no text body"

        Expect.stringContains body "Ship it?" "the body"
        Expect.stringContains body "\"poll\"" "the poll"
        Expect.stringContains body "\"Yes\"" "the options"
        Expect.stringContains body "4320" "the picked duration"
      })

    testCaseAsync
      "the log marks unseen commits at the last visit"
      (async {
        let api = strangerNewerLogApi()

        // The visit sits between the stranger's newer commit and the
        // viewer's older one, under the viewer's own key.
        let seeded =
          LastVisitStore.createWith(
            Map [
              "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d|latest",
              DateTimeOffset(2020, 4, 2, 0, 0, 0, TimeSpan.Zero)
            ],
            ignore
          )

        let! window = startShellSeeded(signedInStore(), api :> IHttp, seeded)

        let! marked = showsText(window, "HEAD@{last visit}")

        Expect.isTrue marked "the last-visit marker never showed"
      })

    testCaseAsync
      "the marker stays away when nothing is older than the visit"
      (async {
        let api = twoDaysLogApi()

        // The visit sits after every canned commit, so nothing counts
        // as already seen and no marker can land.
        let seeded =
          LastVisitStore.createWith(
            Map [
              "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d|latest",
              DateTimeOffset(2020, 4, 5, 0, 0, 0, TimeSpan.Zero)
            ],
            ignore
          )

        let! window = startShellSeeded(signedInStore(), api :> IHttp, seeded)

        let! shown = showsText(window, "First landing.")

        Expect.isTrue shown "the log never rendered"

        let! probe =
          probeUiAsync(fun () -> hasText(window, "HEAD@{last visit}"))

        Expect.isFalse probe "the marker showed with nothing seen"
      })

    testCaseAsync
      "the avatar opens the author's profile"
      (async {
        let api = logApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        let! avatar =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "AuthorAvatar")),
            (fun control ->
              control
              |> Option.map(fun found -> found :? Button)
              |> Option.defaultValue false)
          )

        Expect.isTrue avatar "the avatar trigger never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "AuthorAvatar")
            |> Option.iter(fun control ->
              match control with
              | :? Button as button -> click button
              | _ -> ()))

        let! profile = showsText(window, "Builds things.")

        Expect.isTrue profile "the author's profile never opened"
      })

    testCaseAsync
      "the quote composer hides the attach button"
      (async {
        let api = threadApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        let! quoteable =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "QuoteButton")),
            Option.map(fun control -> control :? Button)
            >> Option.defaultValue false
          )

        Expect.isTrue quoteable "the quote action never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "QuoteButton")
            |> Option.iter(fun control ->
              match control with
              | :? Button as button -> click button
              | _ -> ()))

        let! opened =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Add your take"))
          )

        Expect.isTrue opened "the quote composer never appeared"

        let! attachable =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.exists(fun visual ->
              visual.Name = "AttachButton"
              && (match visual with
                  | :? Control as control -> effectiveVisible control
                  | _ -> false)))

        Expect.isFalse attachable "the quote composer offered images"
      })

    testCaseAsync
      "a second click on the active tab reloads the log"
      (async {
        let api = logApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        let logReads() =
          api.Requests
          |> List.filter(fun request ->
            request.Method = "GET" && request.Target = "/api/v1/log/latest")
          |> List.length

        let! before = probeUiAsync logReads
        Expect.isGreaterThanOrEqual before 1 "the boot never read the log"

        do! clickWhenShown(window, "Latest")

        let! reloaded =
          waitForAsync(5000, logReads, fun count -> count > before)

        Expect.isTrue reloaded "the second click never reloaded the log"
      })

    testCaseAsync
      "rapid clicks on the active tab pass the gate"
      (async {
        let api = logApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        let logReads() =
          api.Requests
          |> List.filter(fun request ->
            request.Method = "GET" && request.Target = "/api/v1/log/latest")
          |> List.length

        let! before = probeUiAsync logReads

        // Four clicks inside the gate's window: one reload passes,
        // the rest only scroll.
        do!
          onUiAsync(fun () ->
            for _ in 1..4 do
              buttonWithText(window, "Latest") |> Option.iter click)

        do! Async.Sleep 200
        do! pumpAsync()

        let! after = probeUiAsync logReads
        Expect.equal after (before + 1) "the gate let extra reloads through"
      })

    testCaseAsync
      "a second click returns a deep feed to its top"
      (async {
        let! window = startShell(signedInStore(), manyCardsApi() :> IHttp)

        let! shown = showsText(window, "Card 1.")
        Expect.isTrue shown "the log never rendered"

        do! scrollFeedToEndAsync window
        do! settleAsync()

        let! deep = probeUiAsync(fun () -> feedOffset window)
        Expect.isGreaterThan deep 0.5 "the feed never scrolled"

        do! clickWhenShown(window, "Latest")
        do! settleAsync()

        let! top = probeUiAsync(fun () -> feedOffset window)
        Expect.isLessThanOrEqual top 0.5 "the feed never returned to its top"
      })

    testCaseAsync
      "a reply draft survives the feed recycle"
      (async {
        let! window = startShell(signedInStore(), manyCardsApi() :> IHttp)

        let! shown = showsText(window, "Card 1.")
        Expect.isTrue shown "the log never rendered"

        let! revealed =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ReplyButton")),
            Option.isSome
          )

        Expect.isTrue revealed "the reply action never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "ReplyButton")
            |> Option.iter(fun control ->
              match control with
              | :? Button as button -> click button
              | _ -> ()))

        let! typed =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "Write your reply…")),
            Option.isSome
          )

        Expect.isTrue typed "the reply composer never appeared"

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "Write your reply…")
            |> Option.iter(fun box -> box.Text <- "kept draft"))

        do! settleAsync()
        do! scrollFeedToEndAsync window
        do! settleAsync()
        do! scrollFeedToTopAsync window
        do! settleAsync()

        let! kept =
          probeUiAsync(fun () ->
            textBoxWithWatermark(window, "Write your reply…")
            |> Option.map(fun box ->
              match box.Text with
              | null -> ""
              | text -> text)
            |> Option.defaultValue "")

        Expect.equal kept "kept draft" "the recycle wiped the draft"
      })

    testCaseAsync
      "the shared composer commits and reloads the open log"
      (async {
        let api = logApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        let logReads() =
          api.Requests
          |> List.filter(fun request ->
            request.Method = "GET" && request.Target = "/api/v1/log/latest")
          |> List.length

        let! before = probeUiAsync logReads

        let! typed =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "What did you ship?")),
            Option.isSome
          )

        Expect.isTrue typed "the shared composer never appeared"

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun box ->
              box.Text <- "Committed from the chrome"

              buttonNear(box, "Commit") |> Option.iter click))

        let! posted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST" && request.Target = "/api/v1/commits"))
          )

        Expect.isTrue posted "the commit never left the composer"

        let! reloaded =
          waitForAsync(5000, logReads, fun count -> count > before)

        Expect.isTrue reloaded "the landed commit never reloaded the log"
      })

    testCaseAsync
      "a thread and back keeps the log's scroll"
      (async {
        let! window = startShell(signedInStore(), manyCardsApi() :> IHttp)

        let! shown = showsText(window, "Card 1.")

        Expect.isTrue shown "the first card never rendered"

        do! scrollFeedToEndAsync window

        // Whatever offset the scroll landed on is the offset the cached
        // page must still hold on return.
        let! before = probeUiAsync(fun () -> feedOffset window)

        // Realization follows the scroll a dispatcher turn later, so
        // the click waits for the card the way a user would.
        do! clickWhenShown(window, "Card 6.")

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Back to the log") |> Option.iter click)

        // The cached log page comes back with its rows; the offset it
        // left is the offset it still holds.
        let! back =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Card 6."))
          )

        Expect.isTrue back "the log never came back"

        let! after = probeUiAsync(fun () -> feedOffset window)

        Expect.isGreaterThan after (before - 0.5) "the log scroll reset"
      })

    testCaseAsync
      "a Following thread and back keeps the log's scroll"
      (async {
        let api = manyCardsApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "Card 1.")

        Expect.isTrue shown "the first card never rendered"

        let readsOf(target: string) =
          api.Requests
          |> List.filter(fun request ->
            request.Method = "GET" && request.Target.StartsWith target)
          |> List.length

        do! clickWhenShown(window, "Following")

        // The tab's page is a fresh build: it asks the server for the
        // following log and renders the same cards.
        let! opened =
          waitForAsync(
            5000,
            (fun () -> readsOf "/api/v1/log/following"),
            (fun count -> count > 0)
          )

        Expect.isTrue opened "the following log was never requested"

        do! scrollFeedToEndAsync window

        // Whatever offset the Following scroll landed on is the offset
        // the cached page must still hold on return.
        let! parked =
          waitForAsync(
            5000,
            (fun () -> feedOffset window),
            (fun offset -> offset > 0.0)
          )

        Expect.isTrue parked "the Following scroll never left the top"

        let! before = probeUiAsync(fun () -> feedOffset window)

        // Realization follows the scroll a dispatcher turn later, so
        // the click waits for the card the way a user would.
        do! clickWhenShown(window, "Card 6.")

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Back to the log") |> Option.iter click)

        // The cached Following page comes back with its rows; the
        // offset it left is the offset it still holds.
        let! back =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Card 6."))
          )

        Expect.isTrue back "the log never came back"

        let! after = probeUiAsync(fun () -> feedOffset window)

        Expect.isGreaterThan after (before - 0.5) "the log scroll reset"
      })

    testCaseAsync
      "a press on the back line returns to the log"
      (async {
        let! window = startShell(signedInStore(), threadApi() :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "It was DNS.")

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        do! settleAsync()

        let! back =
          probeUiAsync(fun () -> buttonWithText(window, "Back to the log"))

        Expect.isSome back "the back line never appeared"

        do! pressControlAsync(window, back.Value)

        let! returned =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              hasText(root, "Following")
              && not(hasText(root, "Back to the log")))
          )

        Expect.isTrue returned "the press never returned to the log"
      })

    testCaseAsync
      "the back line sits below the drag band and owns its center"
      (async {
        let! window = startShell(signedInStore(), threadApi() :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "It was DNS.")

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        do!
          probeUiAsync(fun () ->
            match window.CaptureRenderedFrame() with
            | null -> ()
            | bitmap -> bitmap.Dispose())

        do! settleAsync()

        let! measured =
          probeUiAsync(fun () ->
            buttonWithText(window, "Back to the log")
            |> Option.bind(fun button ->
              let top = button.TranslatePoint(Point(0.0, 0.0), window)

              if top.HasValue then
                let center =
                  Point(
                    top.Value.X + button.Bounds.Width / 2.0,
                    top.Value.Y + button.Bounds.Height / 2.0
                  )

                Some(top.Value, center, button)
              else
                None))

        Expect.isSome measured "the back line never measured"

        let top, center, back = measured.Value

        let! owns =
          probeUiAsync(fun () ->
            let target = back :> Visual

            match window.GetVisualAt(center) with
            | null -> false
            | hit ->
              hit = target
              || hit.GetVisualAncestors()
                 |> Seq.exists(fun ancestor -> ancestor = target))

        Expect.isTrue
          owns
          "the drag band took the point instead of the back line"

        Expect.isGreaterThanOrEqual
          top.Y
          Theme.bandHeight
          "the back line still sits under the drag band"
      })

    testSequenced(
      testCaseAsync
        "the search box clears the band and takes a press"
        (async {
          let! window =
            startShell(signedInStore(), searchApi(false, false) :> IHttp)

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
              |> Option.iter(fun control ->
                (control :?> TextBox).Text <- "shippin"))

          let! settled = seesVisible(window, "@kai-kou")

          Expect.isTrue settled "the search never settled"

          do! clickWhenShown(window, "See all matching commits")

          let! counted = seesVisible(window, "1 match")

          Expect.isTrue counted "the results page never counted its match"

          let! boxShown =
            waitForAsync(
              5000,
              (fun () -> controlByName(window, "SearchBox")),
              Option.isSome
            )

          Expect.isTrue boxShown "the results page never showed its box"

          do! settleAsync()

          let! box =
            probeUiAsync(fun () ->
              controlByName(window, "SearchBox")
              |> Option.map(fun control -> control :?> TextBox))

          Expect.isSome box "the results page's box never measured"

          let! top =
            probeUiAsync(fun () ->
              let origin = box.Value.TranslatePoint(Point(0.0, 0.0), window)

              if origin.HasValue then origin.Value.Y else nan)

          Expect.isGreaterThanOrEqual
            top
            Theme.bandHeight
            "the search box still reaches into the drag band"

          do! pressControlAsync(window, box.Value)

          let! focused =
            probeUiAsync(fun () ->
              obj.ReferenceEquals(
                window.FocusManager.GetFocusedElement(),
                box.Value
              ))

          Expect.isTrue focused "the press never focused the search box"
        })
    )

    testCaseAsync
      "opening a commit shows the thread with its replies"
      (async {
        let api = threadApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        let! card =
          waitForAsync(
            5000,
            (fun () -> buttonWithText(window, "It was DNS.")),
            Option.isSome
          )

        Expect.isTrue card "the commit card never appeared"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread page never rendered"

        let! replies = showsText(window, "A reply.")

        Expect.isTrue replies "the replies never rendered"

        Expect.exists
          api.Requests
          (fun request ->
            request.Method = "GET"
            && request.Target =
              "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d")
          "the thread request"
      })

    testCaseAsync
      "the reply composer posts a reply on the thread"
      (async {
        let api = threadApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        // The composer stays hidden until the reply button reveals it.
        let! revealed =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ReplyButton")),
            (fun control ->
              control
              |> Option.map(fun found -> found :? Button)
              |> Option.defaultValue false)
          )

        Expect.isTrue revealed "the reply button never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "ReplyButton")
            |> Option.iter(fun control ->
              match control with
              | :? Button as button -> click button
              | _ -> ()))

        let! typed =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "Write your reply…")),
            Option.isSome
          )

        Expect.isTrue typed "the reply composer never appeared"

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "Write your reply…")
            |> Option.iter(fun box ->
              box.Text <- "A reply from the test"

              buttonNear(box, "Commit") |> Option.iter click))

        let! posted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target = "/api/v1/commits"
                && (match request.Body with
                    | HttpBody.Text text -> text.Contains("parent_id")
                    | _ -> false)))
          )

        Expect.isTrue posted "the reply was never posted"

        // Everyone the parent addressed rides the reply by default.
        let replyBody =
          api.Requests
          |> List.choose(fun request ->
            match request.Method, request.Target, request.Body with
            | "POST", "/api/v1/commits", HttpBody.Text text -> Some text
            | _ -> None)
          |> List.exactlyOne

        Expect.stringContains
          replyBody
          "\"reply_recipients\":[\"01a15638-1111-733c-89b3-373a1a0c538a\",\"01a15638-2222-733c-89b3-373a1a0c538a\"]"
          "the default audience rides the reply"
      })

    testCaseAsync
      "the picker leaves one participant out of the reply"
      (async {
        let api = threadApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")
        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")
        Expect.isTrue thread "the thread never rendered"

        do! revealReply window

        let! typed =
          waitForAsync(
            5000,
            (fun () -> visibleBoxWithWatermark(window, "Write your reply…")),
            Option.isSome
          )

        Expect.isTrue typed "the reply composer never appeared"

        // The exclusion outlives the dialog: the picker closes, the
        // reply posts, and the body carries the survivor alone.
        do! clickNamed(window, "ReplyingToLine")

        let! dialog =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ReplyParticipant1")),
            Option.isSome
          )

        Expect.isTrue dialog "the dialog never opened"

        do! clickNamed(window, "ReplyParticipant1")
        do! clickNamed(window, "ReplyPickerDone")

        do!
          onUiAsync(fun () ->
            visibleBoxWithWatermark(window, "Write your reply…")
            |> Option.iter(fun box ->
              box.Text <- "A reply from the test"

              buttonNear(box, "Commit") |> Option.iter click))

        let! posted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target = "/api/v1/commits"
                && (match request.Body with
                    | HttpBody.Text text -> text.Contains("parent_id")
                    | _ -> false)))
          )

        Expect.isTrue posted "the reply was never posted"

        let replyBody =
          api.Requests
          |> List.choose(fun request ->
            match request.Method, request.Target, request.Body with
            | "POST", "/api/v1/commits", HttpBody.Text text -> Some text
            | _ -> None)
          |> List.exactlyOne

        Expect.stringContains
          replyBody
          "\"reply_recipients\":[\"01a15638-2222-733c-89b3-373a1a0c538a\"]"
          "the survivor rode the reply"

        Expect.isFalse
          (replyBody.Contains "01a15638-1111-733c-89b3-373a1a0c538a")
          "the excluded participant stayed out"
      })

    testCaseAsync
      "the author row stays selected and the line reads the site's words"
      (async {
        let api = threadApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")
        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")
        Expect.isTrue thread "the thread never rendered"

        do! revealReply window

        let! typed =
          waitForAsync(
            5000,
            (fun () -> visibleBoxWithWatermark(window, "Write your reply…")),
            Option.isSome
          )

        Expect.isTrue typed "the reply composer never appeared"

        // The trigger names the audience and carries its aria name.
        let! line =
          waitForAsync(5000, (fun () -> replyingLineText window), Option.isSome)

        Expect.isTrue line "the trigger line never read the audience"

        let! lineText = probeUiAsync(fun () -> replyingLineText window)

        Expect.equal
          (Some "Replying to @mona and @hubot")
          lineText
          "the trigger line never read the audience"

        do! clickNamed(window, "ReplyingToLine")

        let! dialog =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ReplyParticipant0")),
            Option.isSome
          )

        Expect.isTrue dialog "the dialog never opened"

        let! author =
          probeUiAsync(fun () ->
            controlByName(window, "ReplyParticipant0")
            |> Option.bind(fun control ->
              match control with
              | :? Button as row ->
                let check =
                  row.GetVisualDescendants()
                  |> Seq.tryFind(fun visual -> visual :? PathIcon)

                let visible =
                  check
                  |> Option.map(fun mark ->
                    match mark with
                    | :? Control as mark -> effectiveVisible mark
                    | _ -> false)

                Some(row.IsEnabled, visible)
              | _ -> None))

        Expect.equal (Some(false, Some true)) author "the author row left"

        let! caption = showsText(window, "Commit author")
        Expect.isTrue caption "the author row never carried its caption"
      })

    testCaseAsync
      "a thread with only the author never offers the trigger"
      (async {
        let api = undoApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "My own commit.")
        Expect.isTrue shown "the own log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "My own commit.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")
        Expect.isTrue thread "the thread never rendered"

        do! revealReply window

        let! typed =
          waitForAsync(
            5000,
            (fun () -> visibleBoxWithWatermark(window, "Write your reply…")),
            Option.isSome
          )

        Expect.isTrue typed "the reply composer never appeared"

        do! settleAsync()

        let! absent =
          probeUiAsync(fun () ->
            controlByName(window, "ReplyingToLine")
            |> Option.map effectiveVisible)

        Expect.isFalse (absent = Some true) "the trigger stood with no audience"
      })

    testCaseAsync
      "the inline log reply offers the audience its thread read landed"
      (async {
        let api = threadApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")
        Expect.isTrue shown "the log never rendered"

        // The inline composer opens straight on the log row.
        do! revealReply window

        let! typed =
          waitForAsync(
            5000,
            (fun () -> visibleBoxWithWatermark(window, "Write your reply…")),
            Option.isSome
          )

        Expect.isTrue typed "the inline reply never opened"

        // The parent's thread read lands and the picker rides in.
        let! offered =
          waitForAsync(5000, (fun () -> replyingLineText window), Option.isSome)

        Expect.isTrue offered "the audience never reached the inline composer"

        let! lineText = probeUiAsync(fun () -> replyingLineText window)

        Expect.equal
          (Some "Replying to @mona and @hubot")
          lineText
          "the audience never reached the inline composer"

        do!
          onUiAsync(fun () ->
            visibleBoxWithWatermark(window, "Write your reply…")
            |> Option.iter(fun box ->
              box.Text <- "A reply from the test"

              buttonNear(box, "Commit") |> Option.iter click))

        let! posted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target = "/api/v1/commits"
                && (match request.Body with
                    | HttpBody.Text text ->
                      text.Contains "01a15638-1111-733c-89b3-373a1a0c538a"
                    | _ -> false)))
          )

        Expect.isTrue posted "the audience never rode the inline reply"
      })

    testCaseAsync
      "a failed thread read posts the reply without participant ids"
      (async {
        let api = logApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")
        Expect.isTrue shown "the log never rendered"

        do! revealReply window

        let! typed =
          waitForAsync(
            5000,
            (fun () -> visibleBoxWithWatermark(window, "Write your reply…")),
            Option.isSome
          )

        Expect.isTrue typed "the inline reply never opened"

        // The read fails against this api; the picker never shows and
        // the reply still posts, carrying no ids.
        do! settleAsync()

        let! absent =
          probeUiAsync(fun () ->
            controlByName(window, "ReplyingToLine")
            |> Option.map effectiveVisible)

        Expect.isFalse
          (absent = Some true)
          "the trigger stood over a failed read"

        do!
          onUiAsync(fun () ->
            visibleBoxWithWatermark(window, "Write your reply…")
            |> Option.iter(fun box ->
              box.Text <- "A reply from the test"

              buttonNear(box, "Commit") |> Option.iter click))

        let! posted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target = "/api/v1/commits"
                && (match request.Body with
                    | HttpBody.Text text -> text.Contains("parent_id")
                    | _ -> false)))
          )

        Expect.isTrue posted "the reply was never posted"

        let replyBody =
          api.Requests
          |> List.choose(fun request ->
            match request.Method, request.Target, request.Body with
            | "POST", "/api/v1/commits", HttpBody.Text text -> Some text
            | _ -> None)
          |> List.exactlyOne

        Expect.stringContains replyBody "\"reply_recipients\":[]" "no ids rode"
      })

    testCaseAsync
      "the participant bound speaks before any request"
      (async {
        let api = overflowThreadApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")
        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")
        Expect.isTrue thread "the thread never rendered"

        do! revealReply window

        let! typed =
          waitForAsync(
            5000,
            (fun () -> visibleBoxWithWatermark(window, "Write your reply…")),
            Option.isSome
          )

        Expect.isTrue typed "the reply composer never appeared"

        do!
          onUiAsync(fun () ->
            visibleBoxWithWatermark(window, "Write your reply…")
            |> Option.iter(fun box ->
              box.Text <- "A reply from the test"

              buttonNear(box, "Commit") |> Option.iter click))

        let! refused =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              hasText(
                root,
                "A reply may include at most 100 participants across selected recipients and explicit mentions."
              )
          )

        Expect.isTrue refused "the bound never spoke"

        Expect.isFalse
          (api.Requests
           |> List.exists(fun request ->
             request.Method = "POST" && request.Target = "/api/v1/commits"))
          "the bound held the reply back"
      })

    testCaseAsync
      "a landed reply shows on the thread at once"
      (async {
        let api = replyingThreadApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        let! revealed =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ReplyButton")),
            (fun control ->
              control
              |> Option.map(fun found -> found :? Button)
              |> Option.defaultValue false)
          )

        Expect.isTrue revealed "the reply button never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "ReplyButton")
            |> Option.iter(fun control ->
              match control with
              | :? Button as button -> click button
              | _ -> ()))

        let! typed =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "Write your reply…")),
            Option.isSome
          )

        Expect.isTrue typed "the reply composer never appeared"

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "Write your reply…")
            |> Option.iter(fun box ->
              box.Text <- "Fresh from the thread test"

              buttonNear(box, "Commit") |> Option.iter click))

        // The reload a landed reply triggers re-reads the replies, so
        // the fresh reply renders without leaving the page.
        let! landed = showsText(window, "Fresh from the thread test")

        Expect.isTrue landed "the landed reply never showed on the thread"
      })

    testCaseAsync
      "the pastel card action posts the pastel from the log"
      (async {
        let api = threadApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        let! pastel =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "PastelButton")),
            (fun control ->
              control
              |> Option.map(fun c -> c :? Button)
              |> Option.defaultValue false)
          )

        Expect.isTrue pastel "the pastel action never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "PastelButton")
            |> Option.iter(fun control -> click(control :?> Button)))

        let! posted =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target =
                  "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/pasteis"))
          )

        Expect.isTrue posted "the pastel was never posted"
      })

    testCaseAsync
      "the log shows a feed card's replies and walks the rest"
      (async {
        let api = previewLogApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        // The payload's preview slice renders as real reply cards under
        // the card, with the count link behind it.
        let! previewed = showsText(window, "Preview reply one.")

        Expect.isTrue previewed "the preview replies never rendered"

        let! counted = showsText(window, "Show 3 more replies")

        Expect.isTrue counted "the count link never rendered"

        do! clickWhenShown(window, "Show 3 more replies")

        let! walked = showsText(window, "Expanded reply one.")

        Expect.isTrue walked "the walked replies never rendered"

        let repliesRead =
          api.Requests
          |> List.exists(fun request ->
            request.Method = "GET"
            && request.Target.StartsWith
              "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/replies")

        Expect.isTrue repliesRead "the replies endpoint was never read"
      })

    testCaseAsync
      "a reply-less commit draws no rail"
      (async {
        let! window = startShell(signedInStore(), previewLogApi() :> IHttp)

        let! first = showsText(window, "It was DNS.")

        Expect.isTrue first "the log never rendered"

        // The reply-less commit rides under the replied one; scrolling
        // realizes its row. The row — the card plus everything mounted
        // under it — then holds no rail piece at all: the empty
        // preview slice draws nothing.
        do! scrollFeedToEndAsync window

        let! drew =
          probeUiAsync(fun () ->
            let quiet =
              window.GetVisualDescendants()
              |> Seq.tryFind(function
                | :? TextBlock as block when block.Text = "Quiet commit." ->
                  true
                | _ -> false)

            let commitRow(visual: Visual) =
              match visual with
              | :? Button as card -> card.Classes.Contains(name CommitRow)
              | _ -> false

            quiet
            |> Option.bind(fun body ->
              body.GetVisualAncestors()
              |> Seq.tryPick(fun ancestor ->
                if commitRow ancestor then Some ancestor else None)
              |> Option.bind(fun card ->
                match card.Parent with
                | :? Control as row ->
                  Some(
                    row.GetVisualDescendants()
                    |> Seq.filter(fun visual -> visual.Name = "ThreadElbow")
                    |> Seq.length
                  )
                | _ -> None)))

        Expect.isSome drew "the quiet card never rendered"
        Expect.equal drew.Value 0 "the reply-less card drew a rail"
      })

    testCaseAsync
      "the elbow lands on the reply's avatar center"
      (async {
        let! window = startShell(signedInStore(), previewLogApi() :> IHttp)

        let! shown = showsText(window, "Preview reply one.")

        Expect.isTrue shown "the preview replies never rendered"

        // Three facts of one rail: the elbow starts flush at the
        // card's bottom edge (no gap), its horizontal lands on the
        // ring's center, and the ring paints after the lines (the
        // rail passes behind the avatar).
        let! landing =
          probeUiAsync(fun () ->
            let first(name: string) =
              window.GetVisualDescendants()
              |> Seq.tryFind(fun visual -> visual.Name = name)

            let cardBottom =
              window.GetVisualDescendants()
              |> Seq.tryFind(function
                | :? TextBlock as block when block.Text = "It was DNS." -> true
                | _ -> false)
              |> Option.bind(fun body ->
                body.GetVisualAncestors()
                |> Seq.tryPick(fun ancestor ->
                  match ancestor with
                  | :? Button as card when
                    card.Classes.Contains(name CommitRow)
                    ->
                    Some card
                  | _ -> None))
              |> Option.bind(fun card ->
                let bottom =
                  card.TranslatePoint(Point(0.0, card.Bounds.Height), window)

                if bottom.HasValue then Some bottom.Value.Y else None)

            match first "ThreadElbow", first "ReplyRing", cardBottom with
            | Some elbowControl, Some ringControl, Some bottomY ->
              let elbow = elbowControl :?> Path
              let ring = ringControl :?> Border

              let horizontal =
                elbow.TranslatePoint(Point(0.0, elbow.Bounds.Height), window)

              let elbowTop = elbow.TranslatePoint(Point(0.0, 0.0), window)

              let center =
                ring.TranslatePoint(
                  Point(0.0, ring.Bounds.Height / 2.0),
                  window
                )

              let ringBehind =
                match ring.Parent with
                | :? Panel as rail ->
                  rail.Children.IndexOf ring > rail.Children.IndexOf elbow
                | _ -> false

              if
                horizontal.HasValue && elbowTop.HasValue && center.HasValue
              then
                Some(
                  abs(horizontal.Value.Y - center.Value.Y),
                  abs(elbowTop.Value.Y - bottomY),
                  ringBehind
                )
              else
                None
            | _ -> None)

        Expect.isSome landing "the rail never rendered"

        let landingError, gapError, ringBehind = landing.Value

        Expect.isLessThanOrEqual landingError 2.0 "the elbow misses the ring"

        Expect.isLessThanOrEqual
          gapError
          1.0
          "the elbow leaves a gap below the card"

        Expect.isTrue ringBehind "the rail draws over the avatar"
      })

    testCaseAsync
      "the feed nests a reply's sub-thread"
      (async {
        let! window = startShell(signedInStore(), previewLogApi() :> IHttp)

        let! nested = showsText(window, "Nested in the feed.")

        Expect.isTrue nested "the nested reply never rendered"
      })

    testCaseAsync
      "the thread rail elbows the nested chain"
      (async {
        let! window = startShell(signedInStore(), threadedApi() :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        let! nested = showsText(window, "Nested under the root.")

        Expect.isTrue nested "the nested reply never rendered"

        // The rail ties the replies into the commit's lane: an elbow
        // branches each level off its parent's spine.
        let! elbows =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.filter(fun visual -> visual.Name = "ThreadElbow")
            |> Seq.length)

        Expect.isGreaterThan elbows 1 "the rail never drew its elbows"
      })

    testCaseAsync
      "the thread walks a node's deeper replies"
      (async {
        let! window = startShell(signedInStore(), threadedApi() :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! nested = showsText(window, "Nested under the root.")

        Expect.isTrue nested "the nested reply never rendered"

        do! clickWhenShown(window, "Show the last reply")

        let! walked = showsText(window, "Deeper reply walked in.")

        Expect.isTrue walked "the deeper reply never rendered"
      })

    testCaseAsync
      "hovering a mention opens the hover card"
      (async {
        let! window = startShell(signedInStore(), mentionLogApi() :> IHttp)

        // The body rides as inlines once it carries a mention, so the
        // handle itself is the proof the log rendered.
        let! handle =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "MentionHandle")),
            Option.isSome
          )

        Expect.isTrue handle "the mention handle never rendered"

        // A real pointer move over the handle: the flyout answers with
        // its loading card after the open delay.
        do!
          onUiAsync(fun () ->
            controlByName(window, "MentionHandle")
            |> Option.iter(fun handle ->
              let trigger =
                handle.GetVisualAncestors()
                |> Seq.tryPick(function
                  | :? Button as button -> Some button
                  | _ -> None)

              trigger
              |> Option.iter(fun button ->
                let center =
                  Point(
                    button.Bounds.Width / 2.0,
                    button.Bounds.Height / 2.0
                  )

                let moved = button.TranslatePoint(center, window)

                if moved.HasValue then
                  window.MouseMove moved.Value)))

        let! card = showsText(window, "Reading the profile…")

        Expect.isTrue card "the hover card never opened"
      })

    testCaseAsync
      "an organization mention opens the organization page, not the hover card"
      (async {
        let page =
          match LogPage.decode orgMentionLogPageJson with
          | Ok page -> page
          | Error failure ->
            failwith $"the org mention page never decoded: {failure.Message}"

        let commit =
          match page.Page.Items with
          | [ LiveCommit commit ] -> commit
          | _ -> failwith "the org mention page never carried its commit"

        let opened = ResizeArray<string>()

        let env = TestEnv(signedInStore(), logApi() :> IHttp)

        let source: UserFlyout.Source = {
          Fetch = fun _ -> async { return None }
          OnOpen = ignore
          OnOpenOrg = opened.Add
          OnOpenTag = ignore
          Cache = (env :> IProvideImageCache).ImageCache
        }

        let! window =
          probeUiAsync(fun () ->
            let body =
              CommitCard.body(
                {
                  Flyout = ValueSome source
                  Tone = CommitCard.Accent
                  Mentions = commit.Mentions
                },
                commit.Body,
                commit.BodySegments
              )

            let window = Window(Content = body)
            window.Show()
            window)

        // The org handle renders in the accent, like a user's.
        let! handle =
          waitForAsync(
            5000,
            (fun () ->
              controlByName(window, "MentionHandle")
              |> Option.bind(fun control ->
                match control with
                | :? TextBlock as block -> Some block.Text
                | _ -> None)),
            (fun text -> text = Some "@devnull")
          )

        Expect.isTrue handle "the organization handle never rendered"

        let! accented =
          probeUiAsync(fun () ->
            controlByName(window, "MentionHandle")
            |> Option.map(fun handle -> handle.Classes.Contains(name FgAccent))
            |> Option.defaultValue false)

        Expect.isTrue accented "the organization handle never wore the accent"

        // No hover card rides the org kind.
        let! noFlyout =
          probeUiAsync(fun () ->
            controlByName(window, "UserFlyout") |> Option.isNone)

        Expect.isTrue noFlyout "the organization mention grew a hover card"

        // The wrap is the file's own inline button: the nearest
        // ancestor of the handle takes the click.
        do!
          onUiAsync(fun () ->
            controlByName(window, "MentionHandle")
            |> Option.bind(fun handle ->
              handle.GetVisualAncestors()
              |> Seq.tryPick(function
                | :? Button as button -> Some button
                | _ -> None))
            |> Option.iter click)

        let! navigated =
          waitForAsync(
            5000,
            (fun () -> opened),
            (fun opened -> opened.Count = 1)
          )

        Expect.isTrue navigated "the click never raised the navigation"

        Expect.equal opened[0] "devnull" "the navigation never named the org"
      })

    testCaseAsync
      "a hashtag links its search, and only in prose"
      (async {
        let opened = ResizeArray<string>()

        let env = TestEnv(signedInStore(), logApi() :> IHttp)

        let source: UserFlyout.Source = {
          Fetch = fun _ -> async { return None }
          OnOpen = ignore
          OnOpenOrg = ignore
          OnOpenTag = opened.Add
          Cache = (env :> IProvideImageCache).ImageCache
        }

        let! window =
          probeUiAsync(fun () ->
            let body =
              CommitCard.body(
                {
                  Flyout = ValueSome source
                  Tone = CommitCard.Accent
                  Mentions = []
                },
                "",
                [
                  TextSegment("Pair with #fsharp and ref #123 and word a#b")
                  CodeSegment "#fsharp"
                ]
              )

            let window = Window(Content = body)
            window.Show()
            window)

        let! tag =
          waitForAsync(
            5000,
            (fun () ->
              controlByName(window, "HashtagTag")
              |> Option.bind(fun control ->
                match control with
                | :? TextBlock as block -> Some block.Text
                | _ -> None)),
            (fun text -> text = Some "#fsharp")
          )

        Expect.isTrue tag "the hashtag never rendered"

        // `#123` and `a#b` stay prose, and the code segment never
        // tokenizes: exactly one named tag control exists.
        let! count =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.filter(fun visual -> visual.Name = "HashtagTag")
            |> Seq.length)

        Expect.equal count 1 "more than the real tag became clickable"

        let! accented =
          probeUiAsync(fun () ->
            controlByName(window, "HashtagTag")
            |> Option.map(fun tag -> tag.Classes.Contains(name FgAccent))
            |> Option.defaultValue false)

        Expect.isTrue accented "the hashtag never wore the accent"

        // The wrap is the file's own inline button: the nearest
        // ancestor of the tag takes the click.
        do!
          onUiAsync(fun () ->
            controlByName(window, "HashtagTag")
            |> Option.bind(fun tag ->
              tag.GetVisualAncestors()
              |> Seq.tryPick(function
                | :? Button as button -> Some button
                | _ -> None))
            |> Option.iter click)

        let! navigated =
          waitForAsync(
            5000,
            (fun () -> opened),
            (fun opened -> opened.Count = 1)
          )

        Expect.isTrue navigated "the click never raised the navigation"

        Expect.equal opened[0] "#fsharp" "the navigation never named the tag"

        Expect.equal
          "/search/%23fsharp"
          ("/search/" + Uri.EscapeDataString opened[0])
          "the search url never escaped the tag"
      })

    testCaseAsync
      "the hover card opens the profile, not the commit"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        // A real pointer move over the avatar opens the card after the
        // open delay.
        do!
          onUiAsync(fun () ->
            controlByName(window, "AuthorAvatar")
            |> Option.iter(fun control ->
              match control with
              | :? Button as button ->
                let center =
                  Point(button.Bounds.Width / 2.0, button.Bounds.Height / 2.0)

                let moved = button.TranslatePoint(center, window)

                if moved.HasValue then
                  window.MouseMove moved.Value
              | _ -> ()))

        let! card = showsText(window, "Reading the profile…")

        Expect.isTrue card "the hover card never opened"

        // The card is its own button: its click opens the profile, and
        // the handled click never reaches the commit behind it. The
        // opaque surface rides inside the button, so the theme's hover
        // wash cannot turn the card translucent.
        do!
          onUiAsync(fun () ->
            controlByName(window, "UserFlyout")
            |> Option.iter(fun control ->
              match control with
              | :? ContentControl as host ->
                match host.Content with
                | :? Button as openCard ->
                  Expect.isTrue
                    (match openCard.Content with
                     | :? Border -> true
                     | _ -> false)
                    "the card's surface left the button"

                  click openCard
                | _ -> ()
              | _ -> ()))

        let! shut =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Reading the profile…")))
          )

        Expect.isTrue shut "the card click never closed the flyout"

        // The identity meta line only renders on the profile page;
        // neither the aside card nor the hover card draws it. The
        // viewer's own page carries no follow or mute buttons.
        let! profile =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              hasText(
                root,
                "GitHub · San Francisco · example.test · Joined January 2026"
              ))
          )

        Expect.isTrue profile "the profile never opened"

        let! fellThrough =
          probeUiAsync(fun () -> hasText(window, "Back to the log"))

        Expect.isFalse fellThrough "the card click fell through to the commit"
      })

    testCaseAsync
      "a link preview is its own surface, not the card's click"
      (async {
        let! window, env =
          startShellWithBrowser(signedInStore(), mentionLogApi() :> IHttp)

        // The card being its own button, with the site's own line on
        // it, is the contract. The click opens through the headless
        // browser's recorder, never a real browser.
        let! preview =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "LinkPreviewCard")),
            (fun found ->
              found
              |> Option.map(fun control -> control :? Button)
              |> Option.defaultValue false)
          )

        Expect.isTrue preview "the link preview card never appeared"

        let! titled = showsText(window, "The postmortem")

        Expect.isTrue titled "the preview title never rendered"

        let! sited = showsText(window, "example.test")

        Expect.isTrue sited "the preview site line never rendered"

        do!
          onUiAsync(fun () ->
            controlByName(window, "LinkPreviewCard")
            |> Option.bind(function
              | :? Button as button -> Some button
              | _ -> None)
            |> Option.iter click)

        let! opened =
          waitForAsync(
            5000,
            (fun () -> env.OpenedUrls),
            (fun urls -> urls = [ "https://example.test/postmortem" ])
          )

        Expect.isTrue opened "the link preview never opened externally"
      })

    testCaseAsync
      "the thread's quote action arms the composer over the commit"
      (async {
        let api = threadApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        do!
          onUiAsync(fun () ->
            controlByName(window, "QuoteButton")
            |> Option.map(fun control -> control :?> Button)
            |> Option.iter click)

        // The quote composer opens in place, over the quoted commit:
        // the thread stays on screen behind it.
        let! armed = showsText(window, "Add your take")

        Expect.isTrue armed "the quote composer never armed"

        let! stayed = showsText(window, "Back to the log")

        Expect.isTrue stayed "the thread left its own quote"

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "Add your take")
            |> Option.iter(fun box ->
              box.Text <- "My take on this"

              buttonNear(box, "Commit") |> Option.iter click))

        let! posted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST" && request.Target = "/api/v1/commits"))
          )

        Expect.isTrue posted "the quote never posted"

        // The landed quote opens the created commit's thread.
        let! left =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Add your take")))
          )

        Expect.isTrue left "the quote composer never closed"
      })

    testCaseAsync
      "a quoted card opens the quoted commit's thread"
      (async {
        let api = quotedLogApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        // The quoted card rides under the quoting commit's body.
        let! quoted = showsText(window, "Second commit.")

        Expect.isTrue quoted "the quoted card never rendered"

        do!
          onUiAsync(fun () ->
            controlByName(window, "QuotedCard")
            |> Option.map(fun control -> control :?> Button)
            |> Option.iter click)

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the quoted thread never opened"

        let quotedRead =
          api.Requests
          |> List.exists(fun request ->
            request.Target.StartsWith
              "/api/v1/commits/018f4c1e-9999-7c3a-9b2d-5e6f7a8b9c0d")

        Expect.isTrue quotedRead "the quoted commit was never read"
      })

    testCaseAsync
      "the inbox lists notifications"
      (async {
        let api = accountApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Notifications")

        let! header = showsText(window, "Notifications · 3 unread")

        Expect.isTrue header "the inbox header never rendered"

        let! replied = showsText(window, "replied to your commit")

        Expect.isTrue replied "the notification never rendered"

        let! preview = showsText(window, "It was DNS.")

        Expect.isTrue preview "the commit preview never rendered"

        Expect.exists
          api.Requests
          (fun request -> request.Target.StartsWith "/api/v1/notifications")
          "the inbox request"
      })

    testCaseAsync
      "an organization context carries the whose wording"
      (async {
        let api = orgContextInboxApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Notifications")

        let! replied = showsText(window, "replied to @acme's commit")

        Expect.isTrue replied "the org-context reply never rendered"

        let! mentioned = showsText(window, "mentioned @acme")

        Expect.isTrue mentioned "the org-context mention never rendered"

        let! actor = showsText(window, "Acme")

        Expect.isTrue actor "the organization actor never rendered"
      })

    testCaseAsync
      "the inbox filter requests the typed list"
      (async {
        let api = groupedInboxApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Notifications")

        let! groupRow = showsText(window, "starred your commit")

        Expect.isTrue groupRow "the group row never rendered"

        do! clickWhenShown(window, "Stars")

        let! typed =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Target.Contains "type=stars"))
          )

        Expect.isTrue typed "the typed inbox was never requested"
      })

    testCaseAsync
      "a group expands and lists its events"
      (async {
        let api = groupedInboxApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Notifications")

        do! clickWhenShown(window, "3 stars")

        let! expanded =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Starred once."))
          )

        if not expanded then
          let! texts =
            probeUiAsync(fun () ->
              window.GetVisualDescendants()
              |> Seq.choose(fun visual ->
                match visual with
                | :? TextBlock as textBlock -> Option.ofObj textBlock.Text
                | _ -> None)
              |> Seq.toList)

          let visible = System.String.Join(" | ", texts)

          Expect.isTrue
            expanded
            $"the group events never rendered; visible: {visible}"

        Expect.exists
          api.Requests
          (fun request -> request.Target.Contains "/group")
          "the group request"
      })

    testCaseAsync
      "the profile shows the signed-in user and their commits"
      (async {
        let api = accountApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Profile")

        // The 2.0 program loop renders through the dispatcher: the
        // bio lands a dispatcher turn after the click, so the test
        // waits for it instead of probing the tree synchronously.
        let! bio = showsText(window, "Builds things.")

        Expect.isTrue bio "the bio never rendered"

        let! commits = showsText(window, "It was DNS.")

        Expect.isTrue commits "the commits never rendered"

        Expect.exists
          api.Requests
          (fun request -> request.Target = "/api/v1/users/octocat")
          "the profile request"

        // the viewer's own profile carries no follow or mute controls
        let! follow = probeUiAsync(fun () -> buttonWithText(window, "Follow"))

        Expect.isNone follow "the follow button leaked"
      })

    testCaseAsync
      "a bio mention chip opens the mapped user page"
      (async {
        let api = bioMentionsApi()

        let profiles =
          FakeProfiles(
            ByUsername =
              Map.ofList [
                "devnull", Some(profileDetailsOf devnullProfileJson)
              ]
          )

        let! window =
          startShellWithProfiles(signedInStore(), api :> IHttp, profiles)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Profile")

        let! mention = showsText(window, "@DevNull")

        Expect.isTrue mention "the mention chip never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "@DevNull") |> Option.iter click)

        let! resolved =
          waitForAsync(
            5000,
            (fun () -> profiles.Asked),
            (fun asked -> asked |> List.contains "devnull")
          )

        Expect.isTrue resolved "the profile resolution never happened"

        let! opened = showsText(window, "Dev Null")

        Expect.isTrue opened "the mapped user page never opened"
      })

    testCaseAsync
      "a bio mention chip opens the mapped organization page"
      (async {
        let api = bioMentionsApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Profile")

        let! mention = showsText(window, "@ACME")

        Expect.isTrue mention "the mention chip never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "@ACME") |> Option.iter click)

        let! resolved =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "GET"
                && request.Target = "/api/v1/organizations/acme"))
          )

        Expect.isTrue resolved "the organization resolution never happened"

        let! opened = showsText(window, "Acme")

        Expect.isTrue opened "the mapped organization page never opened"
      })

    testCaseAsync
      "an unmapped bio mention stays on the profile"
      (async {
        let api = bioMentionsApi()

        let! window, env = startShellWithBrowser(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Profile")

        let! mention = showsText(window, "@stranger-dev")

        Expect.isTrue mention "the unmapped chip never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "@stranger-dev") |> Option.iter click)

        let! opened =
          waitForAsync(
            5000,
            (fun () -> env.OpenedUrls),
            (fun urls -> urls = [ "https://github.com/stranger-dev" ])
          )

        Expect.isTrue opened "the unmapped chip never opened externally"

        let! still = showsText(window, "The Octocat")

        Expect.isTrue still "the unmapped mention navigated away"
      })

    testCaseAsync
      "the profile shows the activity, streaks, and level of the user"
      (async {
        let! window = startShell(signedInStore(), statsProfileApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Profile")

        let! level = showsText(window, "Lv. 3 · Wizard")

        Expect.isTrue level "the level chip never rendered"

        let! streaks =
          showsText(window, "longest streak 12 days · current streak 3 days")

        Expect.isTrue streaks "the streaks never rendered"

        let! xp = showsText(window, "1234 xp · next 2000")

        Expect.isTrue xp "the xp never rendered"

        let! badges = showsText(window, "1/1 badges")

        Expect.isTrue badges "the badge count never rendered"
      })

    testCaseAsync
      "a short window starts the profile compact"
      (async {
        let api = statsProfileApi()
        let! window = startShell(signedInStore(), api :> IHttp)

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

        let! room =
          probeUiAsync(fun () ->
            scrollerAbove(window, "It was DNS.")
            |> Option.map(fun scroller ->
              scroller.Viewport.Height, (window :> TopLevel).ClientSize.Height))

        match room with
        | Some(viewport, clientHeight) ->
          Expect.isGreaterThanOrEqual
            viewport
            (clientHeight * 0.55)
            "the feed never kept most of the window"
        | None -> failtest "the feed scroller never showed"
      })

    testCaseAsync
      "reducing the header never rebuilds the feed"
      (async {
        let! window = startShell(signedInStore(), longProfileApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            window.MinHeight <- 0.0
            window.Height <- 900.0)

        do! settleAsync()

        do! clickWhenShown(window, "Profile")

        let! feed = showsText(window, "It was DNS.")

        Expect.isTrue feed "the feed never rendered"

        do!
          onUiAsync(fun () ->
            scrollerAbove(window, "It was DNS.")
            |> Option.iter(fun scroller ->
              scroller.Offset <- Vector(0.0, 240.0)))

        do! settleAsync()

        // The mid offset already asked the header to give way, so the
        // two chevron clicks unfold and fold again; either way the
        // feed's pieces must stand recorded and unmoved.
        let! recorded =
          probeUiAsync(fun () ->
            scrollerAbove(window, "It was DNS.")
            |> Option.bind(fun scroller ->
              let items =
                scroller.GetVisualDescendants()
                |> Seq.tryFind(fun visual -> visual :? ItemsControl)
                |> Option.map(fun visual -> visual :?> Control)

              let presenter =
                blockWithText(window, "It was DNS.")
                |> Option.bind(fun block ->
                  block.GetVisualAncestors()
                  |> Seq.tryPick(function
                    | :? ContentPresenter as inner -> Some(inner :> Control)
                    | _ -> None))

              match items, presenter with
              | Some items, Some presenter ->
                Some(scroller, items, presenter, scroller.Offset.Y)
              | _ -> None))

        match recorded with
        | None -> failtest "the feed never stood up"
        | Some(scroller, items, presenter, offset) ->
          Expect.isTrue
            (abs(offset - 240.0) <= 0.5)
            $"the feed never took the mid place: {offset}"

          do! clickVisibleHeaderToggle(window)

          do! settleAsync()

          do! clickVisibleHeaderToggle(window)

          do! settleAsync()

          let! stood =
            probeUiAsync(fun () ->
              scrollerAbove(window, "It was DNS.")
              |> Option.map(fun current ->
                obj.ReferenceEquals(current, scroller)
                && (current.GetVisualDescendants()
                    |> Seq.exists(fun visual ->
                      obj.ReferenceEquals(visual, items)))
                && (blockWithText(window, "It was DNS.")
                    |> Option.map(fun block ->
                      block.GetVisualAncestors()
                      |> Seq.exists(fun ancestor ->
                        obj.ReferenceEquals(ancestor, presenter)))
                    |> Option.defaultValue false)
                && abs(current.Offset.Y - 240.0) <= 0.5))

          Expect.equal stood (Some true) "the feed never stood still"
      })

    testCaseAsync
      "scrolling into the feed gives way, top restores"
      (async {
        let! window = startShell(signedInStore(), longProfileApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            window.MinHeight <- 0.0
            window.Height <- 900.0)

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

        Expect.isTrue folded "the scroll into the feed never gave way"

        do! scrollFeedToTopAsync window

        let! restored = seesVisible(window, "activity · week")

        Expect.isTrue restored "the top never restored the full header"
      })

    testCaseAsync
      "the reader's choice sticks"
      (async {
        let! window = startShell(signedInStore(), longProfileApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            window.MinHeight <- 0.0
            window.Height <- 900.0)

        do! settleAsync()

        do! clickWhenShown(window, "Profile")

        let! feed = showsText(window, "It was DNS.")

        Expect.isTrue feed "the feed never rendered"

        do! clickVisibleHeaderToggle(window)

        let! folded =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(visibleText(root, "activity · week")))
          )

        Expect.isTrue folded "the reduce never folded the header"

        do! scrollFeedToEndAsync window
        do! scrollFeedToTopAsync window
        do! settleAsync()

        let! stuck =
          probeUiAsync(fun () -> not(visibleText(window, "activity · week")))

        Expect.isTrue stuck "the reader's fold never stuck"
      })

    testCaseAsync
      "a pinned full header keeps the feed reachable"
      (async {
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

        let! reachable =
          probeUiAsync(fun () ->
            let clientHeight = (window :> TopLevel).ClientSize.Height

            // On screen means inside the client area: the broken
            // header laid the tabs below the window edge with every
            // IsVisible flag still true.
            let onScreen(control: Control) =
              control.TranslatePoint(Point(0.0, 0.0), window)
              |> Option.ofNullable
              |> Option.map(fun corner ->
                corner.Y >= 0.0 && corner.Y < clientHeight)
              |> Option.defaultValue false

            let tabShown =
              buttonWithText(window, "Commits") |> Option.map onScreen

            scrollerAbove(window, "It was DNS.")
            |> Option.map(fun scroller ->
              tabShown,
              scroller.Viewport.Height,
              scrollerAbove(window, "activity · week")
              |> Option.map(fun header ->
                header.Extent.Height > header.Viewport.Height)))

        match reachable with
        | Some(tabShown, viewport, headerScrolls) ->
          Expect.equal
            tabShown
            (Some true)
            "the Commits tab never came back in view"

          Expect.isGreaterThanOrEqual
            viewport
            100.0
            "the feed band never kept 100 DIP"

          Expect.equal
            headerScrolls
            (Some true)
            "the capped header never scrolls"
        | None -> failtest "the feed scroller never showed"
      })

    testCaseAsync
      "the profile shows the pinned commit above an empty feed"
      (async {
        let! window = startShell(signedInStore(), pinApi true :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Profile")

        let! caption = showsText(window, "Pinned")

        Expect.isTrue caption "the pinned caption never rendered"

        let! pinned = showsText(window, "My own commit.")

        Expect.isTrue pinned "the pinned commit never rendered"

        // A profile with nothing but a pinned commit is not an empty
        // one: the slot carries it.
        let! empty =
          probeUiAsync(fun () -> visibleText(window, "No commits yet."))

        Expect.isFalse empty "the pinned-only profile read as empty"
      })

    testCaseAsync
      "pinning a commit from the profile moves it into the slot"
      (async {
        let api = pinApi false
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Profile")

        let! row = showsText(window, "My own commit.")

        Expect.isTrue row "the profile feed never rendered"

        do! clickWhenShown(window, "Pin")

        let! noted = showsText(window, "Commit pinned to your profile.")

        Expect.isTrue noted "the pin's own words never showed"

        let! caption = showsText(window, "Pinned")

        Expect.isTrue caption "the pinned caption never rendered"

        Expect.exists
          api.Requests
          (fun request ->
            request.Method = "PUT"
            && request.Target =
              "/api/v1/commits/018f4c1e-0aa0-7c3a-9b2d-5e6f7a8b9c0d/pin")
          "the pin request"
      })

    testCaseAsync
      "unpinning from the slot returns the commit to the feed"
      (async {
        let! window = startShell(signedInStore(), pinApi true :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Profile")

        let! caption = showsText(window, "Pinned")

        Expect.isTrue caption "the pinned caption never rendered"

        do! clickWhenShown(window, "Unpin")

        let! noted = showsText(window, "Commit unpinned.")

        Expect.isTrue noted "the unpin's own words never showed"

        let! gone =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Pinned")))
          )

        Expect.isTrue gone "the pinned caption never left"

        let! row = showsText(window, "My own commit.")

        Expect.isTrue row "the unpinned commit never returned to the feed"
      })

    testCaseAsync
      "a pin made on a thread shows on the profile"
      (async {
        let api = pinApi false
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "My own commit.")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "My own commit.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        do! clickWhenShown(window, "Pin")

        let! noted = showsText(window, "Commit pinned to your profile.")

        Expect.isTrue noted "the pin never settled on the thread"

        do! clickWhenShown(window, "Profile")

        let! caption = showsText(window, "Pinned")

        Expect.isTrue caption "the profile never showed the pinned slot"

        let! pinned = showsText(window, "My own commit.")

        Expect.isTrue pinned "the profile never showed the pinned commit"
      })

    testCaseAsync
      "the thread lists the related commits under it"
      (async {
        let api = relatedApi secondCommitJson
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! heading = showsText(window, "Related commits")

        Expect.isTrue heading "the related heading never rendered"

        let! related = showsText(window, "Second commit.")

        Expect.isTrue related "the related commit never rendered"

        Expect.exists
          api.Requests
          (fun request ->
            request.Target =
              "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/related")
          "the related request"
      })

    testCaseAsync
      "a wide link preview never pushes the related cards past the column"
      (async {
        let previewCommitJson =
          secondCommitJson
            .Replace("018f4c1e-9999", "018f4c1e-9998")
            .Replace(
              "\"body_segments\":[]",
              "\"body_segments\":[],\"link_preview\":{\"url\":\"https://guides.rubyonrails.org/action_controller_overview.html\",\"title\":\"Rails Guides — Action Controller Overview\",\"description\":null,\"image_url\":null,\"site_name\":\"guides.rubyonrails.org\",\"is_pending\":false,\"embed\":null}"
            )

        let api = relatedApi $"{previewCommitJson},{secondCommitJson}"
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! sited = showsText(window, "guides.rubyonrails.org")

        Expect.isTrue sited "the preview's site line never rendered"

        do! settleAsync()

        // The related rows must share the reading column's right edge
        // with the focused card above: a preview that measured its
        // whole URL once pushed them under the scrollbar.
        let! aligned =
          probeUiAsync(fun () ->
            let rightAt(control: Control) =
              let corner = control.TranslatePoint(Point(0.0, 0.0), window)

              if corner.HasValue then
                Some(corner.Value.X + control.Bounds.Width)
              else
                None

            let rowsOf(root: Control) : Button list =
              root.GetVisualDescendants()
              |> Seq.choose(function
                | :? Button as button when
                  button.Classes.Contains(Theme.name Theme.CommitRow)
                  ->
                  Some button
                | _ -> None)
              |> List.ofSeq

            let section =
              window.GetVisualDescendants()
              |> Seq.tryFind(fun visual -> visual.Name = "RelatedCommits")
              |> Option.map(fun visual -> visual :?> Control)

            match section with
            | Some section ->
              let related = section |> rowsOf |> List.choose rightAt

              let focused =
                window
                |> rowsOf
                |> List.filter(fun row ->
                  row.GetVisualAncestors()
                  |> Seq.forall(fun ancestor -> ancestor <> section))
                |> List.choose rightAt
                |> List.tryHead

              match focused, related with
              | Some edge, edges when not edges.IsEmpty ->
                edges |> List.forall(fun right -> abs(right - edge) < 0.5)
              | _ -> false
            | None -> false)

        Expect.isTrue aligned "the related cards left the reading column"
      })

    testCaseAsync
      "the search pair opens the palette"
      (async {
        let! window =
          startShell(signedInStore(), searchApi(false, false) :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            window.RaiseEvent(
              KeyEventArgs(
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.K,
                KeyModifiers = KeyModifiers.Control
              )
            ))

        let! opened = seesVisible(window, "Search developers or commits")

        Expect.isTrue opened "the search dialog never opened"
      })

    testCaseAsync
      "the search dialog lists people and commits as they land"
      (async {
        let api = searchApi(false, false)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Search")

        let! boxShown =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "SearchInput")),
            Option.isSome
          )

        Expect.isTrue boxShown "the search box never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchInput")
            |> Option.iter(fun control ->
              (control :?> TextBox).Text <- "kai kou"))

        let! person = seesVisible(window, "@kai-kou")

        Expect.isTrue person "the person never listed"

        let! heading = seesVisible(window, "Most relevant commits")

        Expect.isTrue heading "the commits heading never showed"

        let! found = seesVisible(window, "It was DNS.")

        Expect.isTrue found "the commit never listed"

        let! seeAll = seesVisible(window, "See all matching commits")

        Expect.isTrue seeAll "the see-all row never showed"

        Expect.exists
          api.Requests
          (fun request ->
            request.Target = "/api/v1/search/profiles?q=kai%20kou")
          "the profile search"

        Expect.exists
          api.Requests
          (fun request ->
            request.Target = "/api/v1/search/commits?q=kai%20kou&limit=5")
          "the commit search with the dialog's limit"
      })

    testCaseAsync
      "the search dialog lists organizations under their heading"
      (async {
        let api = searchApi(false, false)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Search")

        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchInput")
            |> Option.iter(fun control ->
              (control :?> TextBox).Text <- "kai kou"))

        let! person = seesVisible(window, "@kai-kou")

        Expect.isTrue person "the person never listed"

        let! heading = seesVisible(window, "Organizations")

        Expect.isTrue heading "the organizations heading never showed"

        let! org = seesVisible(window, "@acme")

        Expect.isTrue org "the organization never listed"

        let! bare = seesVisible(window, "@void")

        Expect.isTrue bare "the second organization never listed"

        Expect.exists
          api.Requests
          (fun request ->
            request.Target = "/api/v1/search/profiles?q=kai%20kou")
          "the profile search"
      })

    testCaseAsync
      "the search dialog stays inside a short window"
      (async {
        let api = searchApi(false, false)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            window.MinHeight <- 0.0
            window.Height <- 360.0)

        do! clickWhenShown(window, "Search")

        // A topic term lists commits and profiles together, the tall
        // list a short window must scroll inside the card.
        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchInput")
            |> Option.iter(fun control ->
              (control :?> TextBox).Text <- "kai kou"))

        let! settled = seesVisible(window, "@acme")

        Expect.isTrue settled "the search never settled"

        let! inside =
          probeUiAsync(fun () ->
            let bottomOf(control: Control) =
              control.TranslatePoint(Point(0.0, control.Bounds.Height), window)
              |> Option.ofNullable
              |> Option.map(fun point -> point.Y)

            let footer =
              window.GetVisualDescendants()
              |> Seq.tryPick(fun visual ->
                match visual with
                | :? TextBlock as block when block.Text = "to move" ->
                  Some(block :> Control)
                | _ -> None)

            match
              controlByName(window, "SearchDialogCard"),
              controlByName(window, "SearchDialogScroller"),
              footer
            with
            | Some card, Some scroller, Some footer ->
              match bottomOf card, bottomOf scroller, bottomOf footer with
              | Some cardBottom, Some scrollerBottom, Some footerBottom ->
                let head = card.TranslatePoint(Point(0.0, 0.0), window)

                match head |> Option.ofNullable with
                | Some top ->
                  top.Y >= 0.0
                  && cardBottom <= (window :> TopLevel).ClientSize.Height + 0.5
                  && scrollerBottom <= cardBottom + 0.5
                  && footerBottom <= cardBottom + 0.5
                | None -> false
              | _ -> false
            | _ -> false)

        Expect.isTrue inside "the dialog content ran past the short window"
      })

    testCaseAsync
      "the keys walk into the organizations and open one"
      (async {
        let api = searchApi(false, false)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Search")

        // The at keeps the developer reading, so the person stands
        // first and the organizations follow.
        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchInput")
            |> Option.iter(fun control -> (control :?> TextBox).Text <- "@kai"))

        let! settled = seesVisible(window, "@kai-kou")

        Expect.isTrue settled "the search never settled"

        let! org = seesVisible(window, "@acme")

        Expect.isTrue org "the organization never listed"

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

        // Enter opens the armed organization.
        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchInput")
            |> Option.iter(fun control ->
              (control :?> TextBox)
                .RaiseEvent(
                  KeyEventArgs(
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Enter
                  )
                )))

        let! opened = seesVisible(window, "Ships everything, eventually.")

        Expect.isTrue opened "the organization page never opened"

        Expect.exists
          api.Requests
          (fun request -> request.Target = "/api/v1/organizations/acme")
          "the organization read"
      })

    testCaseAsync
      "opening a person from the dialog lands on the profile"
      (async {
        let api = searchApi(false, false)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Search")

        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchInput")
            |> Option.iter(fun control ->
              (control :?> TextBox).Text <- "kai kou"))

        let! settled = seesVisible(window, "@kai-kou")

        Expect.isTrue settled "the search never settled"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "kai kou") |> Option.iter click)

        let! gone = seesVisible(window, "Search developers or commits")

        Expect.isFalse gone "the dialog never closed"

        Expect.exists
          api.Requests
          (fun request -> request.Target = "/api/v1/users/kai-kou")
          "the profile read"
      })

    testCaseAsync
      "a commit term under the server's minimum never searches commits"
      (async {
        let api = searchApi(false, false)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Search")

        let! boxShown =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "SearchInput")),
            Option.isSome
          )

        Expect.isTrue boxShown "the search box never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchInput")
            |> Option.iter(fun control -> (control :?> TextBox).Text <- "ab"))

        // The people channel still answers a short term; wait for its
        // request, then give a wrongly-gated commit call its window.
        let! asked =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Target = "/api/v1/search/profiles?q=ab"))
          )

        Expect.isTrue asked "the profile search never ran"

        do! Async.Sleep 700

        Expect.isFalse
          (api.Requests
           |> List.exists(fun request ->
             request.Target.StartsWith "/api/v1/search/commits"))
          "a short term searched commits"
      })

    testCaseAsync
      "the slow-down answer shows the site's own line"
      (async {
        let! window =
          startShell(signedInStore(), searchApi(true, false) :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Search")

        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchInput")
            |> Option.iter(fun control ->
              (control :?> TextBox).Text <- "rails"))

        let! slowed =
          seesVisible(window, "error: too many searches, give it a few seconds")

        Expect.isTrue slowed "the slow-down line never showed"
      })

    testCaseAsync
      "a search that finds nothing says so"
      (async {
        let! window =
          startShell(signedInStore(), searchApi(false, true) :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Search")

        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchInput")
            |> Option.iter(fun control -> (control :?> TextBox).Text <- "zzz"))

        let! nothing = seesVisible(window, "fatal: nothing matches 'zzz'")

        Expect.isTrue nothing "the fatal line never showed"
      })

    testCaseAsync
      "a settled search becomes a recent"
      (async {
        let recents = RecentSearchStore.createWith(Map.empty, ignore)

        let! window =
          startShellWithRecents(
            signedInStore(),
            searchApi(false, false) :> IHttp,
            recents
          )

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Search")

        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchInput")
            |> Option.iter(fun control ->
              (control :?> TextBox).Text <- "kai kou"))

        let! settled = seesVisible(window, "@kai-kou")

        Expect.isTrue settled "the search never settled"

        // The commit answer is the settle's other half; only then does
        // the term become a recent.
        let! answered = seesVisible(window, "It was DNS.")

        Expect.isTrue answered "the commit answer never settled"

        let! stored =
          waitForAsync(
            5000,
            (fun () -> recents),
            (fun store ->
              not(store.Recent("018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d").IsEmpty))
          )

        Expect.isTrue stored "the term never became a recent"

        // The escape closes the palette; a reopen lists the term.
        do!
          onUiAsync(fun () ->
            window.RaiseEvent(
              KeyEventArgs(
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape
              )
            ))

        let! closed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(visibleText(root, "Search developers or commits")))
          )

        Expect.isTrue closed "the palette never closed"

        do! clickWhenShown(window, "Search")

        let! recent = seesVisible(window, "Recent searches")

        Expect.isTrue recent "the recents heading never showed"

        let! row = seesVisible(window, "kai kou")

        Expect.isTrue row "the remembered term never listed"
      })

    testCaseAsync
      "see all lands on the results page with the term"
      (async {
        let api = searchApi(false, false)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Search")

        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchInput")
            |> Option.iter(fun control ->
              (control :?> TextBox).Text <- "shippin"))

        let! settled = seesVisible(window, "@kai-kou")

        Expect.isTrue settled "the search never settled"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "See all matching commits")
            |> Option.iter click)

        let! counted = seesVisible(window, "1 match")

        Expect.isTrue counted "the results page never counted its match"

        let! boxHeld =
          waitForAsync(
            5000,
            (fun () ->
              controlByName(window, "SearchBox")
              |> Option.map(fun control -> control :?> TextBox)),
            (fun box -> box |> Option.map(_.Text) = Some "shippin")
          )

        Expect.isTrue boxHeld "the page never held the term"

        Expect.exists
          api.Requests
          (fun request -> request.Target = "/api/v1/search/commits?q=shippin")
          "the page asked for a page of results"
      })

    testCaseAsync
      "the results page lists developers and organizations above the commits"
      (async {
        let api = searchApi(false, false)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Search")

        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchInput")
            |> Option.iter(fun control ->
              (control :?> TextBox).Text <- "kai kou"))

        let! settled = seesVisible(window, "@kai-kou")

        Expect.isTrue settled "the search never settled"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "See all matching commits")
            |> Option.iter click)

        let! counted = seesVisible(window, "1 match")

        Expect.isTrue counted "the results page never counted its match"

        let! developers = seesVisible(window, "Developers")

        Expect.isTrue developers "the developers heading never showed"

        let! organizations = seesVisible(window, "Organizations")

        Expect.isTrue organizations "the organizations heading never showed"

        // The profile sections stand above the commit results.
        let! ordered =
          probeUiAsync(fun () ->
            match
              buttonWithText(window, "Acme"),
              buttonWithText(window, "It was DNS.")
            with
            | Some org, Some commit ->
              let orgAt =
                org.TranslatePoint(
                  Point(org.Bounds.Width / 2.0, org.Bounds.Height / 2.0),
                  window
                )

              let commitAt =
                commit.TranslatePoint(
                  Point(commit.Bounds.Width / 2.0, commit.Bounds.Height / 2.0),
                  window
                )

              orgAt.HasValue
              && commitAt.HasValue
              && orgAt.Value.Y < commitAt.Value.Y
            | _ -> false)

        Expect.isTrue ordered "the profile rows never stood above the commits"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Acme") |> Option.iter click)

        let! opened = seesVisible(window, "Ships everything, eventually.")

        Expect.isTrue opened "the organization page never opened"

        Expect.exists
          api.Requests
          (fun request -> request.Target = "/api/v1/organizations/acme")
          "the organization read"
      })

    testCaseAsync
      "neither search surface calls the deprecated people endpoint"
      (async {
        let api = searchApi(false, false)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Search")

        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchInput")
            |> Option.iter(fun control ->
              (control :?> TextBox).Text <- "kai kou"))

        let! settled = seesVisible(window, "@kai-kou")

        Expect.isTrue settled "the search never settled"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "See all matching commits")
            |> Option.iter click)

        let! counted = seesVisible(window, "1 match")

        Expect.isTrue counted "the results page never counted its match"

        Expect.isFalse
          (api.Requests
           |> List.exists(fun request ->
             request.Target.Contains "/api/v1/search/users"))
          "the deprecated people endpoint answered a search"

        Expect.exists
          api.Requests
          (fun request -> request.Target.StartsWith "/api/v1/search/profiles")
          "the profile search ran"
      })

    testCaseAsync
      "a thread with nothing related shows no section"
      (async {
        let! window = startShell(signedInStore(), relatedApi "" :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        do! settleAsync()

        let! section =
          probeUiAsync(fun () -> visibleText(window, "Related commits"))

        Expect.isFalse section "an empty related list still drew its section"
      })

    testCaseAsync
      "the replies tab requests the replies feed"
      (async {
        let api = accountApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Profile")

        do! clickWhenShown(window, "Replies")

        let! replied =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Target.StartsWith "/api/v1/users/octocat/commits"
                && request.Target.Contains "replies=1"))
          )

        Expect.isTrue replied "the replies feed was never requested"
      })

    testCaseAsync
      "the saved list renders the starred commits"
      (async {
        let api = accountApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Saved")

        let! saved = showsText(window, "It was DNS.")

        Expect.isTrue saved "the saved commit never rendered"

        Expect.exists
          api.Requests
          (fun request -> request.Target.StartsWith "/api/v1/starred")
          "the starred request"
      })

    testCaseAsync
      "the saved page's quote opens the quote experience"
      (async {
        let api = accountApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Saved")

        let! saved = showsText(window, "It was DNS.")

        Expect.isTrue saved "the saved commit never rendered"

        let! quoteable =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "QuoteButton")),
            (fun found ->
              found
              |> Option.map(fun control -> control :? Button)
              |> Option.defaultValue false)
          )

        Expect.isTrue quoteable "the quote action never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "QuoteButton")
            |> Option.iter(fun control ->
              match control with
              | :? Button as button -> click button
              | _ -> ()))

        let! armed = showsText(window, "Add your take")

        Expect.isTrue armed "the quote composer never opened"

        let! quoted = showsText(window, "It was DNS.")

        Expect.isTrue quoted "the quoted commit never showed under the composer"
      })

    testCaseAsync
      "signing out lands on sign-in and drops the session"
      (async {
        let store = signedInStore()
        let! window = startShell(store, accountApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Settings")

        let! settings = showsText(window, "@octocat")

        Expect.isTrue settings "the settings never rendered"

        do! clickWhenShown(window, "Sign out")

        let! signIn = showsText(window, "Sign in to /dev/null")

        Expect.isTrue signIn "the sign-in page never came back"

        let! state = probeUiAsync(fun () -> store.Current)

        Expect.equal state SignedOut "the store kept the session"
      })

    testCaseAsync
      "the notifications entry carries the unread badge"
      (async {
        let! window = startShell(signedInStoreWithUnread 9, logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        let! badge = showsText(window, "9")

        Expect.isTrue badge "the unread badge never rendered"
      })

    testCaseAsync
      "the sidebar folds into a drawer on narrow windows"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        let! wideSidebar =
          probeUiAsync(fun () ->
            buttonWithText(window, "Settings")
            |> Option.map effectiveVisible
            |> Option.defaultValue false)

        Expect.isTrue
          wideSidebar
          "the sidebar never rendered on the wide window"

        let! wideHamburger =
          probeUiAsync(fun () ->
            controlByName(window, "MenuButton")
            |> Option.map effectiveVisible
            |> Option.defaultValue false)

        Expect.isFalse wideHamburger "the hamburger showed on the wide window"

        do!
          onUiAsync(fun () ->
            window.Width <- 400.0
            window.Height <- 700.0)

        let! hamburger =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "MenuButton")),
            (fun control ->
              control
              |> Option.bind(fun c ->
                if c :? Button then Some(c :?> Button) else None)
              |> visibleOption)
          )

        Expect.isTrue
          hamburger
          "the hamburger never showed on the narrow window"

        let! sidebarHidden =
          probeUiAsync(fun () ->
            buttonWithText(window, "Settings") |> visibleOption)

        Expect.isFalse
          sidebarHidden
          "the sidebar stayed open on the narrow window"

        do!
          onUiAsync(fun () ->
            controlByName(window, "MenuButton")
            |> Option.iter(fun control -> click(control :?> Button)))

        let! drawer =
          waitForAsync(
            5000,
            (fun () -> buttonWithText(window, "Settings")),
            visibleOption
          )

        Expect.isTrue drawer "the drawer never opened"

        do! clickWhenShown(window, "Settings")

        let! closed =
          waitForAsync(
            5000,
            (fun () -> buttonWithText(window, "Settings")),
            (fun button ->
              not(
                button
                |> Option.map effectiveVisible
                |> Option.defaultValue true
              ))
          )

        Expect.isTrue closed "the drawer stayed open after navigation"

        let! settings = showsText(window, "@octocat")

        Expect.isTrue settings "the settings page never rendered"
      })

    testCaseAsync
      "the sidebar collapses to the rail and expands again"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        let! collapse =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "NavCollapse")),
            Option.isSome
          )

        Expect.isTrue collapse "the collapse row never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "NavCollapse")
            |> Option.iter(fun control -> click(control :?> Button)))

        let! rail =
          waitForAsync(
            5000,
            (fun () -> splitWidth(window, "SidebarSplit")),
            (fun width -> width = Some 80.0)
          )

        Expect.isTrue rail "the pane never took the rail width"

        let! mark =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> visibleText(root, "/dev"))
          )

        Expect.isTrue mark "the rail mark never rendered"

        let! labelsGone =
          probeUiAsync(fun () ->
            [ "Log"; "Notifications"; "Profile"; "Saved"; "Settings"; "About" ]
            |> List.forall(fun label -> not(visibleText(window, label))))

        Expect.isTrue labelsGone "a navigation label stayed in the rail"

        let! wordmarkGone =
          probeUiAsync(fun () -> not(visibleText(window, "/dev/null")))

        Expect.isTrue wordmarkGone "the wordmark stayed in the rail"

        do!
          onUiAsync(fun () ->
            controlByName(window, "NavCollapse")
            |> Option.iter(fun control -> click(control :?> Button)))

        let! expanded =
          waitForAsync(
            5000,
            (fun () -> splitWidth(window, "SidebarSplit")),
            (fun width -> width = Some 256.0)
          )

        Expect.isTrue expanded "the pane never expanded"

        let! wordmark =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> visibleText(root, "/dev/null"))
          )

        Expect.isTrue wordmark "the wordmark never came back"
      })

    testCaseAsync
      "the rail band hides the labels and the toggle"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! onUiAsync(fun () -> window.Width <- 900.0)
        do! settleAsync()

        let! rail =
          waitForAsync(
            5000,
            (fun () -> splitWidth(window, "SidebarSplit")),
            (fun width -> width = Some 80.0)
          )

        Expect.isTrue rail "the rail band never narrowed the pane"

        let! toggleGone =
          probeUiAsync(fun () ->
            controlByName(window, "NavCollapse")
            |> Option.forall(fun control -> not(effectiveVisible control)))

        Expect.isTrue toggleGone "the toggle showed below the label breakpoint"

        let! mark =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> visibleText(root, "/dev"))
          )

        Expect.isTrue mark "the rail mark never rendered"

        let! labelsGone =
          probeUiAsync(fun () ->
            [ "Log"; "Notifications"; "Profile"; "Saved"; "Settings"; "About" ]
            |> List.forall(fun label -> not(visibleText(window, label))))

        Expect.isTrue labelsGone "a navigation label stayed in the rail"

        let! accountWidth =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "SidebarAccount")),
            (fun control ->
              control |> Option.exists(fun value -> value.Bounds.Width >= 79.0))
          )

        Expect.isTrue
          accountWidth
          "the account row stayed narrower than the rail"

        do! onUiAsync(fun () -> window.Width <- 700.0)
        do! settleAsync()

        let! hamburger =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "MenuButton")),
            (fun control ->
              control
              |> Option.exists(fun value ->
                match value with
                | :? Button as button -> effectiveVisible button
                | _ -> false))
          )

        Expect.isTrue hamburger "the hamburger never showed at 700"

        let! paneHidden =
          probeUiAsync(fun () ->
            controlByName(window, "SidebarPane")
            |> Option.forall(fun control -> not(effectiveVisible control)))

        Expect.isTrue paneHidden "the docked pane stayed at 700"
      })

    testCaseAsync
      "the copy-link action puts the commit URL on the clipboard"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do! openOwnMenu window

        let! link =
          waitForAsync(
            5000,
            (fun () -> menuRowOf(window, "Copy link")),
            Option.isSome
          )

        Expect.isTrue link "the link action never appeared"

        do!
          onUiAsync(fun () ->
            menuRowOf(window, "Copy link") |> Option.iter click)

        let! url =
          probeUiAsync(fun () ->
            match TopLevel.GetTopLevel window with
            | null -> ""
            | top ->
              match top.Clipboard with
              | null -> ""
              | clipboard ->
                match clipboard.TryGetDataAsync().GetAwaiter().GetResult() with
                | null -> ""
                | data ->
                  match data.TryGetTextAsync().GetAwaiter().GetResult() with
                  | null -> ""
                  | text -> text)

        Expect.equal
          url
          "https://dev-null.sh/@octocat/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
          "the clipboard carried another text"
      })

    testCaseAsync
      "the shell keeps the drawn chrome to the border only"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        let! decorations = probeUiAsync(fun () -> window.WindowDecorations)

        Expect.equal
          decorations
          WindowDecorations.BorderOnly
          "the drawn chrome painted its caption over the client again"
      })

    testCaseAsync
      "the caption buttons drive the window state"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        let byName(name: string) =
          controlByName(window, name)
          |> Option.map(fun control -> control :?> Button)

        let! hasMinimize =
          probeUiAsync(fun () -> byName "MinimizeButton" |> Option.isSome)

        let! hasMaximize =
          probeUiAsync(fun () -> byName "MaximizeButton" |> Option.isSome)

        let! hasClose =
          probeUiAsync(fun () -> byName "CloseButton" |> Option.isSome)

        Expect.isTrue hasMinimize "no minimize button"
        Expect.isTrue hasMaximize "no maximize button"
        Expect.isTrue hasClose "no close button"

        do! onUiAsync(fun () -> byName "MinimizeButton" |> Option.iter click)

        let! minimized = probeUiAsync(fun () -> window.WindowState)

        Expect.equal
          minimized
          WindowState.Minimized
          "the minimize button never ran"

        do!
          onUiAsync(fun () ->
            window.WindowState <- WindowState.Normal
            byName "MaximizeButton" |> Option.iter click)

        let! maximized = probeUiAsync(fun () -> window.WindowState)

        Expect.equal
          maximized
          WindowState.Maximized
          "the maximize button never ran"

        do! onUiAsync(fun () -> byName "MaximizeButton" |> Option.iter click)

        let! restored = probeUiAsync(fun () -> window.WindowState)

        Expect.equal restored WindowState.Normal "the restore never ran"
      })

    testCaseAsync
      "fullscreen hides the caption row and toggling brings it back"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! onUiAsync(fun () -> (window :?> Shell<TestEnv>).ToggleFullScreen())

        let! fullscreen = probeUiAsync(fun () -> window.WindowState)

        Expect.equal
          fullscreen
          WindowState.FullScreen
          "the window never went fullscreen"

        let! captionHidden =
          probeUiAsync(fun () ->
            controlByName(window, "CaptionRow")
            |> Option.map effectiveVisible
            |> Option.defaultValue false)

        Expect.isFalse
          captionHidden
          "the caption row stayed visible in fullscreen"

        do! onUiAsync(fun () -> (window :?> Shell<TestEnv>).ToggleFullScreen())

        let! normal = probeUiAsync(fun () -> window.WindowState)

        Expect.equal
          normal
          WindowState.Normal
          "the window never left fullscreen"

        let! restored =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "CaptionRow")),
            (fun control ->
              control
              |> Option.map effectiveVisible
              |> Option.defaultValue false)
          )

        Expect.isTrue restored "the caption row never came back"
      })

    testCaseAsync
      "closing with close-to-tray hides the window and Quit closes it"
      (async {
        let settings = AppSettingsStore.create(None, ignore)
        settings.SetCloseToTray true

        let env = TestEnv(signedInStore(), logApi() :> IHttp, settings)
        let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
        let! _ = showAsync window

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        let closes = ResizeArray<string>()

        do! onUiAsync(fun () -> window.Closed.Add(fun _ -> closes.Add "closed"))

        do! onUiAsync(fun () -> window.Close())

        let! visible = probeUiAsync(fun () -> window.IsVisible)

        Expect.isFalse visible "the window stayed visible after the close"

        let! closedCount = probeUiAsync(fun () -> closes.Count)

        Expect.equal closedCount 0 "the close was never cancelled"

        do! onUiAsync(fun () -> (window :?> Shell<TestEnv>).Quit())

        let! quit =
          waitForAsync(5000, (fun () -> closes.Count), (fun count -> count > 0))

        Expect.isTrue quit "the quit never closed the window"
      })

    testCaseAsync
      "closing without the flag closes the window"
      (async {
        let env = TestEnv(signedInStore(), logApi() :> IHttp)
        let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
        let! _ = showAsync window

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        let closes = ResizeArray<string>()

        do! onUiAsync(fun () -> window.Closed.Add(fun _ -> closes.Add "closed"))

        do! onUiAsync(fun () -> window.Close())

        let! closed =
          waitForAsync(5000, (fun () -> closes.Count), (fun count -> count > 0))

        Expect.isTrue closed "the window never closed"
      })

    testCaseAsync
      "the settings toggle persists close-to-tray"
      (async {
        let saves = ResizeArray<AppSettings>()

        let settings =
          AppSettingsStore.create(None, fun value -> saves.Add value)

        let env = TestEnv(signedInStore(), logApi() :> IHttp, settings)
        let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
        let! _ = showAsync window

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Settings")

        let! section =
          showsText(window, "Keep running in the tray when the window closes")

        Expect.isTrue section "the application section never rendered"

        do!
          onUiAsync(fun () ->
            controlByName(window, "CloseToTrayToggle")
            |> Option.iter(fun control ->
              (control :?> ToggleSwitch).IsChecked <- Nullable true))

        // The write rides the program loop: dispatch, update, command,
        // store — each a dispatcher turn, so the assertions wait for
        // the outcome instead of probing synchronously.
        let! stored =
          waitForAsync(5000, (fun () -> settings.Current.CloseToTray), id)

        Expect.isTrue stored "the store never took the flag"

        let! saved =
          waitForAsync(5000, (fun () -> saves.Count), (fun count -> count = 1))

        Expect.isTrue saved "the save hook never ran"
      })

    testCaseAsync
      "the theme carries distinct dark and light tokens"
      (async {
        let! dark =
          probeUiAsync(fun () -> solid(ThemeVariant.Dark, Keys.background))

        let! light =
          probeUiAsync(fun () -> solid(ThemeVariant.Light, Keys.background))

        Expect.notEqual dark light "the variants share the background token"
      })

    testCaseAsync
      "the theme choice parses the stored names"
      (async {
        Expect.equal (parseTheme "dark") (Some Dark) "dark never parsed"
        Expect.equal (parseTheme "light") (Some Light) "light never parsed"
        Expect.equal (parseTheme " system ") (Some System) "system never parsed"
        Expect.isNone (parseTheme "solarized") "an unknown name parsed"
      })

    testCaseAsync
      "the settings store publishes theme changes"
      (async {
        let saves = ResizeArray<AppSettings>()
        let store = AppSettingsStore.create(None, fun value -> saves.Add value)

        store.SetTheme Light

        Expect.equal store.Current.Theme Light "the store never took the choice"

        let! saved = probeUiAsync(fun () -> saves.Count)

        Expect.equal saved 1 "the save hook never ran"

        Expect.equal saves[0].Theme Light "the save hook ran with the default"

        store.SetTheme System

        Expect.equal store.Current.Theme System "the store never came back"
      })

    testCaseAsync
      "the settings store publishes the sidebar choice"
      (async {
        let saves = ResizeArray<AppSettings>()
        let store = AppSettingsStore.create(None, fun value -> saves.Add value)

        store.SetSidebarExpanded false

        Expect.isFalse
          store.Current.SidebarExpanded
          "the store never took the sidebar choice"

        let! saved = probeUiAsync(fun () -> saves.Count)

        Expect.equal saved 1 "the save hook never ran"

        Expect.isFalse
          saves[0].SidebarExpanded
          "the save hook kept the old sidebar choice"
      })

    testCaseAsync
      "the active navigation wears the primary wash"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        let! active =
          probeUiAsync(fun () ->
            buttonWithText(window, "Log")
            |> Option.exists(fun button ->
              button.Classes.Contains(name NavItemActive)))

        Expect.isTrue active "the log entry never went active"
      })

    testCaseAsync
      "the sidebar names the service in its one true form"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        // The brand asks for the plain name: lowercase, both slashes,
        // one run of text. The sidebar adds the unofficial line, so no
        // endorsement is implied.
        let! named = showsText(window, "/dev/null")

        Expect.isTrue named "the name never rendered"

        let! unofficial = showsText(window, "unofficial desktop client")

        Expect.isTrue unofficial "the unofficial line never rendered"
      })

    testCaseAsync
      "the about page states the project and its disclaimer"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "About")

        let! disclaimer =
          showsText(
            window,
            "This project is not affiliated with or endorsed by /dev/null. "
            + "The name appears here only to name the service. "
            + "Every mark belongs to its owner."
          )

        Expect.isTrue disclaimer "the disclaimer never rendered"

        let! stack = showsText(window, "Avalonia")

        Expect.isTrue stack "the stack never rendered"

        // The wrapped text must fit its box: the block never runs past
        // the border that carries it.
        let! fits =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(function
              | :? TextBlock as block ->
                match block.Text with
                | null -> None
                | text when text.StartsWith "This project is not affiliated" ->
                  Some block
                | _ -> None
              | _ -> None)
            |> Option.bind(fun block ->
              match block.Parent with
              | :? Border as box ->
                Some(
                  block.Bounds.Width,
                  box.Bounds.Width
                  - box.Padding.Left
                  - box.Padding.Right
                  - 2.0
                )
              | _ -> None))

        Expect.isSome fits "the disclaimer box never rendered"

        let textWidth, boxWidth = fits.Value

        Expect.isLessThanOrEqual
          textWidth
          (boxWidth + 0.5)
          "the disclaimer runs past its box"
      })

    testCaseAsync
      "a returning log page refetches and restores its place"
      (async {
        let api = threadApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        let logReads() =
          api.Requests
          |> List.filter(fun request ->
            request.Method = "GET" && request.Target.StartsWith "/api/v1/log/")
          |> List.length

        let! before = probeUiAsync logReads

        do! clickWhenShown(window, "It was DNS.")

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        do! clickWhenShown(window, "Back to the log")

        let! back = showsText(window, "It was DNS.")

        Expect.isTrue back "the log never came back"

        // The page that returns is fresh: it asks the server for its
        // page again, and the timeline service hands back its place.
        let! after = waitForAsync(2000, logReads, (fun count -> count > before))

        Expect.isTrue after "the returning page never refetched"
      })

    testCaseAsync
      "the inbox title follows a live session refresh"
      (async {
        let store = signedInStoreWithUnread 3
        let! window = startShell(store, accountApi() :> IHttp)

        do! clickWhenShown(window, "Notifications")

        let! inbox = showsText(window, "Notifications · 3 unread")

        Expect.isTrue inbox "the initial unread title never rendered"

        let refreshed = {
          sessionOf "octocat" with
              UnreadNotificationsCount = 7
        }

        do! probeUiAsync(fun () -> store.Refresh refreshed) |> Async.Ignore

        let! title = showsText(window, "Notifications · 7 unread")

        Expect.isTrue title "the title never followed the refresh"
      })

    testCaseAsync
      "a row's relative time follows the shared clock"
      (async {
        // Created 50 seconds ago: the row lands as "now", then flips
        // to "1m" once the shared clock's ticks carry the model's Now
        // past the minute boundary.
        let created = DateTimeOffset.UtcNow - TimeSpan.FromSeconds 50.0

        let stamp = created.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")

        let freshNotification =
          notificationJson.Replace(
            "\"created_at\":\"2026-10-20T04:17:19Z\"",
            $"\"created_at\":\"{stamp}\""
          )

        let inbox =
          sprintf
            "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"filters\":{\"type\":\"all\"},\"read_at\":\"2026-10-20T04:20:00Z\"}"
            freshNotification

        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/notifications" then
              ok inbox
            elif request.Target.StartsWith "/api/v1/log/" then
              ok logPageJson
            else
              ok "{}")

        let! window = startShell(signedInStore(), api :> IHttp)

        do! clickWhenShown(window, "Notifications")

        let! fresh = showsText(window, "now")

        Expect.isTrue fresh "the fresh row never rendered"

        // The flip can take up to a minute-budget of real time; the
        // default showsText window is too short for it.
        let! flipped =
          waitForAsync(
            25000,
            (fun () -> window),
            (fun root -> hasText(root, "1m"))
          )

        Expect.isTrue flipped "the relative time never followed the clock"
      })

    testCaseAsync
      "the pastel controls appear when the session turns them on"
      (async {
        let store = signedInStoreWithoutPasteis()
        let! window = startShell(store, threadApi() :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        let! hidden =
          probeUiAsync(fun () -> controlByName(window, "PastelButton"))

        Expect.isNone hidden "the pastel action leaked while off"

        let refreshed = {
          sessionOf "octocat" with
              PasteisMode = true
        }

        do! probeUiAsync(fun () -> store.Refresh refreshed) |> Async.Ignore

        // The session subscription flips the model's flag; the card's
        // host rebuilds, so the named control enters the tree live.
        let! appeared =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "PastelButton")),
            Option.isSome
          )

        Expect.isTrue appeared "the pastel action never appeared"
      })

    testCaseAsync
      "the aside shows the profile card on wide windows"
      (async {
        let! window = startShell(signedInStore(), accountApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            window.Width <- 1440.0
            window.Height <- 900.0)

        let! bio = showsText(window, "Builds things.")

        Expect.isTrue bio "the aside bio never rendered"

        let! followers = showsText(window, "followers")

        Expect.isTrue followers "the aside stats never rendered"
      })

    testCaseAsync
      "a since check stages the new count, and the pill replaces the feed"
      (async {
        let session = signedInSession()

        let env = TestEnv(signedInStore(), twoDaysLogApi() :> IHttp)

        let props: LogView.Props<TestEnv> = {
          Env = env
          Log = Latest
          Session = session

          // The pill messages never navigate, so the router stays out.
          Nav = Unchecked.defaultof<Navs.INavigable>
          Context = Unchecked.defaultof<Navs.RouteContext>
        }

        let newer =
          match LogPage.decode newerPageJson with
          | Ok page -> page
          | Error failure ->
            failwith $"the newer page never decoded: {failure.Message}"

        // The since read answers the same first page; only the envelope
        // count is news, so the items never stage behind the pill.
        let model = {
          LogView.State.initial with
              Head = Some "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
              Items =
                match newer.Page.Items with
                | [ node ] -> [ node ]
                | nodes -> nodes
        }

        let staged, _ =
          LogView.State.update props (LogView.State.NewChecked(Ok newer)) model

        Expect.equal staged.NewCount (Some 1) "the new count never staged"

        Expect.equal staged.Items model.Items "the since read touched the feed"

        // The pill fetches the fresh first page and replaces the feed:
        // every id lands once, so the keyed diff can never see a
        // repeated key.
        let refreshed, _ =
          LogView.State.update props LogView.State.NewRequested staged

        Expect.isNone refreshed.NewCount "the pill never cleared"

        Expect.equal refreshed.Items model.Items "the refresh was never staged"

        Expect.equal
          (refreshed.Items
           |> List.map(fun node ->
             match node with
             | LiveCommit commit ->
               LogView.State.keyOf(LogView.LogRow.Node commit)
             | Tombstone tombstone ->
               LogView.State.keyOf(LogView.LogRow.TombstoneRow tombstone))
           |> List.distinct
           |> List.length)
          (refreshed.Items |> List.length)
          "the refreshed feed carries a repeated key"

        // The landed page returns its reader to the top: the command
        // the fresh page carries asks the timeline to scroll, the way
        // the keyed feed's own diff never will.
        let timeline =
          (env :> DevNull.Client.Timeline.IProvideTimeline).Timeline

        let scrolled = ResizeArray<Timeline.Request>()
        let listen = timeline.Requests.Subscribe scrolled.Add

        let _, landed =
          LogView.State.update
            props
            (LogView.State.NewFetched(Ok newer))
            refreshed

        landed ignore ignore
        listen.Dispose()

        Expect.contains
          scrolled
          Timeline.ScrollToTop
          "the fresh first page never scrolled the feed to its top"
      })

    testCaseAsync
      "a late older commit lands in place, and heard ones ride the next poll"
      (async {
        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/log/" then
              ok latePageJson
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let env = TestEnv(signedInStore(), api :> IHttp)

        let props: LogView.Props<TestEnv> = {
          Env = env
          Log = Latest
          Session = signedInSession()
          Nav = Unchecked.defaultof<Navs.INavigable>
          Context = Unchecked.defaultof<Navs.RouteContext>
        }

        let headPage =
          sprintf
            "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
            (datedCommitJson(
              "0a0a0a0a-0000-0000-0000-000000000001",
              "aaa1111",
              "First landing.",
              "2020-04-03T12:00:00Z"
            ))

        let loaded =
          match LogPage.decode headPage with
          | Ok page -> page
          | Error failure ->
            failwith $"the loaded page never decoded: {failure.Message}"

        let model = {
          LogView.State.initial with
              Head = Some "0a0a0a0a-0000-0000-0000-000000000001"
              Items = loaded.Page.Items
        }

        let late =
          match LogPage.decode latePageJson with
          | Ok page -> page
          | Error failure ->
            failwith $"the poll page never decoded: {failure.Message}"

        // The late commit is not newer than the head, so the burst
        // lands in the feed in one update: no pill, no reload.
        let staged, _ =
          LogView.State.update props (LogView.State.NewChecked(Ok late)) model

        Expect.isNone staged.NewCount "the late commit staged the pill"

        Expect.equal 2 staged.Items.Length "the late commit never landed"

        Expect.equal [] staged.HeardIds "the late commit never became heard"

        let rows = LogView.State.toRows(staged.Items, false, None)

        Expect.contains
          (rows |> List.map LogView.State.keyOf)
          "0a0a0a0a-0000-0000-0000-000000000000"
          "the late commit never showed in place"

        // A newer-than-head commit stages the pill and is heard; the
        // next poll reports it as arrived.
        let newer =
          match LogPage.decode newerPageJson with
          | Ok page -> page
          | Error failure ->
            failwith $"the newer page never decoded: {failure.Message}"

        let heard, _ =
          LogView.State.update props (LogView.State.NewChecked(Ok newer)) model

        Expect.equal
          (Some 1)
          heard.NewCount
          "the newer commit never staged the pill"

        Expect.equal
          [ "0a0a0a0a-0000-0000-0000-000000000003" ]
          heard.HeardIds
          "the newer commit never became heard"

        Expect.equal model.Items heard.Items "the newer commit touched the feed"

        let _, polled = LogView.State.update props LogView.State.CheckNew heard

        polled ignore ignore

        let! sent =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            fun requests ->
              requests
              |> List.exists(fun request ->
                request.Target.Contains "arrived[]=")
          )

        Expect.isTrue sent "the next poll never sent the heard ids"

        let poll =
          api.Requests
          |> List.find(fun request -> request.Target.Contains "arrived[]=")

        Expect.isTrue
          (poll.Target.Contains "arrived[]=0a0a0a0a-0000-0000-0000-000000000003")
          "the heard id never rode the poll"

        Expect.isTrue
          (poll.Target.Contains "pending[]=")
          "the loaded ids never rode the poll"

        Expect.isTrue
          (poll.Headers |> List.exists(fun (name, _) -> name = "X-Log-Through"))
          "the oldest range never rode the poll"
      })

    testCaseAsync
      "a burst of two late commits lands together"
      (async {
        let env = TestEnv(signedInStore(), twoDaysLogApi() :> IHttp)

        let props: LogView.Props<TestEnv> = {
          Env = env
          Log = Latest
          Session = signedInSession()
          Nav = Unchecked.defaultof<Navs.INavigable>
          Context = Unchecked.defaultof<Navs.RouteContext>
        }

        let headJson =
          datedCommitJson(
            "0a0a0a0a-0000-0000-0000-000000000001",
            "aaa1111",
            "First landing.",
            "2020-04-03T12:00:00Z"
          )

        let headPage =
          sprintf
            "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
            headJson

        let loaded =
          match LogPage.decode headPage with
          | Ok page -> page
          | Error failure ->
            failwith $"the head page never decoded: {failure.Message}"

        let model = {
          LogView.State.initial with
              Head = Some "0a0a0a0a-0000-0000-0000-000000000001"
              Items = loaded.Page.Items
        }

        let olderJson =
          datedCommitJson(
            "0a0a0a0a-0000-0000-0000-000000000010",
            "old1111",
            "The older late.",
            "2020-04-01T12:00:00Z"
          )

        let burstJson =
          sprintf
            "{\"data\":[%s,%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
            lateCommitJson
            olderJson

        let burst =
          match LogPage.decode burstJson with
          | Ok page -> page
          | Error failure ->
            failwith $"the burst page never decoded: {failure.Message}"

        // One poll burst lands as that one update, newest first.
        let staged, _ =
          LogView.State.update props (LogView.State.NewChecked(Ok burst)) model

        Expect.equal 3 staged.Items.Length "the burst never landed together"

        Expect.isNone staged.NewCount "the burst staged the pill"

        Expect.equal
          [
            "0a0a0a0a-0000-0000-0000-000000000001"
            "0a0a0a0a-0000-0000-0000-000000000000"
            "0a0a0a0a-0000-0000-0000-000000000010"
          ]
          (staged.Items
           |> List.map(function
             | LiveCommit commit -> commit.Id
             | Tombstone tombstone -> tombstone.Id))
          "the burst landed out of order"
      })

    testCaseAsync
      "reselecting the log scrolls to its top"
      (async {
        let env = TestEnv(signedInStore(), logApi() :> IHttp)

        let! window = async {
          let! born = probeUiAsync(fun () -> Shell(env, None) :> Window)
          return! showAsync born
        }

        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        let timeline =
          (env :> DevNull.Client.Timeline.IProvideTimeline).Timeline

        let scrolled = ResizeArray<Timeline.Request>()
        let listen = timeline.Requests.Subscribe scrolled.Add
        scrolled.Clear()

        do! clickWhenShown(window, "Log")

        let! raised =
          waitForAsync(
            5000,
            (fun () -> scrolled),
            fun seen -> seen.Contains Timeline.ScrollToTop
          )

        listen.Dispose()
        Expect.isTrue raised "the Log entry never raised a scroll to top"
      })

    testCaseAsync
      "a stored session that predates the terms never gates"
      (async {
        let api = logApi()

        let! window = startShell(signedInStoreWithUnknownTerms(), api :> IHttp)

        let! usable = showsText(window, "The Octocat")

        Expect.isTrue usable "the app never rendered behind the gate"

        let! settled = settleAsync()

        let gated =
          window.GetVisualDescendants()
          |> Seq.exists(fun visual -> visual.Name = "TermsDialog")

        Expect.isFalse gated "unknown terms opened the gate"
      })

    testCaseAsync
      "the terms gate asks and records the acceptance"
      (async {
        let api = termsApi()

        let! window = startShell(signedInStoreWithoutTerms(), api :> IHttp)

        let dialogStanding(root: Control) =
          root.GetVisualDescendants()
          |> Seq.exists(fun visual -> visual.Name = "TermsDialog")

        let! standing = waitForAsync(5000, (fun () -> window), dialogStanding)

        Expect.isTrue standing "the terms dialog never stood"

        // Ticking the box arms the Accept button.
        do!
          onUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(function
              | :? CheckBox as box -> Some box
              | _ -> None)
            |> Option.iter(fun box -> box.IsChecked <- Nullable true))

        let! armed =
          waitForAsync(
            5000,
            (fun () -> buttonWithText(window, "Accept")),
            (fun found ->
              found
              |> Option.map(fun button -> button.IsEnabled)
              |> Option.defaultValue false)
          )

        Expect.isTrue armed "the accept button never armed"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Accept") |> Option.iter click)

        let acceptanceWentOut(request: HttpRequest) =
          request.Method = "PUT" && request.Target = "/api/v1/me/terms"

        let! sent =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            List.exists acceptanceWentOut
          )

        Expect.isTrue sent "the acceptance never went out"

        let bodyOf(request: HttpRequest) =
          match request.Body with
          | Text body -> body
          | Multipart _ -> ""
          | Empty -> ""

        let acceptance = api.Requests |> List.find acceptanceWentOut

        Expect.equal """{"version":"20261001"}""" (bodyOf acceptance) "body"

        let! left =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(dialogStanding root))
          )

        Expect.isTrue left "the terms dialog never left"
      })

    testList "search row identity" [
      testCaseAsync
        "a wide handle trims inside its row"
        (async {
          let images =
            ImageCacheService.create(
              System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "devnull-searchidentity-" + Guid.NewGuid().ToString("N")
              ),
              (fun _ -> async { return Ok [||] }),
              NullLogger.Instance
            )

          let ctx: RowCtx = {
            Images = images
            Flyout = ValueNone
            Now = DateTimeOffset.UtcNow
          }

          let commit: Commit = {
            Id = "01a15638-ae71-733c-89b3-373a1a0c538a"
            ShortHash = "e31db7c"
            Body = "Where is the cool F# people?"
            BodySegments = []
            PreviewSegments = None
            Mentions = []
            ParentId = None
            Parent = None
            HtmlUrl = "/commit/01a15638"
            CreatedAt = DateTimeOffset.UtcNow
            StarsCount = None
            RepliesCount = None
            Status = Approved
            Author =
              Person {
                Id = "01a15638-ae71-733c-89b3-373a1a0c538a"
                Handle = String.replicate 300 "n"
                Name = "found"
                AvatarUrl = None
                Bio = None
                Lane = None
                CommitsCount = None
                FollowersCount = None
                FollowingCount = None
                FollowedByFollowingCount = None
                IsFollowed = None
                FollowsYou = None
              }
            Images = []
            Starred = None
            IsPinned = false
            PasteisCount = 0
            QuotedCommitId = None
            QuotedCommit = None
            Poll = None
            LinkPreview = None
            ReplyPreview = None
            Translation = None
          }

          // The row sits in a 360-wide host: the identity line must trim
          // to fit it, never overflow past it.
          let hostWidth = 360.0

          let metaRight(root: Control) : float =
            root.GetVisualDescendants()
            |> Seq.choose(fun visual ->
              match visual with
              | :? TextBlock as block when block.Inlines <> null ->
                let carriesHandle =
                  block.Inlines
                  |> Seq.exists(fun inlineNode ->
                    match inlineNode with
                    | :? Run as run ->
                      run.Text <> null && run.Text.StartsWith "@"
                    | _ -> false)

                if carriesHandle then Some block.Bounds.Right else None
              | _ -> None)
            |> Seq.tryHead
            |> Option.defaultValue infinity

          let! window =
            probeUiAsync(fun () ->
              Window(
                Content =
                  Border(
                    Width = hostWidth,
                    Child = commitIdentity(ctx, commit)
                  )
              ))

          do! showAsync window |> Async.Ignore

          let! within =
            waitForAsync(
              5000,
              (fun () -> metaRight window),
              (fun right -> right <= hostWidth + 1.0)
            )

          Expect.isTrue within "the identity line overflowed its row"
        })
    ]

    testList "connections pages" [
      testCaseAsync
        "the profile's following count opens the following list"
        (async {
          let api = connectionsApi()
          let! window = startShell(signedInStore(), api :> IHttp)

          let! shown = showsText(window, "The Octocat")

          Expect.isTrue shown "the log never rendered"

          do! clickWhenShown(window, "Profile")

          let! bio = showsText(window, "Builds things.")

          Expect.isTrue bio "the profile never rendered"

          do! clickVisibleNamed(window, "ProfileFollowingCount:octocat")

          let! back = showsText(window, "Follow back")

          Expect.isTrue back "the following list never rendered its rows"

          let! orgs = showsText(window, "DevNull")

          Expect.isTrue orgs "the organizations section never rendered"

          let! people = showsText(window, "People")

          Expect.isTrue people "the people heading never rendered"

          Expect.exists
            api.Requests
            (fun request -> request.Target = "/api/v1/users/octocat/following")
            "the following read"

          Expect.exists
            api.Requests
            (fun request ->
              request.Target = "/api/v1/users/octocat/following/organizations")
            "the followed organizations read"
        })

      testCaseAsync
        "a Follow back row flips through the follow control"
        (async {
          let api = connectionsApi()
          let! window = startShell(signedInStore(), api :> IHttp)

          let! shown = showsText(window, "The Octocat")

          Expect.isTrue shown "the log never rendered"

          do! clickWhenShown(window, "Profile")
          do! clickVisibleNamed(window, "ProfileFollowingCount:octocat")

          let! word =
            waitForAsync(
              5000,
              (fun () -> namedButtonWord(window, "FollowConnection:dora")),
              (fun current -> current = Some "Follow back")
            )

          Expect.isTrue word "the Follow back word never rendered"

          do! clickVisibleNamed(window, "FollowConnection:dora")

          let! posted =
            waitForAsync(
              5000,
              (fun () -> api.Requests),
              (fun requests ->
                requests
                |> List.exists(fun request ->
                  request.Method = "POST"
                  && request.Target = "/api/v1/users/dora/follow"))
            )

          Expect.isTrue posted "the follow never went out"

          let! flipped =
            waitForAsync(
              5000,
              (fun () -> namedButtonWord(window, "FollowConnection:dora")),
              (fun current -> current = Some "Unfollow")
            )

          Expect.isTrue flipped "the word never flipped to Unfollow"

          do! clickVisibleNamed(window, "FollowConnection:dora")

          let! takenBack =
            waitForAsync(
              5000,
              (fun () -> api.Requests),
              (fun requests ->
                requests
                |> List.exists(fun request ->
                  request.Method = "DELETE"
                  && request.Target = "/api/v1/users/dora/follow"))
            )

          Expect.isTrue takenBack "the unfollow never went out"

          let! again =
            waitForAsync(
              5000,
              (fun () -> namedButtonWord(window, "FollowConnection:dora")),
              (fun current -> current = Some "Follow back")
            )

          Expect.isTrue again "the word never came back to Follow back"
        })

      testCaseAsync
        "the viewer's own row carries no follow control"
        (async {
          let! window = startShell(signedInStore(), connectionsApi() :> IHttp)

          let! shown = showsText(window, "The Octocat")

          Expect.isTrue shown "the log never rendered"

          do! clickWhenShown(window, "Profile")
          do! clickVisibleNamed(window, "ProfileFollowingCount:octocat")

          let! row =
            waitForAsync(
              5000,
              (fun () -> controlByName(window, "ConnectionRow:octocat")),
              Option.isSome
            )

          Expect.isTrue row "the viewer's own row never rendered"

          let! control =
            probeUiAsync(fun () ->
              controlByName(window, "FollowConnection:octocat"))

          Expect.isNone control "the viewer's own row grew a follow control"
        })

      testCaseAsync
        "the Follows-you chip follows the list's owner"
        (async {
          let api = connectionsApi()
          let! window = startShell(signedInStore(), api :> IHttp)

          let! shown = showsText(window, "The Octocat")

          Expect.isTrue shown "the log never rendered"

          do! clickWhenShown(window, "Profile")

          // The viewer's own follower list hides the chip: everyone on
          // it follows the viewer already.
          do! clickVisibleNamed(window, "ProfileFollowersCount:octocat")

          let! rows = showsText(window, "Dora Default")

          Expect.isTrue rows "the follower rows never rendered"

          do! settleAsync()

          let! hidden =
            probeUiAsync(fun () -> visibleText(window, "Follows you"))

          Expect.isFalse hidden "the chip leaked on the viewer's own list"

          // Another user's list carries it.
          do! clickVisibleNamed(window, "OpenConnection:dora")

          let! dora = showsText(window, "@dora")

          Expect.isTrue dora "dora's profile never opened"

          do! clickVisibleNamed(window, "ProfileFollowersCount:dora")

          let! again = showsText(window, "Dora Default")

          Expect.isTrue again "the other user's list never rendered"

          let! chip = seesVisible(window, "Follows you")

          Expect.isTrue chip "the chip never showed on another user's list"
        })

      testCaseAsync
        "Show more organizations pulls the next page in place"
        (async {
          let api = connectionsApi()
          let! window = startShell(signedInStore(), api :> IHttp)

          let! shown = showsText(window, "The Octocat")

          Expect.isTrue shown "the log never rendered"

          do! clickWhenShown(window, "Profile")
          do! clickVisibleNamed(window, "ProfileFollowingCount:octocat")

          let! section = showsText(window, "Organizations")

          Expect.isTrue section "the organizations section never rendered"

          let! first = showsText(window, "DevNull")

          Expect.isTrue first "the first organization never rendered"

          do! clickWhenShown(window, "Show more organizations")

          let! second = showsText(window, "The Octopus")

          Expect.isTrue second "the second organizations page never landed"

          do! settleAsync()

          let! people = seesVisible(window, "People")

          Expect.isTrue people "the people heading never rendered"

          let expectedNext =
            "/api/v1/users/octocat/following/organizations?cursor=next"

          let nextRead =
            api.Requests
            |> List.exists(fun request -> request.Target = expectedNext)

          Expect.isTrue nextRead "the next organizations read"
        })

      testCaseAsync
        "the followers list renders its rows and its empty words"
        (async {
          let api = connectionsApi()
          let! window = startShell(signedInStore(), api :> IHttp)

          let! shown = showsText(window, "The Octocat")

          Expect.isTrue shown "the log never rendered"

          do! clickWhenShown(window, "Profile")
          do! clickVisibleNamed(window, "ProfileFollowersCount:octocat")

          let! rows = showsText(window, "Kai Kou")

          Expect.isTrue rows "the follower rows never rendered"

          do! onUiAsync(fun () -> window.Close())

          let! second =
            startShell(signedInStore(), emptyFollowersApi() :> IHttp)

          let! booted = showsText(second, "The Octocat")

          Expect.isTrue booted "the log never rendered"

          do! clickWhenShown(second, "Profile")
          do! clickVisibleNamed(second, "ProfileFollowersCount:octocat")

          let! words = seesVisible(second, "Nobody is following @octocat yet.")

          Expect.isTrue words "the empty words never rendered"
        })

      testCaseAsync
        "a signed-out visitor cannot open the list routes"
        (async {
          let store = signedInStore()
          let! window = startShell(store, connectionsApi() :> IHttp)

          let! shown = showsText(window, "The Octocat")

          Expect.isTrue shown "the log never rendered"

          do! clickWhenShown(window, "Profile")

          do! onUiAsync(fun () -> store.Leave())

          do! clickVisibleNamed(window, "ProfileFollowingCount:octocat")

          let! signIn = showsText(window, "Sign in to /dev/null")

          Expect.isTrue signIn "the following route never asked to sign in"

          do! onUiAsync(fun () -> window.Close())

          // The followers route answers the same way: the tab that
          // names it navigates through the same guard.
          let other = signedInStore()
          let! second = startShell(other, connectionsApi() :> IHttp)

          let! booted = showsText(second, "The Octocat")

          Expect.isTrue booted "the log never rendered"

          do! clickWhenShown(second, "Profile")
          do! clickVisibleNamed(second, "ProfileFollowingCount:octocat")

          let! list = showsText(second, "Follow back")

          Expect.isTrue list "the list never opened"

          do! onUiAsync(fun () -> other.Leave())
          do! clickWhenShown(second, "Followers")

          let! gate = showsText(second, "Sign in to /dev/null")

          Expect.isTrue gate "the followers route never asked to sign in"
        })
    ]
  ]
