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

/// One remembered place coming back: lands once when the loaded extent
/// reaches it, asks for more pages while it does not, then stays out of
/// the way of the user's own scrolling.
type Restore(target: float, needMore: (unit -> unit) voption) =
  member _.Target = target

  member val Landed = false with get, set

  member this.Chase(scroller: ScrollViewer) =
    if not this.Landed then
      // The landing clamps until the panel has realized enough rows, so
      // the place counts as reached only when the offset itself holds
      // it; until then the extent keeps growing under the re-asserted
      // offset and the chase keeps asking for pages.
      if scroller.Offset.Y >= this.Target - 0.5 then
        this.Landed <- true
      elif scroller.Extent.Height - scroller.Viewport.Height >= this.Target then
        scroller.Offset <- Vector(0.0, this.Target)
      else
        needMore |> ValueOption.iter(fun ask -> ask())

type Props<'T when 'T: equality> = {
  Items: IObservable<'T list>
  KeyOf: 'T -> string
  Row: 'T -> Control
  Footer: Control voption
  /// Emits when the feed must return to its top, whatever scrolled it
  /// down. The listener ends when the feed leaves the visual tree.
  ScrollTop: IObservable<unit> voption
  /// The scroll place this feed picks up, or none for a fresh feed.
  Restore: float voption
  /// Reports every settled scroll place, so the host can park it.
  OnScroll: (float -> unit) voption
  /// Asks the host for the next page while a remembered place sits
  /// beyond the loaded extent.
  OnNeedMore: (unit -> unit) voption
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

  // A remembered place comes back through the chase; every settled
  // place reports out so the host can park it. The handler is the
  // scroller's own event, so it dies with the feed.
  let restore =
    match props.Restore with
    | ValueSome target when target > 0.0 ->
      Some(Restore(target, props.OnNeedMore))
    | _ -> None

  scroller.ScrollChanged.Add(fun _ ->
    props.OnScroll |> ValueOption.iter(fun park -> park scroller.Offset.Y)

    restore |> Option.iter(fun chase -> chase.Chase scroller))

  // A return-to-top signal reaches the feed's own scroller; the
  // listener ends with the feed's detach, so a parked page's stashed
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
