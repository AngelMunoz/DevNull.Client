module DevNull.Client.Components.CommitCard

open System
open System.Text.RegularExpressions
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
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

/// What one card reports up: open the thread, toggle the star, give a
/// pastel, or start a quote.
[<Struct>]
type Event =
  | Opened of Commit
  | StarToggled of Commit
  | PastelGiven of Commit
  | QuoteRequested of Commit

/// One commit row. `OnEvent` receives every interaction. Every card
/// rides the graph rail with its author's avatar; dense rows (the
/// replies of a thread) shrink the node.
[<Struct>]
type Props = {
  Commit: Commit
  OnEvent: Event -> unit
  Pastels: bool
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
      .Margin(0.0, 4.0, 0.0, 0.0)
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
      .Margin(0.0, 4.0, 0.0, 0.0)
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
      .BorderBrush(Brushes.border)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .Padding(10.0)
      .Margin(0.0, 6.0, 0.0, 0.0)
      .Child(
        StackPanel()
          .Spacing(2.0)
          .Children(
            TextBlock()
              .Classes(Theme.name Theme.Mono)
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
    option.Percentage
    |> Option.map(fun percentage -> $" · {percentage}%%")
    |> Option.defaultValue ""

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
    TextBlock().Classes(Theme.name Theme.Muted).Text(title)

    for option in poll.Options do
      pollOptionRow option
  |]

  Border()
    .BorderBrush(Brushes.border)
    .BorderThickness(Thickness(1.0))
    .CornerRadius(8.0)
    .Padding(10.0)
    .Margin(0.0, 6.0, 0.0, 0.0)
    .Child(StackPanel().Children(children))

let linkPreviewView(preview: LinkPreview) : Control =
  Border()
    .BorderBrush(Brushes.border)
    .BorderThickness(Thickness(1.0))
    .CornerRadius(8.0)
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

/// One quiet action of the row: an icon, an optional mono count, no
/// border. A handled click never opens the thread as a side effect.
let iconAction
  (name: string, glyph: Glyph, label: string voption, onClick: Button -> unit)
  : Button =
  let children: Control array = [|
    Icons.iconAt(glyph, 16.0)
    match label with
    | ValueNone -> ()
    | ValueSome count -> TextBlock().Classes(Theme.name Theme.Mono).Text(count)
  |]

  Button()
    .Name(name)
    .Classes(Theme.name Theme.GhostButton)
    .Padding(6.0, 2.0, 6.0, 2.0)
    .Content(
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(4.0)
        .Children(children)
    )
    .OnClickHandler(fun sender args ->
      args.Handled <- true
      onClick sender)

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

/// The identity line: name, handle, and time in the site's rhythm,
/// the short hash docked to the right edge.
let header(commit: Commit) : Control =
  let hash = TextBlock().Classes(Theme.name Theme.Mono).Text(commit.ShortHash)

  let identity =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(6.0)
      .Children(
        TextBlock().Classes(Theme.name Theme.Subtitle).Text(commit.Author.Name),
        TextBlock().Classes(Theme.name Theme.Muted).Text("·"),
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .Text($"@{commit.Author.Username}")
          .TextTrimming(TextTrimming.CharacterEllipsis),
        TextBlock().Classes(Theme.name Theme.Muted).Text("·"),
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .Text(
            UiClock.minute
            |> Observable.map(fun now -> timeAgo(now, commit.CreatedAt))
          )
      )

  DockPanel().Children(hash.DockRight(), identity)

/// The "replying to" line under the identity, when the commit answers
/// one.
let replyLine(commit: Commit) : Control option =
  match commit.Parent with
  | Some(LiveCommit parent) ->
    Some(
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Margin(0.0, 2.0, 0.0, 0.0)
        .Text($"replying to @{parent.Author.Username}")
    )
  | Some(Tombstone _) ->
    Some(
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Margin(0.0, 2.0, 0.0, 0.0)
        .Text("replying to [deleted]")
    )
  | None -> None

/// The rail cell: the author's avatar sitting on a short lane-colored
/// tail that ends in a dot, the site's loose feed rhythm. Dense rows
/// shrink the node.
let railCell(avatar: Control, lane: Lane option, dense: bool) : Panel =
  let nodeSize = if dense then 28.0 else 40.0

  let railWidth = if dense then 48.0 else 64.0

  let nodeTop = if dense then 20.0 else 32.0

  let dotZone = if dense then 12.0 else 20.0

  let tail =
    Border()
      .Width(2.0)
      .Background(Brushes.laneOf lane)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Stretch)
      .Margin(0.0, nodeTop, 0.0, dotZone)

  // The dot: a lane core inside a card gap inside a lane ring.
  let dot =
    Grid()
      .Width(18.0)
      .Height(18.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Bottom)
      .Margin(0.0, 0.0, 0.0, dotZone - 9.0)
      .Children(
        Ellipse().Width(18.0).Height(18.0).Fill(Brushes.laneOf lane),
        Ellipse()
          .Width(12.0)
          .Height(12.0)
          .StrokeThickness(3.0)
          .Stroke(Brushes.card)
          .Fill(Brushes.laneOf lane)
      )

  let ring =
    Border()
      .Background(Brushes.card)
      .BorderBrush(Brushes.laneOf lane)
      .BorderThickness(Thickness(2.0))
      .CornerRadius(nodeSize)
      .Padding(1.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Top)
      .Margin(0.0, nodeTop - (nodeSize + 6.0) / 2.0, 0.0, 0.0)
      .Child(avatar)

  Panel()
    .Width(railWidth)
    .MinHeight(if dense then 48.0 else 64.0)
    .Children(tail, dot, ring)

let actions(props: Props) : Control =
  let commit = props.Commit
  let onEvent = props.OnEvent
  let starred = commit.Starred |> Option.contains true

  let starLabel =
    commit.StarsCount
    |> Option.filter(fun count -> count > 0)
    |> Option.map shortCount
    |> ValueOption.ofOption

  let star =
    iconAction(
      "StarButton",
      Glyph.Star,
      starLabel,
      fun _ -> onEvent(StarToggled commit)
    )
      .Foreground(if starred then Brushes.accent else Brushes.muted)

  let pastel =
    let count =
      if commit.PasteisCount > 0 then
        ValueSome(shortCount commit.PasteisCount)
      else
        ValueNone

    iconAction(
      "PastelButton",
      Glyph.Pastel,
      count,
      fun _ -> onEvent(PastelGiven commit)
    )

  let quote =
    iconAction(
      "QuoteButton",
      Glyph.Quote,
      ValueNone,
      fun _ -> onEvent(QuoteRequested commit)
    )

  let link =
    iconAction(
      "LinkButton",
      Glyph.Link,
      ValueNone,
      fun button -> copyLink(button, commit.HtmlUrl)
    )

  let leftChildren: Control array = [|
    star
    if props.Pastels then
      pastel
    quote
  |]

  DockPanel()
    .LastChildFill(true)
    .Margin(0.0, 8.0, 0.0, 0.0)
    .Children(
      link.DockRight(),
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(2.0)
        .Children(leftChildren)
    )

let view(props: Props) : Control =
  let commit = props.Commit
  let dense = props.Dense

  // The site's order: the reply pointer, the body, the poll, the
  // quote, the link preview.
  let bodyChildren: Control array = [|
    match replyLine commit with
    | Some line -> line
    | None -> ()

    bodyView commit

    match commit.Poll with
    | Some poll -> pollView poll
    | None -> ()

    match commit.QuotedCommit with
    | Some quoted -> quotedView quoted
    | None -> ()

    match commit.LinkPreview with
    | Some preview -> linkPreviewView preview
    | None -> ()
  |]

  let body = StackPanel().Margin(0.0, 2.0, 0.0, 0.0).Children(bodyChildren)

  let avatar =
    Avatar.view {
      Name = commit.Author.Name
      Url = commit.Author.AvatarUrl
      Lane = commit.Author.Lane
      Size = if dense then 28.0 else 40.0
    }

  let pad = if dense then 8.0 else 12.0

  let content =
    StackPanel()
      .Margin(0.0, pad, 16.0, pad)
      .Children(header commit, body, actions props)

  // The hairline stops at the rail column: the rail runs unbroken.
  let contentColumn =
    Border()
      .BorderBrush(Brushes.border)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Child(content)

  let grid = Grid()

  grid.ColumnDefinitions <- ColumnDefinitions(if dense then "48,*" else "64,*")

  Button()
    .Classes(Theme.name Theme.CommitRow)
    .Content(
      grid.Children(
        railCell(avatar, commit.Author.Lane, dense).Column(0),
        contentColumn.Column(1)
      )
    )
    .OnClickHandler(fun _ _ -> props.OnEvent(Opened commit))

let tombstone() : Control =
  let line =
    Border()
      .Width(2.0)
      .Background(Brushes.rail)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Stretch)
      .Margin(0.0, 32.0, 0.0, 0.0)

  let node =
    Ellipse()
      .Width(24.0)
      .Height(24.0)
      .Fill(Brushes.card)
      .StrokeThickness(2.0)
      .Stroke(Brushes.rail)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Top)
      .Margin(0.0, 20.0, 0.0, 0.0)

  node.StrokeDashArray <-
    Avalonia.Collections.AvaloniaList<double>([ 3.0; 3.0 ])

  let text =
    Border()
      .BorderBrush(Brushes.border)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Child(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Margin(0.0, 12.0, 16.0, 12.0)
          .Children(
            TextBlock().Classes(Theme.name Theme.Mono).Text("[deleted]"),
            TextBlock()
              .Classes(Theme.name Theme.Muted)
              .Text("This commit was deleted.")
          )
      )

  let grid = Grid()

  grid.ColumnDefinitions <- ColumnDefinitions("64,*")

  grid.Children(Panel().Children(line, node).Column(0), text.Column(1))
