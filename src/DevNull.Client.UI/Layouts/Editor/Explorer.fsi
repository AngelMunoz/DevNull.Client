module DevNull.Client.Layouts.Editor.Explorer

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes

/// What the explorer renders: the selected ref, the two counts, the
/// viewer's handle, and the host's answers.
type Props = {
  Selected: LogKind
  Saved: int
  Queue: int
  Viewer: string
  OnSelectRef: LogKind -> unit
}

/// The side bar: the three refs with HEAD on the selected one, the
/// saved and queue counts, and the signed-in user's repository row.
val view: props: Props -> Control
