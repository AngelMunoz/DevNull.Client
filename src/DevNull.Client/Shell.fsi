module DevNull.Client.Shell

open Avalonia.Controls
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Icons
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.Unread
open Navs

/// The one window of the app: the sidebar, the router outlet, and the
/// forwarded sign-in callback. The env names every capability the pages
/// use.
type Shell<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvidePkceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideSettings
  and 'env :> IProvideLogging
  and 'env :> IProvideUnread> =
  new: env: 'env * callback: string option -> Shell<'env>
  inherit Window

  /// Walks the window in and out of fullscreen.
  member ToggleFullScreen: unit -> unit

  /// Leaves fullscreen when it holds; other states stay as they are.
  member ExitFullScreen: unit -> unit

  /// Closes for real: the close-to-tray flag never hides this path.
  member Quit: unit -> unit
