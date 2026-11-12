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

  // Every switch ask, from a mounted layout, from the window's own
  // ask, or from a pick in Settings once a second choice ships. The
  // feed breaks the loop between the host surface and the switch
  // itself.
  let switchAsked = new System.Reactive.Subjects.Subject<string>()

  // The slot's registry: the built-ins plus whatever the window
  // registered at run time.
  let registered = ResizeArray<RegisteredLayout>()

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

    // The layout registry: the built-ins plus whatever the window
    // registers at run time. Each layout builds its own routes at
    // mount and restores the path the route store kept, so a switch
    // needs no shared router — the tray asks intents and the callback
    // rides the active layout's own navigation.
    let registry = Layouts.builtIn(env, undoWindow)

    registered.AddRange(registry)

    // The host surface every layout receives at mount: the window it
    // stands in, the visibility gate its own loops may follow, and the
    // switch ask that lands on the feed below.
    let surface = {
      new IHostSurface with
        member _.Window = this

        member _.Active = windowActive

        member _.Switch name = switchAsked.OnNext name
    }

    // The mounted layout: the host offers the window surface, and the
    // registry mounts the arrangement the stored choice names; a
    // name no entry answers falls back to the default, with one line.
    let mountOf(name: string) : ILayout =
      match registered |> Seq.tryFind(fun entry -> entry.Name = name) with
      | Some entry -> entry.Mount surface
      | None ->
        log.LogWarning(
          "The layout {Name} is not registered; the default layout mounts.",
          name
        )

        (List.head registry).Mount surface

    let mounted =
      new BehaviorSubject<ILayout>(
        mountOf(layoutString settings.Current.Layout)
      )

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

    // The band follows the active layout: the narrow band rides the
    // layout the slot holds — switched as the slot swaps — and the
    // drawer toggle asks whatever layout stands in it, so no band
    // binding outlives the layout it was built against.
    let activeNarrow =
      mounted |> Observable.switchMap(fun layout -> layout.Narrow)

    let bandPanel =
      band {
        Narrow = activeNarrow
        ToggleDrawer = fun () -> mounted.Value.ToggleDrawer()
        WindowState = windowState
        Minimize = fun () -> this.WindowState <- WindowState.Minimized
        ToggleMaximize =
          fun () ->
            match this.WindowState with
            | WindowState.Maximized -> this.WindowState <- WindowState.Normal
            | _ -> this.WindowState <- WindowState.Maximized
        Close = fun () -> this.Close()
      }

    // The window's one tree: the content slot under the terms gate and
    // the band, so every layout inherits the gate and the chrome.
    let content = Panel().Children(mounted.Value.Root, termsLayer, bandPanel)

    this.Content <- content

    // The live switch: the retired layout lets go of everything it
    // wired, the named one mounts in the same slot, and focus lands on
    // the new root, so no key press reaches a dead control. One frame:
    // the window, the gates, the band, and the loops never rebuild.
    let switchTo(name: string) : unit =
      if name <> mounted.Value.Name then
        let retired = mounted.Value

        retired.Dispose()
        content.Children.RemoveAt(0)

        let next = mountOf name

        content.Children.Insert(0, next.Root)
        mounted.OnNext next
        next.Root.Focus() |> ignore

    // One ask, one path: a layout's own Switch call, the window's own
    // ask, and — once a second choice ships — a pick in Settings all
    // land here. The stored choice is written first, so it persists
    // even if the app dies mid-switch; a name no choice backs (a
    // layout registered at run time) mounts for this run only.
    switchAsked
    |> Observable.add(fun name ->
      match parseLayout name with
      | Some choice -> settings.SetLayout choice
      | None ->
        log.LogDebug(
          "The layout {Name} carries no stored choice; it mounts for this run only.",
          name
        )

      switchTo name)

    // The stored choice drives the slot: the replay carries the choice
    // the window opened with, which is already mounted, and a later
    // change mounts what it names.
    settings.Observe
    |> Observable.map(fun value -> layoutString value.Layout)
    |> Observable.distinctUntilChanged
    |> Observable.add(fun name ->
      if name <> mounted.Value.Name then
        switchTo name)

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
      Intents = fun intent -> mounted.Value.Handle intent
      Unread = env.Unread.Count
      Log = log
      Quit =
        fun () ->
          log.LogInformation("The client quits from the tray.")
          quitRequested.OnNext true
          this.Close()
    }

    // The one key table, tunneled ahead of every focused control. The
    // host owns the tunnel; every ask reads the active layout, so a
    // switch rebinds the table without a second install, and no route
    // outlives its page through it.
    Shortcuts.install {
      Root = this
      Actions = {
        Compose = fun () -> mounted.Value.Actions.Compose()
        Search = fun () -> mounted.Value.Actions.Search()
        Help = fun () -> mounted.Value.Actions.Help()
        Escape = fun () -> mounted.Value.Actions.Escape()
        Navigate = fun url -> mounted.Value.Actions.Navigate url
        ToggleFullScreen = fun () -> mounted.Value.Actions.ToggleFullScreen()
        WalkCards = fun step -> mounted.Value.Actions.WalkCards step
      }
      OverlayOpen = fun () -> mounted.Value.OverlayOpen()
    }

    match callback with
    | Some uri ->
      log.LogInformation("The sign-in callback arrived while the window runs.")

      asyncEx {
        do! DeviceAuth.finish(env, uri, log) |> Async.Ignore

        mounted.Value.Navigate "/signin"
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

  /// Adds one more layout to this window's registry, beside the
  /// built-ins. The test harness registers its layouts this way.
  member this.RegisterLayout(entry: RegisteredLayout) : unit =
    registered.Add entry

  /// Asks the window to mount the layout a wire name stands for — the
  /// same ask a mounted layout makes through the host surface, and
  /// the one a pick in Settings rides once a second choice ships.
  member this.SwitchLayout(name: string) : unit = switchAsked.OnNext name
