module DevNull.Client.Components.MenuPicks

open System
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Theme

[<Struct>]
type ConfirmProps = {
  Label: string
  Title: string
  Words: string
  Verb: string
  OnConfirm: unit -> unit
  Busy: IObservable<bool>
}

let menuPick(label: string, onPick: unit -> unit) : Control =
  Button()
    .Classes(Theme.name Theme.GhostButton)
    .Padding(6.0, 2.0, 6.0, 2.0)
    .FontSize(12.0)
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .HorizontalContentAlignment(HorizontalAlignment.Left)
    .Content(label)
    .OnClickHandler(fun _ args ->
      args.Handled <- true
      onPick())

// The destructive card the flyout carries: the site's question,
// the consequences, and the two answers.
let confirmCard(props: ConfirmProps, close: unit -> unit) : Control =
  Border()
    .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
    .BorderThickness(1.0)
    .CornerRadius(6.0)
    .Padding(10.0)
    .MaxWidth(300.0)
    .Child(
      StackPanel()
        .Spacing(6.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .FontSize(13.0)
            .Text(props.Title),
          TextBlock()
            .Classes(Theme.name Theme.Muted)
            .FontSize(12.0)
            .TextWrapping(TextWrapping.Wrap)
            .Text(props.Words),
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .HorizontalAlignment(HorizontalAlignment.Right)
            .Children(
              Button()
                .Classes(Theme.name Theme.GhostButton)
                .Padding(8.0, 2.0, 8.0, 2.0)
                .FontSize(12.0)
                .Content("Cancel")
                .OnClickHandler(fun _ args ->
                  args.Handled <- true
                  close()),
              Button()
                .Classes(Theme.name Theme.DangerButton)
                .Padding(8.0, 2.0, 8.0, 2.0)
                .FontSize(12.0)
                .Content(props.Verb)
                .IsEnabled(props.Busy |> Observable.map not)
                .OnClickHandler(fun _ args ->
                  args.Handled <- true
                  close()
                  props.OnConfirm())
            )
        )
    )

// The pick that opens the destructive card.
let confirmTrigger(label: string, onOpen: unit -> unit) : Control =
  Button()
    .Classes(Theme.name Theme.GhostButton)
    .Padding(6.0, 2.0, 6.0, 2.0)
    .FontSize(12.0)
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .HorizontalContentAlignment(HorizontalAlignment.Left)
    .Content(label)
    .OnClickHandler(fun _ args ->
      args.Handled <- true
      onOpen())

let confirmPick(props: ConfirmProps) : Control =
  let flyout = Popup()

  let trigger =
    confirmTrigger(props.Label, fun () -> flyout.IsOpen <- not flyout.IsOpen)

  let placed =
    flyout
      .Name("ConfirmFlyout")
      .PlacementTarget(trigger)
      .Placement(PlacementMode.BottomEdgeAlignedLeft)
      .Child(confirmCard(props, fun () -> flyout.IsOpen <- false))

  Panel().Children(trigger, placed)
