module DevNull.Client.UI.Tests.App

// The shell checks: the boot route follows the session, the timeline
// renders through the signed API with fakes, the sidebar navigates, and
// the guard sends a signed-out visitor to sign-in.

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Shapes
open Avalonia.Headless
open Avalonia.Input
open Avalonia.Styling
open Avalonia.VisualTree

open Expecto

open DevNull.Client
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.LastVisitStore
open DevNull.Client.SettingsStore
open DevNull.Client.Shell
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.Timeline
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness
open DevNull.Client.Views

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

/// Waits until a TextBlock under the window carries the text.
let showsText(window: Window, text: string) : Async<bool> =
  waitForAsync(5000, (fun () -> window), (fun root -> hasText(root, text)))

/// True when an effectively visible TextBlock under the root carries the
/// text, so a hidden rail mark never counts.
let visibleText(root: Control, text: string) : bool =
  root.GetVisualDescendants()
  |> Seq.exists(fun visual ->
    match visual with
    | :? TextBlock as block -> block.Text = text && effectiveVisible block
    | _ -> false)

/// Clicks one button by its text once it shows up.
let clickWhenShown(window: Window, text: string) : Async<unit> = async {
  let! appeared =
    waitForAsync(5000, (fun () -> buttonWithText(window, text)), Option.isSome)

  Expect.isTrue appeared $"the {text} button never appeared"

  do! onUiAsync(fun () -> buttonWithText(window, text) |> Option.iter click)
}

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

    testCaseAsync
      "the settings page lists the devices and revokes one"
      (async {
        let api = devicesApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        do! clickWhenShown(window, "Settings")

        let! listed = showsText(window, "A Mac laptop")

        Expect.isTrue listed "the other device never listed"

        let! selfNamed = showsText(window, "This device")

        Expect.isTrue selfNamed "this device never named"

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

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "My own commit.") |> Option.iter click)

        do! clickWhenShown(window, "Delete commit")

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

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "My own commit.") |> Option.iter click)

        do! clickWhenShown(window, "Delete commit")

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

        let reloads =
          api.Requests
          |> List.filter(fun request ->
            request.Method = "GET"
            && request.Target.StartsWith "/api/v1/log/latest")

        Expect.isGreaterThan reloads.Length 1 "the log never reloaded"
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

        let reloads =
          api.Requests
          |> List.filter(fun request ->
            request.Method = "GET"
            && request.Target.StartsWith "/api/v1/log/latest")

        Expect.isGreaterThan reloads.Length 1 "the log never reloaded"
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
          LastVisitStore(
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
          LastVisitStore(
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
      })

    testCaseAsync
      "giving a pastel posts the pastel"
      (async {
        let! window = startShell(signedInStore(), threadApi() :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        do! clickWhenShown(window, "Give a pastel")

        let! note = showsText(window, "Pastel given. 6 left this cycle.")

        Expect.isTrue note "the pastel note never showed"
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
      "pastel controls stay hidden while pasteis mode is off"
      (async {
        let! window =
          startShell(signedInStoreWithoutPasteis(), threadApi() :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        let! leakedCard =
          probeUiAsync(fun () -> controlByName(window, "PastelButton"))

        Expect.isNone leakedCard "the card pastel action leaked"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        let! leakedThread =
          probeUiAsync(fun () -> buttonWithText(window, "Give a pastel"))

        Expect.isNone leakedThread "the thread pastel button leaked"

        let! leakedReply =
          probeUiAsync(fun () -> controlByName(window, "PastelButton"))

        Expect.isNone leakedReply "the reply pastel action leaked"
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
        let! window = startShell(signedInStore(), mentionLogApi() :> IHttp)

        // The handled click never runs here: it would launch a real
        // browser. The card being its own button, with the outside
        // url on it, is the contract.
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

        let! addressed = showsText(window, "https://example.test/postmortem")

        Expect.isTrue addressed "the preview address never rendered"
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

        let! link =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "LinkButton")),
            (fun control ->
              control
              |> Option.map(fun c -> c :? Button)
              |> Option.defaultValue false)
          )

        Expect.isTrue link "the link action never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "LinkButton")
            |> Option.map(fun control -> control :?> Button)
            |> Option.iter click)

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
        let settings = AppSettingsStore(None, ignore)
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

        let settings = AppSettingsStore(None, fun value -> saves.Add value)

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
        let store = AppSettingsStore(None, fun value -> saves.Add value)

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
        let store = AppSettingsStore(None, fun value -> saves.Add value)

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
  ]
