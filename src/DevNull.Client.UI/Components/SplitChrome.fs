module DevNull.Client.Components.SplitChrome

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Icons

/// The width below which the chrome gives up its two columns: one pane
/// at a time.
let breakpoint = 720.0

[<Struct>]
type Props = {
  Side: Control
  Main: Control
  MainOpen: IObservable<bool>
  OnBack: unit -> unit
}

let view(props: Props) : Control =
  let columns = Grid()

  let shape =
    Observable.combineLatest
      (columns.GetObservable(Visual.BoundsProperty)
       |> Observable.map(fun bounds -> bounds.Width))
      props.MainOpen
    |> Slices.share

  // The side takes the whole strip while it stands alone, its share of
  // a wide one (the site's 320, proportionally less, floored so the
  // rows stay readable), and nothing once the main pane owns a narrow
  // one.
  let sideWidth =
    shape
    |> Observable.map(fun (width, isOpen) ->
      if width < breakpoint then
        (if isOpen then 0.0 else width)
      else
        min 320.0 (max 232.0 (width * 0.42)))

  // The side pane's right hairline rides a border around it: the
  // column itself is a plain panel.
  let side =
    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 1.0, 0.0))
      .Width(sideWidth)
      .IsVisible(
        shape
        |> Observable.map(fun (width, isOpen) ->
          width >= breakpoint || not isOpen)
      )
      .Child(props.Side)

  // The one-pane main's way back to the side.
  let back =
    Button()
      .Name("BackToInbox")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(6.0, 2.0, 6.0, 2.0)
      .HorizontalAlignment(HorizontalAlignment.Left)
      .Content(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(4.0)
          .Children(
            Icons.iconAt(Glyph.ChevronLeft, 14.0),
            TextBlock().FontSize(12.0).Text("inbox")
          )
      )
      .IsVisible(
        shape
        |> Observable.map(fun (width, isOpen) -> width < breakpoint && isOpen)
      )
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        props.OnBack())

  let mainHost = Grid()

  mainHost.RowDefinitions <- RowDefinitions("Auto,*")

  mainHost.Children(back.Row(0).Margin(0.0, 0.0, 0.0, 4.0), props.Main.Row(1))
  |> ignore

  mainHost.IsVisible(
    shape
    |> Observable.map(fun (width, isOpen) -> width >= breakpoint || isOpen)
  )
  |> ignore

  columns.ColumnDefinitions <- ColumnDefinitions("Auto,*")

  columns.Children(
    side.Column(0),
    mainHost.Column(1).Margin(16.0, 0.0, 16.0, 0.0)
  )
  |> ignore

  // The chrome owns the page's top inset: the shell keeps the band.
  Grid().Margin(Thickness(0.0, Theme.bandHeight, 0.0, 0.0)).Children(columns)
