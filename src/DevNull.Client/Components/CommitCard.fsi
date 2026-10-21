module DevNull.Client.Components.CommitCard

open System
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
/// viewer's pastel controls show at all, live so the flag can move
/// under a built card; `Now` is the clock the relative time follows.
[<Struct>]
type Props = {
  Commit: Commit
  OnEvent: Event -> unit
  Pastels: IObservable<bool>
  Now: IObservable<DateTimeOffset>
  Dense: bool
}

/// Renders one commit as a row of the log.
val view: props: Props -> Control

/// Renders a deleted commit placeholder.
val tombstone: unit -> Control

/// Everything one card row needs from its host page: where its events
/// go, the live pastéis flag and clock, and the row's density.
[<Struct>]
type RowProps = {
  OnEvent: Event -> unit
  Pasteis: IObservable<bool>
  Now: IObservable<DateTimeOffset>
  Dense: bool
}

/// One commit row over a feed node: the card with the host's event
/// routing, or the deletion marker. Every feed page shares this;
/// only the routing differs.
val feedRow: props: RowProps -> node: CommitNode -> Control
