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

/// One commit row. `OnEvent` receives every interaction. `Rail`
/// carries the lane of the row above: `ValueSome` draws the commit on
/// the graph rail, `ValueNone` lays the row out flat. Dense rows (the
/// replies of a thread) shrink the avatar.
[<Struct>]
type Props = {
  Commit: Commit
  OnEvent: Event -> unit
  Rail: Lane option voption
  Dense: bool
}

/// Renders one commit as a row of the log.
val view: props: Props -> Control

/// Renders a deleted commit placeholder.
val tombstone: tombstone: Tombstone -> Control
