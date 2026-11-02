module DevNull.Client.Components.Feed

open System
open Avalonia.Controls

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
  /// Emits when the feed must return to its top. The page hands over a
  /// slice of its own model that emits only on a real change, with the
  /// initial replay skipped, so a restored offset survives the attach.
  ScrollTop: IObservable<unit> voption
  /// The scroll place this feed picks up, or none for a fresh feed.
  Restore: float voption
  /// Reports every settled scroll place, so the host can park it.
  OnScroll: (float -> unit) voption
  /// Asks the host for the next page while a remembered place sits
  /// beyond the loaded extent.
  OnNeedMore: (unit -> unit) voption
}

/// Builds the feed.
val view: props: Props<'T> -> Control
