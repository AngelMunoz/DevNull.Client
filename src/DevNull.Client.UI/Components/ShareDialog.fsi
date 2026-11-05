module DevNull.Client.Components.ShareDialog

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Remotes
open DevNull.Client.Stores

/// Everything the share dialog reads: the env its candidate search and
/// its send use, the signed account it sends as, the approved commit it
/// shares, the recipient it hands back after a send, and its own close.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideRemotes> = {
  Env: 'env
  Session: SignedInSession
  Commit: Commit
  Sent: User -> unit
  Close: unit -> unit
}

/// The share flow: find a developer the commit can reach, see the
/// commit once more, and send its link as a direct message. Built on
/// open, torn down on detach.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideRemotes> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
