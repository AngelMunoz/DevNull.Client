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
open FSharp.Control.Reactive
open Navs
open Navs.Avalonia
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.DeviceAuth
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.Navigation
open DevNull.Client.Profiles
open DevNull.Client.SessionFiles
open DevNull.Client.SettingsStore
open DevNull.Client.Shortcuts
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.Timeline
open DevNull.Client.RouteStore
open DevNull.Client.Tray
open DevNull.Client.Unread
open DevNull.Client.UiClock
open DevNull.Client.Views

/// The window width under which the sidebar folds into a drawer, the
/// site's `md` breakpoint.
let narrowWidth = 768.0

/// The window width from which the sidebar can show its labels, the
/// site's `lg` breakpoint.
let toggleWidth = 1024.0

/// The window width from which the aside column opens, the site's
/// wide breakpoint.
let asideWidth = 1280.0

/// The widest the whole shell strip grows, the site's 1264px app row:
/// past it the strip centers, so the timeline keeps the window's
/// middle instead of hugging the sidebar.
let stripWidth = 1264.0

/// The four width bands of the shell: the folded drawer, the forced
/// icon rail, the collapsible sidebar without the aside, and the full
/// strip with the aside.
type Layout =
  | Narrow
  | Rail
  | Standard
  | Wide

let layoutOf(width: float) : Layout =
  if width < narrowWidth then Narrow
  elif width < toggleWidth then Rail
  elif width >= asideWidth then Wide
  else Standard

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

// The client surface fills the window behind the caption row. The
// window paints the themed background to its edges: a transparency hint
// left the resize border transparent, and the desktop showed through
// beside the rail.
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
  | _ ->
    window.TransparencyLevelHint <- ResizeArray [ WindowTransparencyLevel.None ]

    window.Background(Brushes.themed Keys.background) |> ignore

/// What one sidebar entry reads: where it goes, which section is
/// active, and whether the labels are hidden.
[<Struct>]
type NavContext = {
  Navigate: string -> unit
  Active: BehaviorSubject<string voption>
  Compact: IObservable<bool>
}

let navItem(context: NavContext, entry: NavEntry) : Control =
  let active =
    context.Active
    |> Observable.map(fun current -> current = ValueSome entry.Section)

  // One icon for both widths. The unread count rides its top corner as
  // a small pill, the way the site's badge sits on the icon.
  let icon: Control =
    match entry.Badge with
    | ValueNone -> Icons.icon entry.Glyph
    | ValueSome unread ->
      let badge =
        Border()
          .Background(Brushes.accent)
          .CornerRadius(8.0)
          .MinWidth(16.0)
          .Height(16.0)
          .Padding(4.0, 0.0, 4.0, 0.0)
          .HorizontalAlignment(HorizontalAlignment.Right)
          .VerticalAlignment(VerticalAlignment.Top)
          .Margin(0.0, -4.0, -6.0, 0.0)
          .IsVisible(unread |> Observable.map(fun count -> count > 0))
          .Child(
            TextBlock()
              .FontSize(10.0)
              .Foreground(Brushes.onAccent)
              .Text(
                unread
                |> Observable.map(fun count ->
                  if count > 99 then "99+" else string count)
              )
          )

      Panel().Width(18.0).Height(18.0).Children(Icons.icon entry.Glyph, badge)

  let label =
    TextBlock()
      .Text(entry.Label)
      .VerticalAlignment(VerticalAlignment.Center)
      .IsVisible(context.Compact |> Observable.map not)

  let content =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(10.0)
      .Children(icon, label)

  let item =
    Button()
      .Classes(Theme.name Theme.NavItem)
      .Content(content)
      .OnClickHandler(fun _ _ -> context.Navigate entry.Url)

  ToolTip.SetTip(item, entry.Label)

  // The active look lives in the NavItemActive class, whose colors are
  // themed resources; the subscription only moves the class.
  Theme.bindClass(item, Theme.NavItemActive, active)
  Theme.bindClass(item, Theme.RailItem, context.Compact)

  item

/// What the sidebar footer reads: where it goes, the session, the
/// avatar cache, and whether the labels are hidden.
[<Struct>]
type FooterContext = {
  Navigate: string -> unit
  Session: IObservable<SessionState>
  Images: ImageCacheService
  Compact: IObservable<bool>
}

/// The sidebar footer: the signed-in user's row, or the sign-in entry.
/// The account row rebuilds only when the signed-in user changes — an
/// unread refresh keeps the built one.
let footer(context: FooterContext) : Control =
  let signedIn =
    context.Session
    |> Observable.map(function
      | SignedIn _ -> true
      | SignedOut -> false)

  let host = ContentControl()

  // The identity cell, the way the chrome's guard works: a poll's
  // session refresh lands here without rebuilding the row.
  let account = new BehaviorSubject<string>("")

  context.Session
  |> Observable.add(fun state ->
    match state with
    | SignedIn entry when account.Value <> entry.Session.User.Id ->
      account.OnNext entry.Session.User.Id

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
              Cache = context.Images
            },
            StackPanel()
              .VerticalAlignment(VerticalAlignment.Center)
              .IsVisible(context.Compact |> Observable.map not)
              .Children(
                TextBlock()
                  .Classes(Theme.name Theme.Subtitle)
                  .Text(entry.Session.User.Name),
                TextBlock()
                  .Classes(Theme.name Theme.Mono)
                  .Text("@" + entry.Session.User.Username)
              )
          )
    | SignedIn _ -> ()
    | SignedOut ->
      account.OnNext ""
      host.Content <- null)

  let account =
    Button()
      .Classes(Theme.name Theme.NavItem)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Content(host)
      .IsVisible(signedIn)
      .OnClickHandler(fun _ _ -> context.Navigate "/profile")

  account.Name <- "SidebarAccount"
  Theme.bindClass(account, Theme.RailItem, context.Compact)

  let signIn =
    Button()
      .Classes(Theme.name Theme.NavButton)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Content("Sign in")
      .IsVisible(signedIn |> Observable.map not)
      .OnClickHandler(fun _ _ -> context.Navigate "/signin")

  Theme.bindClass(signIn, Theme.RailItem, context.Compact)

  StackPanel().Children(account, signIn)

/// Everything the window band needs: the responsive state, the live
/// window state, and the caption actions the shell answers.
[<Struct>]
type BandProps = {
  Narrow: IObservable<bool>
  ToggleDrawer: unit -> unit
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
      .OnClickHandler(fun _ _ -> props.ToggleDrawer())

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
              Register = ValueNone
              Draft = ValueNone
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
/// dialog over it. A hidden composer — the shared chrome off the log —
/// never takes the focus.
let focusInlineComposer(window: Window) : bool =
  let onScreen(visual: Visual) =
    match visual with
    | :? Control as control ->
      control.IsVisible
      && (control.GetVisualAncestors()
          |> Seq.forall(fun ancestor ->
            match ancestor with
            | :? Control as parent -> parent.IsVisible
            | _ -> true))
    | _ -> false

  match
    window.GetVisualDescendants()
    |> Seq.tryFind(fun visual ->
      visual.Name = "ComposerBody" && onScreen visual)
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
  and 'env :> IProvideLogging
  and 'env :> IProvideUnread
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLastVisit
  and 'env :> IProvideProfiles
  and 'env :> IProvideTimeline
  and 'env :> IProvideRoutes
  and 'env :> IProvideSshKeys>(env: 'env, callback: string option) as this =
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
      WindowIcon(
        AssetLoader.Open(Uri "avares://DevNull.Client.UI/Assets/app.ico")
      )

    // The type face and the body size ride the inherited font
    // properties; the themed resources carry the template fonts.
    TextElement.SetFontFamily(this, Fonts.sans)
    TextElement.SetFontSize(this, 15.0)

    applyWindowChrome this

    let routes = Routes.build env
    let router = routes.Router

    let activeSection =
      new BehaviorSubject<string voption>(Navigation.sectionOf routes.BootUrl)

    let navigate(url: string) = Routes.go(env, router, url)

    router.Changed
    |> Observable.map(fun snapshot ->
      snapshot.Route
      |> ValueOption.map _.Path
      |> ValueOption.bind Navigation.sectionOf)
    |> Observable.subscribe activeSection.OnNext
    |> ignore

    // One responsive value fed from the client size; narrow and wide
    // are projections of it, so the drawer and the aside cannot
    // disagree.
    let layout = new BehaviorSubject<Layout>(layoutOf this.Width)

    let narrow = layout |> Observable.map(fun current -> current = Narrow)

    let wide = layout |> Observable.map(fun current -> current = Wide)

    this.GetObservable(TopLevel.ClientSizeProperty)
    |> Observable.add(fun size -> layout.OnNext(layoutOf size.Width))

    // The sidebar's own width choice. The site stores it in a cookie;
    // the client stores it in the settings file. The choice applies
    // only from the label breakpoint up: the rail band and the drawer
    // decide their width themselves.
    let settings = (env :> IProvideSettings).Settings
    let expanded = new BehaviorSubject<bool>(settings.Current.SidebarExpanded)
    let compact = new BehaviorSubject<bool>(false)

    let updateCompact() =
      let next =
        match layout.Value with
        | Narrow -> false
        | Rail -> true
        | Standard
        | Wide -> not expanded.Value

      if compact.Value <> next then
        compact.OnNext next

    let toggleSidebar() =
      let next = not expanded.Value
      settings.SetSidebarExpanded next
      expanded.OnNext next

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

    // The bindings list: the same overlay shape as the compose dialog,
    // built on open and released on close.
    let helpOpen = new BehaviorSubject<bool>(false)

    let helpHost =
      ContentControl()
        .HorizontalContentAlignment(HorizontalAlignment.Stretch)
        .VerticalContentAlignment(VerticalAlignment.Stretch)

    let closeHelp() = helpOpen.OnNext false

    helpOpen
    |> Observable.add(fun isOpen ->
      if isOpen then
        helpHost.Content <- Shortcuts.helpCard()
      else
        helpHost.Content <- null)

    let helpScrim =
      Border()
        .Background(Brushes.solid "#8A000000")
        .OnPointerPressedHandler(fun _ _ -> closeHelp())

    let helpLayer =
      Panel()
        .IsVisible(helpOpen :> IObservable<bool>)
        .Children(helpScrim, helpHost)

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
        Badge = ValueSome(env.Unread.Count)
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
      {
        Glyph = Glyph.Info
        Label = "About"
        Section = "settings"
        Url = "/about"
        Badge = ValueNone
      }
    ]

    let item entry =
      navItem(
        {
          Navigate = navigate
          Active = activeSection
          Compact = compact
        },
        entry
      )

    // The sidebar: one tree for both modes, so no control lands in two
    // parents.
    let sidebarContent() : Control =
      // The service's name as plain text, the one form the brand asks
      // for: lowercase, both slashes. No retyped wordmark and no
      // copied lockup; this is an unofficial client. The rail keeps
      // the first form of the name, because the full one cannot fit.
      let wordmark =
        TextBlock()
          .Classes(Theme.name Theme.Title)
          .Text("/dev/null")
          .IsVisible(compact |> Observable.map not)

      let mark =
        TextBlock()
          .Classes(Theme.name Theme.Title)
          .Text("/dev")
          .FontSize(20.0)
          .HorizontalAlignment(HorizontalAlignment.Center)
          .IsVisible(compact)

      let tagline =
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .Text("unofficial desktop client")
          .IsVisible(compact |> Observable.map not)

      // The header clears the drag band the shell lays over the top
      // strip; the site's own py-6 keeps the same rhythm below it.
      let header =
        StackPanel()
          .Margin(Thickness(14.0, Theme.bandHeight + 12.0, 14.0, 24.0))
          .Children(mark, wordmark, tagline)

      let commitButton =
        Button()
          .Classes(Theme.name Theme.NavCommit)
          .Margin(0.0, 8.0, 0.0, 8.0)
          .Content(
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(12.0)
              .Children(
                Icons.icon Glyph.Commit,
                TextBlock()
                  .Text("Commit")
                  .IsVisible(compact |> Observable.map not)
              )
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
      Theme.bindClass(commitButton, Theme.RailItem, compact)

      // The site's collapse row, under Commit: the labels show only
      // while the sidebar can expand.
      let collapse =
        let iconLeft =
          Icons
            .iconAt(Glyph.PanelLeftClose, 18.0)
            .IsVisible(compact |> Observable.map not)

        let iconRight =
          Icons.iconAt(Glyph.PanelLeftOpen, 18.0).IsVisible(compact)

        let label =
          TextBlock().Text("Collapse").IsVisible(compact |> Observable.map not)

        let button =
          Button()
            .Name("NavCollapse")
            .Classes(Theme.name Theme.NavItem)
            .Content(
              StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(10.0)
                .Children(iconLeft, iconRight, label)
            )
            .IsVisible(
              layout
              |> Observable.map(fun current ->
                match current with
                | Standard
                | Wide -> true
                | _ -> false)
            )
            .OnClickHandler(fun _ _ -> toggleSidebar())

        ToolTip.SetTip(button, "Toggle navigation")
        Theme.bindClass(button, Theme.RailItem, compact)
        button

      let navChildren: Control array = [|
        header
        for entry in entries do
          item entry
        commitButton
        collapse
      |]

      let navList = ScrollViewer().Content(StackPanel().Children(navChildren))

      // The rail keeps the footer's full width: the old page margin
      // plus the nav row padding clipped the 36px avatar.
      let footerMargin =
        compact
        |> Observable.map(fun isCompact ->
          if isCompact then
            Thickness(0.0, 8.0, 0.0, 12.0)
          else
            Thickness(14.0, 8.0, 14.0, 12.0))

      let sidebarFooter =
        footer(
          {
            Navigate = navigate
            Session = session.Observe
            Images = (env :> IProvideImageCache).ImageCache
            Compact = compact
          }
        )
          .DockBottom()
          .Margin(footerMargin)

      DockPanel().LastChildFill(true).Children(sidebarFooter, navList)

    // The pane keeps one look per mode: the drawer fills with the
    // surface and wears a hairline under the band; the docked pane
    // paints the app background, so the desktop behind the window never
    // shows through the rail.
    let paneBackground =
      narrow
      |> Observable.map(fun isNarrow ->
        if isNarrow then Brushes.surface else Brushes.background)
      |> Observable.bind id

    let paneMargin =
      narrow
      |> Observable.map(fun isNarrow ->
        if isNarrow then
          Thickness(0.0, Theme.bandHeight, 0.0, 0.0)
        else
          Thickness(0.0))

    let paneHairline =
      narrow
      |> Observable.map(fun isNarrow ->
        if isNarrow then
          Thickness(0.0, 0.0, 1.0, 0.0)
        else
          Thickness(0.0))

    let sidebarPane =
      Border()
        .Child(sidebarContent())
        .BorderBrush(Brushes.border)
        .Background(paneBackground)
        .Margin(paneMargin)
        .BorderThickness(paneHairline)

    sidebarPane.Name <- "SidebarPane"

    // The aside: the signed-in user's card, the way the site's right
    // column does past its wide breakpoint. It rebuilds only when the
    // signed-in user changes, the way the chrome does.
    let asideHost = ContentControl()

    asideHost.Name <- "AsideHost"

    let asideUser = new BehaviorSubject<string>("")

    session.Observe
    |> Observable.add(fun state ->
      match state with
      | SignedIn entry when asideUser.Value <> entry.Session.User.Id ->
        asideUser.OnNext entry.Session.User.Id
        asideHost.Content <- ProfileCard.Page { Env = env; Session = entry }
      | SignedIn _ -> ()
      | SignedOut ->
        asideUser.OnNext ""
        asideHost.Content <- null)

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

    // The route store carries the live path to any chrome that follows
    // the route; the shell feeds it once, for its own app lifetime.
    // The boot navigation predates the subscription, so its route lands
    // by hand.
    env.Routes.Land routes.BootUrl

    router.Changed
    |> Observable.add(fun snapshot ->
      snapshot.Route
      |> ValueOption.iter(fun route -> env.Routes.Land route.Path))

    // The shared log chrome: the tabs and the composer every log page
    // posts through. It rebuilds only when the signed-in user changes,
    // so a poll's session refresh never touches the composer's draft.
    let chromeHost = ContentControl()
    let chromeUser = new BehaviorSubject<string>("")

    session.Observe
    |> Observable.add(fun state ->
      match state with
      | SignedIn entry when chromeUser.Value <> entry.Session.User.Id ->
        chromeUser.OnNext entry.Session.User.Id

        chromeHost.Content <-
          LogChrome.Page {
            Env = env
            Session = entry
            Nav = router
          }
      | SignedIn _ -> ()
      | SignedOut ->
        chromeUser.OnNext ""
        chromeHost.Content <- null)

    // The reading column: the site's main — a card surface between two
    // hairlines, 620px wide while the labels show, 796px once the rail
    // collapses. The log chrome rides above the outlet inside it; where
    // the window leaves room, the column centers in it.
    let mainColumn = Grid()

    mainColumn.RowDefinitions <- RowDefinitions("Auto,*")

    mainColumn.Children(chromeHost.Row(0), outlet.Row(1)) |> ignore

    // The site's reading column: 620px while the sidebar shows its
    // labels, 796px once the desktop sidebar collapses. The forced rail
    // below the label breakpoint keeps 620px, as the site's `md` rule
    // does there.
    let mainWidth =
      Observable.combineLatest layout expanded
      |> Observable.map(fun (current, isExpanded) ->
        match current with
        | Standard
        | Wide when not isExpanded -> 796.0
        | _ -> 620.0)

    let mainHost =
      Border()
        .Background(Brushes.card)
        .BorderBrush(Brushes.border)
        .BorderThickness(Thickness(1.0, 0.0, 1.0, 0.0))
        .MaxWidth(mainWidth)
        .Child(mainColumn)

    // The shell: the reading column and the aside — one strip that
    // never grows past the site's app row. Stretch with the cap fills
    // the window and the clamp centers the strip past it; a centered
    // alignment here would shrink the strip to its content instead.
    let shell =
      let grid = Grid()

      grid.ColumnDefinitions <- ColumnDefinitions("*,Auto")
      grid.MaxWidth <- stripWidth

      grid.Children(mainHost.Column(0), aside.Column(1)) |> ignore

      grid

    // One Fluent SplitView owns the sidebar: a docked Inline pane past
    // the narrow line, an Overlay drawer below it. The docked pane has
    // two widths, the site's 256px labels and 80px rail; the drawer is
    // always 256px. The pane content binds `IsPaneOpen` because the
    // template never hides it while the pane width animates to zero.
    let split = SplitView()

    split.Name <- "SidebarSplit"
    split.OpenPaneLength <- 256.0
    split.PaneBackground <- Avalonia.Media.Brushes.Transparent
    split.UseLightDismissOverlayMode <- true

    split.Resources.Add(
      "SplitViewLightDismissOverlayBackground",
      Brushes.solid "#8A000000"
    )

    split.Pane <- sidebarPane
    split.Content <- shell

    sidebarPane.IsVisible(split.GetObservable(SplitView.IsPaneOpenProperty))
    |> ignore

    // The mode and the width choice drive the pane. Every navigation
    // closes only the drawer: a desktop pane must stay docked.
    let applyMode() =
      match layout.Value with
      | Narrow ->
        split.DisplayMode <- SplitViewDisplayMode.Overlay
        split.OpenPaneLength <- 256.0
      | Rail ->
        split.DisplayMode <- SplitViewDisplayMode.Inline
        split.IsPaneOpen <- true
        split.OpenPaneLength <- 80.0
      | Standard
      | Wide ->
        split.DisplayMode <- SplitViewDisplayMode.Inline
        split.IsPaneOpen <- true
        split.OpenPaneLength <- if compact.Value then 80.0 else 256.0

    updateCompact()
    applyMode()
    split.IsPaneOpen <- layout.Value <> Narrow

    let previous = new BehaviorSubject<Layout>(layout.Value)

    layout
    |> Observable.add(fun current ->
      let prior = previous.Value
      previous.OnNext current
      updateCompact()
      applyMode()

      if current = Narrow && prior <> Narrow then
        split.IsPaneOpen <- false)

    expanded
    |> Observable.add(fun _ ->
      updateCompact()
      applyMode())

    router.Changed
    |> Observable.add(fun _ ->
      if layout.Value = Narrow then
        split.IsPaneOpen <- false)

    let windowState = this.GetObservable(Window.WindowStateProperty)

    let bandPanel =
      band {
        Narrow = narrow
        ToggleDrawer = fun () -> split.IsPaneOpen <- not split.IsPaneOpen
        WindowState = windowState
        Minimize = fun () -> this.WindowState <- WindowState.Minimized
        ToggleMaximize =
          fun () ->
            match this.WindowState with
            | WindowState.Maximized -> this.WindowState <- WindowState.Normal
            | _ -> this.WindowState <- WindowState.Maximized
        Close = fun () -> this.Close()
      }

    this.Content <- Panel().Children(split, composeLayer, helpLayer, bandPanel)

    // The inbox marks everything read as it lists, so the poller stops
    // while the inbox is on screen.
    let unreadPoll =
      env.Unread.Start(
        activeSection
        |> Observable.map(fun section -> section = ValueSome "notifications"),
        TimeSpan.FromSeconds 45.0
      )

    this.Closed.Add(fun _ -> unreadPoll.Dispose())

    // Close-to-tray: the flag turns a close into a hide, so the tray
    // and the poller keep the app alive. `Quit` marks the bypass first.
    this.Closing.Add(fun args ->
      if not quitRequested.Value && settings.Current.CloseToTray then
        args.Cancel <- true
        this.Hide())

    Tray.install {
      Window = this
      Router = router
      Unread = env.Unread.Count
      Quit =
        fun () ->
          quitRequested.OnNext true
          this.Close()
    }

    // The one key table, tunneled ahead of every focused control. The
    // handler captures nothing a page owns — navigation, the overlay
    // subjects, and the window's own members — so no route outlives its
    // page through it. Esc closes the top overlay first, then leaves
    // fullscreen; F11 walks the window in and out of it.
    Shortcuts.install {
      Root = this
      Actions = {
        Compose = fun () -> composeOpen.OnNext true
        Help = fun () -> helpOpen.OnNext true
        Escape =
          fun () ->
            match helpOpen.Value, composeOpen.Value with
            | true, _ -> closeHelp()
            | _, true -> closeCompose()
            | _, _ -> this.ExitFullScreen()
        Navigate = navigate
        ToggleFullScreen = this.ToggleFullScreen
        WalkCards = fun step -> Shortcuts.moveCardFocus(this, step)
      }
      OverlayOpen = fun () -> composeOpen.Value || helpOpen.Value
    }

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

        Routes.go(env, router, "/signin")
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
