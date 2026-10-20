module DevNull.Client.Shell

open System
open System.Reactive.Subjects
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open IcedTasks
open Navs
open Navs.Avalonia
open Serilog
open DevNull.Client
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.DeviceAuth
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Icons
open DevNull.Client.Navigation
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.Views

/// One sidebar destination.
[<Struct>]
type NavEntry = {
  Glyph: Glyph
  Label: string
  Section: string
  Url: string
}

let inline go(router: IRouter<Control>, url: string) : unit =
  Navigation.go(
    router,
    url,
    fun message ->
      Log.Error("Navigation to {Url} failed: {Error}", url, message)
  )

// The client surface fills the window behind the system caption. Only
// Windows answers the Mica chain; the opaque theme background stays
// elsewhere.
let applyWindowChrome(window: Window) : unit =
  window.ExtendClientAreaToDecorationsHint <- true
  window.ExtendClientAreaTitleBarHeightHint <- 32.0

  if OperatingSystem.IsWindows() then
    window.TransparencyLevelHint <-
      ResizeArray [
        WindowTransparencyLevel.Mica
        WindowTransparencyLevel.AcrylicBlur
        WindowTransparencyLevel.Transparent
      ]

    window.Background <- Brushes.transparent

let navItem
  (
    router: IRouter<Control>,
    activeSection: BehaviorSubject<string voption>,
    entry: NavEntry
  ) : Control =
  let active =
    activeSection
    |> Observable.map(fun current -> current = ValueSome entry.Section)

  Button()
    .Classes(Theme.name Theme.NavItem)
    .Content(
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(10.0)
        .Children(Icons.icon entry.Glyph, TextBlock().Text(entry.Label))
    )
    .Background(
      active
      |> Observable.map(fun flag ->
        if flag then Brushes.card :> IBrush else Brushes.transparent)
    )
    .Foreground(
      active
      |> Observable.map(fun flag ->
        if flag then Brushes.text else Brushes.muted)
    )
    .OnClickHandler(fun _ _ -> go(router, entry.Url))

let footer
  (session: IObservable<SessionState>, router: IRouter<Control>)
  : Control =
  let signedIn =
    session
    |> Observable.map(function
      | SignedIn _ -> true
      | SignedOut -> false)

  let signedOut =
    session
    |> Observable.map(function
      | SignedIn _ -> false
      | SignedOut -> true)

  let name =
    session
    |> Observable.map(function
      | SignedIn entry -> entry.Session.User.Name
      | SignedOut -> "")

  let handle =
    session
    |> Observable.map(function
      | SignedIn entry -> "@" + entry.Session.User.Username
      | SignedOut -> "")

  let signedInFooter =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(10.0)
      .IsVisible(signedIn)
      .Children(
        TextBlock().Classes(Theme.name Theme.Subtitle).Text(name),
        TextBlock().Classes(Theme.name Theme.Muted).Text(handle)
      )

  let signedOutFooter =
    Button()
      .Classes(Theme.name Theme.NavButton)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Content("Sign in")
      .IsVisible(signedOut)
      .OnClickHandler(fun _ _ -> go(router, "/signin"))

  StackPanel().Margin(12.0).Children(signedInFooter, signedOutFooter)

type Shell<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvidePkceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideLogging>(env: 'env, callback: string option) as this =
  inherit Window()

  do
    let session = (env :> IProvideSession).Session

    this.Title <- "/dev/null"
    this.Width <- 1080.0
    this.Height <- 720.0
    this.MinWidth <- 860.0
    this.Background <- Brushes.background
    applyWindowChrome this

    let routes = Routes.build env
    let router = routes.Router

    let outlet = RouterOutlet(Router = Some router)

    let activeSection =
      new BehaviorSubject<string voption>(Navigation.sectionOf routes.BootUrl)

    router.Changed
    |> Observable.map(fun snapshot ->
      snapshot.Route
      |> ValueOption.map _.Path
      |> ValueOption.bind Navigation.sectionOf)
    |> Observable.subscribe activeSection.OnNext
    |> ignore

    let entries = [
      {
        Glyph = Glyph.Log
        Label = "Log"
        Section = "log"
        Url = "/log/latest"
      }
      {
        Glyph = Glyph.Notifications
        Label = "Notifications"
        Section = "notifications"
        Url = "/notifications"
      }
      {
        Glyph = Glyph.Profile
        Label = "Profile"
        Section = "profile"
        Url = "/profile"
      }
      {
        Glyph = Glyph.Saved
        Label = "Saved"
        Section = "saved"
        Url = "/saved"
      }
      {
        Glyph = Glyph.Settings
        Label = "Settings"
        Section = "settings"
        Url = "/settings"
      }
    ]

    let item entry = navItem(router, activeSection, entry)

    let header =
      StackPanel()
        .Margin(8.0, 8.0, 8.0, 20.0)
        .Children(
          TextBlock().Classes(Theme.name Theme.Title).Text("// /dev/null"),
          TextBlock()
            .Classes(Theme.name Theme.Muted)
            .Text("a social network for devs")
        )

    let remotes =
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Content("Remotes")
        .IsEnabled(false)

    let navChildren: Control array = [|
      header
      for entry in entries do
        item entry
      remotes
    |]

    let navList = ScrollViewer().Content(StackPanel().Children(navChildren))

    let commitButton =
      Button()
        .Classes(Theme.name Theme.AccentButton)
        .Margin(8.0)
        .HorizontalContentAlignment(HorizontalAlignment.Center)
        .Content(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .Children(Icons.icon Glyph.Commit, TextBlock().Text("Commit"))
        )
        .OnClickHandler(fun _ _ -> go(router, "/compose"))

    let sidebarHost =
      DockPanel()
        .LastChildFill(true)
        .Children(
          commitButton.DockBottom(),
          footer(session.Observe, router).DockBottom(),
          navList
        )

    let sidebarPane =
      Border().Background(Brushes.surface).Width(260.0).Child(sidebarHost)

    let root = Grid()

    root.ColumnDefinitions <- ColumnDefinitions("260,*")

    this.Content <- root.Children(sidebarPane.Column(0), outlet.Column(1))

    match callback with
    | Some uri ->
      asyncEx {
        let! keyOutcome = Task.Run(fun () -> ensureKey())

        match keyOutcome with
        | Error message -> saveLastError message
        | Ok key ->
          let! result = complete(env, key, uri)

          match result with
          | Ok _ -> ()
          | Error error -> saveLastError(describe error)

        go(router, "/signin")
      }
      |> Async.StartImmediate
    | None -> ()
