module DevNull.Client.Views.SearchView

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
open DevNull.Client.Timeline
open DevNull.Client.UiClock

/// Everything the search page reads: the env it searches through, the
/// signed account, the term the route opened with (`?q=`), the recents
/// it shows beside an empty box, and the router its picks open on.
/// `Searches` is the page's own commit session; the page class builds
/// one over the env when none is handed in.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideTimeline
  and 'env :> IProvideLogging> = {
  Env: 'env
  SignedIn: SignedInSession
  Term: string
  Recents: RecentSearchStore
  Searches: Session
  Nav: INavigable
  Context: RouteContext
}

/// The search results page: commits by meaning, as the site's own
/// results page shows them.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideTimeline
  and 'env :> IProvideLogging> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
