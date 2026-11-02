module DevNull.Client.Components.SplitChrome

open System
open Avalonia.Controls

/// One two-pane frame: the page's side and main panes, and whether the
/// main pane has a target to show. The chrome owns the breakpoint, the
/// side width, the hairline, the back row, and the band inset.
[<Struct>]
type Props = {
  /// The side pane: the inbox-shaped column.
  Side: Control
  /// The main pane: the thread-shaped column.
  Main: Control
  /// The page model's answer to whether the main pane has a target.
  MainOpen: IObservable<bool>
  /// The back row's action, shown while the main pane stands alone.
  OnBack: unit -> unit
}

/// Builds the two-pane frame: both panes on a wide window, one pane at
/// a time below the chrome's breakpoint.
val view: props: Props -> Control
