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

/// One commit card. `OnEvent` receives every interaction.
type Props = {
  Commit: Commit
  OnEvent: Event -> unit
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
      TextBlock().Classes(Theme.name Theme.Muted).Text(share)
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
            .Classes(Theme.name Theme.Muted)
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
        .Children(Icons.icon glyph, TextBlock().Text(label))
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

let header(commit: Commit) : Control =
  let lane =
    commit.Author.Lane
    |> Option.bind(function
      | LaneIndex index -> Some index
      | LaneColor _ -> None)

  let avatar =
    Avatar.view {
      Name = commit.Author.Name
      Url = commit.Author.AvatarUrl
      Lane = lane
      Size = 36.0
    }

  let headGrid = Grid()

  headGrid.ColumnDefinitions <- ColumnDefinitions.Parse "Auto,*"

  let nameGrid = Grid()

  nameGrid.ColumnDefinitions <- ColumnDefinitions.Parse "Auto,*,Auto"

  let name =
    TextBlock().Classes(Theme.name Theme.Subtitle).Text(commit.Author.Name)

  let handle =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Text($"@{commit.Author.Username}")
      .Margin(6.0, 0.0, 0.0, 0.0)
      .TextTrimming(TextTrimming.CharacterEllipsis)

  let time =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Text(timeAgo(DateTimeOffset.UtcNow, commit.CreatedAt))
      .Margin(6.0, 0.0, 0.0, 0.0)

  let hash =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Text(commit.ShortHash)
      .HorizontalAlignment(HorizontalAlignment.Right)

  nameGrid.Children(name.Column(0), handle.Column(1), time.Column(2)) |> ignore

  let side = StackPanel().Column(1).Children(nameGrid, hash)

  headGrid.Children(avatar.Margin(0.0, 0.0, 10.0, 0.0), side) |> ignore

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
    star.Foreground <- Brushes.accent

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

let view(props: Props) : Control =
  let commit = props.Commit

  let quotedPart =
    commit.QuotedCommit |> Option.map quotedView |> Option.toArray

  let pollPart = commit.Poll |> Option.map pollView |> Option.toArray

  let previewPart =
    commit.LinkPreview |> Option.map linkPreviewView |> Option.toArray

  let bodyChildren =
    Array.concat [ quotedPart; [| bodyView commit |]; pollPart; previewPart ]

  let body = StackPanel().Margin(0.0, 8.0, 0.0, 0.0).Children(bodyChildren)

  Button()
    .Margin(0.0, 0.0, 0.0, 8.0)
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .HorizontalContentAlignment(HorizontalAlignment.Stretch)
    .Background(Brushes.transparent)
    .Content(
      Border()
        .Classes(Theme.name Theme.Card)
        .Child(
          StackPanel()
            .Children(header commit, body, actions(commit, props.OnEvent))
        )
    )
    .OnClickHandler(fun _ _ -> props.OnEvent(Opened commit))

let tombstone(tombstone: Tombstone) : Control =
  TextBlock()
    .Classes(Theme.name Theme.Muted)
    .Text("this commit was deleted")
    .Margin(0.0, 0.0, 0.0, 8.0)
