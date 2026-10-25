module DevNull.Client.Components.ImageViewer

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache

/// One gallery show: the owner window, the cache the photos load
/// through, the commit's photos, and the photo the show opens on.
[<Struct>]
type Show = {
  Owner: Window
  Cache: ImageCacheService
  Photos: Image list
  Start: int
}

/// Opens the commit's photos as one gallery: a borderless window over
/// the owner, one photo at a time, the arrows walking the photos, and
/// Esc, the close button, or a click on the backdrop ending the show.
/// A click on the photo itself only looks.
val show: show: Show -> unit
