module DevNull.Client.UI.Tests.App

// The shell checks: the boot route follows the session, the timeline
// renders through the signed API with fakes, the sidebar navigates, and
// the guard sends a signed-out visitor to sign-in.

open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.VisualTree

open Expecto

open DevNull.Client.Core
open DevNull.Client.Core.Http
open DevNull.Client.Shell
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

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

      Expect.isTrue
        (showsText(window, "Add your comment to the quote…"))
        "the quote composer never rendered"
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
  ]
