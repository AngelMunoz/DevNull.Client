module DevNull.Client.Components.GoLiveDialog

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Stores

/// Everything the go-live dialog reads: the env its calls need,
/// the session they sign for, where a landed stream goes, and where
/// the dialog closes.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher> = {
  Env: 'env
  Session: SignedInSession
  /// Where a landed stream goes: the caller refetches its profile.
  WentLive: Result<Me, AppError> -> unit
  /// Where the dialog closes without starting a stream.
  Close: unit -> unit
}

/// The go-live dialog: the stream link, for how many hours, and the
/// submit. The server's refusal stays in the dialog until the next
/// words fix it; a landed stream closes the dialog and reports back.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
