module DevNull.Client.Workbench.WorkbenchChrome

open System
open Avalonia.Controls

/// One refs tree row: the ref and the count it shows, when it has one.
[<Struct>]
type RefRow = { Ref: Ref; Count: int option }

/// The refs tree's model: the rows it lists and the one that holds
/// HEAD.
[<Struct>]
type RefsModel = { Rows: RefRow list; Selected: Ref }

/// The status bar's model: the checked-out ref, the signed-in handle,
/// the last fetch, and the clock the fetch line reads against.
[<Struct>]
type StatusModel = {
  Ref: Ref
  Handle: string
  FetchedAt: DateTimeOffset option
  Now: DateTimeOffset
}

/// Renders the refs tree from its model. A selected row carries the
/// HEAD marker, mono, and the active nav wash. Selecting a row calls
/// the host.
val refs: model: RefsModel * onSelect: (Ref -> unit) -> Control

/// The refs tree rows in display order: Latest, Following, Trending,
/// then Saved and Notifications with their counts.
val rows: saved: int * unread: int -> RefRow list

/// The status bar's one line: the ref, the handle, and the fetch time.
val statusText: model: StatusModel -> string
