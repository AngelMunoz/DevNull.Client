module DevNull.Client.Components.AsyncImage

open Avalonia.Controls
open Avalonia.Media
open DevNull.Client.ImageCache

/// One asynchronously loaded image: the caller reserves the box (Width
/// and Height on the returned control), an empty panel holds it, and
/// the decoded bitmap replaces the panel when it lands. Attach starts
/// the load; detach and recycle cancel it.
[<Struct>]
type Props = {
  Cache: ImageCacheService
  Url: string
  DecodeWidth: int
  /// Decodes by height instead of width: the fixed-height cells of the
  /// feed and the viewer's viewport fit their images this way, and a
  /// tall photo costs its displayed pixels, not its full frame.
  DecodeHeight: int voption
  Stretch: Stretch
  ToolTip: string voption
}

/// Builds the control.
val view: props: Props -> Control
