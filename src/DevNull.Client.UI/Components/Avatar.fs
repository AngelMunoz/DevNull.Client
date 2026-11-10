module DevNull.Client.Components.Avatar

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Components.AsyncImage
open DevNull.Client.Components.ImageViewer
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Formatting
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
  /// Whether the avatar opens the picture full size on a click;
  /// off — and with no URL on — the avatar renders exactly as it
  /// did before the viewer mattered.
  Viewable: bool
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

  let avatar = Grid().Width(props.Size).Height(props.Size).Children(children)

  // The picture viewer, where the host arms it: one photo at full
  // size, the way the web's profile picture opens. The owner comes
  // from the tree, as the commit card's gallery does.
  match props.Viewable, props.Url with
  | true, Some url ->
    Button()
      .Name("AvatarView")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(0.0)
      .Content(avatar)
      .OnClickHandler(fun _ args ->
        // The viewer never opens the page behind the avatar.
        args.Handled <- true

        match TopLevel.GetTopLevel avatar with
        | :? Window as owner ->
          ImageViewer.show {
            Owner = owner
            Cache = props.Cache
            Photos = [
              {
                Id = url
                Width = 0
                Height = 0
                Description = Some($"""{props.Name}'s profile picture""")
                Url = sizedAvatarUrl(url, 1024)
              }
            ]
            Start = 0
          }
        | _ -> ())
  | _ -> avatar
