module DevNull.Client.Editor.Explorer

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes

/// What the explorer renders: the selected ref, the two counts, the
/// viewer's handle, and the host's answers.
type Props = {
  Selected: LogKind
  Saved: int
  Queue: int
  /// An optional line under the queue branch.
  QueueHint: string option
  Viewer: string
  OnSelectRef: LogKind -> unit
  OnOpenSaved: unit -> unit
  OnOpenQueue: unit -> unit
  OnOpenViewer: unit -> unit
}

/// The side bar as a file tree: the history refs indented under their
/// group header, the saved and queue branches, and the signed-in
/// user's repository row.
val view: props: Props -> Control
