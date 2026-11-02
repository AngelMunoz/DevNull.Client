module DevNull.Client.Components.Status

open System
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Theme
open FSharp.Control.Reactive

/// One line of text in the given foreground class, following an
/// observable source.
let panel(text: IObservable<string>, fg: StyleClass) : Control =
  TextBlock()
    .TextWrapping(TextWrapping.Wrap)
    .TextAlignment(TextAlignment.Center)
    .Classes(Theme.name fg)
    .Margin(24.0)
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .VerticalAlignment(VerticalAlignment.Center)
    .Text(text)

/// The busy panel: one quiet line of text, centered.
let inline busy(message: string) : Control =
  panel(Observable.single message, Muted)

/// The empty panel: one line of muted text, centered.
let inline empty(message: string) : Control =
  panel(Observable.single message, Muted)

/// The placeholder panel for a route the API does not serve yet.
let inline soon(message: string) : Control =
  panel(Observable.single message, Muted)
