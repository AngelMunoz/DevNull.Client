module DevNull.Client.Components.AttachmentStrip

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client.Components.ImageChip
open DevNull.Client.Core.Commit

[<Struct>]
type Props = {
  Images: IObservable<ImageAttachment list>
  OnRemove: int -> unit
  MarkHandled: bool
  Descriptions: (int -> Description) voption
  Spacing: float
  HideWhenEmpty: bool
}

/// One chip for the staged image at its index.
let chipOf(props: Props, index: int, image: ImageAttachment) : Control =
  ImageChip.view {
    Image = image
    Index = index
    OnRemove = fun () -> props.OnRemove index
    MarkHandled = props.MarkHandled
    Description =
      match props.Descriptions with
      | ValueSome at -> ValueSome(at index)
      | ValueNone -> ValueNone
  }

/// One strip of staged image chips over the live list.
let view(props: Props) : Control =
  let visibility =
    if props.HideWhenEmpty then
      props.Images |> Observable.map(fun staged -> not staged.IsEmpty)
    else
      Observable.single true

  ContentControl()
    .Content(
      props.Images
      |> Observable.distinctUntilChanged
      |> Observable.map(function
        | [] -> Panel() :> Control
        | staged ->
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(props.Spacing)
            .Children
            [|
              for index, image in List.indexed staged ->
                chipOf(props, index, image)
            |])
      |> _.ToBinding()
    )
    .IsVisible(visibility)
