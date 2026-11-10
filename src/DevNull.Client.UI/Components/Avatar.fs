module DevNull.Client.Components.Avatar

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Components.AsyncImage
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache

/// One user's avatar: the image over the initial disc when a URL
/// exists, the initial on a lane-colored disc otherwise.
[<Struct>]
type Props = {
  Name: string
  Url: string option
  Lane: Lane option
  Size: float
  Cache: ImageCacheService
  /// The square shape the organization's avatar uses; persons draw
  /// the disc.
  Square: bool
}

let initial(name: string) =
  let trimmed = name.Trim()

  if trimmed = "" then
    "?"
  else
    trimmed.Substring(0, 1).ToUpperInvariant()

let view(props: Props) : Control =
  let backing: Control =
    if props.Square then
      (Border().Width(props.Size).Height(props.Size).CornerRadius(8.0))
      :> Control
    else
      (Ellipse().Width(props.Size).Height(props.Size)) :> Control

  match backing with
  | :? Shape as shape -> Theme.laneFill(shape, props.Lane)
  | :? Border as border -> Theme.laneBack(border, props.Lane)
  | _ -> ()

  let children: Control array = [|
    backing

    match props.Url with
    | Some url ->
      // The image clips to the disc; the letter under it stays the
      // fallback until the bitmap lands.
      let clip: Geometry =
        if props.Square then
          RectangleGeometry(Rect(0.0, 0.0, props.Size, props.Size))
        else
          EllipseGeometry(Rect(0.0, 0.0, props.Size, props.Size))

      let image =
        AsyncImage.view {
          Cache = props.Cache
          Url = url
          DecodeWidth = int(props.Size * 2.0)
          DecodeHeight = ValueNone
          Stretch = Stretch.UniformToFill
          ToolTip = ValueNone
        }

      image.Name("AvatarImage").Clip(clip).ClipToBounds(true)
    | None ->
      TextBlock()
        .Text(initial props.Name)
        .FontSize(props.Size * 0.42)
        .FontWeight(FontWeight.SemiBold)
        .Classes(Theme.name Theme.FgOnAccent)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Center)
  |]

  Grid().Width(props.Size).Height(props.Size).Children(children)
