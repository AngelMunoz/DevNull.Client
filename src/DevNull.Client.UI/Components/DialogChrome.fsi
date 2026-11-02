module DevNull.Client.Components.DialogChrome

open System
open Avalonia.Controls

/// Where a layer places its content: the band-aware center of the
/// window for dialogs, or the bottom center for the undo toast.
type Place =
  | Center
  | Bottom

/// One overlay layer: the shell's open signal, the content factory,
/// the scrim policy, and the place. The shell owns when the layer
/// opens and what it builds; the chrome owns the shape around it.
[<Struct>]
type Props = {
  /// The shell's open signal. The layer builds on each rising edge
  /// and releases on each falling edge.
  Open: IObservable<bool>
  /// The content factory, called once on each open edge.
  Content: unit -> Control
  /// Whether the scrim surface shows. Modal dialogs show it; the undo
  /// toast does not.
  Scrim: bool
  /// The scrim click action. `ValueNone` keeps the scrim from
  /// dismissing the layer, and it does nothing when `Scrim` is false.
  OnScrimClick: (unit -> unit) voption
  /// Where the layer places its content.
  Place: Place
}

/// One stateless overlay layer: the scrim, the content host, and the
/// open/close edges. The shell supplies the open signal and the
/// content; the layer owns the scrim surface, placement, and margins.
val layer: props: Props -> Control
