module DevNull.Client.Components.CommitCard

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes

/// What one card reports up: open the thread, toggle the star, or
/// start a quote.
[<Struct>]
type Event =
  | Opened of Commit
  | StarToggled of Commit
  | QuoteRequested of Commit

/// One commit card. `OnEvent` receives every interaction.
type Props = {
  Commit: Commit
  OnEvent: Event -> unit
}

/// Renders one commit as a card.
val view: props: Props -> Control

/// Renders a deleted commit placeholder.
val tombstone: tombstone: Tombstone -> Control
