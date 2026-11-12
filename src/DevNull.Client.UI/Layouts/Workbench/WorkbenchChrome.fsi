module DevNull.Client.Layouts.Workbench.WorkbenchChrome

open System
open Avalonia.Controls

/// The refs tree's model: the refs it lists and the one that holds
/// HEAD.
[<Struct>]
type RefsModel = { Refs: Ref list; Selected: Ref }

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

/// The three history refs in tree order, each built through the parse
/// leaf: Latest, Following, Trending.
val historyRefs: unit -> Ref list

/// Renders the status bar from its model: the ref, the handle, and the
/// last fetch time.
val status: model: StatusModel -> Control
