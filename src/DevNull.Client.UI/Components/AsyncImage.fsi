module DevNull.Client.Components.AsyncImage

open Avalonia.Controls
open Avalonia.Media
open Avalonia.Media.Imaging
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

/// Hands a decoded bitmap to the control that asked for it: a load
/// cancelled between the decode and the UI post disposes the bitmap it
/// can no longer show.
val internal handOver:
  cancelled: bool * show: (Bitmap -> unit) -> bitmap: Bitmap -> unit

/// Builds the control.
val view: props: Props -> Control
