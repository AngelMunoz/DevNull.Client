module DevNull.Client.Components.Avatar

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes

/// One user's avatar: the initial on a lane-colored disc. Props record
/// keeps the constructor under the three-parameter rule.
[<Struct>]
type Props = {
  Name: string
  Url: string option
  Lane: Lane option
  Size: float
}

/// Renders the avatar.
val view: props: Props -> Control
