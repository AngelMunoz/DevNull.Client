module DevNull.Client.Components.SearchDialog

open Avalonia.Controls
open Navs
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
  and 'env :> IProvideDispatcher
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging> = {
  Env: 'env
  SignedIn: SignedInSession
  Searches: Session
  Recents: RecentSearchStore
  Nav: INavigable
  /// The term a center host opens with; the palette leaves it empty.
  InitialTerm: string option
  OpenCompose: unit -> unit
  Close: unit -> unit
}

/// Starts the dialog as a host-owned child: the program runs on the
/// given dispatcher until the returned handle is disposed, and the
/// handle disposes the search session with it. The handle holds no
/// view, so a host may keep it for the child's whole lifetime.
val mount:
  dispatcher: Avalonia.Threading.IDispatcher * props: Props<'env> ->
    Control * System.IDisposable

/// The search dialog: the site's command palette over the same two
/// searches. Built on open, torn down on detach.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
  /// Releases the dialog's program and search session when its host
  /// closes it or it leaves the visual tree.
  interface System.IDisposable
