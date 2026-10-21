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
open Avalonia.Threading
open Avalonia.VisualTree
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

/// The widest the whole shell strip grows, the site's 1264px app row:
/// past it the strip centers, so the timeline keeps the window's
/// middle instead of hugging the sidebar.
let stripWidth = 1264.0

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

/// The sidebar footer: the signed-in user's row, or the sign-in entry.
/// The account row rebuilds on every sign-in state change, the way the
/// shell's aside does.
let footer
  (router: IRouter<Control>, session: IObservable<SessionState>)
  : Control =
  let signedIn =
    session
    |> Observable.map(function
      | SignedIn _ -> true
      | SignedOut -> false)

  let host = ContentControl()

  session
  |> Observable.add(fun state ->
    match state with
    | SignedIn entry ->
      host.Content <-
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(12.0)
          .Children(
            Avatar.view {
              Name = entry.Session.User.Name
              Url = entry.Session.User.AvatarUrl
              Lane = None
              Size = 36.0
            },
            StackPanel()
              .VerticalAlignment(VerticalAlignment.Center)
              .Children(
                TextBlock()
                  .Classes(Theme.name Theme.Subtitle)
                  .Text(entry.Session.User.Name),
                TextBlock()
                  .Classes(Theme.name Theme.Mono)
                  .Text("@" + entry.Session.User.Username)
              )
          )
    | SignedOut -> host.Content <- null)

  let account =
    Button()
      .Classes(Theme.name Theme.NavItem)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Content(host)
      .IsVisible(signedIn)
      .OnClickHandler(fun _ _ -> go(router, "/profile"))

  let signIn =
    Button()
      .Classes(Theme.name Theme.NavButton)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Content("Sign in")
      .IsVisible(signedIn |> Observable.map not)
      .OnClickHandler(fun _ _ -> go(router, "/signin"))

  StackPanel().Children(account, signIn)

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

/// The commit dialog the site's Commit button opens: the composer on a
/// card over a scrim. A commit, a scrim click, or Esc closes it.
let composeDialog
  (
    env: #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp,
    session: SignedInSession,
    close: unit -> unit
  ) : Control =
  let card =
    Border()
      .Classes(Theme.name Theme.Card)
      .MaxWidth(560.0)
      .Margin(24.0, Theme.bandHeight + 24.0, 24.0, 24.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Center)
      .Child(
        StackPanel()
          .Spacing(8.0)
          .Children(
            TextBlock().Classes(Theme.name Theme.Title).Text("Write a commit"),
            Composer.Page {
              Env = env
              Session = session
              Target = Composer.Fresh
              OnCommitted = fun _ -> close()
            }
          )
      )

  card.Name <- "ComposeDialog"

  card.AttachedToVisualTree.Add(fun _ ->
    // The program builds the box a layout pass later; the focus lands
    // once it exists.
    Dispatcher.UIThread.Post(fun () ->
      card.GetVisualDescendants()
      |> Seq.tryFind(fun visual -> visual.Name = "ComposerBody")
      |> Option.iter(fun visual -> (visual :?> TextBox).Focus() |> ignore)))

  card

/// Focuses the composer on screen, when one is. The site's Commit
/// button focuses the log's inline composer instead of opening the
/// dialog over it.
let focusInlineComposer(window: Window) : bool =
  match
    window.GetVisualDescendants()
    |> Seq.tryFind(fun visual -> visual.Name = "ComposerBody")
  with
  | Some(:? TextBox as box) ->
    box.Focus() |> ignore
    true
  | _ -> false

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

    // The type face and the body size ride the inherited font
    // properties; the themed resources carry the template fonts.
    TextElement.SetFontFamily(this, Fonts.sans)
    TextElement.SetFontSize(this, 15.0)

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

    // The drawer layer shows only while narrow and open. The sidebar
    // column itself leaves the layout below the narrow line, so the
    // timeline takes the full width; wide windows keep the aside, the
    // way the site does past its wide breakpoint.
    let drawerShown = new BehaviorSubject<bool>(false)

    let syncDrawer() =
      drawerShown.OnNext(narrow.Value && drawerOpen.Value)

    narrow |> Observable.add(fun _ -> syncDrawer())

    drawerOpen |> Observable.add(fun _ -> syncDrawer())

    let unread = new BehaviorSubject<int>(0)

    unreadCount session |> Observable.add unread.OnNext

    // The compose dialog: one overlay above the panes, below the band
    // so the caption stays reachable. It opens only signed in.
    let composeOpen = new BehaviorSubject<bool>(false)

    let composeHost =
      ContentControl()
        .HorizontalContentAlignment(HorizontalAlignment.Stretch)
        .VerticalContentAlignment(VerticalAlignment.Stretch)

    let closeCompose() = composeOpen.OnNext false

    composeOpen
    |> Observable.add(fun isOpen ->
      if isOpen then
        match session.Current with
        | SignedIn entry ->
          composeHost.Content <- composeDialog(env, entry, closeCompose)
        | SignedOut -> closeCompose()
      else
        composeHost.Content <- null)

    let composeScrim =
      Border()
        .Background(Brushes.solid "#8A000000")
        .OnPointerPressedHandler(fun _ _ -> closeCompose())

    let composeLayer =
      Panel()
        .IsVisible(composeOpen :> IObservable<bool>)
        .Children(composeScrim, composeHost)

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

    // The sidebar, built fresh per host: the pane keeps one tree and
    // the narrow drawer another, so no control lands in two parents.
    let sidebarContent() : Control =
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

      // The header clears the drag band the shell lays over the top
      // strip; the site's own py-6 keeps the same rhythm below it.
      let header =
        StackPanel()
          .Margin(Thickness(14.0, Theme.bandHeight + 12.0, 14.0, 24.0))
          .Children(
            wordmark,
            TextBlock()
              .Classes(Theme.name Theme.Muted)
              .Text("a social network for devs")
          )

      let commitButton =
        Button()
          .Classes(Theme.name Theme.NavCommit)
          .Margin(0.0, 8.0, 0.0, 8.0)
          .Content(
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(12.0)
              .Children(Icons.icon Glyph.Commit, TextBlock().Text("Commit"))
          )
          .OnClickHandler(fun _ _ ->
            // The site focuses the log's own composer when one is on
            // screen, and opens the dialog everywhere else.
            if
              activeSection.Value <> ValueSome "log"
              || not(focusInlineComposer this)
            then
              composeOpen.OnNext true)

      commitButton.Name <- "NavCommitButton"

      let navChildren: Control array = [|
        header
        for entry in entries do
          item entry
        commitButton
      |]

      let navList = ScrollViewer().Content(StackPanel().Children(navChildren))

      DockPanel()
        .LastChildFill(true)
        .Children(
          footer(router, session.Observe)
            .DockBottom()
            .Margin(Thickness(14.0, 8.0, 14.0, 12.0)),
          navList
        )

    let sidebarPane = Border().Width(260.0).Child(sidebarContent())

    // The aside: the signed-in user's card, the way the site's right
    // column does past its wide breakpoint. It rebuilds on every
    // sign-in state change.
    let asideHost = ContentControl()

    asideHost.Name <- "AsideHost"

    session.Observe
    |> Observable.add(fun state ->
      match state with
      | SignedIn entry ->
        asideHost.Content <- ProfileCard.Page { Env = env; Session = entry }
      | SignedOut -> asideHost.Content <- null)

    let aside =
      ScrollViewer()
        .Width(320.0)
        .IsVisible(wide)
        .Padding(Thickness(24.0, Theme.bandHeight + 24.0, 24.0, 24.0))
        .Content(asideHost)

    let outlet = RouterOutlet(Router = Some router)

    // The outlet ships a 150ms cross-fade; swap immediately instead.
    outlet.PageTransition <-
      ImmediatePageTransition() :> Animation.IPageTransition

    // The reading column: the site's main — a card surface between two
    // hairlines, never wider than the site's 620px column. Where the
    // window leaves room, the column centers in it.
    let mainHost =
      Border()
        .Background(Brushes.card)
        .BorderBrush(Brushes.border)
        .BorderThickness(Thickness(1.0, 0.0, 1.0, 0.0))
        .MaxWidth(620.0)
        .Child(outlet)

    // The shell: sidebar, reading column, aside — one strip that never
    // grows past the site's app row. Stretch with the cap fills the
    // window and the clamp centers the strip past it; a centered
    // alignment here would shrink the strip to its content instead.
    // The side columns are Auto, so a hidden sidebar or aside leaves
    // no reserved strip.
    let shell =
      let grid = Grid()

      grid.ColumnDefinitions <- ColumnDefinitions("Auto,*,Auto")
      grid.MaxWidth <- stripWidth

      grid.Children(sidebarPane.Column(0), mainHost.Column(1), aside.Column(2))

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

    // The narrow drawer: the sidebar's content over a scrim, under the
    // band so the hamburger stays reachable.
    let scrim =
      Border()
        .Background(Brushes.solid "#8A000000")
        .OnPointerPressedHandler(fun _ _ -> drawerOpen.OnNext false)

    let drawerPane =
      Border()
        .Background(Brushes.surface)
        .BorderBrush(Brushes.border)
        .BorderThickness(Thickness(0.0, 0.0, 1.0, 0.0))
        .Width(260.0)
        .HorizontalAlignment(HorizontalAlignment.Left)
        .Margin(0.0, Theme.bandHeight, 0.0, 0.0)

    let drawerLayer =
      Panel()
        .IsVisible(drawerShown :> IObservable<bool>)
        .Children(scrim, drawerPane)

    // One sidebar tree, re-parented: docked in the shell while wide,
    // riding the drawer layer while narrow. The drawer's own visibility
    // drives the narrow mode, so no second copy of the nav exists.
    let placeSidebar(isNarrow: bool) =
      if isNarrow then
        shell.Children.Remove(sidebarPane) |> ignore
        drawerPane.Child <- sidebarPane
      else
        drawerPane.Child <- null
        drawerOpen.OnNext false

        if not(shell.Children.Contains sidebarPane) then
          shell.Children.Add sidebarPane

    narrow |> Observable.add placeSidebar

    this.Content <-
      Panel().Children(shell, drawerLayer, composeLayer, bandPanel)

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

    // F11 walks the window in and out of fullscreen; Esc leaves it, but
    // never past an open compose dialog. The band hides its caption row
    // while fullscreen holds.
    this.KeyDown.Add(fun args ->
      match args.Key with
      | Key.F11 -> this.ToggleFullScreen()
      | Key.Escape ->
        if composeOpen.Value then
          closeCompose()
        else
          this.ExitFullScreen()
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
