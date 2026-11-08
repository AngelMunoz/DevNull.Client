module DevNull.Client.Components.ReplyPicker

open System
open Avalonia.Controls

/// One person a reply may reach, as the host renders it: a flat,
/// render-ready row.
type ParticipantRow = {
  Id: string
  Handle: string
  IsAuthor: bool
}

/// The reply's audience picker: the rows the host built, the
/// exclusions the composer holds, and the toggle that flips one
/// handle in or out.
[<Struct>]
type Props = {
  /// The audience rows: the parent's author first, then the thread
  /// read's other participants.
  Rows: ParticipantRow list
  /// The handles the reply leaves out, as the composer model holds them.
  Excluded: IObservable<string list>
  /// Flips one handle in or out of the reply.
  OnToggle: string -> unit
}

/// The trigger line above the body box (`ValueNone` when no row
/// besides the author exists, so the stack keeps its spacing), and
/// the dialog's overlay layer that opens from it.
[<Struct>]
type View = {
  Trigger: Control voption
  Layer: Control
}

/// One audience picker: the trigger names who the reply goes to, the
/// dialog lists every row with its check, the author row stays
/// selected, and Done closes.
val view: props: Props -> View
