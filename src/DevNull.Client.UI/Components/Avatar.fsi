module DevNull.Client.Components.Avatar

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache

/// One user's avatar: the image over the initial disc when a URL
/// exists, the initial on a lane-colored disc otherwise. Props record
/// keeps the constructor under the three-parameter rule.
[<Struct>]
type Props = {
  Name: string
  Url: string option
  Lane: Lane option
  Size: float
  Cache: ImageCacheService
  /// The square shape the organization's avatar uses; persons draw
  /// the disc.
  Square: bool
}

/// Renders the avatar.
val view: props: Props -> Control
