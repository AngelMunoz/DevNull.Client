module DevNull.Client.Components.ComposerKeys

open Avalonia.Controls
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Commit

/// Which Enter keys commit: the commit composer's Ctrl/Cmd pair, or
/// the thread composer's plain or Ctrl Enter.
[<Struct>]
type Commit =
  | ControlOrMeta
  | PlainOrControl

/// The box and the gestures one composer answers: the paste that asks
/// the attachment sources, the commit keys, and the escape the thread
/// owns.
[<Struct>]
type Props = {
  Body: TextBox
  Attachments: IAttachmentSources
  Commit: Commit
  OnCommit: unit -> unit
  OnStaged: ImageAttachment -> unit
  OnEscape: (unit -> unit) voption
}

/// Wire the paste gesture, the commit keys, and the optional escape
/// onto the box, through one tunnel handler.
val attach: props: Props -> unit
