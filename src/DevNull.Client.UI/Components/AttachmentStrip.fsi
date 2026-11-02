module DevNull.Client.Components.AttachmentStrip

open System
open Avalonia.Controls
open DevNull.Client.Core.Commit

/// The staged images and how a chip joins: the live list, the remove,
/// the description lane per index, and the strip's own spacing and
/// empty rule.
[<Struct>]
type Props = {
  Images: IObservable<ImageAttachment list>
  OnRemove: int -> unit
  MarkHandled: bool
  Descriptions: (int -> DevNull.Client.Components.ImageChip.Description) voption
  Spacing: float
  HideWhenEmpty: bool
}

/// One strip of staged image chips over the live list.
val view: props: Props -> Control
