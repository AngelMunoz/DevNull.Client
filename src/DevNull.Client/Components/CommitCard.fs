module DevNull.Client.Components.CommitCard

open System
open System.Text.RegularExpressions
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open IcedTasks
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Formatting
open DevNull.Client.Icons
open DevNull.Client.Theme

/// What one card reports up: open the thread, toggle the star, or
/// start a quote.
[<Struct>]
type Event =
  | Opened of Commit
  | StarToggled of Commit
  | QuoteRequested of Commit

/// One commit row. `OnEvent` receives every interaction. `Rail`
/// carries the lane of the row above: `ValueSome` draws the commit on
/// the graph rail, `ValueNone` lays the row out flat.
[<Struct>]
type Props = {
  Commit: Commit
  OnEvent: Event -> unit
  Rail: Lane option voption
  Dense: bool
}

/// The server renders code blocks as html; the client shows the text
/// with the tags off.
let plainText(html: string) : string = Regex.Replace(html, "<[^>]+>", "")

let segmentView(segment: Segment) : Control =
  match segment with
  | TextSegment text -> TextBlock().Text(text).TextWrapping(TextWrapping.Wrap)
  | CodeSegment code ->
    Border()
      .Background(Brushes.surface)
      .CornerRadius(6.0)
      .Padding(10.0)
      .Margin(0.0, 6.0, 0.0, 0.0)
      .Child(
        TextBlock()
          .Classes(Theme.name Theme.CodeBlock)
          .Text(code)
          .TextWrapping(TextWrapping.Wrap)
      )
  | BlockSegment(_, html) ->
    Border()
      .Background(Brushes.surface)
      .CornerRadius(6.0)
      .Padding(10.0)
      .Margin(0.0, 6.0, 0.0, 0.0)
      .Child(
        TextBlock()
          .Classes(Theme.name Theme.CodeBlock)
          .Text(plainText html)
          .TextWrapping(TextWrapping.Wrap)
      )

let bodyView(commit: Commit) : Control =
  match commit.BodySegments with
  | [] -> TextBlock().Text(commit.Body).TextWrapping(TextWrapping.Wrap)
  | segments ->
    StackPanel().Children([| for segment in segments -> segmentView segment |])

let quotedView(quoted: CommitNode) : Control =
  match quoted with
  | LiveCommit commit ->
    Border()
      .Background(Brushes.surface)
      .CornerRadius(6.0)
      .Padding(10.0)
      .Margin(0.0, 6.0, 0.0, 0.0)
      .Child(
        StackPanel()
          .Spacing(2.0)
          .Children(
            TextBlock()
              .Classes(Theme.name Theme.Muted)
              .Text($"@{commit.Author.Username}"),
            TextBlock()
              .Text(commit.Body)
              .TextWrapping(TextWrapping.Wrap)
              .MaxLines(4)
              .TextTrimming(TextTrimming.CharacterEllipsis)
          )
      )
  | Tombstone _ ->
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Text("quoting a deleted commit")
      .Margin(0.0, 6.0, 0.0, 0.0)

let pollOptionRow(option: PollOption) : Control =
  let share =
    option.Percentage |> Option.map(sprintf " · %d%%") |> Option.defaultValue ""

  StackPanel()
    .Spacing(2.0)
    .Margin(0.0, 4.0, 0.0, 0.0)
    .Children(
      TextBlock().Text(option.Label),
      TextBlock().Classes(Theme.name Theme.Mono).Text(share)
    )

let pollView(poll: Poll) : Control =
  let title =
    poll.TotalVotes
    |> Option.map(fun votes -> $"poll · {votes} votes")
    |> Option.defaultValue "poll"

  let children: Control array = [|
    yield TextBlock().Classes(Theme.name Theme.Muted).Text(title)
    for option in poll.Options do
      yield pollOptionRow option
  |]

  Border()
    .Background(Brushes.surface)
    .CornerRadius(6.0)
    .Padding(10.0)
    .Margin(0.0, 6.0, 0.0, 0.0)
    .Child(StackPanel().Children(children))

let linkPreviewView(preview: LinkPreview) : Control =
  Border()
    .Background(Brushes.surface)
    .CornerRadius(6.0)
    .Padding(10.0)
    .Margin(0.0, 6.0, 0.0, 0.0)
    .Child(
      StackPanel()
        .Spacing(2.0)
        .Children(
          TextBlock()
            .Text(preview.Title |> Option.defaultValue preview.Url)
            .TextWrapping(TextWrapping.Wrap),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text(preview.Url)
            .TextTrimming(TextTrimming.CharacterEllipsis)
        )
    )

let actionButton
  (glyph: Glyph, label: string, onClick: Button -> unit)
  : Button =
  Button()
    .Classes(Theme.name Theme.GhostButton)
    .Padding(6.0, 2.0, 6.0, 2.0)
    .Content(
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(4.0)
        .Children(
          Icons.icon glyph,
          TextBlock().Classes(Theme.name Theme.Mono).Text(label)
        )
    )
    // The card itself is a button: a handled action click never opens
    // the thread as a side effect.
    .OnClickHandler(fun button args ->
      args.Handled <- true
      onClick button)

/// Puts the commit's web URL on the clipboard.
let copyLink(source: Control, url: string) : unit =
  match TopLevel.GetTopLevel source with
  | null -> ()
  | top ->
    match top.Clipboard with
    | null -> ()
    | clipboard ->
      let data = new DataTransfer()
      data.Add(DataTransferItem.CreateText url)

      asyncEx { do! clipboard.SetDataAsync data } |> Async.StartImmediate

let header(commit: Commit, dense: bool, avatar: Control voption) : Control =
  let headGrid = Grid()

  let nameGrid = Grid()

  nameGrid.ColumnDefinitions <- ColumnDefinitions.Parse "Auto,Auto,*,Auto"

  let name =
    TextBlock()
      .Classes(Theme.name Theme.Subtitle)
      .VerticalAlignment(VerticalAlignment.Center)
      .Text(commit.Author.Name)

  let handle =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .VerticalAlignment(VerticalAlignment.Center)
      .Margin(6.0, 0.0, 0.0, 0.0)
      .Text($"@{commit.Author.Username}")
      .TextTrimming(TextTrimming.CharacterEllipsis)

  let time =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .VerticalAlignment(VerticalAlignment.Center)
      .Margin(6.0, 0.0, 0.0, 0.0)
      .Text(timeAgo(DateTimeOffset.UtcNow, commit.CreatedAt))

  let hash =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .VerticalAlignment(VerticalAlignment.Center)
      .Text(commit.ShortHash)

  nameGrid.Children(
    name.Column(0),
    handle.Column(1),
    time.Column(2),
    hash.Column(3)
  )
  |> ignore

  match avatar with
  | ValueSome face ->
    headGrid.ColumnDefinitions <- ColumnDefinitions.Parse "Auto,*"

    headGrid.Children(face.Margin(0.0, 0.0, 10.0, 0.0), nameGrid.Column(1))
    |> ignore
  | ValueNone ->
    headGrid.ColumnDefinitions <- ColumnDefinitions.Parse "*"

    headGrid.Children(nameGrid.Column(0)) |> ignore

  headGrid

let actions(commit: Commit, onEvent: Event -> unit) : Control =
  let starLabel =
    commit.StarsCount |> Option.map shortCount |> Option.defaultValue ""

  let replyLabel =
    commit.RepliesCount |> Option.map shortCount |> Option.defaultValue ""

  let starred = commit.Starred |> Option.contains true

  let star =
    actionButton(Glyph.Star, starLabel, fun _ -> onEvent(StarToggled commit))

  if starred then
    star.Foreground(Brushes.accent) |> ignore

  let reply =
    actionButton(Glyph.Reply, replyLabel, fun _ -> onEvent(Opened commit))

  let quote =
    actionButton(Glyph.Quote, "Quote", fun _ -> onEvent(QuoteRequested commit))

  let link =
    actionButton(
      Glyph.Link,
      "Link",
      fun button -> copyLink(button, commit.HtmlUrl)
    )

  let actionRow =
    WrapPanel().Orientation(Orientation.Horizontal).Margin(0.0, 8.0, 0.0, 0.0)

  actionRow.ItemSpacing <- 8.0

  actionRow.Children(star, reply, quote, link)

/// The rail cell of a graph row: the line from the row above, the
/// line to the row below, and the avatar sitting on the seam.
let railCell
  (avatar: Control, ownLane: Lane option, prevLane: Lane option)
  : Panel =
  let line =
    Border()
      .Width(2.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Stretch)

  let paintOwn(control: Border) =
    match ownLane with
    | Some(LaneIndex index) -> control.Background(Brushes.lane index)
    | Some(LaneColor hex) -> control.Background(Brushes.solid hex)
    | None -> control.Background(Brushes.laneFallback)

  let paintPrevious(control: Border) =
    match prevLane with
    | Some(LaneIndex index) -> control.Background(Brushes.lane index)
    | Some(LaneColor hex) -> control.Background(Brushes.solid hex)
    | None -> control.Background(Brushes.rail)

  // The avatar center sits 28 dips down: half the top margin plus
  // half the avatar.
  let above =
    Border()
      .Width(2.0)
      .Height(28.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Top)

  paintOwn line |> ignore
  paintPrevious above |> ignore

  let face =
    Border()
      .Background(Brushes.card)
      .CornerRadius(20.0)
      .Padding(2.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Top)
      .Margin(0.0, 10.0, 0.0, 0.0)
      .Child(avatar)

  Panel().Children(line, above, face)

let view(props: Props) : Control =
  let commit = props.Commit
  let dense = props.Dense

  let quotedPart =
    commit.QuotedCommit |> Option.map quotedView |> Option.toArray

  let pollPart = commit.Poll |> Option.map pollView |> Option.toArray

  let previewPart =
    commit.LinkPreview |> Option.map linkPreviewView |> Option.toArray

  let bodyChildren =
    Array.concat [ quotedPart; [| bodyView commit |]; pollPart; previewPart ]

  let body = StackPanel().Margin(0.0, 6.0, 0.0, 0.0).Children(bodyChildren)

  // One avatar per row: on the rail it rides the rail cell, flat rows
  // carry it in the header.
  let avatar =
    Avatar.view {
      Name = commit.Author.Name
      Url = commit.Author.AvatarUrl
      Lane = commit.Author.Lane
      Size = if dense then 24.0 else 36.0
    }

  let headerAvatar =
    match props.Rail with
    | ValueSome _ ->
      // The rail cell owns the avatar's placement.
      ValueNone
    | ValueNone ->
      avatar.Margin(0.0, 0.0, 10.0, 0.0) |> ignore
      ValueSome avatar

  let content =
    StackPanel()
      .Margin(
        Thickness(
          0.0,
          (if dense then 6.0 else 10.0),
          16.0,
          (if dense then 6.0 else 10.0)
        )
      )
      .Children(
        header(commit, dense, headerAvatar),
        body,
        actions(commit, props.OnEvent)
      )

  // The hairline stops at the rail column: the rail runs unbroken.
  let contentColumn =
    Border()
      .BorderBrush(Brushes.border)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Child(content)

  let row: Control =
    match props.Rail with
    | ValueNone -> contentColumn :> Control
    | ValueSome prevLane ->
      let grid = Grid()

      grid.ColumnDefinitions <- ColumnDefinitions("56,*")

      grid.Children(
        railCell(avatar, commit.Author.Lane, prevLane).Column(0),
        contentColumn.Column(1)
      )

  Button()
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .HorizontalContentAlignment(HorizontalAlignment.Stretch)
    .Background(Brushes.transparent)
    .Content(row)
    .OnClickHandler(fun _ _ -> props.OnEvent(Opened commit))

let tombstone(tombstone: Tombstone) : Control =
  TextBlock()
    .Classes(Theme.name Theme.Mono)
    .Text($"rm {tombstone.Id} → /dev/null")
    .Margin(0.0, 0.0, 0.0, 8.0)
