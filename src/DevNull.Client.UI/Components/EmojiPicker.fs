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
open DevNull.Client.Core.Emoji
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

let markActive(cells: Border array, active: int) =
  cells
  |> Array.iteri(fun index cell ->
    let on = index = active
    let carries = cell.Classes.Contains activeClass

    if on && not carries then
      cell.Classes.Add activeClass
    elif not on && carries then
      cell.Classes.Remove activeClass |> ignore)

[<Struct>]
type CellArgs = {
  Props: Props
  Emoji: string
  Name: string
  Index: int
  Pick: string -> unit
}

[<Struct>]
type GroupLayout = {
  Content: Control
  Cells: Border array
  Sections: Control array
}

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

let groupsView(props: Props, pick: string -> unit) : GroupLayout =
  let offsets =
    Emoji.groups |> Array.scan (fun at group -> at + group.Emoji.Length) 0

  let groupCells =
    Emoji.groups
    |> Array.mapi(fun groupIndex group ->
      let offset = offsets[groupIndex]

      group.Emoji
      |> Array.mapi(fun index emoji ->
        cell {
          Props = props
          Emoji = emoji
          Name = Emoji.nameOf emoji
          Index = offset + index
          Pick = pick
        }))

  let cells = groupCells |> Array.collect id

  let sections =
    Emoji.groups
    |> Array.mapi(fun groupIndex group ->
      StackPanel()
        .Spacing(4.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
            .FontSize(11.0)
            .Margin(4.0, 6.0, 4.0, 2.0)
            .Text(group.Name),
          UniformGrid()
            .Columns(8)
            .Children(
              groupCells[groupIndex] |> Array.map(fun cell -> cell :> Control)
            )
        )
      :> Control)

  {
    Content = StackPanel().Spacing(8.0).Children(sections) :> Control
    Cells = cells
    Sections = sections
  }

let resultsView
  (props: Props, query: string, pick: string -> unit)
  : Control * Border array =
  let matches = Emoji.search query

  if matches.Length = 0 then
    let empty =
      TextBlock()
        .Classes(Theme.name Theme.FgMuted)
        .FontSize(13.0)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .Margin(0.0, 24.0, 0.0, 24.0)
        .Text($"no emoji for “{query.Trim()}”")

    StackPanel().Children(empty) :> Control, [||]
  else
    let cells =
      matches
      |> Array.mapi(fun index found ->
        cell {
          Props = props
          Emoji = found.Emoji
          Name = found.Name
          Index = index
          Pick = pick
        })

    let content =
      StackPanel()
        .Spacing(4.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
            .FontSize(11.0)
            .Margin(4.0, 6.0, 4.0, 2.0)
            .Text("Results"),
          UniformGrid()
            .Columns(8)
            .Children(cells |> Array.map(fun cell -> cell :> Control))
        )

    content :> Control, cells

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

  let contentHost = ContentControl()

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

  let scroller = ScrollViewer().Name("EmojiScroller").Content(contentHost)

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

  let bodyHost = Grid().Children(scroller, pinnedHeader)

  let activeFeed = new SerialDisposable()

  let groupsContent = lazy (groupsView(props, pick))

  let pin() =
    if not popup.IsOpen then
      pinnedHeader.IsVisible <- false
    else
      let layout = groupsContent.Value

      if not(obj.ReferenceEquals(contentHost.Content, layout.Content)) then
        pinnedHeader.IsVisible <- false
      else
        let current =
          layout.Sections
          |> Array.mapi(fun index section ->
            let at = section.TranslatePoint(Point(0.0, 0.0), layout.Content)

            let top =
              if at.HasValue then at.Value.Y - scroller.Offset.Y else nan

            index, top)
          |> Array.filter(fun (_, top) -> not(Double.IsNaN top) && top <= 0.0)
          |> Array.tryLast

        match current with
        | Some(index, top) when top < -0.5 ->
          pinnedName.Text <- Emoji.groups[index].Name
          pinnedHeader.IsVisible <- true
        | _ -> pinnedHeader.IsVisible <- false

  let show(query: string) =
    let content, cells =
      if Emoji.normalize query = "" then
        let layout = groupsContent.Value
        layout.Content, layout.Cells
      else
        resultsView(props, query, pick)

    contentHost.Content <- content
    scroller.Offset <- Vector(0.0, 0.0)
    pin()

    activeFeed.Disposable <-
      props.Active
      |> Observable.map(fun at -> clamp(cells.Length, at))
      |> Observable.distinctUntilChanged
      |> Observable.subscribe(fun at ->
        markActive(cells, at)

        cells |> Array.tryItem at |> Option.iter _.BringIntoView())

  scroller.ScrollChanged.Add(fun _ -> pin())

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

  search.LostFocus.Add(fun _ ->
    if popup.IsOpen && not panel.IsKeyboardFocusWithin then
      popup.IsOpen <- false
      props.Events Closed)

  search.AddHandler(
    InputElement.KeyDownEvent,
    (fun _ args ->
      if args.Key = Key.Escape && popup.IsOpen then
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
        if isOpen then
          let mode, height = placementOf(spaceOf props.Body)
          popup.Placement <- mode
          panel.MaxHeight <- height

        popup.IsOpen <- isOpen

        if isOpen then
          search.Text <- ""
          search.Focus() |> ignore
          show "")
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
