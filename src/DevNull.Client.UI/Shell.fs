module DevNull.Client.Shell

open System
open System.Reactive.Subjects
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
open FSharp.Control.Reactive
open Microsoft.Extensions.Logging
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.DeviceAuth
open DevNull.Client.Env
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.Layouts
open DevNull.Client.Natives
open DevNull.Client.Navigation
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Remotes
open DevNull.Client.RouteStore
open DevNull.Client.SettingsStore
open DevNull.Client.Shortcuts
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.Timeline
open DevNull.Client.TranslationCache
open DevNull.Client.Tray
open DevNull.Client.UiClock
open DevNull.Client.Unread
open DevNull.Client.UserSettingsStore

// The client surface fills the window behind the caption row. The
// window paints the themed background to its edges: a transparency hint
// left the resize border transparent, and the desktop showed through
// beside the rail.
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
  and 'env :> IProvideBrowser
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

    let settings = (env :> IProvideSettings).Settings

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

    // The app's routes: the router every surface shares — the tray,
    // the sign-in callback, and the mounted layout — and the URL the
    // first navigation landed on. The registry builds them once, so a
    // future layout switch keeps the router and its path alive.
    let registry = Layouts.builtIn(env, undoWindow)
    let router = registry.Router

    // The route store carries the live path to any chrome that follows
    // the route; the host feeds it for the app lifetime. The boot
    // navigation predates the subscription, so its route lands by
    // hand.
    env.Routes.Land registry.BootUrl

    router.Changed
    |> Observable.add(fun snapshot ->
      snapshot.Route
      |> ValueOption.iter(fun route ->
        log.LogDebug("The route landed at {Url}.", route.Path)
        env.Routes.Land route.Path))

    // The mounted layout: the host offers the window surface, and the
    // registry's first entry — the default — mounts the website
    // arrangement in the content slot.
    let surface = {
      new IHostSurface with
        member _.Window = this

        member _.Active = windowActive

        // The live switch lands with the registry's second entry;
        // one layout is mounted today.
        member _.Switch _ = ()
    }

    let site = (List.head registry.Layouts).Mount surface

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

    let windowState = this.GetObservable(Window.WindowStateProperty)

    let bandPanel =
      band {
        Narrow = site.Narrow
        ToggleDrawer = site.ToggleDrawer
        WindowState = windowState
        Minimize = fun () -> this.WindowState <- WindowState.Minimized
        ToggleMaximize =
          fun () ->
            match this.WindowState with
            | WindowState.Maximized -> this.WindowState <- WindowState.Normal
            | _ -> this.WindowState <- WindowState.Maximized
        Close = fun () -> this.Close()
      }

    // The window's one tree: the mounted layout under the terms gate
    // and the band, so every layout inherits the gate.
    this.Content <- Panel().Children(site.Root, termsLayer, bandPanel)

    // The inbox marks everything read as it lists, so the poller stops
    // while the inbox is on screen. The gate reads the live route the
    // host feeds to the route store, the path every layout shares.
    let unreadPoll =
      env.Unread.Start(
        env.Routes.Path
        |> Observable.map(fun path ->
          Navigation.sectionOf path = ValueSome "notifications"),
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
    // host owns the tunnel; the mounted layout owns the actions it
    // reaches, so no route outlives its page through it.
    Shortcuts.install {
      Root = this
      Actions = site.Actions
      OverlayOpen = site.OverlayOpen
    }

    match callback with
    | Some uri ->
      log.LogInformation("The sign-in callback arrived while the window runs.")

      asyncEx {
        do! DeviceAuth.finish(env, uri, log) |> Async.Ignore

        site.Navigate "/signin"
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
