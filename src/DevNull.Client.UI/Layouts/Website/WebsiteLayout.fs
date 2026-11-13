module DevNull.Client.WebsiteLayout

open System
open System.Reactive.Subjects
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Styling
open Avalonia.Threading
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
open DevNull.Client.RouteStore
open DevNull.Client.SettingsStore
open DevNull.Client.Shortcuts
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.Timeline
open DevNull.Client.TranslationCache
open DevNull.Client.UiClock
open DevNull.Client.Unread
open DevNull.Client.UserSettingsStore
open DevNull.Client.Views
open DevNull.Client.Views.Routes

let shortcut(keys: string list, words: string) : CheatsheetItem = {
  Keys = keys
  Words = words
}

let cheatsheet: CheatsheetItem list = [
  shortcut([ "J" ], "Next commit")
  shortcut([ "K" ], "Previous commit")
  shortcut([ "Enter" ], "Open the focused commit")
  shortcut([ "Y" ], "Copy the focused commit's link")
  shortcut([ "R" ], "Reply to the focused commit")
  shortcut([ "Q" ], "Quote the focused commit")
  shortcut([ "C" ], "Write a commit")
  shortcut([ "Ctrl K" ], "Search")
  shortcut([ "G"; "L" ], "Go to your log")
  shortcut([ "G"; "E" ], "Go to trending")
  shortcut([ "G"; "P" ], "Go to your profile")
  shortcut([ "G"; "S" ], "Go to settings")
  shortcut([ "?" ], "Show this list")
  shortcut([ "F11" ], "Fullscreen")
]

/// Everything the website arrangement reads: the env its pages and
/// dialogs call, the router the app built, the URL it booted on, and
/// the window the arrangement stands in. The router and its boot URL
/// belong to the app, not the arrangement: they outlive the layout.
type Props<'env
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
  and 'env :> IProvideBrowser
  and 'env :> IProvideNatives> = {
  Env: 'env
  Window: Window
  UndoWindow: TimeSpan
}

/// The mounted website arrangement: the root control the host slots
/// in, the key actions and cheatsheet the host's tunnel reads, the
/// overlay probe, the navigation window-level intents fall through
/// to, the narrow band and the drawer toggle the host's band binds,
/// and the unmount that releases every handle the arrangement wired.
type Mount = {
  Root: Control
  Actions: Actions
  Cheatsheet: CheatsheetItem list
  OverlayOpen: unit -> bool
  Navigate: string -> unit
  Narrow: IObservable<bool>
  ToggleDrawer: unit -> unit
  Dispose: unit -> unit
}

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

// A page transition that swaps immediately. The outlet's default
// cross-fade never finishes under a headless clock, which keeps the
// previous page attached — and its buttons steal clicks from the
// live one.
type ImmediatePageTransition() =

  interface Animation.IPageTransition with
    member _.Start(_, _, _, _) = Task.CompletedTask

// What one sidebar entry reads: where it goes, which section is
// active, whether the labels are hidden, and the badge brushes the
// mount owns.
[<Struct>]
type NavContext = {
  Navigate: string -> unit
  Active: BehaviorSubject<string voption>
  Compact: IObservable<bool>
  ScrollToTop: unit -> unit
  Accent: IObservable<IBrush>
  OnAccent: IObservable<IBrush>
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
          .Background(context.Accent)
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
              .Foreground(context.OnAccent)
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
      .OnClickHandler(fun _ _ ->
        if entry.Section = "log" && context.Active.Value = ValueSome "log" then
          context.ScrollToTop()
        else
          context.Navigate entry.Url)

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
            Viewable = false
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
          Viewable = false
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
  Accent: IObservable<IBrush>
  OnAccent: IObservable<IBrush>
  OpenCompose: unit -> unit
  OpenSearch: unit -> unit
  ToggleSidebar: unit -> unit
  FocusComposer: unit -> bool
  ScrollToTop: unit -> unit
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
    ScrollToTop = props.ScrollToTop
    Accent = props.Accent
    OnAccent = props.OnAccent
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
  and 'env :> IProvideUiClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideBrowser
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
          LiveNow.Page { Env = props.Env; Session = entry },
          WhoToFollow.Page {
            Env = props.Env
            Session = entry
            OnOpen = props.Navigate
          }
        )
    | SignedOut -> Panel()

  // The aside's pages run real programs (the live poll, the route
  // path), and a page built while the aside is hidden is built
  // detached: its program starts but its detach never comes. Build
  // the stack only in the wide band, and let the narrow band drop it.
  let content =
    Observable.combineLatest props.Wide props.Session
    |> fun source ->
      PageChrome.rebuildOn(
        source,
        (fun (wide, state) -> wide, userKey state),
        (fun (wide, state) -> if wide then stack state else Panel())
      )

  let host = ContentControl().Content(content |> _.ToBinding())

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

let mount(props: Props<'env>) : Mount =
  let env = props.Env
  let window = props.Window

  let session = (env :> IProvideSession).Session

  let log = env.LoggerFactory.CreateLogger "DevNull.Client.WebsiteLayout"

  // Every feed the arrangement wires; unmounting releases them all.
  let handles = ResizeArray<IDisposable>()

  let inline keep(handle: IDisposable) = handles.Add handle

  // The arrangement's own route table and router: built at mount, so
  // no outlet outlives its tree and no remount re-attaches a page
  // whose programs ended at the first detach. The path the route
  // store kept — the last path while another layout stood in the
  // slot — is read before this mount's own boot lands.
  let storedPath = env.Routes.Current

  let routes = Routes.build env
  let router = routes.Router

  let navigate(url: string) = Routes.go(env, router, url)

  // The route store carries the live path to any chrome that follows
  // the route; this mount feeds it while it stands. The boot
  // navigation predates the subscription, so its route lands by hand.
  env.Routes.Land routes.BootUrl

  router.Changed
  |> Observable.subscribe(fun snapshot ->
    snapshot.Route |> ValueOption.iter(fun route -> env.Routes.Land route.Path))
  |> keep

  // A remount restores the last path: the fresh boot lands first,
  // then the stored path supersedes it.
  if storedPath <> "" && storedPath <> routes.BootUrl then
    navigate storedPath

  let activeSection =
    new BehaviorSubject<string voption>(Navigation.sectionOf env.Routes.Current)

  router.Changed
  |> Observable.map(fun snapshot ->
    snapshot.Route
    |> ValueOption.map _.Path
    |> ValueOption.bind Navigation.sectionOf)
  |> Observable.subscribe activeSection.OnNext
  |> keep

  // One responsive value fed from the client size; narrow and wide
  // are projections of it, so the drawer and the aside cannot
  // disagree.
  let layout = new BehaviorSubject<Layout>(layoutOf window.Width)

  let narrow = layout |> Observable.map(fun current -> current = Narrow)

  let wide = layout |> Observable.map(fun current -> current = Wide)

  window.GetObservable(TopLevel.ClientSizeProperty)
  |> Observable.subscribe(fun size -> layout.OnNext(layoutOf size.Width))
  |> keep

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
  |> Observable.subscribe(fun commitId ->
    emitOnUi(env.Dispatcher, undoToast, ValueSome(commitId, None))

    asyncEx {
      do! Async.Sleep props.UndoWindow
      dismissUndo commitId
    }
    |> Async.Start)
  |> keep

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
                  asyncEx { return! restore commitId } |> Async.StartImmediate
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

  // The arrangement mirrors the app-level streams it binds, so every
  // binding's observer dies with the mount: no app-lifetime subject
  // holds a binding — and through it a control — after the slot
  // swaps. The feeds ride the handles the unmount releases.
  let sessionStates = new BehaviorSubject<SessionState>(session.Current)

  session.Observe |> Observable.subscribe sessionStates.OnNext |> keep

  let remotesStates = new BehaviorSubject<RemotesState>(env.Remotes.Current)

  env.Remotes.Observe |> Observable.subscribe remotesStates.OnNext |> keep

  let unreadCounts = new BehaviorSubject<int>(0)

  env.Unread.Count |> Observable.subscribe unreadCounts.OnNext |> keep

  let routePaths = new BehaviorSubject<string>(env.Routes.Current)

  env.Routes.Path |> Observable.subscribe routePaths.OnNext |> keep

  // The themed brushes ride the app's own resource observables, which
  // outlive every mount and never let a detached binding go. The
  // arrangement mirrors the brushes it binds, so a retired tree holds
  // nothing the app keeps.
  let brushOf(source: IObservable<IBrush>) : BehaviorSubject<IBrush> =
    let mirror = new BehaviorSubject<IBrush>(Brushes.Transparent)

    source |> Observable.subscribe mirror.OnNext |> keep

    mirror

  let accentBrushes = brushOf(Brushes.accent)
  let onAccentBrushes = brushOf(Brushes.onAccent)
  let surfaceBrushes = brushOf(Brushes.surface)
  let backgroundBrushes = brushOf(Brushes.background)
  let cardBrushes = brushOf(Brushes.card)
  let borderBrushes = brushOf(Brushes.border)

  // The remotes entry shows only while the account carries the
  // direct messages; its badge follows the store's unread total.
  let remotesUnread =
    remotesStates
    |> Observable.map(fun state -> state.Unread |> Option.defaultValue 0)

  let remotesOn =
    sessionStates
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
      Badge = ValueSome unreadCounts
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
      Session = sessionStates
      Images = (env :> IProvideImageCache).ImageCache
      Accent = accentBrushes
      OnAccent = onAccentBrushes
      OpenCompose = fun () -> composeOpen.OnNext true
      OpenSearch = fun () -> searchOpen.OnNext true
      ToggleSidebar = toggleSidebar
      FocusComposer = focusInlineComposer
      ScrollToTop = timeline.ScrollToTop
    }

  // The pane keeps one look per mode: the drawer fills with the
  // surface and wears a hairline under the band; the docked pane
  // paints the app background, so the desktop behind the window never
  // shows through the rail.
  let paneBackground =
    narrow
    |> Observable.map(fun isNarrow ->
      if isNarrow then
        surfaceBrushes :> IObservable<IBrush>
      else
        backgroundBrushes :> IObservable<IBrush>)
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
      .BorderBrush(borderBrushes)
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
      Session = sessionStates
      Navigate = navigate
      Wide = wide
    }

  let outlet = RouterOutlet(Router = Some router)

  // The outlet ships a 150ms cross-fade; swap immediately instead.
  outlet.PageTransition <-
    ImmediatePageTransition() :> Animation.IPageTransition

  // The shared log chrome: the tabs and the composer every log page
  // posts through. It rebuilds only when the signed-in user changes,
  // so a poll's session refresh never touches the composer's draft.
  let chromeHost =
    chromeFeed {
      Env = env
      Nav = router
      Session = sessionStates
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
    routePaths |> Observable.map(fun path -> path.StartsWith "/remotes")

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
      .Background(cardBrushes)
      .BorderBrush(borderBrushes)
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
  |> Observable.subscribe(fun current ->
    let prior = previous.Value
    previous.OnNext current
    updateCompact()
    applyMode()

    if current = Narrow && prior <> Narrow then
      split.IsPaneOpen <- false)
  |> keep

  expanded
  |> Observable.subscribe(fun _ ->
    updateCompact()
    applyMode())
  |> keep

  router.Changed
  |> Observable.subscribe(fun _ ->
    if layout.Value = Narrow then
      split.IsPaneOpen <- false)
  |> keep

  // The arrangement's root: the strip and its overlays, one tree the
  // host slots under its own gates.
  let root = Panel().Children(split, composeLayer, searchLayer, undoLayer)

  let dispose() =
    let subjects: IDisposable list = [
      activeSection
      layout
      expanded
      compact
      composeOpen
      searchOpen
      undoToast
      inlineComposerBody
      previous
      sessionStates
      remotesStates
      unreadCounts
      routePaths
      accentBrushes
      onAccentBrushes
      surfaceBrushes
      backgroundBrushes
      cardBrushes
      borderBrushes
    ]

    for handle in handles do
      handle.Dispose()

    for subject in subjects do
      subject.Dispose()

    // The pages hand their lifetimes to their route context, and a
    // navigation away is what ends them. The retired arrangement
    // parks its router on the about page — the one page that asks for
    // nothing — after its route-store feed is cut, so the pages it
    // showed end with it and the store keeps the path to restore.
    navigate "/about"

  {
    Root = root
    Cheatsheet = cheatsheet
    Actions = {
      Compose = fun () -> composeOpen.OnNext true
      Search = fun () -> searchOpen.OnNext true
      Escape =
        fun () ->
          match searchOpen.Value, composeOpen.Value with
          | true, _ -> closeSearch()
          | _, true -> closeCompose()
          | _ ->
            match window.WindowState with
            | WindowState.FullScreen -> window.WindowState <- WindowState.Normal
            | _ -> ()
      Navigate = navigate
      ToggleFullScreen =
        fun () ->
          match window.WindowState with
          | WindowState.FullScreen -> window.WindowState <- WindowState.Normal
          | _ -> window.WindowState <- WindowState.FullScreen
      WalkCards =
        fun step -> Shortcuts.moveCardFocus(env.Dispatcher, window, step)
    }
    OverlayOpen = fun () -> composeOpen.Value || searchOpen.Value
    Navigate = navigate
    Narrow = narrow
    ToggleDrawer = fun () -> split.IsPaneOpen <- not split.IsPaneOpen
    Dispose = dispose
  }
