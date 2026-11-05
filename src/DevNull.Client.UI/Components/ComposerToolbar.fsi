module DevNull.Client.Components.ComposerToolbar

open System
open Avalonia.Controls
open DevNull.Client.Core.Commit
open DevNull.Client.Errors

/// The composer's action row: what it offers, the live slices its
/// buttons read, and what each asks for.
[<Struct>]
type Props = {
  Attachments: IObservable<ImageAttachment list>
  Poll: IObservable<DevNull.Client.Components.PollEditor.Draft option>
  Busy: IObservable<bool>
  Scheduling: IObservable<bool>
  Picked: IObservable<DevNull.Client.Components.SchedulePicker.Pick voption>
  Error: IObservable<AppError option>
  OfferAttach: bool
  /// The poll and schedule buttons: a fresh commit offers both.
  OfferExtras: bool
  OnAttach: unit -> unit
  OnPoll: unit -> unit
  OnSchedule: unit -> unit
  /// The body selection as it stands when the trigger asks for it.
  BodySelection: unit -> int * int
  OnEmoji: int * int -> unit
  OnSubmit: unit -> unit
}

/// The action row and the emoji trigger.
[<Struct>]
type View = { Row: Control; Emoji: Button }

/// The action row: the attach, poll, schedule, and emoji buttons and
/// the commit button docked right, the error line filling the rest. A
/// pointer press on the emoji trigger records the body selection before
/// focus reaches the button; the click that follows hands it to
/// OnEmoji. A press released away from the trigger drops the recording,
/// so only the click that completes that press sees it. A keyboard
/// click hands over the body selection as it stands.
val view: props: Props -> View
