module DevNull.Client.Components.CommitCard

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes

/// What one card reports up: open the thread, toggle the star, give a
/// pastel, or start a quote.
[<Struct>]
type Event =
  | Opened of Commit
  | StarToggled of Commit
  | PastelGiven of Commit
  | QuoteRequested of Commit

/// One commit row. `OnEvent` receives every interaction. Every card
/// rides the graph rail with its author's avatar; dense rows (the
/// replies of a thread) shrink the node. `Pastels` says whether the
/// viewer's pastel controls show at all.
[<Struct>]
type Props = {
  Commit: Commit
  OnEvent: Event -> unit
  Pastels: bool
  Dense: bool
}

/// Renders one commit as a row of the log.
val view: props: Props -> Control

/// Renders a deleted commit placeholder.
val tombstone: unit -> Control
