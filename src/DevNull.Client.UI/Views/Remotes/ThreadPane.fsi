namespace DevNull.Client.Views

open System
open Avalonia.Controls
open MVUish.Avalonia

/// The open thread: the detail column's reading pane. The pane itself
/// rebuilds only when the checked-out target moves; everything inside
/// follows the model stream, so a poll refresh never rebuilds a row.
module ThreadPane =

  /// The detail column: the pane of the checked-out target — rebuilt
  /// only when the target moves — or the quiet invitation.
  val column:
    props: Props<'env> *
    model: IObservable<State.Model> *
    dispatch: Dispatch<State.Msg> ->
      Control
