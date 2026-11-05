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
  OnEmoji: unit -> unit
  OnSubmit: unit -> unit
}

/// The action row: the attach, poll, schedule, and emoji buttons and
/// the commit button docked right, the error line filling the rest.
val view: props: Props -> Control
