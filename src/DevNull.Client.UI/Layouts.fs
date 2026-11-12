module DevNull.Client.Layouts

open System
open Avalonia.Controls
open Navs
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.WebsiteLayout
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

/// The built-ins for one env: the layouts in settings order, and the
/// routes every surface shares. The routes are built once here — the
/// host keeps the router for the tray and the sign-in callback, and
/// the layout factories close over the same one, so a future switch
/// keeps the router and its path alive.
type BuiltIns = {
  Layouts: RegisteredLayout list
  Router: IRouter<Control>
  BootUrl: string
}

let builtIn
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvidePkceSource & #IProvideMachineName & #IProvideSigner & #IProvideHttp & #IProvideDispatcher & #IProvideSession & #IProvideSettings & #IProvideLogging & #IProvideUnread & #IProvideUserSettings & #IProvideTranslations & #IProvideUiClock & #IProvideImageCache & #IProvideAttachmentSources & #IProvideLastVisit & #IProvideRecentSearches & #IProvideProfiles & #IProvideTimeline & #IProvideRoutes & #IProvideSshKeys & #IProvideRemotes & #IProvideBrowser & #IProvideNatives,
    undoWindow: TimeSpan
  ) : BuiltIns =
  let routes = WebsiteLayout.buildRoutes env

  // The default layout's intents are the identity mapping to routes:
  // the tray's Notifications and Saved land on the pages they name,
  // and the two compose intents open the arrangement's own dialogs.
  let current(host: IHostSurface) : ILayout =
    let site =
      WebsiteLayout.mount {
        Env = env
        Router = routes.Router
        BootUrl = routes.BootUrl
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
          | LayoutIntent.OpenNotifications -> site.Navigate "/notifications"
          | LayoutIntent.OpenSaved -> site.Navigate "/saved"
          | LayoutIntent.ComposeNow -> site.Actions.Compose()
          | LayoutIntent.SearchNow -> site.Actions.Search()

      interface IDisposable with
        member _.Dispose() = site.Dispose()
    }

  {
    Layouts = [ { Name = "current"; Mount = current } ]
    Router = routes.Router
    BootUrl = routes.BootUrl
  }
