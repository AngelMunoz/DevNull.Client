module DevNull.Client.Views.NotificationsView

open Avalonia.Controls
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Stores
open Navs

/// The notification inbox.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
}

/// One inbox, self-contained: mounts the MVUish program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
