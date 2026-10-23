module DevNull.Client.Status

open System
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Theme

/// One line of text in the given foreground class.
let panel(text: string, fg: StyleClass) : Control =
  TextBlock()
    .Text(text)
    .TextWrapping(TextWrapping.Wrap)
    .TextAlignment(TextAlignment.Center)
    .Classes(Theme.name fg)
    .Margin(24.0)
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .VerticalAlignment(VerticalAlignment.Center)

let busy message = panel(message, Muted)

let error message = panel(message, FgDanger)

let empty message = panel(message, Muted)

let soon message = panel(message, Muted)
