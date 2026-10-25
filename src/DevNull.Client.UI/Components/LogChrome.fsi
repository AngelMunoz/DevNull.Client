module DevNull.Client.Components.LogChrome

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.AttachmentSources
open DevNull.Client.ImageCache
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Timeline
open DevNull.Client.RouteStore

/// The shared log chrome: the three log tabs and the one composer
/// every tab posts through, the way the site's log header does. The
/// chrome lives above the router outlet; a click on the active tab
/// asks the timeline service to reload (through its gate) and return
/// the feed to its top. The chrome follows the route store through its
/// program's subscriptions; the page ends on its first detach, and the
/// chrome is replaced, never re-shown.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideTimeline
  and 'env :> IProvideRoutes
  and 'env :> IProvideSshKeys> = {
  Env: 'env
  Session: SignedInSession
  Nav: Navs.INavigable
}

/// The log chrome page: the tab strip over the composer rail.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideTimeline
  and 'env :> IProvideRoutes
  and 'env :> IProvideSshKeys> =
  inherit UserControl

  new: props: Props<'env> -> Page<'env>
