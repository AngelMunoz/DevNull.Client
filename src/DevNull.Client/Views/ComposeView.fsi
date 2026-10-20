module DevNull.Client.Views.ComposeView

open Avalonia.Controls
open DevNull.Client.Components
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Stores
open Navs

/// The full-height composer page: a fresh commit or a quote, per the
/// route that opened it.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> = {
  Env: 'env
  Session: SignedInSession
  Target: Composer.Target
  Nav: INavigable
}

/// One compose page, self-contained: mounts the MVUish composer.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
