module DevNull.Client.Components.TabStrip

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Theme

type Tab = {
  Label: string
  Active: IObservable<bool>
  Choose: unit -> unit
}

let view(tabs: Tab list) : Control =
  let strip = Grid()

  if not tabs.IsEmpty then
    strip.ColumnDefinitions <-
      tabs |> List.map(fun _ -> "*") |> String.concat "," |> ColumnDefinitions

  let buttons: Control array = [|
    for index, tab in List.indexed tabs ->
      ToggleButton()
        .Classes(Theme.name Theme.TabButton)
        .Content(tab.Label)
        .Column(index)
        .IsChecked(tab.Active |> Observable.map(fun active -> Nullable active))
        .OnClickHandler(fun _ _ -> tab.Choose())
  |]

  Border()
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
    .IsVisible(not tabs.IsEmpty)
    .Child(strip.Children(buttons))
