module DevNull.Client.WebsiteLayout

open System
open Avalonia.Controls
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

/// Everything the website arrangement reads: the env its pages and
/// dialogs call, the window the arrangement stands in, and how long
/// its undo toast stands. The arrangement builds its own route table
/// and router at mount, and restores the path the route store kept
/// while it stood down.
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
  /// How long the delete-undo toast stands; the window's own value,
  /// shortened only by the tests.
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
  /// The keys the arrangement answers, in the order its help card
  /// shows them.
  Cheatsheet: CheatsheetItem list
  OverlayOpen: unit -> bool
  Navigate: string -> unit
  Narrow: IObservable<bool>
  ToggleDrawer: unit -> unit
  Dispose: unit -> unit
}

/// Mounts the website arrangement — the dev-null site's own frame: the
/// responsive sidebar, the reading column, the aside, the route
/// outlet, and the compose, search, help, and undo overlays. The
/// arrangement owns its programs and its feeds; `Dispose` releases
/// them.
val mount: props: Props<'env> -> Mount
