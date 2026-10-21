module DevNull.Client.Components.Avatar

open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Theme

/// One user's avatar: the initial on a lane-colored disc.
[<Struct>]
type Props = {
  Name: string
  Url: string option
  Lane: Lane option
  Size: float
}

let initial(name: string) =
  let trimmed = name.Trim()

  if trimmed = "" then
    "?"
  else
    trimmed.Substring(0, 1).ToUpperInvariant()

let view(props: Props) : Control =
  let disc = Ellipse().Width(props.Size).Height(props.Size)

  let paint(element: Ellipse) =
    match props.Lane with
    | Some(LaneIndex index) -> element.Fill(Brushes.lane index)
    | Some(LaneColor hex) -> element.Fill(Brushes.solid hex)
    | None -> element.Fill(Brushes.laneFallback)

  paint disc |> ignore

  let letter =
    TextBlock()
      .Text(initial props.Name)
      .FontSize(props.Size * 0.42)
      .FontWeight(FontWeight.SemiBold)
      .Foreground(Brushes.onAccent)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Center)

  Grid().Width(props.Size).Height(props.Size).Children(disc, letter)
