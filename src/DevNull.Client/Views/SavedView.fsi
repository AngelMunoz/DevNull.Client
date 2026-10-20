module DevNull.Client.Views.SavedView

open Avalonia.Controls
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Stores
open Navs

/// The saved commits of the signed-in user.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
}

/// One saved list, self-contained: mounts the MVUish program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
