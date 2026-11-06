module DevNull.Client.Components.EmojiPicker

open System
open System.Reactive.Disposables
open System.Reactive.Subjects
open Avalonia
open Avalonia.Automation
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open Avalonia.VisualTree
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Components.Feed
open DevNull.Client.Core.Types

module Emoji = DevNull.Client.Core.Emoji

type Event =
  | QueryChanged of query: string
  | ActiveChanged of active: int
  | Closed
  | Picked of emoji: string

[<Struct>]
type Props = {
  Open: IObservable<bool>
  Query: IObservable<string>
  Active: IObservable<int>
  Selection: IObservable<int * int>
  Body: TextBox
  Trigger: Control
  Events: Event -> unit
}

let activeClass = Theme.name Theme.AccentBack

let panelCap = 400.0

let usableFloor = 200.0

[<Struct>]
type Space = { Above: float; Below: float }

let spaceOf(body: TextBox) : Space =
  match TopLevel.GetTopLevel body with
  | null -> { Above = panelCap; Below = panelCap }
  | top ->
    (body.TranslatePoint(Point(0.0, 0.0), top) |> Option.ofNullable,
     body.TranslatePoint(Point(0.0, body.Bounds.Height), top)
     |> Option.ofNullable)
    ||> Option.map2(fun head tail -> {
      Above = max 0.0 head.Y
      Below = max 0.0 (top.ClientSize.Height - tail.Y)
    })
    |> Option.defaultValue { Above = panelCap; Below = panelCap }

let placementOf(space: Space) : PlacementMode * float =
  if space.Below < usableFloor && space.Above > space.Below then
    PlacementMode.TopEdgeAlignedRight, min panelCap space.Above
  else
    PlacementMode.BottomEdgeAlignedRight, min panelCap space.Below

let inline labelOf(emoji: string, name: string) =
  if name = emoji then emoji else $":{name}:"

let inline clamp(count: int, at: int) =
  if count = 0 then 0 else max 0 (min at (count - 1))

// One line of the picker's virtualized list: a group's header, one
// eight-wide row of cells, or the line a query with no matches shows.
type Block =
  | Header of key: string * name: string
  | Row of key: string * start: int * cells: (string * string) array
  | Nothing of key: string * query: string

let inline keyOf(block: Block) : string =
  match block with
  | Header(key, _)
  | Row(key, _, _)
  | Nothing(key, _) -> key

let inline cellCount(blocks: Block[]) : int =
  blocks
  |> Array.sumBy(fun block ->
    match block with
    | Row(_, _, cells) -> cells.Length
    | _ -> 0)

let groupBlocks() : Block[] =
  let offsets =
    Emoji.groups |> Array.scan (fun at group -> at + group.Emoji.Length) 0

  Emoji.groups
  |> Array.mapi(fun index group -> [|
    Header($"g{index}", group.Name)

    yield!
      group.Emoji
      |> Array.map(fun emoji -> emoji, Emoji.nameOf emoji)
      |> Array.chunkBySize 8
      |> Array.mapi(fun row chunk ->
        Row($"g{index}r{row}", offsets[index] + row * 8, chunk))
  |])
  |> Array.collect id

let resultBlocks(query: string) : Block[] =
  let matches = Emoji.search query

  if Array.isEmpty matches then
    Array.singleton(Nothing("empty", query))
  else
    matches
    |> Array.map(fun found -> found.Emoji, found.Name)
    |> Array.chunkBySize 8
    |> Array.mapi(fun row chunk -> Row($"r{row}", row * 8, chunk))

[<Struct>]
type CellArgs = {
  Props: Props
  Emoji: string
  Name: string
  Index: int
  Pick: string -> unit
}

let cellShellPrefix = "EmojiCellShell"

let headerPrefix = "EmojiHeader"

let inline named(name: string | null, prefix: string) : bool =
  name |> Option.ofObj |> Option.exists(fun actual -> actual.StartsWith prefix)

let inline cellIndexOf(name: string | null) : int voption =
  match Option.ofObj name with
  | Some name when named(name, cellShellPrefix) ->
    match Int32.TryParse(name.Substring cellShellPrefix.Length) with
    | true, index -> ValueSome index
    | _ -> ValueNone
  | _ -> ValueNone

let realizedCells(scroller: ScrollViewer) : (int * Border) list =
  scroller.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? Border as border ->
      cellIndexOf border.Name
      |> ValueOption.map(fun index -> index, border)
      |> ValueOption.toOption
    | _ -> None)
  |> List.ofSeq

// The pinned group name comes from the block walk, not from the
// realized headers: a virtualized panel keeps only the rows near the
// viewport, so the header of the group being read is usually not in
// the tree at all.
[<Struct>]
type Walk = {
  Blocks: Block list
  Offset: float
  RowPitch: float
  HeaderPitch: float
}

[<TailCall>]
let rec pinnedAt(y: float, walk: Walk, name: string | null) : string | null =
  match walk.Blocks with
  | [] -> name
  | Header(_, next) :: tail ->
    let carried = if y <= walk.Offset - 0.5 then next else name

    pinnedAt(y + walk.HeaderPitch, { walk with Blocks = tail }, carried)
  | Row(_, _, _) :: tail ->
    pinnedAt(y + walk.RowPitch, { walk with Blocks = tail }, name)
  | Nothing _ :: tail -> pinnedAt(y, { walk with Blocks = tail }, name)

let pitchOf(scroller: ScrollViewer, prefix: string, fallback: float) : float =
  scroller.GetVisualDescendants()
  |> Seq.tryPick(fun visual ->
    match visual with
    | :? Control as control when named(control.Name, prefix) ->
      Some control.Bounds.Height
    | _ -> None)
  |> Option.defaultValue fallback

// The blocks walk a pitch estimate, so a key walk can land on a row
// the panel has not realized: the height of the first realized block
// of each kind sets the pitch for the rest.
[<Struct>]
type Place = {
  Blocks: Block list
  At: int
  RowPitch: float
  HeaderPitch: float
}

[<TailCall>]
let rec placeAt(y: float, walk: Place) : float voption =
  match walk.Blocks with
  | [] -> ValueNone
  | Header _ :: tail ->
    placeAt(y + walk.HeaderPitch, { walk with Blocks = tail })
  | Row(_, start, cells) :: tail ->
    let at = walk.At

    if at >= start && at < start + cells.Length then
      ValueSome y
    else
      placeAt(y + walk.RowPitch, { walk with Blocks = tail })
  | Nothing _ :: tail -> placeAt(y, { walk with Blocks = tail })

let scrollTo(scroller: ScrollViewer, blocks: Block list, at: int) =
  let walk = {
    Blocks = blocks
    At = at
    RowPitch = pitchOf(scroller, cellShellPrefix, 34.0)
    HeaderPitch = pitchOf(scroller, headerPrefix, 27.0)
  }

  match placeAt(0.0, walk) with
  | ValueSome y ->
    let room = max 0.0 (scroller.Extent.Height - scroller.Viewport.Height)
    let target = min room (max 0.0 (y - scroller.Viewport.Height / 2.0))

    scroller.Offset <- Vector(0.0, target)
  | ValueNone -> ()

let markRealized(scroller: ScrollViewer, at: int) =
  realizedCells scroller
  |> List.iter(fun (index, border) ->
    let on = index = at
    let carries = border.Classes.Contains activeClass

    if on && not carries then
      border.Classes.Add activeClass
    elif not on && carries then
      border.Classes.Remove activeClass |> ignore)

let arm(scroller: ScrollViewer, blocks: Block list, at: int) =
  markRealized(scroller, at)

  match realizedCells scroller |> List.tryFind(fst >> (=) at) with
  | Some(_, border) -> border.BringIntoView()
  | None -> scrollTo(scroller, blocks, at)

let cell(args: CellArgs) : Border =
  let label = labelOf(args.Emoji, args.Name)

  let glyph =
    TextBlock()
      .Text(args.Emoji)
      .FontSize(20.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Center)

  let button =
    Button()
      .Name($"EmojiCell{args.Index}")
      .Classes(Theme.name Theme.GhostButton)
      .Width(34.0)
      .Height(34.0)
      .Padding(Thickness(0.0))
      .Focusable(false)
      .HorizontalContentAlignment(HorizontalAlignment.Center)
      .VerticalContentAlignment(VerticalAlignment.Center)
      .Content(glyph)
      .OnClickHandler(fun _ _ -> args.Pick args.Emoji)

  ToolTip.SetTip(button, label)
  AutomationProperties.SetName(button, label)

  button.PointerEntered.Add(fun _ ->
    args.Props.Events(ActiveChanged args.Index))

  Border()
    .Name($"EmojiCellShell{args.Index}")
    .CornerRadius(6.0)
    .Width(34.0)
    .Height(34.0)
    .Child(button)

let blockView(props: Props, pick: string -> unit, block: Block) : Control =
  match block with
  | Header(key, name) ->
    TextBlock()
      .Name($"{headerPrefix}{key}")
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .FontSize(11.0)
      .Margin(4.0, 6.0, 4.0, 2.0)
      .Text(name)
    :> Control
  | Row(_, start, cells) ->
    UniformGrid()
      .Columns(8)
      .Children(
        cells
        |> Array.mapi(fun at (emoji, name) ->
          cell {
            Props = props
            Emoji = emoji
            Name = name
            Index = start + at
            Pick = pick
          }
          :> Control)
      )
    :> Control
  | Nothing(_, query) ->
    TextBlock()
      .Name("EmojiEmpty")
      .Classes(Theme.name Theme.Muted)
      .FontSize(13.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Margin(0.0, 24.0, 0.0, 24.0)
      .Text($"no emoji for “{query.Trim()}”")
    :> Control

let groupItems =
  lazy
    (Emoji.groups
     |> Array.collect(fun group ->
       group.Emoji |> Array.map(fun emoji -> emoji, Emoji.nameOf emoji)))

let itemsFor(query: string) : (string * string) array =
  let normalized = Emoji.normalize query

  if normalized = "" then
    groupItems.Value
  else
    Emoji.search query |> Array.map(fun found -> found.Emoji, found.Name)

let view(props: Props) : Control =
  let selection = new BehaviorSubject<int * int>((0, 0))
  let standing = new BehaviorSubject<int>(0)

  let popup =
    Popup()
      .Name("EmojiPanel")
      .PlacementTarget(props.Body)
      .Placement(PlacementMode.BottomEdgeAlignedRight)
      .IsLightDismissEnabled(false)

  let bodyText() =
    props.Body.Text |> Option.ofObj |> Option.defaultValue ""

  let pick(emoji: string) =
    let start, finish = selection.Value
    let text = bodyText()

    let inserted =
      Emoji.insertAt {
        Text = text
        Start = start
        Finish = finish
        Emoji = emoji
        MaxLength = CommitBodies.maxRawLength
      }

    popup.IsOpen <- false
    props.Body.Focus() |> ignore

    match inserted with
    | ValueSome result ->
      props.Body.Text <- result.Text
      props.Body.CaretIndex <- result.Caret
    | ValueNone ->
      let start = min start text.Length
      let finish = min finish text.Length

      props.Body.CaretIndex <- finish
      props.Body.SelectionStart <- start
      props.Body.SelectionEnd <- finish

    props.Events(Picked emoji)

  let restoreSelection() =
    let text = bodyText()
    let start, finish = selection.Value

    props.Body.CaretIndex <- min finish text.Length
    props.Body.SelectionStart <- min start text.Length
    props.Body.SelectionEnd <- min finish text.Length

  let close() =
    popup.IsOpen <- false
    props.Events Closed
    props.Body.Focus() |> ignore
    restoreSelection()

  let search =
    TextBox()
      .Name("EmojiSearch")
      .PlaceholderText("Search emoji")
      .FontSize(13.0)
      .Margin(8.0, 8.0, 8.0, 6.0)

  let catalog = groupBlocks()

  let blocks = new BehaviorSubject<Block list>(catalog |> List.ofArray)

  let cellTotal = new BehaviorSubject<int>(cellCount catalog)

  let noMore = new BehaviorSubject<bool>(false)

  let footerEmoji =
    TextBlock()
      .Name("EmojiFooterGlyph")
      .FontSize(24.0)
      .VerticalAlignment(VerticalAlignment.Center)

  let footerName =
    TextBlock()
      .Name("EmojiFooterName")
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .FontSize(12.0)
      .VerticalAlignment(VerticalAlignment.Center)

  let footer =
    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
      .Padding(Thickness(4.0, 6.0, 4.0, 0.0))
      .Child(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(8.0)
          .Children(footerEmoji, footerName)
      )

  let scroller =
    Feed.view {
      Items = blocks
      KeyOf = keyOf
      Row = fun block -> blockView(props, pick, block)
      Footer = ValueNone
      HasMore = noMore
      ScrollTo = ValueNone
      Ride = false
      Restore = ValueNone
      OnScroll = ValueNone
      OnRide = ValueNone
      OnNeedMore = ValueNone
    }
    |> fun feed -> feed.Name("EmojiScroller")

  let pinnedName =
    TextBlock()
      .Name("EmojiPinnedHeaderName")
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .FontSize(11.0)
      .Margin(4.0, 6.0, 4.0, 2.0)

  let pinnedHeader =
    Border()
      .Name("EmojiPinnedHeader")
      .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .IsVisible(false)
      .IsHitTestVisible(false)
      .VerticalAlignment(VerticalAlignment.Top)
      .Child(pinnedName)

  // The section identities the automation reads: named once at
  // construction, because a styled element can no longer take a name.
  // The active mark is the mode's landmark; it carries no content.
  let groupsMark = Border().Name("EmojiGroups").IsHitTestVisible(false)

  let resultsMark =
    Border().Name("EmojiResults").IsHitTestVisible(false).IsVisible(false)

  AutomationProperties.SetName(groupsMark, "Emoji groups")

  AutomationProperties.SetName(resultsMark, "Results")

  let bodyHost =
    Grid().Children(scroller, pinnedHeader, groupsMark, resultsMark)

  let activeFeed = new SerialDisposable()

  let pin() =
    let walk = {
      Blocks = blocks.Value
      Offset = scroller.Offset.Y
      RowPitch = pitchOf(scroller, cellShellPrefix, 34.0)
      HeaderPitch = pitchOf(scroller, headerPrefix, 27.0)
    }

    match popup.IsOpen, pinnedAt(0.0, walk, null) with
    | true, name when not(isNull name) ->
      pinnedName.Text <- name
      pinnedHeader.IsVisible <- true
    | _ -> pinnedHeader.IsVisible <- false

  let show(query: string) =
    let results = Emoji.normalize query <> ""
    let next = if results then resultBlocks query else groupBlocks()

    blocks.OnNext(List.ofArray next)
    cellTotal.OnNext(cellCount next)

    groupsMark.IsVisible <- not results
    resultsMark.IsVisible <- results

    scroller.Offset <- Vector(0.0, 0.0)
    pin()

    activeFeed.Disposable <-
      props.Active
      |> Observable.map(fun at -> clamp(cellTotal.Value, at))
      |> Observable.distinctUntilChanged
      |> Observable.subscribe(fun at -> arm(scroller, blocks.Value, at))

  scroller.ScrollChanged.Add(fun _ ->
    markRealized(scroller, standing.Value)
    pin())

  let panel =
    Border()
      .Name("EmojiPanelBody")
      .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
      .BorderThickness(1.0)
      .CornerRadius(8.0)
      .Width(304.0)
      .MaxHeight(400.0)
      .Child(
        DockPanel()
          .LastChildFill(true)
          .Children(search.DockTop(), footer.DockBottom(), bodyHost)
      )

  popup.Child <- panel
  popup.Closed.Add(fun _ -> props.Events Closed)

  search.TextChanged.Add(fun _ ->
    let query = search.Text |> Option.ofObj |> Option.defaultValue ""
    props.Events(QueryChanged query))

  search.LostFocus.Add(fun args ->
    let holdsTheClick =
      args.NavigationMethod = NavigationMethod.Pointer
      && props.Trigger.IsKeyboardFocusWithin

    if
      popup.IsOpen && not panel.IsKeyboardFocusWithin && not holdsTheClick
    then
      popup.IsOpen <- false
      props.Events Closed)

  search.AddHandler(
    InputElement.KeyDownEvent,
    (fun _ args ->
      if not args.Handled && args.Key = Key.Escape && popup.IsOpen then
        args.Handled <- true
        close()
      elif not args.Handled then
        let query = search.Text |> Option.ofObj |> Option.defaultValue ""
        let items = itemsFor query
        let current = clamp(items.Length, standing.Value)

        match args.Key with
        | Key.Right
        | Key.Left
        | Key.Down
        | Key.Up ->
          args.Handled <- true

          let step =
            match args.Key with
            | Key.Right -> 1
            | Key.Left -> -1
            | Key.Down -> 8
            | _ -> -8

          props.Events(ActiveChanged(clamp(items.Length, current + step)))
        | Key.Enter when args.KeyModifiers = KeyModifiers.None ->
          args.Handled <- true

          items
          |> Array.tryItem current
          |> Option.iter(fun (emoji, _) -> pick emoji)
        | _ -> ()),
    RoutingStrategies.Tunnel,
    true
  )

  let start() : IDisposable =
    let feeds = new CompositeDisposable()

    feeds.Add(
      props.Selection
      |> Observable.subscribe(fun value -> selection.OnNext value)
    )

    feeds.Add(
      props.Active
      |> Observable.distinctUntilChanged
      |> Observable.subscribe(fun active -> standing.OnNext active)
    )

    feeds.Add(
      props.Query
      |> Observable.map(fun query -> query.Trim())
      |> Observable.distinctUntilChanged
      |> Observable.subscribe(fun query ->
        if popup.IsOpen then
          show query)
    )

    feeds.Add(
      props.Open
      |> Observable.distinctUntilChanged
      |> Observable.subscribe(fun isOpen ->
        let wasOpen = popup.IsOpen

        if isOpen then
          let mode, height = placementOf(spaceOf props.Body)
          popup.Placement <- mode
          panel.MaxHeight <- height

        popup.IsOpen <- isOpen

        if isOpen then
          search.Text <- ""
          search.Focus() |> ignore
          show ""
        elif wasOpen then
          ignore(props.Body.Focus()))
    )

    feeds.Add(
      Observable.combineLatest props.Query props.Active
      |> Observable.subscribe(fun (query, active) ->
        let items = itemsFor query
        let at = clamp(items.Length, active)

        match items |> Array.tryItem at with
        | Some(emoji, name) ->
          footerEmoji.Text <- emoji
          footerName.Text <- labelOf(emoji, name)
        | None ->
          footerEmoji.Text <- ""
          footerName.Text <- "")
    )

    feeds.Add activeFeed
    feeds

  let startWith(_: Dispatch<obj>) : IDisposable = start()

  let startLane =
    Sub.ofId "emoji-picker" startWith
    |> Sub.untilDetached props.Body
    |> List.exactlyOne
    |> snd

  let _ = startLane ignore

  popup
