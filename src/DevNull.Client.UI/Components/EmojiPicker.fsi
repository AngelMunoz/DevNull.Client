module DevNull.Client.Components.EmojiPicker

open System
open Avalonia.Controls

/// What the picker answers to: the query as typed, the cell the keys
/// hold, the close, and the emoji a pick settled.
type Event =
  | QueryChanged of query: string
  | ActiveChanged of active: int
  | Closed
  | Picked of emoji: string

/// The picker's inputs: its open state, the query and armed cell from
/// the composer, the body selection a pick replaces, the box it inserts
/// into, the trigger whose pointer press hands the click its focus, and
/// the event sink.
[<Struct>]
type Props = {
  Open: IObservable<bool>
  Query: IObservable<string>
  Active: IObservable<int>
  Selection: IObservable<int * int>
  Body: TextBox
  Trigger: Control
  Events: Event -> unit
}

/// The emoji picker over the body: the search field, the eight groups
/// or the ranked results, the armed cell, and the footer. The results
/// section carries no visible header; its automation name stays
/// Results. The current group's header stays pinned while the list
/// scrolls. The panel opens below the body, or above it when the room
/// below is too short to stay usable and the room above is larger;
/// its height never passes the room on the chosen side nor the 400 px
/// cap. A pick closes the panel, returns focus to the body, and inserts
/// at the stored selection; a
/// refusal restores the selection silently. Focus leaving the panel
/// closes it without touching the body, except when a pointer press on
/// the trigger takes it: that click owns the close.
val view: props: Props -> Control
