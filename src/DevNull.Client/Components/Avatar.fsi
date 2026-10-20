module DevNull.Client.Avatar

open Avalonia.Controls
open DevNull.Client.Theme

/// One user's avatar: the initial on a lane-colored disc. Props record
/// keeps the constructor under the three-parameter rule.
[<Struct>]
type Props = {
  Name: string
  Url: string option
  Lane: int option
  Size: float
}

/// Renders the avatar.
val view: props: Props -> Control
