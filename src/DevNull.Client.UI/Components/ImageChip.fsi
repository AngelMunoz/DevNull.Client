module DevNull.Client.Components.ImageChip

open System
open Avalonia.Controls
open DevNull.Client.Core.Commit

/// The live description one staged image carries: the text the box
/// shows and the sink every change reaches.
[<Struct>]
type Description = {
  Text: IObservable<string>
  Changed: string -> unit
}

/// One staged image chip: the decoded preview, the file name, the
/// remove, and — when the host describes images — the description box
/// with its live count. `MarkHandled` lets a host whose row owns the
/// click take the remove without bubbling.
[<Struct>]
type Props = {
  Image: ImageAttachment
  Index: int
  OnRemove: unit -> unit
  MarkHandled: bool
  Description: Description voption
}

/// One staged image chip.
val view: props: Props -> Control
