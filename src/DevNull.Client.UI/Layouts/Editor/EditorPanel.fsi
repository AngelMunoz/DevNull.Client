module DevNull.Client.Editor.EditorPanel

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors

/// Which panel tab is in front.
type Tab =
  | Problems
  | Output
  | Terminal

/// The panel's own state: whether it is open and which tab is in front.
/// The root model holds it.
type State = { Open: bool; Tab: Tab }

/// The last notifications read and its rows.
type ProblemsState = {
  Loading: bool
  Error: AppError option
  Items: Notification list
}

/// One line of the output log: the read, its result, and its moment.
[<Struct>]
type OutputLine = {
  Ref: string
  Result: string
  At: DateTimeOffset
}

/// What the panel host renders: its state and the three bodies.
type Props = {
  State: IObservable<State>
  Problems: IObservable<ProblemsState>
  Output: IObservable<OutputLine list>
  Terminal: Control
  Now: IObservable<DateTimeOffset>
  OnSelect: Tab -> unit
  OnClose: unit -> unit
  OnOpenCommit: Commit -> unit
}

/// The panel height the root reserves when the panel is docked.
val height: float

/// The panel with no tab in front and no panel shown.
val closed: State

/// The panel open on one tab.
val openOn: tab: Tab * state: State -> State

/// The panel open on one tab, replacing the current state.
val select: tab: Tab * state: State -> State

/// The panel closed, with its tab kept for the next open.
val close: state: State -> State

/// The panel toggled on one tab: open that tab, or close it when it is
/// already the open tab.
val toggle: tab: Tab * state: State -> State

/// Whether the panel is open.
val isOpen: state: State -> bool

/// Whether the width band uses the overlay sheet instead of the docked
/// panel.
val overlay: band: WidthBand -> bool

/// The notifications with no read in flight.
val emptyProblems: ProblemsState

/// The same problems with a read in flight.
val beginProblems: state: ProblemsState -> ProblemsState

/// Notification rows grouped by kind in the panel's fixed order.
val problemGroups:
  items: Notification list -> (NotificationKind * Notification list) list

/// The severity mark of one kind: a check for Approved, a cross for
/// Rejected, and nothing for every other kind.
val severityOf: kind: NotificationKind -> string option

/// Whether the server last reported unread events for this notification.
val isUnread: notification: Notification -> bool

/// The group heading of one kind.
val kindLabel: kind: NotificationKind -> string

/// The live commit a notification points at, when it points at one.
val commitOf: notification: Notification -> Commit option

/// The output cap.
val outputCap: int

/// The same lines with one line appended, oldest dropped at the cap.
val appendOutput: line: OutputLine * lines: OutputLine list -> OutputLine list

/// The panel host: the tab row, the three bodies, and the close control.
val view: props: Props -> Control
