module DevNull.Client.Components.TermsDialog

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Stores

/// Everything the terms gate reads: the env its acceptance call needs,
/// the signed account whose terms version it records, and the
/// refreshed account it hands back.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  OnAccepted: Me -> unit
}

/// The one dialog that blocks until the account accepts the terms in
/// force: the policy links, the acceptance box, and Accept, which
/// records the version the app showed and hands the refreshed account
/// back. There is no close: the terms are answered, not skipped.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideLogging> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
