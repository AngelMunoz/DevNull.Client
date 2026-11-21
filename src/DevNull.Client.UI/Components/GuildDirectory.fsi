module DevNull.Client.Components.GuildDirectory

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Stores

/// The directory surface every guild-hosting layout mounts, the site's
/// own directory page: the title and its line, the search row with its
/// button and helper, the count bar — how many guilds the directory
/// holds and which of how many pages — and one numbered page of guild
/// cards in a two-column grid. The wire caps a page at twenty-four
/// cards, so one page is all the grid ever holds. The page reads its
/// first page when it mounts; the Search button and the pager's
/// chevrons ask the other pages; the 404 the flag-off server answers
/// stands as one line where the surface would. The open of one card is
/// the host's; the surface never routes. Join and Create a guild stay
/// web-only: the signed API offers no write for either.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache> = {
  Env: 'env
  SignedIn: SignedInSession
  OnOpen: Guild -> unit
  Title: string
}

/// The directory panel: mounts the MVUish program over its own
/// directory reads. Replace-only: a host builds one and never re-shows
/// it, and the first detach ends the program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
