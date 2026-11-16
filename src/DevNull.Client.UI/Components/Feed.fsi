module DevNull.Client.Components.Feed

open System
open Avalonia.Controls

/// One scroll pass's reading place: whether the reader rides the
/// bottom edge, the scroller's vertical offset, the height the rows
/// exceed the viewport by, the distance from the bottom edge, and the
/// viewport's height.
[<Struct>]
type Ride = {
  Riding: bool
  Offset: float
  Room: float
  FromEnd: float
  Viewport: float
}

/// The semantic paging contract of a cursor feed. `Older` and `Newer`
/// are the feed's meaning, not the wire's `next_cursor`/`prev_cursor`.
/// The feed maps the physical start and end edges through `Order`.
[<Struct>]
type Paging = {
  Order: DevNull.Client.FeedWindow.Order
  HasOlder: IObservable<bool>
  HasNewer: IObservable<bool>
  OnOlder: unit -> unit
  OnNewer: unit -> unit
}

/// The anchor and paging contract of a cursor feed. A feed that leaves
/// every field empty keeps the legacy pixel restore and footer pager.
[<Struct>]
type Advanced<'T> = {
  /// The post key of a row. `ValueNone` uses `KeyOf`; separator and
  /// marker rows return `ValueNone` in their own function.
  AnchorKeyOf: ('T -> string option) voption
  /// The request cursor of the page holding the row, or none for page 1.
  EntryCursorOf: ('T -> string option) voption
  /// The reading place this feed picks up.
  RestoreAnchor: DevNull.Client.FeedWindow.Anchor voption
  /// Reports the visible post after a settled scroll.
  OnAnchor: (DevNull.Client.FeedWindow.Anchor -> unit) voption
  /// Enables two-way edge paging and forces the virtualizing panel.
  Paging: Paging voption
}

/// The empty advanced contract.
module Advanced =

  /// No anchor and no paging: the feed behaves as the legacy feed.
  val none: unit -> Advanced<'T>

/// One feed: a page under ten rows renders every row; a longer page
/// runs through a `VirtualizingStackPanel`, so only the rows near the
/// viewport exist. The feed owns its scroll; `Header` renders above the
/// first row and `Footer` rides under the last row. The rows column is
/// capped at 720 device independent pixels and centered.
type Props<'T when 'T: equality> = {
  /// Content above the first row, inside the feed's scroll owner.
  Header: Control voption
  Items: IObservable<'T list>
  KeyOf: 'T -> string
  Row: 'T -> Control
  Footer: Control voption
  /// Whether the host holds another page of rows. The legacy restore
  /// chase asks for more only while this holds.
  HasMore: IObservable<bool>
  /// Emits the offset the scroller takes. The page hands over a slice
  /// of its own model that emits only on a real change, with the
  /// initial replay skipped, so a restored offset survives the attach.
  ScrollTo: IObservable<float> voption
  /// Whether the feed sticks to its bottom edge while the reader sits
  /// there. An items change that grows the extent scrolls back to the
  /// end until the reader scrolls away.
  Ride: bool
  /// The scroll place this feed picks up, or none for a fresh feed.
  /// The legacy pixel restore; `Advanced.RestoreAnchor` takes priority.
  Restore: float voption
  /// Reports every settled scroll place, so the host can park it.
  /// The legacy pixel report; `Advanced.OnAnchor` is the post-level one.
  OnScroll: (float -> unit) voption
  /// Reports the reading place every settled scroll pass, so the host
  /// can follow the bottom edge. A feed that sets this tracks the tail
  /// even when `Ride` is off; it never sticks to the edge.
  OnRide: (Ride -> unit) voption
  /// Asks the host for the next page while a remembered place sits
  /// beyond the loaded extent. The legacy restore chase.
  OnNeedMore: (unit -> unit) voption
}

/// Builds the feed and hands back the scroller it owns.
val view: props: Props<'T> -> ScrollViewer

/// Builds a cursor feed with anchor tracking, anchor restore, and
/// two-way paging. The ordinary `view` is `viewWith` with the empty
/// advanced contract.
val viewWith: props: Props<'T> * advanced: Advanced<'T> -> ScrollViewer
