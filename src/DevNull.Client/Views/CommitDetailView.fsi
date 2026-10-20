module DevNull.Client.Views.CommitDetailView

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Stores
open Navs

/// The commit thread: the commit, its ancestors, its replies, and the
/// reply composer.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> = {
  Env: 'env
  CommitId: string
  Session: SignedInSession
  Nav: INavigable
}

/// One commit thread, self-contained: mounts the MVUish program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
