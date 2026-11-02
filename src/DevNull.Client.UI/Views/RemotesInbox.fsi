namespace DevNull.Client.Views

open System
open Avalonia.Controls
open MVUish.Avalonia

/// The page's semantic regions; the view only composes these.
module Parts =

  /// The whole inbox column: the search box with the org picker beside
  /// it, the tabs under them, and the rows over the quiet line.
  val inbox:
    props: Props<'env> *
    model: IObservable<State.Model> *
    dispatch: Dispatch<State.Msg> ->
      Control

  /// The thread pane while nothing is checked out: the site's quiet
  /// invitation.
  val noThread: unit -> Control
