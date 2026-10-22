module DevNull.Client.Components.AsyncImage

open Avalonia.Controls
open Avalonia.Media
open DevNull.Client.ImageCache

/// One asynchronously loaded image: the caller reserves the box (Width
/// and Height on the returned control), the placeholder holds it, and
/// the decoded bitmap replaces the placeholder when it lands. Attach
/// starts the load; detach and recycle cancel it.
[<Struct>]
type Props = {
  Cache: ImageCacheService
  Url: string
  DecodeWidth: int
  Stretch: Stretch
  Placeholder: Control voption
  ToolTip: string voption
}

/// Builds the control.
val view: props: Props -> Control
