module DevNull.Client.Components.SchedulePicker

open System
open Avalonia.Controls

/// The moment the person picked, as the panel takes it: a day offset
/// from today, an hour, and a five-minute step.
[<Struct>]
type Pick = { Day: int; Hour: int; Minute: int }

/// The schedule picker's live choice and what its selects ask for.
[<Struct>]
type Props = {
  Now: DateTimeOffset
  Scheduling: IObservable<bool>
  Picked: IObservable<Pick voption>
  OnDayPicked: int -> unit
  OnHourPicked: int -> unit
  OnMinutePicked: int -> unit
  OnOpenQueue: (unit -> unit) voption
}

/// The schedule panel under the poll builder: the site's picker as
/// three selects, the window's help line, and the link into the queue.
val view: props: Props -> Control
