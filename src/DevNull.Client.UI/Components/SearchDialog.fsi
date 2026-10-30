module DevNull.Client.Components.SearchDialog

open System
open Avalonia.Controls
open Navs
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Search
open DevNull.Client.Stores
open DevNull.Client.UiClock

/// Everything the search dialog reads: the env it searches through,
/// the signed account it searches as, the session that owns the
/// debounced calls, the recents it shows when the box is empty, the
/// router it opens finds on, and the two things beside finding —
/// opening the composer and closing itself.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging> = {
  Env: 'env
  SignedIn: SignedInSession
  Searches: Session
  Recents: RecentSearchStore
  Nav: INavigable
  OpenCompose: unit -> unit
  Close: unit -> unit
}

/// The search dialog: the site's command palette over the same two
/// searches. Built on open, torn down on detach.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
