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

  // The workbench signs in through the site layout: signed out, the
  // arrangement holds one line and the switch back. The host's own
  // sign-in surface stays the site's (gap H-0d).
  let signedOutWorkbench(surface: IHostSurface) : ILayout =
    let root =
      StackPanel()
        .Spacing(12.0)
        .Margin(16.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .Text("The workbench signs in through the site layout."),
          Button()
            .Classes(Theme.name Theme.AccentButton)
            .Content(TextBlock().Text("Use default layout"))
            .OnClickHandler(fun _ _ -> surface.Switch "current")
        )

    {
      new ILayout with
        member _.Name = "workbench"
        member _.Root = root

        member _.Actions = {
          Compose = ignore
          Search = ignore
          Help = ignore
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

      interface IDisposable with
        member _.Dispose() = ()
    }

  let workbench(surface: IHostSurface) : ILayout =
    let session = (env :> IProvideSession).Session

    match session.Current with
    | SignedOut -> signedOutWorkbench surface
    | SignedIn _ ->
      let settings = (env :> IProvideSettings).Settings

      // The mount and its sinks meet only after both exist: the lazy
      // cell breaks the circle without a mutable slot.
      let rec mount =
        lazy
          WorkbenchHost.mountFor(
            (env :> IProvideDispatcher).Dispatcher,
            {
              Env = env
              Initial = WorkbenchSettings.parse settings.Current.WorkbenchState
              Images = (env :> IProvideImageCache).ImageCache
              Now = (env :> IProvideUiClock).UiClock.Minute
              Zone =
                workbenchZone(
                  (env :> IProvideUserSettings).UserSettings.Current
                )
              OnOpenAuthor =
                fun handle ->
                  mount
                    .Force()
                    .Send(WorkbenchHost.RefSelected(Build.repository handle))
              OnOpenExternal =
                fun url -> (env :> IProvideBrowser).Browser.Open url |> ignore
              OnStateChanged =
                fun state ->
                  settings.SetWorkbenchState(WorkbenchSettings.store state)
            }
          )

      let mounted = mount.Force()

      {
        new ILayout with
          member _.Name = "workbench"
          member _.Root = mounted.Root

          member _.Actions = {
            Compose = fun () -> mounted.Send WorkbenchHost.StagingToggled
            Search = fun () -> mounted.Send WorkbenchHost.SearchPaletteToggled
            Help = ignore
            Escape = fun () -> mounted.Escape()
            Navigate = ignore
            ToggleFullScreen = fullscreenOf surface
            WalkCards = ignore
          }

          member _.OverlayOpen() = mounted.OverlayOpen()
          member _.Navigate _ = ()
          member _.Narrow = Observable.single false
          member _.ToggleDrawer() = ()

          member _.Handle(intent) =
            match intent with
            | LayoutIntent.OpenHome ->
              mounted.Send(WorkbenchHost.RefSelected(Build.history Latest))
            | LayoutIntent.OpenNotifications ->
              mounted.Send(WorkbenchHost.RefSelected(Build.notifications))
            | LayoutIntent.OpenSaved ->
              mounted.Send(WorkbenchHost.RefSelected(Build.saved))
            | LayoutIntent.ComposeNow ->
              mounted.Send WorkbenchHost.StagingToggled
            | LayoutIntent.SearchNow ->
              mounted.Send WorkbenchHost.SearchPaletteToggled

        interface IDisposable with
          member _.Dispose() = (mounted :> IDisposable).Dispose()
      }

  [
    { Name = "current"; Mount = current }
    {
      Name = "workbench"
      Mount = workbench
    }
  ]
