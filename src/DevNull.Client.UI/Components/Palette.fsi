module DevNull.Client.Components.Palette

open System
open Avalonia.Controls

/// One row's controls: the decoration the list draws before the row
/// (a section heading or caption), the row itself, and — when the keys
/// can land on the row — the control the active index brings into view.
[<Struct>]
type RenderedRow = {
  Leading: Control voption
  Control: Control
  Followed: Control voption
}

/// The rows the palette shows and the keys it follows: the row list,
/// the identity that gates a rebuild, the render function, the active
/// index, and the pointer's report when the host wants it.
type Props<'row, 'key> = {
  Rows: IObservable<'row list>
  KeyOf: 'row -> 'key
  Render: 'row -> RenderedRow
  Active: IObservable<int>
  OnHover: (int -> unit) voption
}

/// The scroller's shape: the name finders use and the tallest list it
/// shows before it scrolls.
[<Struct>]
type Scroll = { Name: string; MaxHeight: float }

/// One palette list over the host's rows: the scroller, the gated row
/// build, and the active row's follow. The host keeps the keys and the
/// model; a key updates the host model, and the palette only consumes
/// the active index. The row controls stay private to the palette, and
/// its follow subscription ends when the palette detaches, so it must
/// not be hosted inline.
val view: scroll: Scroll * props: Props<'row, 'key> -> Control
