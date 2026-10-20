module DevNull.Client.Shell

open Avalonia.Controls
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Icons
open DevNull.Client.Stores
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
  and 'env :> IProvideLogging> =
  new: env: 'env * callback: string option -> Shell<'env>
  inherit Window
