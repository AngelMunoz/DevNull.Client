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

/// One virtualized feed: the rows run through an ItemsControl over a
/// `VirtualizingStackPanel`, so only the rows near the viewport exist.
/// The feed owns its scroll; `Footer` rides under the last row (the
/// load-more button). The rows column is capped at 720 device
/// independent pixels and centered.
type Props<'T when 'T: equality> = {
  Items: IObservable<'T list>
  KeyOf: 'T -> string
  Row: 'T -> Control
  Footer: Control voption
  /// Whether the host holds another page of rows. The restore chase
  /// asks for more only while this holds.
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
  Restore: float voption
  /// Reports every settled scroll place, so the host can park it.
  OnScroll: (float -> unit) voption
  /// Reports the reading place every settled scroll pass, so the host
  /// can follow the bottom edge. A feed that sets this tracks the tail
  /// even when `Ride` is off; it never sticks to the edge.
  OnRide: (Ride -> unit) voption
  /// Asks the host for the next page while a remembered place sits
  /// beyond the loaded extent.
  OnNeedMore: (unit -> unit) voption
}

/// Builds the feed and hands back the scroller it owns.
val view: props: Props<'T> -> ScrollViewer
