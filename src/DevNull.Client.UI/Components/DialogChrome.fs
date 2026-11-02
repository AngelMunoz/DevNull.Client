module DevNull.Client.Components.DialogChrome

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Theme

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

  open'
  |> Observable.add(fun isOpen ->
    if isOpen then
      host.Content <- props.Content()
    else
      host.Content <- null)

  layer
