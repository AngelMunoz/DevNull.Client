module DevNull.Client.Components.Feed

open System
open System.Reactive.Disposables
open Avalonia
open Avalonia.Threading
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Templates
open Avalonia.VisualTree
open FSharp.Control.Reactive
open NXUI.Extensions
open MVUish.Avalonia.Collections
open DevNull.Client

// One remembered place coming back: lands once when the loaded extent
// reaches it, asks for more pages while it does not and the host holds
// one, then stays out of the way of the user's own scrolling.
type Restore
  (target: float, needMore: (unit -> unit) voption, hasMore: IObservable<bool>) as this
  =
  let latest = new SerialDisposable()

  // Starts the has-more watch with the feed's attachment.
  member _.Start() =
    latest.Disposable <- hasMore.Subscribe(fun more -> this.More <- more)

  // Ends the watch with the feed's detach; a re-attached feed starts it
  // again against the latest value.
  member _.Stop() = latest.Disposable <- Disposable.Empty

  member _.Target = target

  member val Landed = false with get, set

  member val More = false with get, set

  // Whether the feed itself may move the scroller past the target
  // while the chase runs. A riding feed does; its own bottom edge
  // must not count as the reader's choice.
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

[<Struct>]
type Ride = {
  Riding: bool
  Offset: float
  Room: float
  FromEnd: float
  Viewport: float
}

[<Struct>]
type Paging = {
  Order: DevNull.Client.FeedWindow.Order
  HasOlder: IObservable<bool>
  HasNewer: IObservable<bool>
  OnOlder: unit -> unit
  OnNewer: unit -> unit
}

[<Struct>]
type Advanced<'T> = {
  AnchorKeyOf: ('T -> string option) voption
  EntryCursorOf: ('T -> string option) voption
  RestoreAnchor: DevNull.Client.FeedWindow.Anchor voption
  OnAnchor: (DevNull.Client.FeedWindow.Anchor -> unit) voption
  Paging: Paging voption
}

module Advanced =

  let none<'T>() : Advanced<'T> = {
    AnchorKeyOf = ValueNone
    EntryCursorOf = ValueNone
    RestoreAnchor = ValueNone
    OnAnchor = ValueNone
    Paging = ValueNone
  }

type Props<'T when 'T: equality> = {
  Header: Control voption
  Items: IObservable<'T list>
  KeyOf: 'T -> string
  Row: 'T -> Control
  Footer: Control voption
  HasMore: IObservable<bool>
  ScrollTo: IObservable<float> voption
  Ride: bool
  Restore: float voption
  OnScroll: (float -> unit) voption
  OnRide: (Ride -> unit) voption
  OnNeedMore: (unit -> unit) voption
}

// Where the reading stands at the feed's bottom edge: whether it
// rides that edge, and the extent the last scroll saw.
type TailPlace = { Riding: bool; Extent: float }

// Keeps a riding reader at the bottom: each pass measures the scroll,
// sticks to the end while the extent grows under a riding reader, and
// stops once the reader scrolls away. A report-only tail computes the
// same place without moving the scroller.
type Tail(stick: bool) =
  member val Place = { Riding = true; Extent = 0.0 } with get, set

  member this.Track(scroller: ScrollViewer) : Ride =
    let extent = scroller.Extent.Height
    let viewport = scroller.Viewport.Height
    let room = extent - viewport
    let fromEnd = room - scroller.Offset.Y
    let current = this.Place

    let riding =
      if fromEnd < 48.0 then
        true
      elif extent > current.Extent + 0.5 && current.Riding then
        if stick then
          scroller.ScrollToEnd()

        current.Riding
      else
        false

    this.Place <- { Riding = riding; Extent = extent }

    {
      Riding = riding
      Offset = scroller.Offset.Y
      Room = room
      FromEnd = room - scroller.Offset.Y
      Viewport = viewport
    }

// The inputs one anchor restore needs. One record keeps the
// constructor at a single argument; the readers stay lazy so each
// try sees the latest feed.
[<Struct>]
type AnchorRestoreArgs<'T> = {
  Anchor: DevNull.Client.FeedWindow.Anchor
  Scroller: ScrollViewer
  ControlOf: unit -> ItemsControl option
  ItemsOf: unit -> 'T list
  KeyOf: 'T -> string
}

// One anchor restore: realizes the row with ScrollIntoView, then
// adjusts the offset until the row's top sits at the stored intra-row
// offset. The row's container may arrive one layout pass later.
type AnchorRestore<'T>(args: AnchorRestoreArgs<'T>) =
  member val Landed = false with get, set

  member val Found = false with get, set

  member val Reported = false with get, set

  member val Seeked = false with get, set

  member val Attempts = 0 with get, set

  member val Scheduled = false with get, set

  member val OnLanded: (DevNull.Client.FeedWindow.Anchor -> unit) =
    ignore with get, set

  member this.Anchor = args.Anchor

  member this.Finish() =
    this.Landed <- true

    if this.Found && not this.Reported then
      this.Reported <- true
      this.OnLanded this.Anchor

  member this.Schedule() =
    if not this.Landed && not this.Scheduled then
      this.Scheduled <- true

      Dispatcher.UIThread.Post(
        Action(fun () ->
          this.Scheduled <- false
          this.Try()),
        DispatcherPriority.Background
      )

  member this.Request() = this.Schedule()

  member this.Try() =
    if not this.Landed then
      match args.ControlOf() with
      | None -> this.Schedule()
      | Some control ->
        match
          args.ItemsOf()
          |> List.tryFindIndex(fun item -> args.KeyOf item = args.Anchor.Key)
        with
        // The row is not loaded yet: the page fetch is still in flight.
        // Stay pending without spending an attempt; the next items
        // emission and the next attach each request a new try.
        | None -> ()
        | Some index ->
          this.Attempts <- this.Attempts + 1

          if this.Attempts > 8 then
            this.Finish()
          else
            this.Found <- true

            if not this.Seeked then
              // The control can lag behind the items after a rebuild:
              // seek only a row it already holds, and try again on the
              // next pass. A missed seek stays unmarked, so the failure
              // path reschedules instead of reporting a seek done.
              if index >= 0 && index < control.ItemCount then
                control.ScrollIntoView(index)
                this.Seeked <- true

              this.Schedule()
            else
              match control.ContainerFromIndex(index) with
              | null -> this.Schedule()
              | container ->
                let point =
                  container.TranslatePoint(Point(0.0, 0.0), args.Scroller)

                if point.HasValue then
                  let delta = point.Value.Y + args.Anchor.Offset

                  if abs delta <= 0.5 || this.Attempts >= 8 then
                    this.Finish()
                  else
                    args.Scroller.Offset <-
                      Vector(0.0, max 0.0 (args.Scroller.Offset.Y + delta))

                    this.Schedule()
                else
                  this.Schedule()

let EdgeThreshold = 240.0

// The paging availability watch. Attach starts it against the page's
// paging contract and detach ends it, so the two subscriptions never
// outlive the feed's scroller; they only write flags, so no control
// is rooted while they run.
type PagingWatch(paging: Paging) =
  let latest = new SerialDisposable()
  let mutable hasOlder = false
  let mutable hasNewer = false
  let mutable olderAsked = false
  let mutable newerAsked = false

  // Starts the watch with the feed's attachment.
  member _.Start() =
    let older =
      paging.HasOlder.Subscribe(fun available ->
        hasOlder <- available

        if not available then
          olderAsked <- false)

    let newer =
      paging.HasNewer.Subscribe(fun available ->
        hasNewer <- available

        if not available then
          newerAsked <- false)

    latest.Disposable <- new CompositeDisposable([| older; newer |])

  // Ends the watch with the feed's detach, mirroring `Restore.Stop`.
  member _.Stop() = latest.Disposable <- Disposable.Empty

  member _.Check(scroller: ScrollViewer) =
    let room = scroller.Extent.Height - scroller.Viewport.Height
    let fromEnd = room - scroller.Offset.Y
    let nearStart = scroller.Offset.Y < EdgeThreshold
    let nearEnd = fromEnd < EdgeThreshold
    let underfilled = room <= 0.0

    let olderEdge =
      match paging.Order with
      | DevNull.Client.FeedWindow.NewestFirst -> underfilled || nearEnd
      | DevNull.Client.FeedWindow.OldestFirst -> underfilled || nearStart

    let newerEdge =
      match paging.Order with
      | DevNull.Client.FeedWindow.NewestFirst -> nearStart
      | DevNull.Client.FeedWindow.OldestFirst -> nearEnd

    if olderEdge && hasOlder && not olderAsked then
      olderAsked <- true
      paging.OnOlder()
    elif newerEdge && hasNewer && not newerAsked then
      newerAsked <- true
      paging.OnNewer()

let anchorKeyOf
  (props: Props<'T>, advanced: Advanced<'T>, item: 'T)
  : string option =
  match advanced.AnchorKeyOf with
  | ValueSome keyOf -> keyOf item
  | ValueNone -> Some(props.KeyOf item)

let entryCursorOf(advanced: Advanced<'T>, item: 'T) =
  match advanced.EntryCursorOf with
  | ValueSome cursorOf -> cursorOf item
  | ValueNone -> None

// The inputs one top-anchor read needs. One record keeps the reader
// at a single argument.
[<Struct>]
type TopAnchorArgs<'T when 'T: equality> = {
  Props: Props<'T>
  Advanced: Advanced<'T>
  Scroller: ScrollViewer
  Control: ItemsControl
  Items: 'T list
}

// The first visible row that names a post. Separator and marker rows
// answer none, so the anchor always restores a post.
let topAnchor
  (args: TopAnchorArgs<'T>)
  : DevNull.Client.FeedWindow.Anchor option =
  args.Items
  |> List.indexed
  |> List.tryPick(fun (index, item) ->
    match args.Control.ContainerFromIndex(index) with
    | null -> None
    | container ->
      let point = container.TranslatePoint(Point(0.0, 0.0), args.Scroller)

      if
        not point.HasValue || point.Value.Y + container.Bounds.Height <= 0.0
      then
        None
      else
        match anchorKeyOf(args.Props, args.Advanced, item) with
        | None -> None
        | Some key ->
          Some {
            Key = key
            Offset = max 0.0 (-point.Value.Y)
            Entry = entryCursorOf(args.Advanced, item) |> ValueOption.ofOption
          })

let viewWith(props: Props<'T>, advanced: Advanced<'T>) : ScrollViewer =
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

  let gated = props.Items |> Slices.gateByRef

  // A cursor feed always runs through the virtualizing panel, even
  // below ten rows: the panel is the only source of native anchor
  // candidates, and a cursor feed must hold its row when a page is
  // prepended. Static short feeds keep the plain panel.
  let build(long: bool) : Control =
    ItemsControl()
      .ItemTemplate(template)
      .ItemsPanel(
        FuncTemplate<Panel>(fun () ->
          if long then
            VirtualizingStackPanel() :> Panel
          else
            StackPanel())
      )
      .BindItemsSource(gated, props.KeyOf)

  let mutable current: (bool * Control) voption = ValueNone
  let mutable latestItems: 'T list = []
  let mutable lastCount = -1
  let mutable lastAnchor: DevNull.Client.FeedWindow.Anchor voption = ValueNone
  let mutable holdAnchor: DevNull.Client.FeedWindow.Anchor voption = ValueNone
  let mutable holdPending = false
  let mutable requestRestore: unit -> unit = ignore
  let mutable requestHold: unit -> unit = ignore

  let select(rows: 'T list) : Control =
    latestItems <- rows
    requestRestore()

    if rows.Length <> lastCount then
      holdAnchor <- lastAnchor
      holdPending <- true
      requestHold()
      lastCount <- rows.Length

    let long =
      rows.Length >= 10
      || ValueOption.isSome advanced.Paging
      || ValueOption.isSome advanced.RestoreAnchor

    match current with
    | ValueSome(mode, control) when mode = long -> control
    | previous ->
      match previous with
      | ValueSome(_, old) ->
        match old with
        | :? ItemsControl as stale -> stale.ItemsSource <- Array.empty<obj>
        | _ -> ()
      | ValueNone -> ()

      let next = build long
      current <- ValueSome(long, next)
      next

  let list =
    ContentControl().Content(gated |> Observable.map select |> _.ToBinding())

  let children: Control array = [|
    match props.Header with
    | ValueNone -> ()
    | ValueSome header -> header
    list
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

  // A remembered place comes back through the legacy chase; every
  // settled place reports out so the host can park it. The handler is
  // the scroller's own event, so it dies with the feed.
  let restore =
    match advanced.RestoreAnchor, props.Restore with
    | ValueSome _, _ -> None
    | ValueNone, ValueSome target when target > 0.0 ->
      let chase = Restore(target, props.OnNeedMore, props.HasMore)
      chase.LandWhenBeyond <- props.Ride
      Some chase
    | _ -> None

  let controlOf() : ItemsControl option =
    match current with
    | ValueSome(_, (:? ItemsControl as control)) -> Some control
    | _ -> None

  let anchorRestore =
    match advanced.RestoreAnchor with
    | ValueSome anchor ->
      Some(
        AnchorRestore(
          {
            Anchor = anchor
            Scroller = scroller
            ControlOf = controlOf
            ItemsOf = (fun () -> latestItems)
            KeyOf = props.KeyOf
          }
        )
      )
    | ValueNone -> None

  let restoring() =
    anchorRestore |> Option.exists(fun restore -> not restore.Landed)

  let reportAnchor(anchor: DevNull.Client.FeedWindow.Anchor) =
    let same =
      match lastAnchor with
      | ValueSome previous ->
        previous.Key = anchor.Key
        && abs(previous.Offset - anchor.Offset) < 0.5
        && previous.Entry = anchor.Entry
      | ValueNone -> false

    if not same then
      lastAnchor <- ValueSome anchor
      advanced.OnAnchor |> ValueOption.iter(fun report -> report anchor)

  anchorRestore |> Option.iter(fun restore -> restore.OnLanded <- reportAnchor)

  requestRestore <-
    fun () -> anchorRestore |> Option.iter(fun restore -> restore.Request())

  // Native scroll anchoring is not available on every panel and
  // headless layout. Hold the top row explicitly when an items change
  // adds or removes rows above it.
  let holdIfNeeded() =
    if holdPending then
      holdPending <- false

      if scroller.Offset.Y > 0.1 && not(restoring()) then
        match holdAnchor, controlOf(), latestItems with
        | ValueSome anchor, Some control, items ->
          let current =
            topAnchor {
              Props = props
              Advanced = advanced
              Scroller = scroller
              Control = control
              Items = items
            }

          if current |> Option.forall(fun place -> place.Key <> anchor.Key) then
            let restore =
              AnchorRestore(
                {
                  Anchor = anchor
                  Scroller = scroller
                  ControlOf = controlOf
                  ItemsOf = (fun () -> latestItems)
                  KeyOf = props.KeyOf
                }
              )

            restore.OnLanded <- reportAnchor
            restore.Request()
        | _ -> ()

  requestHold <-
    fun () ->
      Dispatcher.UIThread.Post(
        Action(fun () -> holdIfNeeded()),
        DispatcherPriority.Background
      )

  let captureAnchor() =
    if not(restoring()) then
      match controlOf(), latestItems with
      | Some control, items ->
        topAnchor {
          Props = props
          Advanced = advanced
          Scroller = scroller
          Control = control
          Items = items
        }
        |> Option.iter reportAnchor
      | None, _ -> ()

  let mutable anchorPending = false

  let requestAnchor() =
    // A scroll burst fires per pixel: park at most one background
    // capture that reads the latest position, so the settled place
    // still reports exactly once.
    if not anchorPending then
      anchorPending <- true

      Dispatcher.UIThread.Post(
        Action(fun () ->
          anchorPending <- false
          captureAnchor()),
        DispatcherPriority.Background
      )

  let pagingWatch =
    match advanced.Paging with
    | ValueSome paging -> Some(PagingWatch(paging))
    | ValueNone -> None

  let checkEdges() =
    pagingWatch |> Option.iter(fun watch -> watch.Check(scroller))

  // The scroller owns the chase's has-more watch: it starts when the
  // feed attaches and ends when the feed leaves, so a rebuilt thread
  // feed drops its old watch with the old scroller.
  restore
  |> Option.iter(fun chase ->
    scroller.AttachedToVisualTree.Add(fun _ -> chase.Start())
    scroller.DetachedFromVisualTree.Add(fun _ -> chase.Stop()))

  scroller.AttachedToVisualTree.Add(fun _ ->
    pagingWatch |> Option.iter(fun watch -> watch.Start())
    requestRestore())

  scroller.DetachedFromVisualTree.Add(fun _ ->
    pagingWatch |> Option.iter(fun watch -> watch.Stop()))

  let tail =
    if props.Ride || ValueOption.isSome props.OnRide then
      Some(Tail(props.Ride))
    else
      None

  scroller.LayoutUpdated.Add(fun _ -> checkEdges())

  scroller.ScrollChanged.Add(fun _ ->
    let ride = tail |> Option.map(fun track -> track.Track scroller)

    restore |> Option.iter(fun chase -> chase.Chase scroller)

    props.OnScroll |> ValueOption.iter(fun park -> park scroller.Offset.Y)

    requestAnchor()

    ride
    |> Option.iter(fun current ->
      props.OnRide |> ValueOption.iter(fun report -> report current))

    checkEdges())

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

let view(props: Props<'T>) : ScrollViewer = viewWith(props, Advanced.none())
