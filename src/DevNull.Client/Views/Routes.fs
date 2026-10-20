module DevNull.Client.Views.Routes

open Avalonia.Controls
open Navs
open Navs.Avalonia
open Serilog
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.DeviceAuth
open DevNull.Client.Env
open DevNull.Client.Stores
open DevNull.Client.Navigation
open DevNull.Client.Theme
open DevNull.Client.Views

let go(nav: INavigable, url: string) : unit =
  Navigation.go(
    nav,
    url,
    fun message ->
      Log.Error("Navigation to {Url} failed: {Error}", url, message)
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

let logPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp & #IProvideSession,
    nav: INavigable,
    context: RouteContext
  ) : Control =
  match env.Session.SignedIn, context.GetParam<string> "log" with
  | ValueSome session, ValueSome raw ->
    let kind = LogKinds.tryParse raw |> Option.defaultValue LogKind.Latest

    LogView.Page(
      {
        Env = env
        Log = kind
        Session = session
        Nav = nav
      }
    )
  | _ -> Status.soon "Sign in to read the log."

let commitPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession,
    nav: INavigable,
    context: RouteContext
  ) : Control =
  match env.Session.SignedIn, context.GetParam<string> "commitId" with
  | ValueSome session, ValueSome commitId ->
    CommitDetailView.Page(
      {
        Env = env
        CommitId = commitId
        Session = session
        Nav = nav
      }
    )
  | _ -> Status.soon "Sign in to read the thread."

let composePage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession,
    nav: INavigable,
    target: Composer.Target
  ) : Control =
  match env.Session.SignedIn with
  | ValueSome session ->
    ComposeView.Page(
      {
        Env = env
        Session = session
        Target = target
        Nav = nav
      }
    )
  | ValueNone -> Status.soon "Sign in to commit."

let quotePage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession,
    nav: INavigable,
    context: RouteContext
  ) : Control =
  match env.Session.SignedIn, context.GetParam<string> "commitId" with
  | ValueSome session, ValueSome commitId ->
    composePage(env, nav, Composer.Quoting commitId)
  | _ -> Status.soon "Sign in to quote."

let signInPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession,
    nav: INavigable
  ) : Control =
  SignInView.Page(
    {
      Env = env
      Nav = nav
      OnDone = fun () -> go(nav, "/log/latest")
    }
  )

let notificationsPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession,
    nav: INavigable
  ) : Control =
  match env.Session.SignedIn with
  | ValueSome session ->
    NotificationsView.Page(
      {
        Env = env
        Session = session
        Nav = nav
      }
    )
  | ValueNone -> Status.soon "Sign in to read the inbox."

let savedPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession,
    nav: INavigable
  ) : Control =
  match env.Session.SignedIn with
  | ValueSome session ->
    SavedView.Page(
      {
        Env = env
        Session = session
        Nav = nav
      }
    )
  | ValueNone -> Status.soon "Sign in to read the saved list."

let profilePage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession,
    nav: INavigable
  ) : Control =
  match env.Session.SignedIn with
  | ValueSome session ->
    ProfileView.Page(
      {
        Env = env
        Username = session.Session.User.Username
        Session = session
        Nav = nav
      }
    )
  | ValueNone -> Status.soon "Sign in to read the profile."

let settingsPage
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession,
    nav: INavigable
  ) : Control =
  match env.Session.SignedIn with
  | ValueSome session ->
    SettingsView.Page(
      {
        Env = env
        Session = session
        Nav = nav
        OnSignOut =
          fun () ->
            env.Session.Leave()
            go(nav, "/signin")
      }
    )
  | ValueNone -> Status.soon "Sign in to see the settings."

let build
  (env:
    #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvidePkceSource & #IProvideHttp & #IProvideSession & #IProvideLogging)
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
    Route.define<Control>(
      "log",
      "/log/:log",
      fun context nav -> logPage(env, nav, context)
    )
    |> gated

    Route.define<Control>(
      "signin",
      "/signin",
      fun _ nav -> signInPage(env, nav)
    )
    |> signedOut

    Route.define<Control>(
      "compose",
      "/compose",
      fun _ nav -> composePage(env, nav, Composer.Fresh)
    )
    |> gated

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
      fun _ nav -> notificationsPage(env, nav)
    )
    |> gated

    Route.define<Control>("saved", "/saved", fun _ nav -> savedPage(env, nav))
    |> gated

    Route.define<Control>(
      "profile",
      "/profile",
      fun _ nav -> profilePage(env, nav)
    )
    |> gated

    Route.define<Control>(
      "settings",
      "/settings",
      fun _ nav -> settingsPage(env, nav)
    )
    |> gated
  ]

  let router =
    AvaloniaRouter(table, logger = env.LoggerFactory.CreateLogger "Navs.Router")

  go(router, bootUrl)

  { Router = router; BootUrl = bootUrl }
