module DevNull.Client.Views.SignInView

open Avalonia.Controls
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Stores
open Navs

/// The sign-in page: starts the device authorization, waits for the
/// browser approval, and hands the session to the store. `OnDone` runs
/// once the session is recorded, so the caller navigates away.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvidePkceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideLogging> = {
  Env: 'env
  Nav: INavigable
  OnDone: unit -> unit
  /// The route the page lives in; owns the page's program lifetime.
  Context: RouteContext
}

/// One sign-in screen, self-contained: mounts the MVUish program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvidePkceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideLogging> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
