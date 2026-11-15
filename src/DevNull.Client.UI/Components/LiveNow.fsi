module DevNull.Client.Components.LiveNow

open Avalonia.Controls
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.UiClock

/// Everything the live-now section reads: the signed account and
/// the env its calls need. The section opens a row's stream outside
/// the app.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideUiClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideBrowser> = { Env: 'env; Session: SignedInSession }

/// The followed users who are live right now, as the shell's aside
/// shows them: a card over the names and live badges, polled a
/// minute at a time and hidden while nobody is live.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideUiClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideBrowser> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
