module DevNull.Client.UI.Tests.App

// The shell checks: the boot route follows the session, the timeline
// renders through the signed API with fakes, the sidebar navigates, and
// the guard sends a signed-out visitor to sign-in.

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Styling
open Avalonia.VisualTree

open Expecto

open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.SettingsStore
open DevNull.Client.Shell
open DevNull.Client.Stores
open DevNull.Client.Theme
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

/// The visible-state probe for the finder helpers: an option-aware
/// `effectiveVisible`.
let visibleOption(option: Button option) : bool =
  option |> Option.map effectiveVisible |> Option.defaultValue false

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

/// Waits until a TextBlock under the window carries the text.
let showsText(window: Window, text: string) : Async<bool> =
  waitForAsync(5000, (fun () -> window), (fun root -> hasText(root, text)))

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

        let rows = LogView.toRows(page.Page.Items, page.IsRanked)

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
        let ranked = LogView.toRows(page.Page.Items, true)

        Expect.equal (ranked |> List.length) 3 "the ranked row count"

        Expect.isNone
          (ranked
           |> List.tryFind(function
             | LogView.LogRow.Day _ -> true
             | _ -> false))
          "the ranked feed carried a day separator"

        let keys = ranked |> List.map LogView.keyOf

        Expect.equal
          (keys |> List.distinct |> List.length)
          (keys |> List.length)
          "the ranked rows carried a repeated key"

        // The same commits on a newest-first reading interleave their
        // days: separators repeat their keys, which is why ranked rides
        // without them.
        let unranked = LogView.toRows(page.Page.Items, false)

        let unrankedKeys = unranked |> List.map LogView.keyOf

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
        }

        let fetched, _ =
          LogView.update props (LogView.Fetched(Ok page)) LogView.initial

        Expect.isTrue fetched.Ranked "the fetch never took the ranked flag"

        let polled, _ = LogView.update props LogView.CheckNew fetched

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
            (fun () -> textBoxWithWatermark(window, "What did you ship?")),
            Option.isSome
          )

        Expect.isTrue typed "the dialog composer never appeared"

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
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
      "a thread and back keeps the log's scroll"
      (async {
        let! window = startShell(signedInStore(), manyCardsApi() :> IHttp)

        let! shown = showsText(window, "Card 1.")

        Expect.isTrue shown "the first card never rendered"

        do! scrollFeedToEndAsync window

        // Whatever offset the scroll landed on is the offset the cached
        // page must still hold on return.
        let! before = probeUiAsync(fun () -> feedOffset window)

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Card 6.") |> Option.iter click)

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
      "quote opens the quote composer"
      (async {
        let! window = startShell(signedInStore(), threadApi() :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        let! quote =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "QuoteButton")),
            (fun control ->
              control
              |> Option.map(fun c -> c :? Button)
              |> Option.defaultValue false)
          )

        Expect.isTrue quote "the quote action never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "QuoteButton")
            |> Option.map(fun control -> control :?> Button)
            |> Option.iter click)

        let! rendered =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Add your comment to the quote…"))
          )

        if not rendered then
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
            rendered
            $"the quote composer never rendered; visible: {visible}"
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

        let! texts =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.filter(fun visual -> visual :? TextBlock)
            |> Seq.map(fun visual ->
              let textBlock = visual :?> TextBlock

              match textBlock.Text with
              | null -> ""
              | nonNull -> nonNull)
            |> Seq.toList)

        let visible = System.String.Join(" | ", texts)

        Expect.isTrue
          (texts |> List.contains "Builds things.")
          $"the bio never rendered; visible: {visible}"

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

        let! stored = probeUiAsync(fun () -> settings.Current.CloseToTray)

        Expect.isTrue stored "the store never took the flag"

        let! saved = probeUiAsync(fun () -> saves.Count)

        Expect.equal saved 1 "the save hook never ran"
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
      "the wordmark renders the brand"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        let! shown = showsText(window, "/dev")

        Expect.isTrue shown "the wordmark never rendered"
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
        }

        let newer =
          match LogPage.decode newerPageJson with
          | Ok page -> page
          | Error failure ->
            failwith $"the newer page never decoded: {failure.Message}"

        // The since read answers the same first page; only the envelope
        // count is news, so the items never stage behind the pill.
        let model = {
          LogView.initial with
              Head = Some "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
              Items =
                match newer.Page.Items with
                | [ node ] -> [ node ]
                | nodes -> nodes
        }

        let staged, _ =
          LogView.update props (LogView.NewChecked(Ok newer)) model

        Expect.equal staged.NewCount (Some 1) "the new count never staged"

        Expect.equal staged.Items model.Items "the since read touched the feed"

        // The pill fetches the fresh first page and replaces the feed:
        // every id lands once, so the keyed diff can never see a
        // repeated key.
        let refreshed, _ = LogView.update props LogView.NewRequested staged

        Expect.isNone refreshed.NewCount "the pill never cleared"

        Expect.equal refreshed.Items model.Items "the refresh was never staged"

        Expect.equal
          (refreshed.Items
           |> List.map(fun node ->
             match node with
             | LiveCommit commit -> LogView.keyOf(LogView.LogRow.Node commit)
             | Tombstone tombstone ->
               LogView.keyOf(LogView.LogRow.TombstoneRow tombstone))
           |> List.distinct
           |> List.length)
          (refreshed.Items |> List.length)
          "the refreshed feed carries a repeated key"
      })
  ]
