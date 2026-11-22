module DevNull.Client.Layout.Website.ProfileShell

open System
open Avalonia
open Avalonia.Controls
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors

/// One feed of the shell: the rows the mount drives, the row key and
/// renderer, the cursor pager, and the quiet line under the last row.
type FeedProps<'row when 'row: equality> = {
  Items: IObservable<'row list>
  KeyOf: 'row -> string
  Row: 'row -> Control
  HasMore: IObservable<bool>
  OnMore: unit -> unit
  /// Reports the feed's reading place, so the host can drive its own
  /// pinned areas.
  OnRide: (Feed.Ride -> unit) voption
  /// The post key of a row; separator and caption rows answer none.
  AnchorKeyOf: ('row -> string option) voption
  /// The request cursor of the page holding the row.
  EntryCursorOf: ('row -> string option) voption
  /// The reading place the feed picks up.
  RestoreAnchor: FeedWindow.Anchor voption
  /// Reports the visible post after a settled scroll.
  OnAnchor: (FeedWindow.Anchor -> unit) voption
  /// Enables two-way edge paging. When set, the shell omits Load more.
  Paging: Feed.Paging voption
  /// The sentence the feed shows once its last page has no cursor.
  End: IObservable<string option>
}

/// Everything one profile-shaped page projects into the shared
/// skeleton: the page model, the sticky title and kind, the header and
/// meta slots, the feed label and tabs, and the feed column.
type Props<'model, 'row when 'row: equality> = {
  Model: IObservable<'model>
  /// The title line, as a projection of the model; empty hides it.
  Title: 'model -> string
  /// The quiet kind label beside the title, when the page carries one.
  Kind: string option
  /// The inset the sticky area takes.
  Padding: Thickness
  ErrorOf: 'model -> AppError option
  /// The busy line while the page loads; Off hides it.
  Busy: 'model -> PageChrome.Line
  /// The empty line when the feed is empty; Off hides it.
  Empty: 'model -> PageChrome.Line
  /// The transient line under the tabs, when the page carries one.
  Note: 'model -> string option
  /// The header slot, projected by the mount.
  Header: unit -> Control
  /// The meta slot under the header, projected by the mount.
  Meta: (unit -> Control) voption
  /// The label above the tab row, when the page carries one.
  FeedLabel: string option
  Tabs: TabStrip.Tab list
  Feed: FeedProps<'row>
}

/// One commit of a feed: the card with its reply preview under it, or
/// the deletion marker.
val commitRow: ctx: CommitCard.RowProps * node: CommitNode -> Control

/// Builds the page skeleton: the sticky title over the header and meta
/// slots, then the tabs, note, and scrolling feed.
val view: props: Props<'model, 'row> -> Control
