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

  /// The detail column: the pane of the checked-out target — rebuilt
  /// only when the target moves — or the quiet invitation. The column
  /// owns the one note stage its panes play through.
  val column:
    props: Props<'env> *
    model: IObservable<State.Model> *
    dispatch: Dispatch<State.Msg> ->
      Control
