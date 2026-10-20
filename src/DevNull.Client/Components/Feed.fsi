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
}

/// Builds the feed.
val view: props: Props<'T> -> Control
