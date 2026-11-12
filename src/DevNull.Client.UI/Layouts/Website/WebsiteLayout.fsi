module DevNull.Client.WebsiteLayout

open System
open Avalonia.Controls
open Navs
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
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
open DevNull.Client.Views.Routes

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
  Router: IRouter<Control>
  BootUrl: string
  Window: Window
  /// How long the delete-undo toast stands; the window's own value,
  /// shortened only by the tests.
  UndoWindow: TimeSpan
}

/// The mounted website arrangement: the root control the host slots
/// in, the key actions and overlay probe the host's tunnel reads, the
/// navigation window-level intents fall through to, the narrow band
/// and the drawer toggle the host's band binds, and the unmount that
/// releases every handle the arrangement wired.
type Mount = {
  Root: Control
  Actions: Actions
  OverlayOpen: unit -> bool
  Navigate: string -> unit
  Narrow: IObservable<bool>
  ToggleDrawer: unit -> unit
  Dispose: unit -> unit
}

/// Builds the app's route table and the router. The first navigation
/// (`/log/latest` signed in, `/signin` signed out) is already in
/// flight when this returns.
val buildRoutes:
  env: 'env -> RouterShell
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvidePkceSource
    and 'env :> IProvideMachineName
    and 'env :> IProvideHttp
    and 'env :> IProvideDispatcher
    and 'env :> IProvideSession
    and 'env :> IProvideUiClock
    and 'env :> IProvideImageCache
    and 'env :> IProvideAttachmentSources
    and 'env :> IProvideLastVisit
    and 'env :> IProvideRecentSearches
    and 'env :> IProvideProfiles
    and 'env :> IProvideTimeline
    and 'env :> IProvideSshKeys
    and 'env :> IProvideSettings
    and 'env :> IProvideUserSettings
    and 'env :> IProvideTranslations
    and 'env :> IProvideLogging
    and 'env :> IProvideRemotes
    and 'env :> IProvideBrowser
    and 'env :> IProvideNatives

/// Mounts the website arrangement — the dev-null site's own frame: the
/// responsive sidebar, the reading column, the aside, the route
/// outlet, and the compose, search, help, and undo overlays. The
/// arrangement owns its programs and its feeds; `Dispose` releases
/// them.
val mount: props: Props<'env> -> Mount
