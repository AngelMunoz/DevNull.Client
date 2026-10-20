module DevNull.Client.Shell

open System
open System.Reactive.Subjects
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Chrome
open Avalonia.Controls.Documents
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
open DevNull.Client.Components
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.DeviceAuth
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Icons
open DevNull.Client.Navigation
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.Tray
open DevNull.Client.UnreadPoller
open DevNull.Client.Views

/// The window width under which the sidebar folds into a drawer.
let narrowWidth = 640.0

/// The window width from which the aside column opens, the site's
/// wide breakpoint.
let asideWidth = 1280.0

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

// The client surface fills the window behind the caption row. Only
// Windows answers the Mica chain; every backdrop follows the theme.
/// A page transition that swaps immediately. The outlet's default
/// cross-fade never finishes under a headless clock, which keeps the
/// previous page attached — and its buttons steal clicks from the
/// live one.
type ImmediatePageTransition() =

  interface Animation.IPageTransition with
    member _.Start(_, _, _, _) = Task.CompletedTask

let applyWindowChrome(window: Window) : unit =
  window.ExtendClientAreaToDecorationsHint <- true
  window.ExtendClientAreaTitleBarHeightHint <- 32.0

  // The app draws the caption row itself in the band. The drawn chrome
  // keeps only the border: its caption template paints an opaque strip
  // over the client, and 12.0.5 exposes no per-button control to drop
  // its fullscreen button, the one that traps the window.
  window.WindowDecorations <- WindowDecorations.BorderOnly

  match Application.Current with
  | null -> ()
  | app ->
    // A Mica miss falls back to the themed background, never to black.
    if OperatingSystem.IsWindows() then
      window.TransparencyLevelHint <-
        ResizeArray [
          WindowTransparencyLevel.Mica
          WindowTransparencyLevel.AcrylicBlur
          WindowTransparencyLevel.Transparent
        ]

      window.Bind(
        TopLevel.TransparencyBackgroundFallbackProperty,
        app.GetResourceObservable Keys.background
      )
      |> ignore

      window.Background <- Brushes.Transparent
    else
      window.Bind(
        Window.BackgroundProperty,
        app.GetResourceObservable Keys.background
      )
      |> ignore

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
      .OnClickHandler(fun _ _ -> go(router, entry.Url))

  // The active look lives in the NavItemActive class, whose colors are
  // themed resources; the subscription only moves the class.
  active
  |> Observable.add(fun flag ->
    if flag then
      item.Classes.Add(Theme.name Theme.NavItemActive)
    else
      item.Classes.Remove(Theme.name Theme.NavItemActive) |> ignore)

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

/// Everything the window band needs: the responsive state, the live
/// window state, and the caption actions the shell answers.
[<Struct>]
type BandProps = {
  Narrow: IObservable<bool>
  DrawerOpen: BehaviorSubject<bool>
  WindowState: IObservable<WindowState>
  Minimize: unit -> unit
  ToggleMaximize: unit -> unit
  Close: unit -> unit
}

/// One app-drawn caption button carrying its system element role.
let captionButton
  (role: WindowDecorationsElementRole, glyph: Glyph, onClick: Button -> unit)
  : Button =
  let button =
    Button().Classes(Theme.name Theme.CaptionButton).Content(Icons.icon glyph)

  WindowDecorationProperties.SetElementRole(button, role)
  button.OnClickHandler(fun sender _ -> onClick sender) |> ignore

  button

/// The window band: a drag surface the window answers, the hamburger
/// that opens the drawer under the narrow layout, and the caption row.
/// The band rides above every pane, so the whole top strip drags the
/// window while the buttons stay clickable.
let band(props: BandProps) : Panel =
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
      .IsVisible(props.Narrow)
      .OnClickHandler(fun _ _ ->
        props.DrawerOpen.OnNext(not props.DrawerOpen.Value))

  hamburger.Name <- "MenuButton"

  WindowDecorationProperties.SetElementRole(
    hamburger,
    WindowDecorationsElementRole.User
  )

  let maximizeGlyph(state: WindowState) =
    match state with
    | WindowState.Maximized -> Glyph.Restore
    | _ -> Glyph.Maximize

  let minimize =
    captionButton(
      WindowDecorationsElementRole.MinimizeButton,
      Glyph.Minimize,
      fun _ -> props.Minimize()
    )

  minimize.Name <- "MinimizeButton"

  let maximize =
    Button()
      .Classes(Theme.name Theme.CaptionButton)
      .Content(
        props.WindowState
        |> Observable.map(fun state -> Icons.icon(maximizeGlyph state))
        |> _.ToBinding()
      )
      .OnClickHandler(fun _ _ -> props.ToggleMaximize())

  WindowDecorationProperties.SetElementRole(
    maximize,
    WindowDecorationsElementRole.MaximizeButton
  )

  maximize.Name <- "MaximizeButton"

  let close =
    captionButton(
      WindowDecorationsElementRole.CloseButton,
      Glyph.Close,
      fun _ -> props.Close()
    )

  close.Name <- "CloseButton"

  let captionRow =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .VerticalAlignment(VerticalAlignment.Top)
      .HorizontalAlignment(HorizontalAlignment.Right)
      .IsVisible(
        props.WindowState
        |> Observable.map(fun state ->
          match state with
          | WindowState.FullScreen -> false
          | _ -> true)
      )
      .Children(minimize, maximize, close)

  captionRow.Name <- "CaptionRow"

  Panel().Children(dragSurface, hamburger, captionRow)

type Shell<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvidePkceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideSettings
  and 'env :> IProvideLogging>(env: 'env, callback: string option) as this =
  inherit Window()

  // The close bypass: `Quit` raises it, so the close-to-tray guard in
  // the Closing handler stands down.
  let quitRequested = new BehaviorSubject<bool>(false)

  do
    let session = (env :> IProvideSession).Session

    this.Title <- "/dev/null"
    this.Width <- 1080.0
    this.Height <- 720.0
    this.MinWidth <- 380.0
    this.MinHeight <- 640.0

    this.Icon <-
      WindowIcon(AssetLoader.Open(Uri "avares://DevNull.Client/Assets/app.ico"))

    // The type face of the app rides the inherited font property; the
    // themed resources carry the template fonts.
    TextElement.SetFontFamily(this, Fonts.sans)

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
    // Wide enough windows open the aside column, the way the site does
    // past its wide breakpoint.
    let narrow = new BehaviorSubject<bool>(this.Width < narrowWidth)

    let wide = new BehaviorSubject<bool>(this.Width >= asideWidth)

    this.GetObservable(TopLevel.ClientSizeProperty)
    |> Observable.add(fun size ->
      narrow.OnNext(size.Width < narrowWidth)
      wide.OnNext(size.Width >= asideWidth))

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

    // The lowercase brand: the second slash wears the lane color, the
    // wordmark's outlined slash.
    let wordmark =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Children(
          TextBlock().Classes(Theme.name Theme.Wordmark).Text("/dev"),
          TextBlock()
            .Classes(Theme.name Theme.Wordmark)
            .Foreground(Brushes.lane 0)
            .Text("/"),
          TextBlock().Classes(Theme.name Theme.Wordmark).Text("null")
        )

    // Wide layout: the sidebar owns the top-left corner. Narrow: the
    // drawer header clears the hamburger the band overlays.
    let header =
      StackPanel()
        .Margin(
          narrow
          |> Observable.map(fun isNarrow ->
            if isNarrow then
              Thickness(8.0, Theme.bandHeight + 8.0, 8.0, 20.0)
            else
              Thickness(8.0, 8.0, 8.0, 20.0))
        )
        .Children(
          wordmark,
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

    // The aside: the signed-in user's card, the way the site's right
    // column does past its wide breakpoint. It rebuilds on every
    // sign-in state change.
    let asideHost = ContentControl()

    asideHost.Name <- "AsideHost"

    session.Observe
    |> Observable.add(fun state ->
      match state with
      | SignedIn entry ->
        asideHost.Content <-
          ProfileCard.Page { Env = env; Session = entry } :> Control
      | SignedOut -> asideHost.Content <- null)

    let aside =
      ScrollViewer()
        .IsVisible(wide)
        .Padding(Thickness(12.0, Theme.bandHeight + 8.0, 12.0, 0.0))
        .Content(asideHost)

    let outlet =
      RouterOutlet(Router = Some router)
        .Column(0)
        .ColumnSpan(2)
        .Margin(
          this.GetObservable(TopLevel.ClientSizeProperty)
          |> Observable.map(fun size ->
            if size.Width < narrowWidth then
              Thickness(0.0)
            elif size.Width >= asideWidth then
              Thickness(260.0, 0.0, 320.0, 0.0)
            else
              Thickness(260.0, 0.0, 0.0, 0.0))
        )

    // The outlet ships a 150ms cross-fade; swap immediately instead.
    outlet.PageTransition <-
      ImmediatePageTransition() :> Animation.IPageTransition

    let root = Grid()

    root.ColumnDefinitions <- ColumnDefinitions("260,*,320")

    let windowState = this.GetObservable(Window.WindowStateProperty)

    let bandPanel =
      band {
        Narrow = narrow :> IObservable<bool>
        DrawerOpen = drawerOpen
        WindowState = windowState
        Minimize = fun () -> this.WindowState <- WindowState.Minimized
        ToggleMaximize =
          fun () ->
            match this.WindowState with
            | WindowState.Maximized -> this.WindowState <- WindowState.Normal
            | _ -> this.WindowState <- WindowState.Maximized
        Close = fun () -> this.Close()
      }

    this.Content <-
      root.Children(
        outlet,
        sidebarPane,
        aside.Column(2),
        bandPanel.Column(0).ColumnSpan(3)
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

    // Close-to-tray: the flag turns a close into a hide, so the tray
    // and the poller keep the app alive. `Quit` marks the bypass first.
    let settings = (env :> IProvideSettings).Settings

    // The theme choice follows the store: system at boot, then
    // whatever the settings page pins.
    settings.Observe |> Observable.add(fun value -> Theme.apply value.Theme)

    this.Closing.Add(fun args ->
      if not quitRequested.Value && settings.Current.CloseToTray then
        args.Cancel <- true
        this.Hide())

    Tray.install {
      Window = this
      Router = router
      Unread = unread :> IObservable<int>
      Quit =
        fun () ->
          quitRequested.OnNext true
          this.Close()
    }

    // F11 walks the window in and out of fullscreen; Esc only leaves
    // it. The band hides its caption row while fullscreen holds.
    this.KeyDown.Add(fun args ->
      match args.Key with
      | Key.F11 -> this.ToggleFullScreen()
      | Key.Escape -> this.ExitFullScreen()
      | _ -> ())

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

  /// Walks the window in and out of fullscreen.
  member this.ToggleFullScreen() : unit =
    match this.WindowState with
    | WindowState.FullScreen -> this.WindowState <- WindowState.Normal
    | _ -> this.WindowState <- WindowState.FullScreen

  /// Leaves fullscreen when it holds; other states stay as they are.
  member this.ExitFullScreen() : unit =
    match this.WindowState with
    | WindowState.FullScreen -> this.WindowState <- WindowState.Normal
    | _ -> ()

  /// Closes for real: the close-to-tray flag never hides this path.
  member this.Quit() : unit =
    quitRequested.OnNext true
    this.Close()
