namespace DevNull.Client.Views

open System
open Avalonia.Controls
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Remotes
open DevNull.Client.SettingsStore
open MVUish.Avalonia

/// The open thread: the detail column's reading pane. The pane itself
/// rebuilds only when the checked-out target moves; everything inside
/// follows the model stream, so a poll refresh never rebuilds a row.
module Thread =

  /// Only one note sounds at a time, the site's own rule: each claim
  /// stops the playback that held the stage before it.
  type Solo =
    new: unit -> Solo
    member Claim: playback: IAudioPlayback -> unit

  /// The detail column: the pane of the checked-out target — rebuilt
  /// only when the target moves — or the quiet invitation.
  val column:
    props: Props<'env> *
    model: IObservable<State.Model> *
    dispatch: Dispatch<State.Msg> *
    solo: Solo ->
      Control
