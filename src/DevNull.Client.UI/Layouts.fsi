module DevNull.Client.Layouts

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

  /// The wire name, the same word `AppSettings.Layout` stores.
  abstract Name: string

  /// The root control. The host sets it as its content.
  abstract Root: Control

  /// The key actions the host's tunnel reaches: the layout's own
  /// verbs, with the window's fullscreen pair filled in.
  abstract Actions: Actions

  /// The keys the layout answers, in the order its help card shows
  /// them. The host builds the one help layer from the active
  /// layout's list.
  abstract Cheatsheet: CheatsheetItem list

  /// True while one of the layout's modal overlays holds the window.
  abstract OverlayOpen: unit -> bool

  /// Navigates the layout's own way, on the router it mounted.
  abstract Navigate: url: string -> unit

  /// True while the layout stands in its narrow band: the host's band
  /// shows its drawer button while it holds.
  abstract Narrow: IObservable<bool>

  /// Opens or closes the layout's drawer; a layout without one
  /// ignores it.
  abstract ToggleDrawer: unit -> unit

  /// Answers a window-level intent, or ignores it.
  abstract Handle: intent: LayoutIntent -> unit

/// What a layout may ask of its window. No layout subscribes to a host
/// observable directly; values enter through the layout's own `Sub`
/// set.
type IHostSurface =
  /// The window the layout is mounted in.
  abstract Window: Window

  /// True while the window stands visible on screen.
  abstract Active: IObservable<bool>

  /// Ask the host to switch layouts by wire name.
  abstract Switch: name: string -> unit

/// One registered layout: its wire name and the factory that mounts
/// it. A factory closes over the env it was registered with, so the
/// registry carries no generic parameter.
type RegisteredLayout = {
  Name: string
  Mount: IHostSurface -> ILayout
}

/// The built-in layouts, in settings order. The first entry is the
/// default layout, the one a stored name no registry knows falls back
/// to. The factories close over the env and the undo window they are
/// given here.
val builtIn:
  env: 'env * undoWindow: TimeSpan -> RegisteredLayout list
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
    and 'env :> IProvideNatives
