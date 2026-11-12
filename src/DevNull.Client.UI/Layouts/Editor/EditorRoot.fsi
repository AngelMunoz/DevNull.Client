module DevNull.Client.Editor.EditorRoot

open System
open Avalonia.Controls
open Avalonia.Threading
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Env
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.Stores
open DevNull.Client.UiClock

/// What the editor frame needs from its host: the signed workers, the
/// signed-in session, the stored ref, the viewer's lane, the zone the
/// times show in, the saved count, the host's active stream, and the
/// clock's first reading. The queue count is read by the frame itself.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideRemotes
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideDispatcher
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Stored: StoredState
  Lane: Lane option
  Zone: Zone
  Saved: IObservable<int>
  Active: IObservable<bool>
  Now: unit -> DateTimeOffset
  /// The host's switch back to the default layout.
  OnUseDefaultLayout: unit -> unit
}

/// Mounts the editor frame: the activity bar, the explorer, the tab
/// strip, the pinned history tab, the panel host, and the status strip
/// at the width band the root's own size selects. The caller owns the
/// returned handle and disposes it once when the layout unmounts.
val mount: dispatcher: IDispatcher * props: Props<'env> -> Control * IDisposable
