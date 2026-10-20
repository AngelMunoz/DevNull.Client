module DevNull.Client.Views.LogView

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Stores
open Navs

/// The log page: one of the three logs, its timeline, and its pager.
/// The page needs the signed request workers and the navigation target.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
  Log: LogKind
  Session: SignedInSession
  Nav: INavigable
}

/// One log timeline, self-contained: mounts the MVUish program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
