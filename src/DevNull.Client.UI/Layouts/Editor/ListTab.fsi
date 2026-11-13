module DevNull.Client.Layouts.Editor.ListTab

open System
open Avalonia.Controls
open Avalonia.Threading
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.UiClock

/// The two ordinary list tabs the explorer opens: the saved commits
/// and the scheduled queue.
type Kind =
  | Saved
  | Queue

/// What one list tab needs from the frame: the signed workers, the
/// session, the zone the times show in, the clock, and the thread
/// opener its saved rows use.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache> = {
  Env: 'env
  Session: SignedInSession
  Kind: Kind
  Zone: Zone
  Now: unit -> DateTimeOffset
  OnOpenThread: Commit -> unit
}

/// The strip title of one list tab.
val title: kind: Kind -> string

/// Starts one list tab. The caller owns the returned handle and
/// disposes it when the tab closes.
val mount: dispatcher: IDispatcher * props: Props<'env> -> Control * IDisposable
