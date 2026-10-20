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

/// Builds and shows the shell over one test env. The window is born on
/// the UI thread: the headless platform only exists there.
let startShell(store: SessionStore, http: IHttp) : Window =
  let env = TestEnv(store, http)
  let window = probeUi(fun () -> Shell(env, None) :> Window)
  show window

/// Waits until a TextBlock under the window carries the text.
let showsText(window: Window, text: string) : bool =
  waitFor(5000, (fun () -> window), (fun root -> hasText(root, text)))

/// Clicks one button by its text once it shows up.
let clickWhenShown(window: Window, text: string) : unit =
  let appeared =
    waitFor(5000, (fun () -> buttonWithText(window, text)), Option.isSome)

  Expect.isTrue appeared $"the {text} button never appeared"

  onUi(fun () -> buttonWithText(window, text) |> Option.iter click)

let tests =
  testList "app shell" [
    test "a signed-in boot lands on the latest log and renders commits" {
      let api = logApi()
      let window = startShell(signedInStore(), api :> IHttp)

      Expect.isTrue
        (showsText(window, "The Octocat"))
        "the commit author never rendered"

      Expect.isNonEmpty api.Requests "no request went out"

      Expect.exists
        api.Requests
        (fun request ->
          request.Method = "GET"
          && request.Target.StartsWith "/api/v1/log/latest")
        "the signed log request"
    }

    test "a signed-out boot lands on the sign-in page" {
      let window = startShell(signedOutStore(), logApi() :> IHttp)

      Expect.isTrue
        (showsText(window, "Sign in to /dev/null"))
        "the sign-in page never rendered"
    }

    test "an empty log shows the empty state" {
      let window = startShell(signedInStore(), emptyLogApi() :> IHttp)

      Expect.isTrue
        (showsText(window, "Nothing here yet. Commit something."))
        "the empty state never rendered"
    }

    test "the log separates the days and rides the rail" {
      let window = startShell(signedInStore(), twoDaysLogApi() :> IHttp)

      Expect.isTrue
        (showsText(window, "Fri, Apr 3"))
        "the first day header never rendered"

      Expect.isTrue
        (showsText(window, "Wed, Apr 1"))
        "the second day header never rendered"

      Expect.isTrue
        (showsText(window, "First landing."))
        "the first commit never rendered"

      Expect.isTrue
        (showsText(window, "The week before."))
        "the second commit never rendered"
    }

    test "toRows interleaves the days of the log" {
      let response =
        (twoDaysLogApi(): IHttp)
          .Send(
            {
              Method = "GET"
              Target = "/api/v1/log/latest"
              Headers = []
              Body = HttpBody.Empty
            }
          )
        |> Async.RunSynchronously

      let page =
        match response with
        | Ok response ->
          match LogPage.decode response.Body with
          | Ok page -> page
          | Error failure -> failwith $"page never decoded: {failure.Message}"
        | Error failure -> failwith $"request failed: {failure.Message}"

      let rows = LogView.toRows page.Page.Items

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

      Expect.equal rows[1] (LogView.LogRow.Node(first, None)) "the first node"

      Expect.equal
        rows[2]
        (LogView.LogRow.Day "Wed, Apr 1")
        "the second separator"

      Expect.equal rows[3] (LogView.LogRow.Node(second, None)) "the second node"
    }

    test "the sidebar navigates to the settings page" {
      let window = startShell(signedInStore(), logApi() :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      clickWhenShown(window, "Settings")

      Expect.isTrue
        (showsText(window, "@octocat"))
        "the settings page never rendered"
    }

    test "the commit button opens the composer" {
      let window = startShell(signedInStore(), logApi() :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      clickWhenShown(window, "Commit")

      Expect.isTrue
        (showsText(window, "What did you ship?"))
        "the composer page never rendered"
    }

    test "the log tabs request the other log" {
      let api = logApi()
      let window = startShell(signedInStore(), api :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      clickWhenShown(window, "Trending")

      let switched =
        waitFor(
          5000,
          (fun () -> api.Requests),
          (fun requests ->
            requests
            |> List.exists(fun request ->
              request.Target.StartsWith "/api/v1/log/trending"))
        )

      Expect.isTrue switched "the trending log was never requested"
    }

    test "a signed-out visitor is sent to sign-in by the guard" {
      let store = signedInStore()
      let window = startShell(store, logApi() :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      onUi(fun () -> store.Leave())
      clickWhenShown(window, "Settings")

      Expect.isTrue
        (showsText(window, "Sign in to /dev/null"))
        "the guard never redirected to sign-in"
    }

    test "the composer posts a commit and reloads the log" {
      let api = logApi()
      let window = startShell(signedInStore(), api :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      let typed =
        waitFor(
          5000,
          (fun () -> textBoxWithWatermark(window, "What did you ship?")),
          Option.isSome
        )

      Expect.isTrue typed "the composer never appeared"

      onUi(fun () ->
        textBoxWithWatermark(window, "What did you ship?")
        |> Option.iter(fun box ->
          box.Text <- "Committed from the test"

          buttonNear(box, "Commit") |> Option.iter click))

      let posted =
        waitFor(
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

      Expect.stringContains postBody "Committed from the test" "the typed body"

      let reloads =
        api.Requests
        |> List.filter(fun request ->
          request.Method = "GET"
          && request.Target.StartsWith "/api/v1/log/latest")

      Expect.isGreaterThan reloads.Length 1 "the log never reloaded"
    }

    test "starring a commit posts the star" {
      let api = logApi()
      let window = startShell(signedInStore(), api :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      let starred =
        waitFor(5000, (fun () -> buttonWithText(window, "7")), Option.isSome)

      Expect.isTrue starred "the star button never appeared"

      onUi(fun () -> buttonWithText(window, "7") |> Option.iter click)

      let posted =
        waitFor(
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
    }

    test "load more appends the next page" {
      let window = startShell(signedInStore(), logApi() :> IHttp)

      Expect.isTrue
        (showsText(window, "It was DNS."))
        "the first page never rendered"

      clickWhenShown(window, "Load more")

      scrollFeedToEnd window

      Expect.isTrue
        (showsText(window, "Second commit."))
        "the second page never rendered"
    }

    test "scrolling recycles rows and realizes them again" {
      let window = startShell(signedInStore(), manyCardsApi() :> IHttp)

      Expect.isTrue
        (showsText(window, "Card 1."))
        "the first card never rendered"

      // The small window makes the six-card feed overflow the viewport
      // plus its cache band, so scrolling must recycle the top rows.
      onUi(fun () ->
        window.Width <- 420.0
        window.Height <- 480.0)

      scrollFeedToEnd window

      let recycled =
        waitFor(
          5000,
          (fun () -> window),
          (fun root -> not(hasText(root, "Card 1.")))
        )

      Expect.isTrue recycled "the first row was never recycled"

      scrollFeedToTop window

      Expect.isTrue
        (showsText(window, "Card 1."))
        "the recycled row never came back"
    }

    test "opening a commit shows the thread with its replies" {
      let api = threadApi()
      let window = startShell(signedInStore(), api :> IHttp)

      Expect.isTrue (showsText(window, "It was DNS.")) "the log never rendered"

      let card =
        waitFor(
          5000,
          (fun () -> buttonWithText(window, "It was DNS.")),
          Option.isSome
        )

      Expect.isTrue card "the commit card never appeared"

      onUi(fun () -> buttonWithText(window, "It was DNS.") |> Option.iter click)

      Expect.isTrue
        (showsText(window, "Back to the log"))
        "the thread page never rendered"

      Expect.isTrue (showsText(window, "A reply.")) "the replies never rendered"

      Expect.exists
        api.Requests
        (fun request ->
          request.Method = "GET"
          && request.Target =
            "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d")
        "the thread request"
    }

    test "the reply composer posts a reply on the thread" {
      let api = threadApi()
      let window = startShell(signedInStore(), api :> IHttp)

      Expect.isTrue (showsText(window, "It was DNS.")) "the log never rendered"

      onUi(fun () -> buttonWithText(window, "It was DNS.") |> Option.iter click)

      Expect.isTrue
        (showsText(window, "Back to the log"))
        "the thread never rendered"

      let typed =
        waitFor(
          5000,
          (fun () -> textBoxWithWatermark(window, "Write your reply…")),
          Option.isSome
        )

      Expect.isTrue typed "the reply composer never appeared"

      onUi(fun () ->
        textBoxWithWatermark(window, "Write your reply…")
        |> Option.iter(fun box ->
          box.Text <- "A reply from the test"

          buttonNear(box, "Commit") |> Option.iter click))

      let posted =
        waitFor(
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
    }

    test "giving a pastel posts the pastel" {
      let window = startShell(signedInStore(), threadApi() :> IHttp)

      Expect.isTrue (showsText(window, "It was DNS.")) "the log never rendered"

      onUi(fun () -> buttonWithText(window, "It was DNS.") |> Option.iter click)

      Expect.isTrue
        (showsText(window, "Back to the log"))
        "the thread never rendered"

      clickWhenShown(window, "Give a pastel")

      Expect.isTrue
        (showsText(window, "Pastel given. 6 left this cycle."))
        "the pastel note never showed"
    }

    test "quote opens the quote composer" {
      let window = startShell(signedInStore(), threadApi() :> IHttp)

      Expect.isTrue (showsText(window, "It was DNS.")) "the log never rendered"

      onUi(fun () -> buttonWithText(window, "It was DNS.") |> Option.iter click)

      Expect.isTrue
        (showsText(window, "Back to the log"))
        "the thread never rendered"

      clickWhenShown(window, "Quote")

      let rendered =
        waitFor(
          5000,
          (fun () -> window),
          (fun root -> hasText(root, "Add your comment to the quote…"))
        )

      if not rendered then
        let texts: string list =
          probeUi(fun () ->
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
    }

    test "the inbox lists notifications" {
      let api = accountApi()
      let window = startShell(signedInStore(), api :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      clickWhenShown(window, "Notifications")

      Expect.isTrue
        (showsText(window, "Notifications · 3 unread"))
        "the inbox header never rendered"

      Expect.isTrue
        (showsText(window, "replied"))
        "the notification never rendered"

      Expect.isTrue
        (showsText(window, "It was DNS."))
        "the commit preview never rendered"

      Expect.exists
        api.Requests
        (fun request -> request.Target.StartsWith "/api/v1/notifications")
        "the inbox request"
    }

    test "the profile shows the signed-in user and their commits" {
      let api = accountApi()
      let window = startShell(signedInStore(), api :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      clickWhenShown(window, "Profile")

      let texts: string list =
        probeUi(fun () ->
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

      Expect.isTrue
        (showsText(window, "It was DNS."))
        "the commits never rendered"

      Expect.exists
        api.Requests
        (fun request -> request.Target = "/api/v1/users/octocat")
        "the profile request"

      // the viewer's own profile carries no follow or mute controls
      Expect.isNone
        (probeUi(fun () -> buttonWithText(window, "Follow")))
        "the follow button leaked"
    }

    test "the saved list renders the starred commits" {
      let api = accountApi()
      let window = startShell(signedInStore(), api :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      clickWhenShown(window, "Saved")

      Expect.isTrue
        (showsText(window, "It was DNS."))
        "the saved commit never rendered"

      Expect.exists
        api.Requests
        (fun request -> request.Target.StartsWith "/api/v1/starred")
        "the starred request"
    }

    test "signing out lands on sign-in and drops the session" {
      let store = signedInStore()
      let window = startShell(store, accountApi() :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      clickWhenShown(window, "Settings")

      Expect.isTrue
        (showsText(window, "@octocat"))
        "the settings never rendered"

      clickWhenShown(window, "Sign out")

      Expect.isTrue
        (showsText(window, "Sign in to /dev/null"))
        "the sign-in page never came back"

      let state = probeUi(fun () -> store.Current)

      Expect.equal state SignedOut "the store kept the session"
    }

    test "the notifications entry carries the unread badge" {
      let window = startShell(signedInStoreWithUnread 9, logApi() :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      Expect.isTrue (showsText(window, "9")) "the unread badge never rendered"
    }

    test "the sidebar folds into a drawer on narrow windows" {
      let window = startShell(signedInStore(), logApi() :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      Expect.isTrue
        (probeUi(fun () ->
          buttonWithText(window, "Settings")
          |> Option.map effectiveVisible
          |> Option.defaultValue false))
        "the sidebar never rendered on the wide window"

      Expect.isFalse
        (probeUi(fun () ->
          controlByName(window, "MenuButton")
          |> Option.map effectiveVisible
          |> Option.defaultValue false))
        "the hamburger showed on the wide window"

      onUi(fun () ->
        window.Width <- 400.0
        window.Height <- 700.0)

      let hamburger =
        waitFor(
          5000,
          (fun () -> controlByName(window, "MenuButton")),
          (fun control ->
            control
            |> Option.bind(fun c ->
              if c :? Button then Some(c :?> Button) else None)
            |> visibleOption)
        )

      Expect.isTrue hamburger "the hamburger never showed on the narrow window"

      Expect.isFalse
        (probeUi(fun () -> buttonWithText(window, "Settings") |> visibleOption))
        "the sidebar stayed open on the narrow window"

      onUi(fun () ->
        controlByName(window, "MenuButton")
        |> Option.iter(fun control -> click(control :?> Button)))

      let drawer =
        waitFor(
          5000,
          (fun () -> buttonWithText(window, "Settings")),
          visibleOption
        )

      Expect.isTrue drawer "the drawer never opened"

      clickWhenShown(window, "Settings")

      let closed =
        waitFor(
          5000,
          (fun () -> buttonWithText(window, "Settings")),
          (fun button ->
            not(
              button |> Option.map effectiveVisible |> Option.defaultValue true
            ))
        )

      Expect.isTrue closed "the drawer stayed open after navigation"

      Expect.isTrue
        (showsText(window, "@octocat"))
        "the settings page never rendered"
    }

    test "the copy-link action puts the commit URL on the clipboard" {
      let window = startShell(signedInStore(), logApi() :> IHttp)

      Expect.isTrue (showsText(window, "It was DNS.")) "the log never rendered"

      clickWhenShown(window, "Link")

      let url =
        probeUi(fun () ->
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
    }

    test "the shell keeps the drawn chrome to the border only" {
      let window = startShell(signedInStore(), logApi() :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      Expect.equal
        (probeUi(fun () -> window.WindowDecorations))
        WindowDecorations.BorderOnly
        "the drawn chrome painted its caption over the client again"
    }

    test "the caption buttons drive the window state" {
      let window = startShell(signedInStore(), logApi() :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      let byName(name: string) =
        controlByName(window, name)
        |> Option.map(fun control -> control :?> Button)

      let exists(name: string) =
        probeUi(fun () -> byName name |> Option.isSome)

      Expect.isTrue (exists "MinimizeButton") "no minimize button"
      Expect.isTrue (exists "MaximizeButton") "no maximize button"
      Expect.isTrue (exists "CloseButton") "no close button"

      onUi(fun () -> byName "MinimizeButton" |> Option.iter click)

      Expect.equal
        (probeUi(fun () -> window.WindowState))
        WindowState.Minimized
        "the minimize button never ran"

      onUi(fun () ->
        window.WindowState <- WindowState.Normal
        byName "MaximizeButton" |> Option.iter click)

      Expect.equal
        (probeUi(fun () -> window.WindowState))
        WindowState.Maximized
        "the maximize button never ran"

      onUi(fun () -> byName "MaximizeButton" |> Option.iter click)

      Expect.equal
        (probeUi(fun () -> window.WindowState))
        WindowState.Normal
        "the restore never ran"
    }

    test "fullscreen hides the caption row and toggling brings it back" {
      let window = startShell(signedInStore(), logApi() :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      onUi(fun () -> (window :?> Shell<TestEnv>).ToggleFullScreen())

      Expect.equal
        (probeUi(fun () -> window.WindowState))
        WindowState.FullScreen
        "the window never went fullscreen"

      Expect.isFalse
        (probeUi(fun () ->
          controlByName(window, "CaptionRow")
          |> Option.map effectiveVisible
          |> Option.defaultValue false))
        "the caption row stayed visible in fullscreen"

      onUi(fun () -> (window :?> Shell<TestEnv>).ToggleFullScreen())

      Expect.equal
        (probeUi(fun () -> window.WindowState))
        WindowState.Normal
        "the window never left fullscreen"

      let restored =
        waitFor(
          5000,
          (fun () -> controlByName(window, "CaptionRow")),
          (fun control ->
            control |> Option.map effectiveVisible |> Option.defaultValue false)
        )

      Expect.isTrue restored "the caption row never came back"
    }

    test "closing with close-to-tray hides the window and Quit closes it" {
      let settings = AppSettingsStore(None, ignore)
      settings.SetCloseToTray true

      let env = TestEnv(signedInStore(), logApi() :> IHttp, settings)
      let window = probeUi(fun () -> Shell(env, None) :> Window)
      show window |> ignore

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      let closes = ResizeArray<string>()

      onUi(fun () -> window.Closed.Add(fun _ -> closes.Add "closed"))

      onUi(fun () -> window.Close())

      Expect.isFalse
        (probeUi(fun () -> window.IsVisible))
        "the window stayed visible after the close"

      Expect.equal
        (probeUi(fun () -> closes.Count))
        0
        "the close was never cancelled"

      onUi(fun () -> (window :?> Shell<TestEnv>).Quit())

      let quit =
        waitFor(5000, (fun () -> closes.Count), (fun count -> count > 0))

      Expect.isTrue quit "the quit never closed the window"
    }

    test "closing without the flag closes the window" {
      let env = TestEnv(signedInStore(), logApi() :> IHttp)
      let window = probeUi(fun () -> Shell(env, None) :> Window)
      show window |> ignore

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      let closes = ResizeArray<string>()

      onUi(fun () -> window.Closed.Add(fun _ -> closes.Add "closed"))

      onUi(fun () -> window.Close())

      let closed =
        waitFor(5000, (fun () -> closes.Count), (fun count -> count > 0))

      Expect.isTrue closed "the window never closed"
    }

    test "the settings toggle persists close-to-tray" {
      let saves = ResizeArray<AppSettings>()

      let settings = AppSettingsStore(None, fun value -> saves.Add value)

      let env = TestEnv(signedInStore(), logApi() :> IHttp, settings)
      let window = probeUi(fun () -> Shell(env, None) :> Window)
      show window |> ignore

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      clickWhenShown(window, "Settings")

      Expect.isTrue
        (showsText(window, "Keep running in the tray when the window closes"))
        "the application section never rendered"

      onUi(fun () ->
        controlByName(window, "CloseToTrayToggle")
        |> Option.iter(fun control ->
          (control :?> ToggleSwitch).IsChecked <- Nullable true))

      Expect.isTrue
        (probeUi(fun () -> settings.Current.CloseToTray))
        "the store never took the flag"

      Expect.equal (probeUi(fun () -> saves.Count)) 1 "the save hook never ran"
    }

    test "the theme carries distinct dark and light tokens" {
      Expect.notEqual
        (probeUi(fun () -> solid(ThemeVariant.Dark, Keys.background)))
        (probeUi(fun () -> solid(ThemeVariant.Light, Keys.background)))
        "the variants share the background token"
    }

    test "the theme choice parses the stored names" {
      Expect.equal (parseTheme "dark") (Some Dark) "dark never parsed"
      Expect.equal (parseTheme "light") (Some Light) "light never parsed"
      Expect.equal (parseTheme " system ") (Some System) "system never parsed"
      Expect.isNone (parseTheme "solarized") "an unknown name parsed"
    }

    test "the settings store publishes theme changes" {
      let saves = ResizeArray<AppSettings>()
      let store = AppSettingsStore(None, fun value -> saves.Add value)

      store.SetTheme Light

      Expect.equal store.Current.Theme Light "the store never took the choice"

      Expect.equal (probeUi(fun () -> saves.Count)) 1 "the save hook never ran"

      Expect.equal saves[0].Theme Light "the save hook ran with the default"

      store.SetTheme System

      Expect.equal store.Current.Theme System "the store never came back"
    }

    test "the active navigation wears the primary wash" {
      let window = startShell(signedInStore(), logApi() :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      let active =
        probeUi(fun () ->
          buttonWithText(window, "Log")
          |> Option.exists(fun button ->
            button.Classes.Contains(name NavItemActive)))

      Expect.isTrue active "the log entry never went active"
    }

    test "the wordmark renders the brand" {
      let window = startShell(signedInStore(), logApi() :> IHttp)

      Expect.isTrue (showsText(window, "/dev")) "the wordmark never rendered"
    }

    test "the aside shows the profile card on wide windows" {
      let window = startShell(signedInStore(), accountApi() :> IHttp)

      Expect.isTrue (showsText(window, "The Octocat")) "the log never rendered"

      onUi(fun () ->
        window.Width <- 1440.0
        window.Height <- 900.0)

      Expect.isTrue
        (showsText(window, "7 commits · 13 following · 11 followers"))
        "the profile card stats never rendered"
    }

    test "a since check stages new commits behind the pill" {
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

      let model = {
        LogView.initial with
            Head = Some "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
      }

      let staged, _ = LogView.update props (LogView.NewChecked(Ok newer)) model

      Expect.isSome staged.New "the newer commits never staged"

      Expect.equal
        (staged.New |> Option.map List.length |> Option.defaultValue 0)
        1
        "the staged count"

      Expect.isEmpty staged.Items "the feed grew before the pill ran"

      let merged, _ = LogView.update props LogView.NewRequested staged

      Expect.isNone merged.New "the pill never cleared"

      Expect.equal (merged.Items |> List.length) 1 "the feed never merged"
    }
  ]
