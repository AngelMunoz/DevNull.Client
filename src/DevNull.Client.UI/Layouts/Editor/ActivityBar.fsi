module DevNull.Client.Layouts.Editor.ActivityBar

open Avalonia.Controls

/// One icon of the strip: the history focus, the problems panel, the
/// remotes badge, or the account.
type Activity =
  | History
  | Problems
  | Remotes
  | Account

/// The counts the strip's badges show.
[<Struct>]
type Badges = { Unread: int; Remotes: int }

/// What the strip renders: the selected icon, the badges, and the
/// host's answer to a click.
type Props = {
  Selected: Activity
  Badges: Badges
  OnSelect: Activity -> unit
}

/// One icon strip with its badges: history, problems, remotes, and the
/// account.
val view: props: Props -> Control
