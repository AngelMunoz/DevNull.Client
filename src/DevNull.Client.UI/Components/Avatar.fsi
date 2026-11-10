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
  /// Whether the avatar opens the picture full size on a click;
  /// off — and with no URL on — the avatar renders exactly as it
  /// did before the viewer mattered.
  Viewable: bool
}

/// Renders the avatar. Where `Viewable` is on and a URL stands,
/// the avatar is a viewer button (`AvatarView`) opening one full-size
/// photo; otherwise the control is the plain avatar.
val view: props: Props -> Control
