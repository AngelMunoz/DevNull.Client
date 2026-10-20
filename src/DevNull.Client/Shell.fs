module DevNull.Client.Shell

open System
open System.Reactive.Subjects
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Chrome
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Platform
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
open DevNull.Client.Tray
open DevNull.Client.UnreadPoller
open DevNull.Client.Views

/// The window width under which the sidebar folds into a drawer.
let narrowWidth = 640.0

/// One sidebar destination. `Badge` carries the live unread count the
/// entry renders, when it renders one.
[<Struct>]
type NavEntry = {
  Glyph: Glyph
  Label: string
  Section: string
  Url: string
  Badge: IObservable<int> voption
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

/// The count the notifications entry badges, from the session store.
let unreadCount(session: SessionStore) : IObservable<int> =
  session.Observe
  |> Observable.map(function
    | SignedIn entry -> entry.Session.UnreadNotificationsCount
    | SignedOut -> 0)

let navItem
  (
    router: IRouter<Control>,
    activeSection: BehaviorSubject<string voption>,
    entry: NavEntry
  ) : Control =
  let active =
    activeSection
    |> Observable.map(fun current -> current = ValueSome entry.Section)

  let label =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(10.0)
      .Children(Icons.icon entry.Glyph, TextBlock().Text(entry.Label))

  let content: Control =
    match entry.Badge with
    | ValueNone -> label
    | ValueSome unread ->
      let grid = Grid()

      grid.ColumnDefinitions <- ColumnDefinitions("Auto,*,Auto")

      let badge =
        Border()
          .Background(Brushes.accent)
          .CornerRadius(9.0)
          .Padding(6.0, 1.0, 6.0, 1.0)
          .VerticalAlignment(VerticalAlignment.Center)
          .IsVisible(unread |> Observable.map(fun count -> count > 0))
          .Child(
            TextBlock()
              .FontSize(11.0)
              .Foreground(Brushes.onAccent)
              .Text(unread |> Observable.map string)
          )

      grid.Children(label.Column(0), badge.Column(2)) |> ignore

      grid

  let item =
    Button()
      .Classes(Theme.name Theme.NavItem)
      .Content(content)
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

  if entry.Badge.IsSome then
    item.HorizontalContentAlignment <- HorizontalAlignment.Stretch

  item

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

/// The window band: a drag surface the drawn chrome answers, plus the
/// hamburger that opens the drawer under the narrow layout. The band
/// rides above every pane, so the whole top strip drags the window
/// while the hamburger stays clickable.
let band(narrow: IObservable<bool>, drawerOpen: BehaviorSubject<bool>) : Panel =
  let dragSurface =
    Border()
      .Background(Brushes.transparent)
      .Height(Theme.bandHeight)
      .VerticalAlignment(VerticalAlignment.Top)

  WindowDecorationProperties.SetElementRole(
    dragSurface,
    WindowDecorationsElementRole.TitleBar
  )

  let hamburger =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(10.0, 6.0, 10.0, 6.0)
      .Content(Icons.icon Glyph.Menu)
      .HorizontalAlignment(HorizontalAlignment.Left)
      .VerticalAlignment(VerticalAlignment.Top)
      .IsVisible(narrow)
      .OnClickHandler(fun _ _ -> drawerOpen.OnNext(not drawerOpen.Value))

  hamburger.Name <- "MenuButton"

  WindowDecorationProperties.SetElementRole(
    hamburger,
    WindowDecorationsElementRole.User
  )

  Panel().Children(dragSurface, hamburger)

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
    this.MinWidth <- 380.0
    this.MinHeight <- 640.0
    this.Background <- Brushes.background

    this.Icon <-
      WindowIcon(AssetLoader.Open(Uri "avares://DevNull.Client/Assets/app.ico"))

    applyWindowChrome this

    let routes = Routes.build env
    let router = routes.Router

    let activeSection =
      new BehaviorSubject<string voption>(Navigation.sectionOf routes.BootUrl)

    router.Changed
    |> Observable.map(fun snapshot ->
      snapshot.Route
      |> ValueOption.map _.Path
      |> ValueOption.bind Navigation.sectionOf)
    |> Observable.subscribe activeSection.OnNext
    |> ignore

    // The responsive state: narrow folds the sidebar into a drawer that
    // the band hamburger opens, and every navigation closes it again.
    let narrow = new BehaviorSubject<bool>(this.Width < narrowWidth)

    this.GetObservable(TopLevel.ClientSizeProperty)
    |> Observable.map(fun size -> size.Width < narrowWidth)
    |> Observable.add narrow.OnNext

    let drawerOpen = new BehaviorSubject<bool>(false)

    router.Changed |> Observable.add(fun _ -> drawerOpen.OnNext false)

    let sidebarVisible = new BehaviorSubject<bool>(true)

    let syncSidebar() =
      sidebarVisible.OnNext(not narrow.Value || drawerOpen.Value)

    narrow |> Observable.add(fun _ -> syncSidebar())

    drawerOpen |> Observable.add(fun _ -> syncSidebar())

    let unread = new BehaviorSubject<int>(0)

    unreadCount session |> Observable.add unread.OnNext

    let entries = [
      {
        Glyph = Glyph.Log
        Label = "Log"
        Section = "log"
        Url = "/log/latest"
        Badge = ValueNone
      }
      {
        Glyph = Glyph.Notifications
        Label = "Notifications"
        Section = "notifications"
        Url = "/notifications"
        Badge = ValueSome(unread :> IObservable<int>)
      }
      {
        Glyph = Glyph.Profile
        Label = "Profile"
        Section = "profile"
        Url = "/profile"
        Badge = ValueNone
      }
      {
        Glyph = Glyph.Saved
        Label = "Saved"
        Section = "saved"
        Url = "/saved"
        Badge = ValueNone
      }
      {
        Glyph = Glyph.Settings
        Label = "Settings"
        Section = "settings"
        Url = "/settings"
        Badge = ValueNone
      }
    ]

    let item entry = navItem(router, activeSection, entry)

    let header =
      StackPanel()
        .Margin(8.0, Theme.bandHeight + 8.0, 8.0, 20.0)
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
      Border()
        .Background(Brushes.surface)
        .Width(260.0)
        .IsVisible(sidebarVisible)
        .Child(sidebarHost)

    let outlet =
      RouterOutlet(Router = Some router)
        .Column(0)
        .ColumnSpan(2)
        .Margin(
          narrow
          |> Observable.map(fun isNarrow ->
            if isNarrow then
              Thickness(0.0)
            else
              Thickness(260.0, 0.0, 0.0, 0.0))
        )

    let root = Grid()

    root.ColumnDefinitions <- ColumnDefinitions("260,*")

    this.Content <-
      root.Children(
        outlet,
        sidebarPane,
        band(narrow, drawerOpen).Column(0).ColumnSpan(2)
      )

    // The inbox marks everything read as it lists, so the poller stops
    // while the inbox is on screen.
    let inboxOpen =
      new BehaviorSubject<bool>(activeSection.Value = ValueSome "notifications")

    activeSection
    |> Observable.map(fun section -> section = ValueSome "notifications")
    |> Observable.subscribe inboxOpen.OnNext
    |> ignore

    let poller = UnreadPoller.run(env, inboxOpen, 45_000)
    this.Closed.Add(fun _ -> poller.Dispose())

    Tray.install(this, router, unread)

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
