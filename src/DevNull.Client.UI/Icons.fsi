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
  | Quote
  | Share
  | Report
  | Delete
  | Close
  | Plus
  | Image
  | Menu
  | Link
  | Minimize
  | Maximize
  | Restore
  | ArrowUp
  | ArrowRight
  | At
  | UserPlus
  | ChevronsUp
  | ChevronLeft
  | ChevronRight
  | PanelLeftClose
  | PanelLeftOpen
  | Check
  | Pastel
  | Pen
  | Poll
  | Info
  | Mic
  | Speaker
  | Reply
  | Reaction
  | Play
  | Pause
  | Refresh
  | Trash
  | Vibrate
  | Stop
  | Clock
  | Pin
  | PinOff
  | Search

/// One icon ready to place: the glyph in the default size, 18 device
/// independent pixels.
val icon: glyph: Glyph -> PathIcon

/// One icon with an explicit size.
val iconAt: glyph: Glyph * size: float -> PathIcon
