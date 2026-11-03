module DevNull.Client.Components.DialogChrome

open System
open System.Reactive.Disposables
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client

type Place =
  | Center
  | Bottom

[<Struct>]
type Props = {
  Open: IObservable<bool>
  Content: unit -> Control
  Scrim: bool
  OnScrimClick: (unit -> unit) voption
  Place: Place
}

let layer(props: Props) : Control =
  let host =
    ContentControl()
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .VerticalAlignment(VerticalAlignment.Stretch)

  match props.Place with
  | Center ->
    host
      .Margin(Thickness(24.0, Theme.bandHeight + 24.0, 24.0, 24.0))
      .HorizontalContentAlignment(HorizontalAlignment.Center)
      .VerticalContentAlignment(VerticalAlignment.Center)
    |> ignore
  | Bottom ->
    host
      .Margin(Thickness(24.0))
      .HorizontalContentAlignment(HorizontalAlignment.Center)
      .VerticalContentAlignment(VerticalAlignment.Bottom)
    |> ignore

  let scrim =
    Border().Classes(Theme.name Theme.ScrimBack).IsVisible(props.Scrim)

  match props.OnScrimClick with
  | ValueSome dismiss ->
    scrim.OnPointerPressedHandler(fun _ _ -> dismiss()) |> ignore
  | ValueNone -> ()

  let open' = props.Open |> Observable.distinctUntilChanged
  let layer = Panel().IsVisible(open').Children(scrim, host)

  let render(isOpen: bool) =
    if isOpen then
      host.Content <- props.Content()
    else
      host.Content <- null

  // The layer owns the build subscription: it starts with the layer's
  // attachment and stops when the layer leaves. A re-attached layer
  // reads the open signal again.
  let opens = new SerialDisposable()

  layer.AttachedToVisualTree.Add(fun _ ->
    opens.Disposable <- open' |> Observable.subscribe render)

  layer.DetachedFromVisualTree.Add(fun _ ->
    opens.Disposable <- Disposable.Empty)

  layer
