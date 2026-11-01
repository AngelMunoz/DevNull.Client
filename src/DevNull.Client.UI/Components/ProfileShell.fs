module DevNull.Client.Components.ProfileShell

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.Theme

type Tab = {
  Label: string
  Active: IObservable<bool>
  Choose: unit -> unit
}

type FeedProps<'row when 'row: equality> = {
  Items: IObservable<'row list>
  KeyOf: 'row -> string
  Row: 'row -> Control
  HasMore: IObservable<bool>
  OnMore: unit -> unit
  End: IObservable<string option>
}

type Props<'model, 'row when 'row: equality> = {
  Model: IObservable<'model>
  Title: 'model -> string
  Kind: string option
  Padding: Thickness
  ErrorOf: 'model -> AppError option
  Busy: 'model -> string option
  Empty: 'model -> string option
  Note: 'model -> string option
  Header: unit -> Control
  Meta: unit -> Control
  FeedLabel: string option
  Tabs: Tab list
  Feed: FeedProps<'row>
}

let commitRow(ctx: CommitCard.RowProps, node: CommitNode) : Control =
  let card = CommitCard.feedRow ctx node

  match node with
  | LiveCommit commit ->
    StackPanel().Children(card, CommitCard.replyBlock(ctx, commit, ValueNone))
    : Control
  | Tombstone _ -> card

let kindLabel(kind: string) : Control =
  Border()
    .Classes(Theme.name Theme.Chip)
    .CornerRadius(999.0)
    .VerticalAlignment(VerticalAlignment.Center)
    .Child(
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .FontFamily(Fonts.mono)
        .FontSize(11.0)
        .Text(kind)
    )

let titleRow
  (model: IObservable<'model>, titleOf: 'model -> string, kind: string option)
  : Control =
  let text = model |> Observable.map titleOf

  let visible = text |> Observable.map(fun title -> title <> "" || kind.IsSome)

  let children: Control array = [|
    TextBlock().Classes(Theme.name Theme.Title).Text(text)
    yield! kind |> Option.map kindLabel |> Option.toArray
  |]

  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(8.0)
    .Margin(0.0, 0.0, 0.0, 12.0)
    .VerticalAlignment(VerticalAlignment.Center)
    .Children(children)
    .IsVisible(visible)

let tabRow(tabs: Tab list) : Control =
  let strip = Grid()

  strip.ColumnDefinitions <-
    ColumnDefinitions(String.Join(",", [ for _ in tabs -> "*" ]))

  let buttons: Control array = [|
    for index, tab in List.indexed tabs ->
      let button =
        Button()
          .Classes(Theme.name Theme.TabButton)
          .Content(tab.Label)
          .Column(index)
          .OnClickHandler(fun _ _ -> tab.Choose())

      Theme.bindClass(button, Theme.TabButtonActive, tab.Active)

      button
  |]

  Border()
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
    .Child(strip.Children(buttons))

let noteLine
  (model: IObservable<'model>, noteOf: 'model -> string option)
  : Control =
  TextBlock()
    .Classes(Theme.name Theme.Muted)
    .Margin(0.0, 8.0, 0.0, 0.0)
    .Text(model |> Observable.map(noteOf >> Option.defaultValue ""))
    .IsVisible(model |> Observable.map(noteOf >> Option.isSome))

let feedColumn(feed: FeedProps<'row>) : Control =
  let endVisible = feed.End |> Observable.map Option.isSome

  let endText = feed.End |> Observable.map(Option.defaultValue "")

  let endLine =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .TextAlignment(TextAlignment.Center)
      .TextWrapping(TextWrapping.Wrap)
      .Margin(24.0)
      .Text(endText)
      .IsVisible(endVisible)

  let footer =
    StackPanel()
      .Spacing(8.0)
      .Children(PageChrome.loadMore(feed.HasMore, feed.OnMore), endLine)

  Feed.view {
    Items = feed.Items
    KeyOf = feed.KeyOf
    Row = feed.Row
    Footer = ValueSome footer
    ScrollTop = ValueNone
    Restore = ValueNone
    OnScroll = ValueNone
    OnNeedMore = ValueNone
  }

let feedLabel(label: string) : Control =
  TextBlock()
    .Classes(Theme.name Theme.Subtitle)
    .Margin(0.0, 0.0, 0.0, 8.0)
    .Text(label)

let view(props: Props<'model, 'row>) : Control =
  let model = props.Model

  let error = PageChrome.errorPanel(model |> Observable.map props.ErrorOf)

  let busyVisible = model |> Observable.map(props.Busy >> Option.isSome)

  let busyText = model |> Observable.map(props.Busy >> Option.defaultValue "")

  let busyPanel =
    PageChrome.messagePanel(busyText, Theme.Muted).IsVisible(busyVisible)

  let emptyVisible = model |> Observable.map(props.Empty >> Option.isSome)

  let emptyText = model |> Observable.map(props.Empty >> Option.defaultValue "")

  let emptyPanel =
    PageChrome.messagePanel(emptyText, Theme.Muted).IsVisible(emptyVisible)

  let pinnedChildren: Control array = [|
    titleRow(model, props.Title, props.Kind)
    props.Header()
    props.Meta()
    error
    busyPanel
    emptyPanel
  |]

  let pinned =
    StackPanel()
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Margin(props.Padding)
      .Children(pinnedChildren)

  let label: Control =
    props.FeedLabel |> Option.map feedLabel |> Option.defaultValue(Panel())

  let body = Grid()

  body.RowDefinitions <- RowDefinitions("Auto,Auto,Auto,*")

  body.Children(
    label.Row(0),
    tabRow(props.Tabs).Row(1),
    noteLine(model, props.Note).Row(2),
    feedColumn(props.Feed).Row(3)
  )
  |> ignore

  let root = Grid()

  root.RowDefinitions <- RowDefinitions("Auto,*")

  root.Children(pinned.Row(0), body.Row(1))
