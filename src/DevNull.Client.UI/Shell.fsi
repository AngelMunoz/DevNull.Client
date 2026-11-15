module DevNull.Client.Shell

open System
open Avalonia.Controls
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.Layouts
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Remotes
open DevNull.Client.RouteStore
open DevNull.Client.SettingsStore
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Timeline
open DevNull.Client.Unread
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore
open DevNull.Client.TranslationCache

/// The one window of the app: the sidebar, the router outlet, and the
/// forwarded sign-in callback. The env names every capability the pages
/// use.
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
  and 'env :> IProvideNatives> =
  new:
    env: 'env * callback: string option * ?undoWindow: TimeSpan -> Shell<'env>

  inherit Window

  /// Walks the window in and out of fullscreen.
  member ToggleFullScreen: unit -> unit

  /// Leaves fullscreen when it holds; other states stay as they are.
  member ExitFullScreen: unit -> unit

  /// Closes for real: the close-to-tray flag never hides this path.
  member Quit: unit -> unit

  /// Adds one more layout to this window's registry, beside the
  /// built-ins. The test harness registers its layouts this way.
  member RegisterLayout: entry: RegisteredLayout -> unit

  /// Asks the window to mount the layout a wire name stands for — the
  /// same ask a mounted layout makes through the host surface, and
  /// the one a pick in Settings rides once a second choice ships.
  member SwitchLayout: name: string -> unit
