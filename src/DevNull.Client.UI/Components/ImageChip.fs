module DevNull.Client.Components.ImageChip

open System
open System.IO
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Media.Imaging
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Core.Commit

[<Struct>]
type Description = {
  Text: IObservable<string>
  Changed: string -> unit
}

[<Struct>]
type Props = {
  Image: ImageAttachment
  Index: int
  OnRemove: unit -> unit
  MarkHandled: bool
  Description: Description voption
}

/// One description box: the text rides the model, and every change
/// reports back.
let descriptionBox(description: Description, index: int) : Control =
  TextBox()
    .Name($"ImageDescription{index}")
    .Width(190.0)
    .PlaceholderText("Describe this image")
    .Text(description.Text |> Observable.distinctUntilChanged |> _.ToBinding())
    .OnTextChangedHandler(fun box _ ->
      let text = box.Text |> Option.ofObj |> Option.defaultValue ""

      description.Changed text)

/// The server's own bound, counted live.
let descriptionCount(description: Description) : Control =
  TextBlock()
    .Classes(Theme.name Theme.Mono, Theme.name Theme.Muted)
    .FontSize(11.0)
    .HorizontalAlignment(HorizontalAlignment.Right)
    .Text(
      description.Text
      |> Observable.map(fun text ->
        if text.Length = 0 then "" else $"{text.Length}/1000")
      |> Observable.distinctUntilChanged
    )

/// The description lane, absent when the host does not describe images.
let descriptionLane(props: Props) : Control array =
  match props.Description with
  | ValueSome description -> [|
      descriptionBox(description, props.Index)
      descriptionCount description
    |]
  | ValueNone -> [||]

/// One staged image chip.
let view(props: Props) : Control =
  // The preview decodes to the chip's own width: staged photos run
  // phone-camera resolutions, and one full decode costs tens of
  // megabytes of native pixels for 48 on-screen ones. The decoded
  // bitmap is native memory, so the chip image disposes it when the
  // strip rebuilds and drops it.
  let bitmap =
    use stream = new MemoryStream(props.Image.Bytes)
    Bitmap.DecodeToWidth(stream, 96)

  let preview =
    Avalonia.Controls
      .Image()
      .Source(bitmap)
      .Width(48.0)
      .Height(48.0)
      .Stretch(Stretch.UniformToFill)

  preview.DetachedFromVisualTree.Add(fun _ -> bitmap.Dispose())

  // The remove captures the action and the flag alone: the chip's
  // record holds the staged bytes, and a handler that kept it would
  // keep those bytes alive past the removal.
  let remove = props.OnRemove
  let markHandled = props.MarkHandled
  let index = props.Index

  let details: Control array = [|
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .FontSize(12.0)
      .Text(props.Image.FileName)
      .MaxWidth(140.0)
      .TextTrimming(TextTrimming.CharacterEllipsis)
    Button()
      .Name($"RemoveImage{index}")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(4.0, 0.0, 4.0, 0.0)
      .FontSize(12.0)
      .Content("remove")
      .OnClickHandler(fun _ args ->
        if markHandled then
          args.Handled <- true

        remove())
    yield! descriptionLane props
  |]

  Border()
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(1.0))
    .CornerRadius(8.0)
    .Padding(6.0)
    .Child(
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(
          preview,
          StackPanel()
            .VerticalAlignment(VerticalAlignment.Center)
            .Children(details)
        )
    )
