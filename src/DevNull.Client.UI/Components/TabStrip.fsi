module DevNull.Client.Components.TabStrip

open System
open Avalonia.Controls

/// One tab of a strip: its label, the stream that says whether it is
/// the active tab, and what picking it does.
type Tab = {
  Label: string
  Active: IObservable<bool>
  Choose: unit -> unit
}

/// One stateless tab strip: one full-width column per tab over a
/// hairline. The active tab rides the toggle's checked state, so the
/// strip owns no class subscription.
val view: tabs: Tab list -> Control
