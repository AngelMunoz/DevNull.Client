module DevNull.Client.Components.Feed

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Templates
open Avalonia.Layout
open NXUI.Extensions
open MVUish.Avalonia
open MVUish.Avalonia.Collections

type Props<'T when 'T: equality> = {
  Items: IObservable<'T list>
  KeyOf: 'T -> string
  Row: 'T -> Control
  Footer: Control voption
  /// Emits when the feed must return to its top, whatever scrolled it
  /// down. The listener ends when the feed leaves the visual tree.
  ScrollTop: IObservable<unit> voption
}

let view(props: Props<'T>) : Control =
  // The recycle path clears the container's content and rebuilds its
  // child with `null` before the next row lands: the template stays
  // total against null.
  let template =
    FuncDataTemplate<'T>(
      (fun item _ ->
        if isNull(box item) then
          Panel() :> Control
        else
          props.Row item),
      false
    )

  let items =
    ItemsControl()
      .ItemTemplate(template)
      .ItemsPanel(FuncTemplate<Panel>(fun () -> VirtualizingStackPanel()))
      .BindItemsSource(props.Items, props.KeyOf)

  let children: Control array = [|
    items
    match props.Footer with
    | ValueNone -> ()
    | ValueSome footer -> footer
  |]

  // The rows run edge to edge of the reading column, the way the
  // site's feed does: the rail owns the left edge, no page gutter.
  let rows = StackPanel().Children(children)

  // The horizontal axis stays locked: wrapped rows measure against the
  // column, never against an unbounded extent.
  let scroller =
    ScrollViewer()
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
      .Content(rows)

  // A return-to-top signal reaches the feed's own scroller; the
  // listener ends with the feed's detach, so a cached page's stashed
  // scroller stops listening.
  match props.ScrollTop with
  | ValueNone -> ()
  | ValueSome signals ->
    let listen =
      Sub.ofId "scroll-top" (fun _ ->
        signals.Subscribe(fun () -> scroller.Offset <- Vector(0.0, 0.0)))
      |> Sub.untilDetached scroller
      |> List.exactlyOne
      |> snd

    listen ignore |> ignore

  scroller
