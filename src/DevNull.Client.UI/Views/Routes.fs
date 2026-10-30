module DevNull.Client.Views.Routes

open System
open Avalonia.Controls
open Microsoft.Extensions.Logging
open Navs
open Navs.Avalonia
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.DeviceAuth
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.Navigation
open DevNull.Client.Timeline
open DevNull.Client.UiClock
open DevNull.Client.Theme
open DevNull.Client.Views

/// Fires one navigation now and reports a failed one through the env's
/// logger.
let go(env: #IProvideLogging, nav: INavigable, url: string) : unit =
  env.LoggerFactory
    .CreateLogger("DevNull.Client.Views.Routes")
    .LogDebug("The app navigates to {Url}.", url)

  Navigation.go(
    nav,
    url,
    fun message ->
      env.LoggerFactory
        .CreateLogger("DevNull.Client.Views.Routes")
        .LogError("Navigation to {Url} failed: {Error}", url, message)
  )

/// The router and the entry the app boots into.
[<Struct>]
type RouterShell = {
  Router: IRouter<Control>
  BootUrl: string
}

let requiresSession(env: #IProvideSession) : GuardResult =
  match env.Session.SignedIn with
  | ValueSome _ -> GuardResult.Continue
  | ValueNone -> GuardResult.Redirect "/signin"

let requiresSignedOut(env: #IProvideSession) : GuardResult =
  match env.Session.SignedIn with
  | ValueSome _ -> GuardResult.Redirect "/log/latest"
  | ValueNone -> GuardResult.Continue

/// Builds one guarded page from the signed-in session, or shows the
/// route's sign-in hint.
let signedIn
  (env: #IProvideSession, message: string, build: SignedInSession -> Control)
  : Control =
  match env.Session.SignedIn with
  | ValueSome session -> build session
  | ValueNone -> Status.soon message

let logPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp & #IProvideSession & #IProvideUiClock & #IProvideTimeline & #IProvideLogging,
    nav: INavigable,
    context: RouteContext
  ) : Control =
  signedIn(
    env,
    "Sign in to read the log.",
    fun session ->
      match context.GetParam<string> "log" with
      | ValueSome raw ->
        let kind = LogKinds.tryParse raw |> Option.defaultValue LogKind.Latest

        LogView.Page {
          Env = env
          Log = kind
          Session = session
          Nav = nav
          Context = context
        }
      | ValueNone -> Status.soon "Sign in to read the log."
  )

let commitPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession & #IProvideUiClock & #IProvideLogging,
    nav: INavigable,
    context: RouteContext
  ) : Control =
  signedIn(
    env,
    "Sign in to read the thread.",
    fun session ->
      match context.GetParam<string> "commitId" with
      | ValueSome commitId ->
        CommitDetailView.Page {
          Env = env
          CommitId = commitId
          Session = session
          Nav = nav
          Context = context
          Quoting = false
        }
      | ValueNone -> Status.soon "Sign in to read the thread."
  )

/// The quote flow reuses the thread: the page renders over the quoted
/// commit, the composer below it armed in quote mode. The quoted
/// commit is the card the reader already sees.
let quotePage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession & #IProvideUiClock & #IProvideLogging,
    nav: INavigable,
    context: RouteContext
  ) : Control =
  signedIn(
    env,
    "Sign in to quote.",
    fun session ->
      match context.GetParam<string> "commitId" with
      | ValueSome commitId ->
        CommitDetailView.Page {
          Env = env
          CommitId = commitId
          Session = session
          Nav = nav
          Context = context
          Quoting = true
        }
      | ValueNone -> Status.soon "Sign in to quote."
  )

let signInPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession & #IProvideLogging,
    nav: INavigable,
    context: RouteContext
  ) : Control =
  SignInView.Page {
    Env = env
    Nav = nav
    OnDone = fun () -> go(env, nav, "/log/latest")
    Context = context
  }

let notificationsPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession & #IProvideUiClock & #IProvideLogging,
    nav: INavigable,
    context: RouteContext
  ) : Control =
  signedIn(
    env,
    "Sign in to read the inbox.",
    fun session ->
      NotificationsView.Page {
        Env = env
        Session = session
        Nav = nav
        Context = context
      }
  )

/// The direct messages: the inbox and the open thread side by side.
/// The page drives the store's polls itself; the env names only the
/// workers its search reads need.
let remotesPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp & #IProvideSession & #IProvideUiClock & #IProvideImageCache & #IProvideProfiles & #IProvideRemotes,
    nav: INavigable,
    context: RouteContext
  ) : Control =
  signedIn(
    env,
    "Sign in to read your remotes.",
    fun session ->
      RemotesView.Page {
        Env = env
        Session = session
        Nav = nav
        Context = context
      }
  )

let savedPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession & #IProvideUiClock & #IProvideLogging,
    nav: INavigable,
    context: RouteContext
  ) : Control =
  signedIn(
    env,
    "Sign in to read the saved list.",
    fun session ->
      SavedView.Page {
        Env = env
        Session = session
        Nav = nav
        Context = context
      }
  )

/// The scheduled queue: the private list of commits the composer's
/// Schedule action queued, one offset page at a time.
let scheduledPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp & #IProvideSession & #IProvideUiClock & #IProvideLogging,
    nav: INavigable,
    context: RouteContext
  ) : Control =
  signedIn(
    env,
    "Sign in to read the scheduled list.",
    fun session ->
      ScheduledView.Page {
        Env = env
        Session = session
        Nav = nav
        Context = context
      }
  )

let profilePage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession & #IProvideUiClock & #IProvideLogging,
    nav: INavigable,
    context: RouteContext
  ) : Control =
  signedIn(
    env,
    "Sign in to read the profile.",
    fun session ->
      // A username in the path opens that user's profile; the bare
      // path opens the viewer's own.
      let username =
        match context.GetParam<string> "username" with
        | ValueSome named -> named
        | ValueNone -> session.Session.User.Handle

      ProfileView.Page {
        Env = env
        Username = username
        Session = session
        Nav = nav
        Context = context
      }
  )

let settingsPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession & #IProvideSettings & #IProvideLogging,
    nav: INavigable,
    context: RouteContext
  ) : Control =
  signedIn(
    env,
    "Sign in to see the settings.",
    fun session ->
      SettingsView.Page {
        Env = env
        Session = session
        Nav = nav
        OnSignOut =
          fun () ->
            env.Session.Leave()
            go(env, nav, "/signin")
        Context = context
      }
  )

/// The term a route hands the page, unescaped: a path segment, not a
/// query — this router's matchers read only the segments they declare.
let termOf(context: RouteContext) : string =
  context.GetParam "term"
  |> ValueOption.map(fun (raw: string) -> Uri.UnescapeDataString raw)
  |> ValueOption.defaultValue ""

let searchPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp & #IProvideSession & #IProvideUiClock & #IProvideImageCache & #IProvideProfiles & #IProvideTimeline & #IProvideLogging & #IProvideRecentSearches,
    nav: INavigable,
    context: RouteContext
  ) : Control =
  signedIn(
    env,
    "Sign in to search.",
    fun session ->
      let log = env.LoggerFactory.CreateLogger "DevNull.Client.Views.Search"

      // The page's own session: its commits channel runs alone, the
      // way the page's submit does, and it dies with the route.
      let searches =
        new Search.Session(
          (fun term ->
            DevNull.Client.Core.Api.Search.users(
              env,
              {
                KeyFile = session.KeyFile
                Term = term
              }
            )),
          (fun term ->
            DevNull.Client.Core.Api.Search.commits(
              env,
              {
                KeyFile = session.KeyFile
                Term = term
                Limit = None
              }
            )),
          log
        )

      context.AddDisposable searches

      SearchView.Page {
        Env = env
        SignedIn = session
        Term = termOf context
        Recents = env.RecentSearches
        Searches = searches
        Nav = nav
        Context = context
      }
  )

let build
  (env:
    #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession & #IProvideUiClock & #IProvideImageCache & #IProvideProfiles & #IProvideTimeline & #IProvideSettings & #IProvideLogging & #IProvideRemotes & #IProvideRecentSearches)
  : RouterShell =
  let bootUrl =
    match env.Session.SignedIn with
    | ValueSome _ -> "/log/latest"
    | ValueNone -> "/signin"

  let inline gated route =
    route |> Route.canActivate(fun _ _ -> requiresSession env)

  let inline signedOut route =
    route |> Route.canActivate(fun _ _ -> requiresSignedOut env)

  let table: RouteDefinition<Control> list = [
    // The log page parks its scroll place and reply drafts in the
    // timeline service before it ends, so a thread and back returns to
    // the same reading place without a cached page holding memory.
    Route.define<Control>(
      "log",
      "/log/:log",
      fun context nav -> logPage(env, nav, context)
    )
    |> gated

    Route.define<Control>(
      "signin",
      "/signin",
      fun context nav -> signInPage(env, nav, context)
    )
    |> signedOut

    Route.define<Control>(
      "quote",
      "/quote/:commitId",
      fun context nav -> quotePage(env, nav, context)
    )
    |> gated

    Route.define<Control>(
      "commit",
      "/commit/:commitId",
      fun context nav -> commitPage(env, nav, context)
    )
    |> gated

    Route.define<Control>(
      "notifications",
      "/notifications",
      fun context nav -> notificationsPage(env, nav, context)
    )
    |> gated

    Route.define<Control>(
      "remotes",
      "/remotes",
      fun context nav -> remotesPage(env, nav, context)
    )
    |> gated

    Route.define<Control>(
      "saved",
      "/saved",
      fun context nav -> savedPage(env, nav, context)
    )
    |> gated

    Route.define<Control>(
      "scheduled",
      "/scheduled",
      fun context nav -> scheduledPage(env, nav, context)
    )
    |> gated

    Route.define<Control>(
      "profile",
      "/profile",
      fun context nav -> profilePage(env, nav, context)
    )
    |> gated

    Route.define<Control>(
      "user-profile",
      "/profile/:username",
      fun context nav -> profilePage(env, nav, context)
    )
    |> gated

    Route.define<Control>(
      "settings",
      "/settings",
      fun context nav -> settingsPage(env, nav, context)
    )
    |> gated

    // The search page: a bare path opens an empty box, a term in the
    // path opens the search for it. A query string cannot carry it —
    // this router matches only the segments a route declares.
    Route.define<Control>(
      "search",
      "/search",
      fun context nav -> searchPage(env, nav, context)
    )
    |> gated

    Route.define<Control>(
      "search-term",
      "/search/:term",
      fun context nav -> searchPage(env, nav, context)
    )
    |> gated

    // The about page carries no session: it states what the project is
    // and that it stands alone from the service.
    Route.define<Control>(
      "about",
      "/about",
      fun context _ ->
        AboutView.view(
          env.LoggerFactory.CreateLogger "DevNull.Client.Views.About"
        )
    )
  ]

  let router =
    AvaloniaRouter(table, logger = env.LoggerFactory.CreateLogger "Navs.Router")

  go(env, router, bootUrl)

  { Router = router; BootUrl = bootUrl }
