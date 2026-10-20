module DevNull.Client.Icons

open Avalonia.Controls
open Avalonia.Media

/// One glyph of the app's icon set: navigation, commit actions, states.
type Glyph =
  | Log
  | Notifications
  | Remotes
  | Profile
  | Saved
  | Settings
  | Commit
  | Collapse
  | Star
  | Reply
  | Quote
  | Share
  | Report
  | Delete
  | Close
  | Image
  | Menu
  | Link
  | Minimize
  | Maximize
  | Restore
  | ArrowUp

/// One icon ready to place: the glyph in the default size, 18 device
/// independent pixels.
val icon: glyph: Glyph -> PathIcon

/// One icon with an explicit size.
val iconAt: glyph: Glyph * size: float -> PathIcon
