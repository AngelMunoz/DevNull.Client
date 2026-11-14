module DevNull.Client.Layouts

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.WebsiteLayout
open DevNull.Client.Workbench
open DevNull.Client.Editor
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Formatting
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Remotes
open DevNull.Client.RouteStore
open DevNull.Client.SettingsStore
open DevNull.Client.Shortcuts
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Timeline
open DevNull.Client.Unread
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore
open DevNull.Client.TranslationCache

/// What the active layout can be asked to do from window-level
/// surfaces: the tray menu, the key tunnel, the host itself.
type LayoutIntent =
  | OpenHome
  | OpenNotifications
  | OpenSaved
  | ComposeNow
  | SearchNow

/// One mounted layout: its control tree, its programs, its intents.
type ILayout =
  inherit IDisposable

  abstract Name: string

  abstract Root: Control

  abstract Actions: Actions

  abstract Cheatsheet: CheatsheetItem list

  abstract OverlayOpen: unit -> bool

  abstract Navigate: url: string -> unit

  abstract Narrow: IObservable<bool>

  abstract ToggleDrawer: unit -> unit

  abstract Handle: intent: LayoutIntent -> unit

/// What a layout may ask of its window. No layout subscribes to a host
/// observable directly; values enter through the layout's own `Sub`
/// set.
type IHostSurface =
  abstract Window: Window

  abstract Active: IObservable<bool>

  abstract Switch: name: string -> unit

/// One registered layout: its wire name and the factory that mounts
/// it. A factory closes over the env it was registered with, so the
/// registry carries no generic parameter.
type RegisteredLayout = {
  Name: string
  Mount: IHostSurface -> ILayout
}

let builtIn
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvidePkceSource & #IProvideMachineName & #IProvideSigner & #IProvideHttp & #IProvideDispatcher & #IProvideSession & #IProvideSettings & #IProvideLogging & #IProvideUnread & #IProvideUserSettings & #IProvideTranslations & #IProvideUiClock & #IProvideImageCache & #IProvideAttachmentSources & #IProvideLastVisit & #IProvideRecentSearches & #IProvideProfiles & #IProvideTimeline & #IProvideRoutes & #IProvideSshKeys & #IProvideRemotes & #IProvideBrowser & #IProvideNatives,
    undoWindow: TimeSpan
  ) : RegisteredLayout list =
  // The default layout's intents are the identity mapping to routes:
  // the tray's home, Notifications, and Saved land on the pages they
  // name, and the two compose intents open the arrangement's own
  // dialogs.
  let current(host: IHostSurface) : ILayout =
    let site =
      WebsiteLayout.mount {
        Env = env
        Window = host.Window
        UndoWindow = undoWindow
      }

    {
      new ILayout with
        member _.Name = "current"
        member _.Root = site.Root
        member _.Actions = site.Actions
        member _.Cheatsheet = site.Cheatsheet
        member _.OverlayOpen() = site.OverlayOpen()
        member _.Navigate(url) = site.Navigate url
        member _.Narrow = site.Narrow
        member _.ToggleDrawer() = site.ToggleDrawer()

        member _.Handle(intent) =
          match intent with
          | LayoutIntent.OpenHome -> site.Navigate "/log/latest"
          | LayoutIntent.OpenNotifications -> site.Navigate "/notifications"
          | LayoutIntent.OpenSaved -> site.Navigate "/saved"
          | LayoutIntent.ComposeNow -> site.Actions.Compose()
          | LayoutIntent.SearchNow -> site.Actions.Search()

      interface IDisposable with
        member _.Dispose() = site.Dispose()
    }

  // The workbench arrangement's zone: the device's own zone, or the
  // one the person's settings name, kept as the pages keep it.
  let workbenchZone(settings: UserSettings option) : Zone =
    let timezone =
      settings
      |> Option.bind(fun current -> Some current.Timezone)
      |> Option.defaultValue FollowDevice

    match timezone with
    | FollowDevice -> {
        Info = TimeZoneInfo.Local
        Label = TimeZoneInfo.Local.Id
      }
    | ZoneOf(id, _) ->
      let found =
        try
          TimeZoneInfo.FindSystemTimeZoneById(id)
        with _ ->
          TimeZoneInfo.Local

      { Info = found; Label = id }

  let fullscreenOf(surface: IHostSurface) : unit -> unit =
    fun () ->
      match surface.Window.WindowState with
      | WindowState.FullScreen ->
        surface.Window.WindowState <- WindowState.Normal
      | _ -> surface.Window.WindowState <- WindowState.FullScreen

  // Each wide layout's real keys, for the host's cheatsheet.
  let workbenchCheatsheet = [
    {
      Keys = [ "c" ]
      Words = "open the staging composer"
    }
    {
      Keys = [ "Ctrl"; "K" ]
      Words = "open the search palette"
    }
    {
      Keys = [ "Esc" ]
      Words = "close the top overlay"
    }
    {
      Keys = [ "F11" ]
      Words = "fullscreen"
    }
  ]

  let editorCheatsheet = [
    {
      Keys = [ "c" ]
      Words = "open the terminal"
    }
    {
      Keys = [ "Ctrl"; "K" ]
      Words = "open the command palette"
    }
    {
      Keys = [ "Esc" ]
      Words = "close the top overlay"
    }
    {
      Keys = [ "F11" ]
      Words = "fullscreen"
    }
  ]

  // A layout without its own sign-in surface holds one line and the
  // switch back; the host's sign-in surface stays the site's (gap
  // H-0d).
  let signedOutLine(name: string, surface: IHostSurface) : ILayout =
    let root =
      StackPanel()
        .Spacing(12.0)
        .Margin(16.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .Text($"The {name} signs in through the site layout."),
          Button()
            .Classes(Theme.name Theme.AccentButton)
            .Content(TextBlock().Text("Use default layout"))
            .OnClickHandler(fun _ _ -> surface.Switch "current")
        )

    {
      new ILayout with
        member _.Name = name
        member _.Root = root

        member _.Actions = {
          Compose = ignore
          Search = ignore
          Escape = ignore
          Navigate = ignore
          ToggleFullScreen = fullscreenOf surface
          WalkCards = ignore
        }

        member _.OverlayOpen() = false
        member _.Navigate _ = ()
        member _.Narrow = Observable.single false
        member _.ToggleDrawer() = ()
        member _.Handle _ = ()
        member _.Cheatsheet = []

      interface IDisposable with
        member _.Dispose() = ()
    }

  let workbench(surface: IHostSurface) : ILayout =
    let session = (env :> IProvideSession).Session

    match session.Current with
    | SignedOut -> signedOutLine("workbench", surface)
    | SignedIn _ ->
      let settings = (env :> IProvideSettings).Settings

      // The sinks can reach the host's message path only after the
      // mount exists; the slot is filled once, right after the call
      // returns, before any message can run. Without the fill the
      // author and profile sinks would stay silent.
      let sendToWorkbench: (WorkbenchHost.Msg -> unit) ref = ref ignore

      let workbenchRoot, workbenchLifetime =
        WorkbenchHost.mount(
          (env :> IProvideDispatcher).Dispatcher,
          {
            Env = env
            Initial = WorkbenchSettings.parse settings.Current.WorkbenchState
            Images = (env :> IProvideImageCache).ImageCache
            Now = (env :> IProvideUiClock).UiClock.Minute
            Zone =
              workbenchZone((env :> IProvideUserSettings).UserSettings.Current)
            OnOpenAuthor =
              fun handle ->
                let send = sendToWorkbench.Value
                send(WorkbenchHost.RefSelected(Build.repository handle))
            OnOpenExternal =
              fun url -> (env :> IProvideBrowser).Browser.Open url |> ignore
            OnStateChanged =
              fun state ->
                settings.SetWorkbenchState(WorkbenchSettings.store state)
            OnUseDefaultLayout = fun () -> surface.Switch "current"
          }
        )

      sendToWorkbench.Value <-
        (workbenchLifetime :?> WorkbenchHost.MountedHost).Send

      {
        new ILayout with
          member _.Name = "workbench"
          member _.Root = workbenchRoot

          member _.Actions = {
            Compose = ignore
            Search = ignore
            Escape = ignore
            Navigate = ignore
            ToggleFullScreen = fullscreenOf surface
            WalkCards = ignore
          }

          member _.OverlayOpen() = false
          member _.Cheatsheet = workbenchCheatsheet
          member _.Navigate _ = ()
          member _.Narrow = Observable.single false
          member _.ToggleDrawer() = ()

          member _.Handle _ = ()

        interface IDisposable with
          member _.Dispose() = workbenchLifetime.Dispose()
      }

  // The editor arrangement: the frame mounts as content, and the
  // window's key verbs ride its own surfaces once its command seam
  // lands; the palette's own switch-back already reaches the host.
  let editor(surface: IHostSurface) : ILayout =
    let session = (env :> IProvideSession).Session

    match session.Current with
    | SignedOut -> signedOutLine("editor", surface)
    | SignedIn signed ->
      let settings = (env :> IProvideSettings).Settings

      let mounted =
        EditorRoot.mountFor(
          (env :> IProvideDispatcher).Dispatcher,
          {
            Env = env
            Session = signed
            Stored = EditorSettings.parse settings.Current.EditorState
            Lane = None
            Zone =
              workbenchZone((env :> IProvideUserSettings).UserSettings.Current)
            Saved = Observable.single 0
            Active = surface.Active
            Now = fun () -> DateTimeOffset.Now
            OnUseDefaultLayout = fun () -> surface.Switch "current"
          }
        )

      {
        new ILayout with
          member _.Name = "editor"
          member _.Root = mounted.Root

          member _.Actions = {
            Compose = fun () -> mounted.Send EditorRoot.OpenTerminal
            Search = fun () -> mounted.Send EditorRoot.OpenPalette
            Escape = fun () -> mounted.Send EditorRoot.Escape
            Navigate = ignore
            ToggleFullScreen = fullscreenOf surface
            WalkCards = ignore
          }

          member _.OverlayOpen() = mounted.OverlayOpen()
          member _.Cheatsheet = editorCheatsheet
          member _.Navigate _ = ()
          member _.Narrow = Observable.single false
          member _.ToggleDrawer() = ()

          member _.Handle(intent) =
            match intent with
            | LayoutIntent.ComposeNow -> mounted.Send EditorRoot.OpenTerminal
            | LayoutIntent.SearchNow -> mounted.Send EditorRoot.OpenPalette
            | LayoutIntent.OpenHome
            | LayoutIntent.OpenNotifications
            | LayoutIntent.OpenSaved -> ()

        interface IDisposable with
          member _.Dispose() = (mounted :> IDisposable).Dispose()
      }

  [
    { Name = "current"; Mount = current }
    {
      Name = "workbench"
      Mount = workbench
    }
    { Name = "editor"; Mount = editor }
  ]
