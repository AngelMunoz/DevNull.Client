module DevNull.Client.Components.ImageViewer

open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Theme

// The API's image and the control share the short name; the domain
// value wins here.
type ApiImage = DevNull.Client.Core.ApiTypes.Image

[<Struct>]
type Show = {
  Owner: Window
  Cache: ImageCacheService
  Photos: ApiImage list
  Start: int
}

/// Builds the control.
let show(show: Show) : unit =
  let photos = show.Photos
  let count = photos.Length
  let ink = SolidColorBrush(Colors.White)

  // The walk wraps around the ends, the way the site's lightbox does.
  let walk(i: int, step: int) = (i + step + count) % count

  let viewer =
    Window()
      .Name("ImageViewer")
      .WindowDecorations(WindowDecorations.None)
      .ShowInTaskbar(false)
      .CanResize(false)
      .Background(SolidColorBrush(Colors.Black))
      .Width(show.Owner.ClientSize.Width * 0.9)
      .Height(show.Owner.ClientSize.Height * 0.9)
      .WindowStartupLocation(WindowStartupLocation.CenterOwner)

  // Every step is one fresh stage: the stage's own handlers carry the
  // index, so the walk needs no state outside the tree.
  let rec render(i: int) : unit =
    let loaded =
      AsyncImage.view {
        Cache = show.Cache
        Url = photos[i].Url
        // The stage fills the owner's frame, so the decode pays for
        // the viewport's height, not a fixed width a tall photo would
        // exceed many times over.
        DecodeWidth = 1600
        DecodeHeight = ValueSome(int(show.Owner.ClientSize.Height * 0.9))
        Stretch = Stretch.Uniform
        Placeholder = ValueNone
        ToolTip = ValueNone
      }

    // A click on the photo only looks: the press stops at the picture
    // and never reaches the backdrop's close.
    let picture =
      Border()
        .Child(loaded)
        .OnPointerPressedHandler(fun _ args -> args.Handled <- true)

    let counter =
      TextBlock()
        .Name("ViewerCount")
        .Classes(Theme.name Theme.Mono)
        .Foreground(ink)
        .Text($"{i + 1} / {count}")
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Bottom)
        .Margin(12.0)

    let close =
      Button()
        .Name("ViewerClose")
        .Classes(Theme.name Theme.GhostButton)
        .Foreground(ink)
        .Content(Icons.iconAt(Glyph.Close, 20.0))
        .HorizontalAlignment(HorizontalAlignment.Right)
        .VerticalAlignment(VerticalAlignment.Top)
        .Margin(12.0)
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          viewer.Close())

    let stage =
      Grid()
        .Name("ImageViewerStage")
        .Focusable(true)
        .Background(SolidColorBrush(Colors.Black))
        .Children(picture, counter, close)

    // The backdrop closes the show; the arrows ride the keys.
    stage.PointerPressed.Add(fun _ -> viewer.Close())

    stage.KeyDown.Add(fun args ->
      match args.Key with
      | Key.Escape -> viewer.Close()
      | Key.Left when count > 1 ->
        args.Handled <- true
        render(walk(i, -1))
      | Key.Right when count > 1 ->
        args.Handled <- true
        render(walk(i, 1))
      | _ -> ())

    viewer.Content <- stage
    // The stage takes the keyboard so the walk and the escape land on
    // it; the bool answer only says whether the focus moved.
    stage.Focus() |> ignore

  viewer.Show(show.Owner)
  render(show.Start)
