module DevNull.Client.Components.ProfileShell

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.Theme

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
  Busy: 'model -> PageChrome.Line
  Empty: 'model -> PageChrome.Line
  Note: 'model -> string option
  Header: unit -> Control
  Meta: (unit -> Control) voption
  FeedLabel: string option
  Tabs: TabStrip.Tab list
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
  let title =
    model
    |> Observable.map titleOf
    |> Observable.distinctUntilChanged
    |> Observable.replayBuffer 1
    |> Observable.refCount

  let visible = title |> Observable.map(fun text -> text <> "" || kind.IsSome)

  let children: Control array = [|
    TextBlock().Classes(Theme.name Theme.Title).Text(title)
    yield! kind |> Option.map kindLabel |> Option.toArray
  |]

  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(8.0)
    .Margin(0.0, 0.0, 0.0, 12.0)
    .VerticalAlignment(VerticalAlignment.Center)
    .Children(children)
    .IsVisible(visible)

let noteLine
  (model: IObservable<'model>, noteOf: 'model -> string option)
  : Control =
  let note =
    model
    |> Observable.map noteOf
    |> Observable.distinctUntilChanged
    |> Observable.replayBuffer 1
    |> Observable.refCount

  TextBlock()
    .Classes(Theme.name Theme.Muted)
    .Margin(0.0, 8.0, 0.0, 0.0)
    .Text(note |> Observable.map(Option.defaultValue ""))
    .IsVisible(note |> Observable.map Option.isSome)

/// The profile pages' feed column. These feeds are always-top: they
/// hand the feed no scroll slots, so a profile page never restores a
/// place; the slots wait for a consumer.
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
    HasMore = feed.HasMore
    ScrollTo = ValueNone
    Ride = false
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
  let label: Control =
    props.FeedLabel |> Option.map feedLabel |> Option.defaultValue(Panel())

  let body = Grid()

  body.RowDefinitions <- RowDefinitions("Auto,Auto,Auto,*")

  body.Children(
    label.Row(0),
    TabStrip.view(props.Tabs).Row(1),
    noteLine(props.Model, props.Note).Row(2),
    feedColumn(props.Feed).Row(3)
  )
  |> ignore

  let meta: Control list =
    props.Meta |> ValueOption.toList |> List.map(fun factory -> factory())

  PageChrome.page {
    Model = props.Model
    TitleOf = fun _ -> ""
    Padding = props.Padding
    ErrorOf = props.ErrorOf
    Busy = props.Busy
    Empty = props.Empty
    Header = [
      titleRow(props.Model, props.Title, props.Kind)
      props.Header()
      yield! meta
    ]
    Body = body
  }
