module DevNull.Client.Components.Feed

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Templates
open Avalonia.Layout
open FSharp.Control.Reactive
open NXUI.Extensions
open MVUish.Avalonia
open MVUish.Avalonia.Collections

/// One remembered place coming back: lands once when the loaded extent
/// reaches it, asks for more pages while it does not and the host holds
/// one, then stays out of the way of the user's own scrolling.
type Restore
  (target: float, needMore: (unit -> unit) voption, hasMore: IObservable<bool>) as this
  =
  do hasMore.Subscribe(fun more -> this.More <- more) |> ignore

  member _.Target = target

  member val Landed = false with get, set

  member val More = false with get, set

  /// Whether the feed itself may move the scroller past the target
  /// while the chase runs. A riding feed does; its own bottom edge
  /// must not count as the reader's choice.
  member val LandWhenBeyond = false with get, set

  member this.Chase(scroller: ScrollViewer) =
    if not this.Landed then
      // The landing clamps until the panel has realized enough rows, so
      // the place counts as reached only when the offset itself holds
      // it; until then the extent keeps growing under the re-asserted
      // offset and the chase keeps asking for pages.
      let room = scroller.Extent.Height - scroller.Viewport.Height

      if this.LandWhenBeyond && room >= this.Target then
        scroller.Offset <- Vector(0.0, this.Target)
        this.Landed <- true
      elif scroller.Offset.Y >= this.Target - 0.5 then
        this.Landed <- true
      elif room >= this.Target then
        scroller.Offset <- Vector(0.0, this.Target)
      elif this.More then
        needMore |> ValueOption.iter(fun ask -> ask())

/// Where the reading stands at the feed's bottom edge: whether it
/// rides that edge, and the extent the last scroll saw.
type TailPlace = { Riding: bool; Extent: float }

/// Keeps a riding reader at the bottom: each pass measures the scroll,
/// sticks to the end while the extent grows under a riding reader, and
/// stops once the reader scrolls away.
type Tail() =
  let mutable place = { Riding = true; Extent = 0.0 }

  member _.Track(scroller: ScrollViewer) =
    let extent = scroller.Extent.Height
    let room = extent - scroller.Viewport.Height
    let fromEnd = room - scroller.Offset.Y
    let current = place

    let riding =
      if fromEnd < 48.0 then
        true
      elif extent > current.Extent + 0.5 && current.Riding then
        scroller.ScrollToEnd()
        current.Riding
      else
        false

    place <- { Riding = riding; Extent = extent }

type Props<'T when 'T: equality> = {
  Items: IObservable<'T list>
  KeyOf: 'T -> string
  Row: 'T -> Control
  Footer: Control voption
  /// Whether the host holds another page of rows. The restore chase
  /// asks for more only while this holds.
  HasMore: IObservable<bool>
  /// Emits the offset the scroller takes. The page hands over a slice
  /// of its own model that emits only on a real change, with the
  /// initial replay skipped, so a restored offset survives the attach.
  ScrollTo: IObservable<float> voption
  /// Whether the feed sticks to its bottom edge while the reader sits
  /// there. An items change that grows the extent scrolls back to the
  /// end until the reader scrolls away.
  Ride: bool
  /// The scroll place this feed picks up, or none for a fresh feed.
  Restore: float voption
  /// Reports every settled scroll place, so the host can park it.
  OnScroll: (float -> unit) voption
  /// Asks the host for the next page while a remembered place sits
  /// beyond the loaded extent.
  OnNeedMore: (unit -> unit) voption
}

let view(props: Props<'T>) : ScrollViewer =
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
      .BindItemsSource(
        props.Items
        |> Observable.distinctUntilChangedKeyCompare
          (fun items -> box items)
          ReferenceEqualityComparer.Instance,
        props.KeyOf
      )

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
      let chase = Restore(target, props.OnNeedMore, props.HasMore)
      chase.LandWhenBeyond <- props.Ride
      Some chase
    | _ -> None

  let tail = if props.Ride then Some(Tail()) else None

  scroller.ScrollChanged.Add(fun _ ->
    tail |> Option.iter(fun track -> track.Track scroller)

    restore |> Option.iter(fun chase -> chase.Chase scroller)

    props.OnScroll |> ValueOption.iter(fun park -> park scroller.Offset.Y))

  // A scroll signal reaches the feed's own scroller through a property
  // binding, so the subscription follows the scroller's life and
  // survives a stash; the page only hands over real changes.
  let scroller =
    match props.ScrollTo with
    | ValueSome offsets ->
      scroller.Offset(
        offsets |> Observable.map(fun offset -> Vector(0.0, offset))
      )
    | ValueNone -> scroller

  scroller
