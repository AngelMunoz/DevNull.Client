module DevNull.Client.Components.WhoToFollow

open Avalonia.Controls
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.RouteStore
open DevNull.Client.Stores

/// Everything the aside rail reads: the signed account, the env whose
/// route store sets its context, and where a row opens.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideRoutes
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  OnOpen: string -> unit
}

/// Who to follow in the wide aside: three people and three spares,
/// refreshed a round at a time, with the page's context when it sits
/// beside a profile or a commit thread.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideRoutes
  and 'env :> IProvideLogging> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
