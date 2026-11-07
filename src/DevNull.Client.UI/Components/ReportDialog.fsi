module DevNull.Client.Components.ReportDialog

open Avalonia.Controls
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Stores

/// Everything the report dialog reads: the env that picks and sends,
/// the signed account it sends as, the commit it reports, and its own
/// close and sent edges.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideAttachmentSources> = {
  Env: 'env
  Session: SignedInSession
  Commit: Commit
  Sent: unit -> unit
  Close: unit -> unit
}

/// One commit report: a reason, optional details, up to three images
/// with an optional description each, and the signed send. Built on
/// open, torn down on detach.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideAttachmentSources> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
