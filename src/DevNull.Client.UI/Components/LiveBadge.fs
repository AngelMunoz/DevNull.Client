module DevNull.Client.Components.LiveBadge

open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Layout
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Core.ApiTypes

let view(onOpenExternal: string -> unit, live: LiveStream option) : Control =
  match live with
  | None -> TextBlock().IsVisible(false)
  | Some stream ->
    let chip =
      Border()
        .Classes(Theme.name Theme.LiveBadge)
        .Child(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(4.0)
            .VerticalAlignment(VerticalAlignment.Center)
            .Children(
              Ellipse()
                .Width(6.0)
                .Height(6.0)
                .Fill(Theme.Brushes.solid "#ffffff"),
              TextBlock().Text("LIVE")
            )
        )

    Button()
      .Name("LiveBadge")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(0.0)
      .Content(chip)
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        onOpenExternal stream.Url)
