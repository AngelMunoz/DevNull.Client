module DevNull.Client.Shell

open System
open System.Reactive.Subjects
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Chrome
open Avalonia.Controls.Documents
open Avalonia.Controls.Primitives
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Platform
open Avalonia.Styling
open Avalonia.Threading
open Avalonia.VisualTree
open NXUI.Extensions
open IcedTasks
open FSharp.Control.Reactive
open Microsoft.Extensions.Logging
open Navs
open Navs.Avalonia
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Shortcuts
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.Timeline
open DevNull.Client.RouteStore
open DevNull.Client.Tray
open DevNull.Client.Unread
open DevNull.Client.UserSettingsStore
open DevNull.Client.TranslationCache
open DevNull.Client.UiClock
open DevNull.Client.Views

// The window width under which the sidebar folds into a drawer, the
// site's `md` breakpoint.
let narrowWidth = 768.0

// The window width from which the sidebar can show its labels, the
// site's `lg` breakpoint.
let toggleWidth = 1024.0

// The window width from which the aside column opens, the site's
// wide breakpoint.
let asideWidth = 1280.0

// The widest the whole shell strip grows, the site's 1264px app row:
// past it the strip centers, so the timeline keeps the window's
// middle instead of hugging the sidebar.
let stripWidth = 1264.0

// The four width bands of the shell: the folded drawer, the forced
// icon rail, the collapsible sidebar without the aside, and the full
// strip with the aside.
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

// One sidebar destination. `Badge` carries the live unread count the
// entry renders, when it renders one; `Visible` carries the live
// flag that hides the entry altogether, when it can hide.
[<Struct>]
type NavEntry = {
  Glyph: Glyph
  Label: string
  Section: string
  Url: string
  Badge: IObservable<int> voption
  Visible: IObservable<bool> voption
}

// The client surface fills the window behind the caption row. The
// window paints the themed background to its edges: a transparency hint
// left the resize border transparent, and the desktop showed through
// beside the rail.
// A page transition that swaps immediately. The outlet's default
// cross-fade never finishes under a headless clock, which keeps the
// previous page attached — and its buttons steal clicks from the
// live one.
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

// What one sidebar entry reads: where it goes, which section is
// active, and whether the labels are hidden.
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
  Theme.bindClass(item, Theme.NavItemActive, active) |> ignore
  Theme.bindClass(item, Theme.RailItem, context.Compact) |> ignore

  entry.Visible
  |> ValueOption.iter(fun visible -> item.IsVisible(visible) |> ignore)

  item

// What the sidebar footer reads: where it goes, the session, the
// avatar cache, and whether the labels are hidden.
[<Struct>]
type FooterContext = {
  Navigate: string -> unit
  Session: IObservable<SessionState>
  Images: ImageCacheService
  Compact: IObservable<bool>
}

// The guard key: the signed-in user's id, empty when signed out. A
// session refresh with the same id leaves a guarded cell alone.
let inline userKey(state: SessionState) : string =
  match state with
  | SignedIn entry -> entry.Session.User.Id
  | SignedOut -> ""

// The sidebar footer: the signed-in user's row, or the sign-in entry.
// The account row rebuilds only when the signed-in user changes — an
// unread refresh keeps the built one.
let footer(context: FooterContext) : Control =
  let signedIn =
    context.Session
    |> Observable.map(function
      | SignedIn _ -> true
      | SignedOut -> false)

  // The identity cell, the way the chrome's guard works: a poll's
  // session refresh lands here without rebuilding the row.
  let accountRow(state: SessionState) : Control =
    match state with
    | SignedIn entry ->
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
            Square = false
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
                .Text("@" + entry.Session.User.Handle)
            )
        )
    | SignedOut -> Panel()

  let host =
    ContentControl()
      .Content(
        PageChrome.rebuildOn(context.Session, userKey, accountRow)
        |> _.ToBinding()
      )

  let account =
    Button()
      .Classes(Theme.name Theme.NavItem)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Content(host)
      .IsVisible(signedIn)
      .OnClickHandler(fun _ _ -> context.Navigate "/profile")

  account.Name <- "SidebarAccount"
  Theme.bindClass(account, Theme.RailItem, context.Compact) |> ignore

  let signIn =
    Button()
      .Classes(Theme.name Theme.NavButton)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Content("Sign in")
      .IsVisible(signedIn |> Observable.map not)
      .OnClickHandler(fun _ _ -> context.Navigate "/signin")

  Theme.bindClass(signIn, Theme.RailItem, context.Compact) |> ignore

  StackPanel().Children(account, signIn)

// Everything the window band needs: the responsive state, the live
// window state, and the caption actions the shell answers.
[<Struct>]
type BandProps = {
  Narrow: IObservable<bool>
  ToggleDrawer: unit -> unit
  WindowState: IObservable<WindowState>
  Minimize: unit -> unit
  ToggleMaximize: unit -> unit
  Close: unit -> unit
}

// One app-drawn caption button carrying its system element role.
let captionButton
  (role: WindowDecorationsElementRole, glyph: Glyph, onClick: Button -> unit)
  : Button =
  let button =
    Button().Classes(Theme.name Theme.CaptionButton).Content(Icons.icon glyph)

  WindowDecorationProperties.SetElementRole(button, role)
  button.OnClickHandler(fun sender _ -> onClick sender) |> ignore

  button

// The window band: a drag surface the window answers, the hamburger
// that opens the drawer under the narrow layout, and the caption row.
// The band rides above every pane, so the whole top strip drags the
// window while the buttons stay clickable.
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

// The commit dialog the site's Commit button opens: the composer on a
// card over a scrim. A commit, a scrim click, or Esc closes it.
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
      .Child(
        StackPanel()
          .Spacing(8.0)
          .Children(
            TextBlock().Classes(Theme.name Theme.Title).Text("Write a commit"),
            Composer.Page {
              Env = env
              Session = session
              Target = Composer.Fresh
              Participants = ValueNone
              OnCommitted = fun _ -> close()
              OnScheduled = ValueSome(fun _ -> close())
              OnOpenQueue = ValueNone
              Draft = ValueNone
              OnBodyReady =
                ValueSome(fun box ->
                  // The box attaches with the dialog; the focus lands
                  // after that layout pass.
                  box.AttachedToVisualTree.Add(fun _ ->
                    env.Dispatcher.Post(
                      (fun () -> ignore(box.Focus())),
                      DispatcherPriority.Default
                    )))
            }
          )
      )

  card.Name <- "ComposeDialog"

  card

// The log chrome's rail node: the signed-in user's avatar, ring and
// all. The chrome keeps the rail line and the 64px column.
let logRailNode(session: SignedInSession, cache: ImageCacheService) : Control =
  let nodeTop = 9.0

  let avatar =
    Border()
      .Name("ComposerRailNode")
      .Classes(Theme.name Theme.CardBack)
      .BorderThickness(Thickness(2.0))
      .CornerRadius(46.0)
      .Padding(1.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Top)
      .Margin(0.0, nodeTop, 0.0, 0.0)
      .Child(
        Avatar.view {
          Name = session.Session.User.Name
          Url = session.Session.User.AvatarUrl
          Lane = None
          Size = 40.0
          Cache = cache
          Square = false
        }
      )

  avatar.Classes(Theme.laneClass(Theme.LaneBorder, None)) |> ignore

  avatar

// What the sidebar content reads: its navigation, the layout band, the
// account it docks, and the shell actions its buttons raise.
type SidebarProps = {
  Navigate: string -> unit
  Active: BehaviorSubject<string voption>
  Compact: IObservable<bool>
  Layout: IObservable<Layout>
  Entries: NavEntry list
  Session: IObservable<SessionState>
  Images: ImageCacheService
  OpenCompose: unit -> unit
  OpenSearch: unit -> unit
  ToggleSidebar: unit -> unit
  FocusComposer: unit -> bool
}

// The sidebar content: the wordmark, the entries, the commit and
// search rows, the collapse row, and the docked footer.
let sidebar(props: SidebarProps) : Control =
  // The service's name as plain text, the one form the brand asks
  // for: lowercase, both slashes. No retyped wordmark and no copied
  // lockup; this is an unofficial client. The rail keeps the first
  // form of the name, because the full one cannot fit.
  let wordmark =
    TextBlock()
      .Classes(Theme.name Theme.Title)
      .Text("/dev/null")
      .IsVisible(props.Compact |> Observable.map not)

  let mark =
    TextBlock()
      .Classes(Theme.name Theme.Title)
      .Text("/dev")
      .FontSize(20.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .IsVisible(props.Compact)

  let tagline =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Text("unofficial desktop client")
      .IsVisible(props.Compact |> Observable.map not)

  // The header clears the drag band the shell lays over the top strip;
  // the site's own py-6 keeps the same rhythm below it.
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
              .IsVisible(props.Compact |> Observable.map not)
          )
      )
      .OnClickHandler(fun _ _ ->
        // The site focuses the log's own composer when one is on
        // screen, and opens the dialog everywhere else.
        if
          props.Active.Value <> ValueSome "log" || not(props.FocusComposer())
        then
          props.OpenCompose())

  commitButton.Name <- "NavCommitButton"
  Theme.bindClass(commitButton, Theme.RailItem, props.Compact) |> ignore

  // The site's collapse row, under Commit: the labels show only while
  // the sidebar can expand.
  let collapse =
    let iconLeft =
      Icons
        .iconAt(Glyph.PanelLeftClose, 18.0)
        .IsVisible(props.Compact |> Observable.map not)

    let iconRight =
      Icons.iconAt(Glyph.PanelLeftOpen, 18.0).IsVisible(props.Compact)

    let label =
      TextBlock()
        .Text("Collapse")
        .IsVisible(props.Compact |> Observable.map not)

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
          props.Layout
          |> Observable.map(fun current ->
            match current with
            | Standard
            | Wide -> true
            | _ -> false)
        )
        .OnClickHandler(fun _ _ -> props.ToggleSidebar())

    ToolTip.SetTip(button, "Toggle navigation")
    Theme.bindClass(button, Theme.RailItem, props.Compact) |> ignore
    button

  // The site's search row: the palette's opener, right under the nav
  // items. The pair that opens it lives in the bindings sheet, not on
  // the row.
  let searchRow =
    let button =
      Button()
        .Name("NavSearchButton")
        .Classes(Theme.name Theme.NavItem)
        .Content(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(10.0)
            .Children(
              Icons.icon Glyph.Search,
              TextBlock()
                .Text("Search")
                .IsVisible(props.Compact |> Observable.map not)
            )
        )
        .OnClickHandler(fun _ _ -> props.OpenSearch())

    ToolTip.SetTip(button, "Search")
    Theme.bindClass(button, Theme.RailItem, props.Compact) |> ignore
    button

  let navContext: NavContext = {
    Navigate = props.Navigate
    Active = props.Active
    Compact = props.Compact
  }

  let navChildren: Control array = [|
    header
    for entry in props.Entries do
      navItem(navContext, entry)
    searchRow
    commitButton
    collapse
  |]

  let navList = ScrollViewer().Content(StackPanel().Children(navChildren))

  // The rail keeps the footer's full width: the old page margin plus
  // the nav row padding clipped the 36px avatar.
  let footerMargin =
    props.Compact
    |> Observable.map(fun isCompact ->
      if isCompact then
        Thickness(0.0, 8.0, 0.0, 12.0)
      else
        Thickness(14.0, 8.0, 14.0, 12.0))

  let sidebarFooter =
    footer(
      {
        Navigate = props.Navigate
        Session = props.Session
        Images = props.Images
        Compact = props.Compact
      }
    )
      .DockBottom()
      .Margin(footerMargin)

  DockPanel().LastChildFill(true).Children(sidebarFooter, navList)

// What the aside composition reads: the env its two cards need, the
// session that guards their rebuild, where a suggestion opens, and
// whether the wide band shows it.
type AsideProps<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideRoutes
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: IObservable<SessionState>
  Navigate: string -> unit
  Wide: IObservable<bool>
}

// The aside composition: the signed-in user's card over who to
// follow, rebuilt only when the signed-in user changes.
let aside(props: AsideProps<'env>) : Control =
  let stack(state: SessionState) : Control =
    match state with
    | SignedIn entry ->
      StackPanel()
        .Spacing(24.0)
        .Children(
          ProfileCard.Page { Env = props.Env; Session = entry },
          WhoToFollow.Page {
            Env = props.Env
            Session = entry
            OnOpen = props.Navigate
          }
        )
    | SignedOut -> Panel()

  let host =
    ContentControl()
      .Content(
        PageChrome.rebuildOn(props.Session, userKey, stack) |> _.ToBinding()
      )

  host.Name <- "AsideHost"
  host.Margin <- Thickness(24.0, Theme.bandHeight + 24.0, 24.0, 24.0)

  ScrollViewer()
    .Width(320.0)
    .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
    .IsVisible(props.Wide)
    .Content(host)

// What the shared log chrome reads: the env its composer and timeline
// calls need, the navigation its tabs follow, and the session that
// guards the rebuild.
type ChromeProps<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLogging
  and 'env :> IProvideTimeline
  and 'env :> IProvideRoutes> = {
  Env: 'env
  Nav: INavigable
  Session: IObservable<SessionState>
  // Reports the composer's body for the shell's focus shortcut, or
  // none when the chrome has no signed-in composer.
  OnComposerBody: TextBox voption -> unit
}

// The shared log chrome: the tabs and the composer every log page
// posts through. It rebuilds only when the signed-in user changes, so
// a poll's session refresh never touches the composer's draft.
let chromeFeed(props: ChromeProps<'env>) : Control =
  let chrome(state: SessionState) : Control =
    match state with
    | SignedIn entry ->
      LogChrome.Page {
        Env = props.Env
        Nav = props.Nav
        Composer =
          fun () ->
            Composer.Page {
              Env = props.Env
              Session = entry
              Target = Composer.Fresh
              Participants = ValueNone
              OnCommitted = fun _ -> props.Env.Timeline.CommitLanded()
              OnScheduled = ValueNone
              OnOpenQueue =
                ValueSome(fun () ->
                  Navigation.go(props.Nav, "/scheduled", ignore))
              Draft = ValueNone
              OnBodyReady =
                ValueSome(fun box -> props.OnComposerBody(ValueSome box))
            }
        RailNode = fun () -> logRailNode(entry, props.Env.ImageCache)
        Header = ValueNone
        OnReload = fun () -> props.Env.Timeline.ForceReload()
      }
    | SignedOut ->
      props.OnComposerBody ValueNone
      Panel()

  ContentControl()
    .Content(
      PageChrome.rebuildOn(props.Session, userKey, chrome) |> _.ToBinding()
    )

type Shell<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvidePkceSource
  and 'env :> IProvideMachineName
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession
  and 'env :> IProvideSettings
  and 'env :> IProvideLogging
  and 'env :> IProvideUnread
  and 'env :> IProvideUserSettings
  and 'env :> IProvideTranslations
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLastVisit
  and 'env :> IProvideRecentSearches
  and 'env :> IProvideProfiles
  and 'env :> IProvideTimeline
  and 'env :> IProvideRoutes
  and 'env :> IProvideSshKeys
  and 'env :> IProvideRemotes
  and 'env :> IProvideNatives>
  (env: 'env, callback: string option, ?undoWindow: TimeSpan) as this =
  inherit Window()

  // The server keeps a deleted commit restorable for its undo window;
  // the toast stands exactly that long. The parameter only shortens it
  // for the tests.
  let undoWindow = defaultArg undoWindow (TimeSpan.FromSeconds 30.0)

  // The close bypass: `Quit` raises it, so the close-to-tray guard in
  // the Closing handler stands down.
  let quitRequested = new BehaviorSubject<bool>(false)

  do
    let session = (env :> IProvideSession).Session

    let log =
      (env :> IProvideLogging).LoggerFactory.CreateLogger "DevNull.Client.Shell"

    log.LogInformation("The window opens.")

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

    // The SplitView's own light-dismiss scrim: the theme key, read once.
    let scrim = Theme.solid(ThemeVariant.Dark, Theme.Keys.scrim)

    // The compose dialog: one overlay above the panes, below the band
    // so the caption stays reachable. It opens only signed in.
    let composeOpen = new BehaviorSubject<bool>(false)

    let closeCompose() = composeOpen.OnNext false

    let composeContent() : Control =
      match session.Current with
      | SignedIn entry -> composeDialog(env, entry, closeCompose)
      | SignedOut ->
        closeCompose()
        Panel()

    let composeLayer =
      DialogChrome.layer {
        Open = composeOpen
        Content = composeContent
        Scrim = true
        OnScrimClick = ValueSome closeCompose
        Place = DialogChrome.Center
      }

    // The search dialog: the site's command palette, one overlay
    // beside the composer. It opens only signed in, and its own
    // session dies with the page it built.
    let searchOpen = new BehaviorSubject<bool>(false)

    let closeSearch() = searchOpen.OnNext false

    let searchContent() : Control =
      match session.Current with
      | SignedIn entry ->
        let log = env.LoggerFactory.CreateLogger "DevNull.Client.SearchDialog"

        let searches =
          new Search.Session(
            (fun term ->
              Search.profiles(env, { KeyFile = entry.KeyFile; Term = term })),
            (fun term ->
              Search.commits(
                env,
                {
                  KeyFile = entry.KeyFile
                  Term = term
                  Limit = Some 5
                }
              )),
            log
          )

        SearchDialog.Page {
          Env = env
          SignedIn = entry
          Searches = searches
          Recents = (env :> IProvideRecentSearches).RecentSearches
          Nav = router
          OpenCompose = fun () -> composeOpen.OnNext true
          Close = closeSearch
        }
      | SignedOut ->
        closeSearch()
        Panel()

    let searchLayer =
      DialogChrome.layer {
        Open = searchOpen :> IObservable<bool>
        Content = searchContent
        Scrim = true
        OnScrimClick = ValueSome closeSearch
        Place = DialogChrome.Center
      }

    // The bindings list: the same overlay shape as the compose dialog,
    // built on open and released on close.
    let helpOpen = new BehaviorSubject<bool>(false)

    let closeHelp() = helpOpen.OnNext false

    let helpLayer =
      DialogChrome.layer {
        Open = helpOpen :> IObservable<bool>
        Content = fun () -> Shortcuts.helpCard(env.Dispatcher)
        Scrim = true
        OnScrimClick = ValueSome closeHelp
        Place = DialogChrome.Center
      }

    // The terms gate: while the signed account has not accepted the
    // terms in force, one dialog blocks the panes. Only the session's
    // own refresh closes it, so the poller can clear it as surely as
    // the dialog's own Accept.
    let termsOpen =
      session.Observe
      |> Observable.map(fun state ->
        match state with
        // Unknown terms never gate: only the server's own no does.
        | SignedIn entry ->
          entry.Session.TermsAccepted |> Option.contains false
        | SignedOut -> false)

    // The layer's change-only gate keeps a poll's session refresh from
    // rebuilding the dialog, so a ticked box survives while the terms
    // stay unaccepted.
    let termsContent() : Control =
      match session.Current with
      | SignedIn entry ->
        TermsDialog.Page {
          Env = env
          Session = entry
          OnAccepted =
            fun me ->
              session.Refresh {
                entry.Session with
                    UnreadNotificationsCount = me.UnreadNotificationsCount
                    TermsVersion = me.TermsVersion
                    TermsAccepted = Some me.TermsAccepted
              }
        }
      | SignedOut -> Panel()

    let termsLayer =
      DialogChrome.layer {
        Open = termsOpen
        Content = termsContent
        Scrim = true
        OnScrimClick = ValueNone
        Place = DialogChrome.Center
      }

    // The delete undo: the server keeps a deleted commit restorable
    // for its undo window, so a delete leaves one quiet toast over the
    // panes. Undo restores the commit and the open log reloads it; the
    // window's end dismisses the toast on its own.
    let undoToast =
      new BehaviorSubject<(string * string option) voption>(ValueNone)

    let dismissUndo(commitId: string) =
      match undoToast.Value with
      | ValueSome(id, _) when id = commitId ->
        emitOnUi(env.Dispatcher, undoToast, ValueNone)
      | _ -> ()

    let timeline = (env :> IProvideTimeline).Timeline

    timeline.Deletions
    |> Observable.add(fun commitId ->
      emitOnUi(env.Dispatcher, undoToast, ValueSome(commitId, None))

      asyncEx {
        do! Async.Sleep undoWindow
        dismissUndo commitId
      }
      |> Async.Start)

    let restore(commitId: string) = asyncEx {
      match session.Current with
      | SignedIn entry ->
        let! outcome =
          Commits.restore(
            env,
            {
              KeyFile = entry.KeyFile
              CommitId = commitId
            }
          )

        match outcome with
        | Ok commit ->
          emitOnUi(env.Dispatcher, undoToast, ValueNone)
          timeline.CommitLanded()

          log.LogInformation("The commit {Commit} was restored.", commit.Id)
        | Error error ->
          // The window's own words: expired, or no longer deleted.
          emitOnUi(
            env.Dispatcher,
            undoToast,
            ValueSome(commitId, Some(describe error))
          )
      | SignedOut -> emitOnUi(env.Dispatcher, undoToast, ValueNone)
    }

    let undoContent() : Control =
      // The card binds its own live slices: a failed restore swaps the
      // words and hides Undo while the toast stays open, and a second
      // delete re-aims the same card. The click reads the current toast,
      // never the one the card was built for.
      let words =
        undoToast
        |> Observable.map(fun current ->
          match current with
          | ValueSome(_, Some word) -> word
          | _ -> "Commit deleted.")

      let canUndo =
        undoToast
        |> Observable.map(fun current ->
          match current with
          | ValueSome(_, Some _) -> false
          | _ -> true)

      Border()
        .Name("UndoToast")
        .Classes(Theme.name Theme.Card)
        .Padding(16.0)
        .Child(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(12.0)
            .Children(
              TextBlock()
                .VerticalAlignment(VerticalAlignment.Center)
                .TextWrapping(TextWrapping.Wrap)
                .Text(words),
              Button()
                .Classes(Theme.name Theme.AccentButton)
                .Content("Undo")
                .IsVisible(canUndo)
                .OnClickHandler(fun _ _ ->
                  match undoToast.Value with
                  | ValueSome(commitId, _) ->
                    asyncEx { return! restore commitId }
                    |> Async.StartImmediate
                  | ValueNone -> ())
            )
        )

    let undoLayer =
      DialogChrome.layer {
        Open = undoToast |> Observable.map ValueOption.isSome
        Content = undoContent
        Scrim = false
        OnScrimClick = ValueNone
        Place = DialogChrome.Bottom
      }

    // The remotes entry shows only while the account carries the
    // direct messages; its badge follows the store's unread total.
    let remotesUnread =
      env.Remotes.Observe
      |> Observable.map(fun state -> state.Unread |> Option.defaultValue 0)

    let remotesOn =
      session.Observe
      |> Observable.map(fun state ->
        match state with
        | SignedIn entry -> entry.Session.Remotes.IsSome
        | SignedOut -> false)

    let entries = [
      {
        Glyph = Glyph.Log
        Label = "Log"
        Section = "log"
        Url = "/log/latest"
        Badge = ValueNone
        Visible = ValueNone
      }
      {
        Glyph = Glyph.Notifications
        Label = "Notifications"
        Section = "notifications"
        Url = "/notifications"
        Badge = ValueSome(env.Unread.Count)
        Visible = ValueNone
      }
      {
        Glyph = Glyph.Remotes
        Label = "Remotes"
        Section = "remotes"
        Url = "/remotes"
        Badge = ValueSome remotesUnread
        Visible = ValueSome remotesOn
      }
      {
        Glyph = Glyph.Profile
        Label = "Profile"
        Section = "profile"
        Url = "/profile"
        Badge = ValueNone
        Visible = ValueNone
      }
      {
        Glyph = Glyph.Saved
        Label = "Saved"
        Section = "saved"
        Url = "/saved"
        Badge = ValueNone
        Visible = ValueNone
      }
      {
        Glyph = Glyph.Settings
        Label = "Settings"
        Section = "settings"
        Url = "/settings"
        Badge = ValueNone
        Visible = ValueNone
      }
      {
        Glyph = Glyph.Info
        Label = "About"
        Section = "settings"
        Url = "/about"
        Badge = ValueNone
        Visible = ValueNone
      }
    ]

    // The inline composer's body, reported by the shared chrome. The
    // sidebar's focus shortcut asks the box itself, so no visual-tree
    // name search is needed; the chrome's own visibility decides.
    let inlineComposerBody = new BehaviorSubject<TextBox voption>(ValueNone)

    let focusInlineComposer() =
      inlineComposerBody.Value
      |> ValueOption.filter(fun box -> box.IsEffectivelyVisible)
      |> ValueOption.map(fun box -> box.Focus())
      |> ValueOption.defaultValue false

    // The sidebar: one tree for both modes, so no control lands in two
    // parents.
    let sidebarContent =
      sidebar {
        Navigate = navigate
        Active = activeSection
        Compact = compact
        Layout = layout
        Entries = entries
        Session = session.Observe
        Images = (env :> IProvideImageCache).ImageCache
        OpenCompose = fun () -> composeOpen.OnNext true
        OpenSearch = fun () -> searchOpen.OnNext true
        ToggleSidebar = toggleSidebar
        FocusComposer = focusInlineComposer
      }

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
        .Child(sidebarContent)
        .BorderBrush(Brushes.border)
        .Background(paneBackground)
        .Margin(paneMargin)
        .BorderThickness(paneHairline)

    sidebarPane.Name <- "SidebarPane"

    // The aside: the signed-in user's card and who to follow, the way
    // the site's right column does past its wide breakpoint. It
    // rebuilds only when the signed-in user changes, the way the chrome
    // does; the rail follows the route on its own.
    let asidePane =
      aside {
        Env = env
        Session = session.Observe
        Navigate = navigate
        Wide = wide
      }

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
      |> ValueOption.iter(fun route ->
        log.LogDebug("The route landed at {Url}.", route.Path)
        env.Routes.Land route.Path))

    // The shared log chrome: the tabs and the composer every log page
    // posts through. It rebuilds only when the signed-in user changes,
    // so a poll's session refresh never touches the composer's draft.
    let chromeHost =
      chromeFeed {
        Env = env
        Nav = router
        Session = session.Observe
        OnComposerBody = inlineComposerBody.OnNext
      }

    // The reading column: the site's main — a card surface between two
    // hairlines, 620px wide while the labels show, 796px once the rail
    // collapses.
    let mainColumn = Grid()

    mainColumn.RowDefinitions <- RowDefinitions("Auto,*")

    // The shared chrome stays above the outlet rather than inside the
    // log page: a log tab click changes only the route, so the outlet
    // swaps its page while the chrome — and the composer program with
    // the draft it holds — survives /log/following, /log/latest, and
    // /log/trending. Off those routes the chrome hides itself.
    mainColumn.Children(chromeHost.Row(0), outlet.Row(1)) |> ignore

    // The site's reading column: 620px while the sidebar shows its
    // labels, 796px once the desktop sidebar collapses. The forced rail
    // below the label breakpoint keeps 620px, as the site's `md` rule
    // does there. The remotes page runs the whole strip instead: its
    // inbox and thread sit side by side, the way the site's own page
    // does.
    let isRemotes =
      env.Routes.Path |> Observable.map(fun path -> path.StartsWith "/remotes")

    let mainWidth =
      Observable.combineLatest
        (Observable.combineLatest layout expanded)
        isRemotes
      |> Observable.map(fun ((current, isExpanded), onRemotes) ->
        if onRemotes then
          stripWidth
        else
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

      grid.Children(mainHost.Column(0), asidePane.Column(1)) |> ignore

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

    split.Resources.Add("SplitViewLightDismissOverlayBackground", scrim)

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

    this.Content <-
      Panel()
        .Children(
          split,
          composeLayer,
          helpLayer,
          searchLayer,
          termsLayer,
          undoLayer,
          bandPanel
        )

    // The inbox marks everything read as it lists, so the poller stops
    // while the inbox is on screen.
    let unreadPoll =
      env.Unread.Start(
        activeSection
        |> Observable.map(fun section -> section = ValueSome "notifications"),
        TimeSpan.FromSeconds 45.0
      )

    // The settings read rides the first requests and never blocks the
    // window: every card follows its automatic translation flag.
    env.UserSettings.Refresh()

    // The first read runs while the app may still wait on the sign-in
    // page: a fresh session reads the settings again.
    env.Session.Observe
    |> Observable.map(function
      | SignedIn session -> Some session.Session.User.Id
      | SignedOut -> None)
    |> Observable.distinctUntilChanged
    |> Observable.add(fun userId ->
      if userId.IsSome then
        env.UserSettings.Refresh())

    log.LogDebug(
      "The unread poll and the remotes watcher start with the window."
    )

    // The chat's own loops: one gate that follows the window's own
    // visibility. Polls run while the window stands on screen —
    // focused or not, the way a visible tab polls on the web — and
    // pause only once it hides: minimized, or away in the tray.
    let windowActive = new BehaviorSubject<bool>(true)

    let pulse() =
      windowActive.OnNext(
        this.IsVisible && this.WindowState <> WindowState.Minimized
      )

    this.Activated.Add(fun _ -> pulse())
    this.Deactivated.Add(fun _ -> pulse())

    this.GetObservable(Window.IsVisibleProperty)
    |> Observable.add(fun _ -> pulse())

    this.GetObservable(Window.WindowStateProperty)
    |> Observable.add(fun _ -> pulse())

    let remotesWatch = env.Remotes.Watcher.Start(windowActive)

    this.Closed.Add(fun _ ->
      unreadPoll.Dispose()
      remotesWatch.Dispose())

    // Close-to-tray: the flag turns a close into a hide, so the tray
    // and the poller keep the app alive. `Quit` marks the bypass first.
    this.Closing.Add(fun args ->
      if not quitRequested.Value && settings.Current.CloseToTray then
        log.LogDebug("The close hides the window into the tray.")
        args.Cancel <- true
        this.Hide())

    Tray.install {
      Window = this
      Router = router
      Unread = env.Unread.Count
      Log = log
      Quit =
        fun () ->
          log.LogInformation("The client quits from the tray.")
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
        Search = fun () -> searchOpen.OnNext true
        Help = fun () -> helpOpen.OnNext true
        Escape =
          fun () ->
            match searchOpen.Value, helpOpen.Value, composeOpen.Value with
            | true, _, _ -> closeSearch()
            | _, true, _ -> closeHelp()
            | _, _, true -> closeCompose()
            | _, _, _ -> this.ExitFullScreen()
        Navigate = navigate
        ToggleFullScreen = this.ToggleFullScreen
        WalkCards =
          fun step -> Shortcuts.moveCardFocus(env.Dispatcher, this, step)
      }
      OverlayOpen =
        fun () -> composeOpen.Value || helpOpen.Value || searchOpen.Value
    }

    match callback with
    | Some uri ->
      log.LogInformation("The sign-in callback arrived while the window runs.")

      asyncEx {
        do! DeviceAuth.finish(env, uri, log) |> Async.Ignore

        Routes.go(env, router, "/signin")
      }
      |> Async.StartImmediate
    | None -> ()

  member this.ToggleFullScreen() : unit =
    match this.WindowState with
    | WindowState.FullScreen -> this.WindowState <- WindowState.Normal
    | _ -> this.WindowState <- WindowState.FullScreen

  member this.ExitFullScreen() : unit =
    match this.WindowState with
    | WindowState.FullScreen -> this.WindowState <- WindowState.Normal
    | _ -> ()

  member this.Quit() : unit =
    quitRequested.OnNext true
    this.Close()
