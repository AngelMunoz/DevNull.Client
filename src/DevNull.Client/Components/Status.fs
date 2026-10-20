module DevNull.Client.Status

open System
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Theme

let panel(text: string, brush: IObservable<IBrush>) : Control =
  TextBlock()
    .Text(text)
    .TextWrapping(TextWrapping.Wrap)
    .TextAlignment(TextAlignment.Center)
    .Foreground(brush)
    .Margin(24.0)
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .VerticalAlignment(VerticalAlignment.Center)

let busy message = panel(message, Brushes.muted)

let error message = panel(message, Brushes.danger)

let empty message = panel(message, Brushes.muted)

let soon message = panel(message, Brushes.muted)
