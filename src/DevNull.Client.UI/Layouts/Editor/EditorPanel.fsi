module DevNull.Client.Layouts.Editor.EditorPanel

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors

/// Which panel tab is in front: one notification kind, the
/// activity log, or the terminal.
type Tab =
  | Kind of NotificationKind
  | Activity
  | Terminal

/// The panel's own state: whether it is open and which tab is in front.
/// The root model holds it.
type State = { Open: bool; Tab: Tab }

/// The last notifications read and its rows.
type ProblemsState = {
  Loading: bool
  Error: AppError option
  Items: Notification list
  /// The cursor for the next notifications page, when one exists.
  NextCursor: string option
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
  OnLoadMore: unit -> unit
}

/// The panel height the root reserves when the panel is docked.
val height: float

/// The panel with no tab in front and no panel shown.
val closed: State

/// The panel open on one tab.
val openOn: tab: Tab * state: State -> State

/// The panel open on one tab, replacing the current state.
val select: tab: Tab * state: State -> State

/// Whether the tab holds one notification kind.
val isKindTab: tab: Tab -> bool

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

/// The notification kinds present in the rows, in display order.
val kindsOf: items: Notification list -> NotificationKind list

/// The rows of one kind, in the read's own order.
val notificationsOf:
  items: Notification list * kind: NotificationKind -> Notification list

/// The tab to show when the held tab is no longer present: the first
/// kind, or Activity when no kind remains.
val effectiveTab: items: Notification list * tab: Tab -> Tab

/// Keeps the held tab when it is present, else moves to the first kind
/// or Activity.
val reconcile: items: Notification list * state: State -> State

/// Opens the panel on the last-used kind when it is present, else the
/// first kind. Before the first read lands, it keeps a first-kind
/// placeholder so the read starts; the next reconcile picks the kind
/// that is actually present.
val openFirst: items: Notification list * state: State -> State

/// Toggles the panel on its current kind, or opens the first kind.
val toggleNotifications: items: Notification list * state: State -> State

/// The severity mark of one kind: a check for Approved, a cross for
/// Rejected, and nothing for every other kind.
val severityOf: kind: NotificationKind -> string option

/// Whether the server last reported unread events for this notification.
val isUnread: notification: Notification -> bool

/// The group heading of one kind.
val kindLabel: kind: NotificationKind -> string

/// The label one panel tab shows.
val tabLabel: tab: Tab -> string

/// The live commit a notification points at, when it points at one.
val commitOf: notification: Notification -> Commit option

/// The output cap.
val outputCap: int

/// The same lines with one line appended, oldest dropped at the cap.
val appendOutput: line: OutputLine * lines: OutputLine list -> OutputLine list

/// The panel host: the tab row, the three bodies, and the close control.
val view: props: Props -> Control
