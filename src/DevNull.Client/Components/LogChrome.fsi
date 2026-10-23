module DevNull.Client.Components.LogChrome

open System
open Avalonia.Controls
open Navs
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.AttachmentSources
open DevNull.Client.ImageCache
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Timeline

/// The shared log chrome: the three log tabs and the one composer
/// every tab posts through, the way the site's log header does. The
/// chrome lives above the router outlet; a click on the active tab
/// asks the timeline service to reload (through its gate) and return
/// the feed to its top.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideTimeline
  and 'env :> IProvideSshKeys> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
  /// The live route path, seeded with the boot url: the strip
  /// underlines the tab it names and the chrome hides off the log.
  Path: IObservable<string>
}

/// Builds the chrome: the tab strip over the composer rail.
val view: props: Props<'env> -> Control
