module DevNull.Client.Components.CommitCard

open System
open System.Text.RegularExpressions
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Documents
open Avalonia.Controls.Primitives
open Avalonia.Controls.Shapes
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open IcedTasks
open DevNull.Client
open DevNull.Client.Components.AsyncImage
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.ApiTypes
open DevNull.Client.DeviceAuth
open DevNull.Client.Formatting
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Theme

// The API's image record shares its name with the image control; the
// alias keeps both in reach.
type ApiImage = DevNull.Client.Core.ApiTypes.Image

/// What one card reports up: open the thread, toggle the star, give a
/// pastel, start a quote, reveal the reply composer, vote a poll
/// option, or walk more replies of a thread preview.
[<Struct>]
type Event =
  | Opened of Commit
  | ReplyToggled of Commit
  | ThreadRequested of Commit
  | MoreRepliesRequested of Commit
  | StarToggled of Commit
  | PastelGiven of Commit
  | QuoteRequested of Commit
  | PollVoted of Commit * PollOption

/// One commit row. `OnEvent` receives every interaction. Every card
/// rides the graph rail with its author's avatar; dense rows (the
/// replies of a thread) shrink the node.
[<Struct>]
type Props = {
  Commit: Commit
  OnEvent: Event -> unit
  ViewerId: string
  Pastels: IObservable<bool>
  Now: IObservable<DateTimeOffset>
  Dense: bool
  Images: ImageCacheService
  Flyout: Source voption
}

/// The card's semantic regions; the view only composes these.
module Parts =

  /// The server renders code blocks as html; the client shows the text
  /// with the tags off.
  let plainText(html: string) : string = Regex.Replace(html, "<[^>]+>", "")

  /// One piece of a text run after the mentions split: plain prose,
  /// or one interactive handle.
  type Piece =
    | Plain of string
    | Handle of string

  /// Splits one text run at its mentioned handles: plain prose between
  /// the @handles the mentions list confirms.
  let rec splitMentions(text: string, usernames: string list) : Piece list =
    let places =
      usernames
      |> List.choose(fun username ->
        let token = "@" + username
        let at = text.IndexOf(token, StringComparison.Ordinal)

        if at < 0 then None else Some(at, token))
      |> List.sortBy fst

    match places with
    | [] -> [ Plain text ]
    | (at, token) :: _ ->
      let before = text.Substring(0, at)
      let after = text.Substring(at + token.Length)
      let head = if before = "" then [] else [ Plain before ]

      head @ [ Handle(token.Substring 1) ] @ splitMentions(after, usernames)

  /// The user a mention names; the payload carries the handle only,
  /// the card fills the rest.
  let mentioned(username: string) : User = {
    Id = ""
    Username = username
    Name = username
    AvatarUrl = None
    Lane = None
    CommitsCount = None
    FollowersCount = None
    FollowingCount = None
    IsFollowed = None
    FollowsYou = None
  }

  /// One @handle of the body: the accent color, the hover card, and
  /// the click that opens the profile.
  let handleInline(flyout: Source voption, username: string) : Inline =
    let handle =
      TextBlock()
        .Name("MentionHandle")
        .Text("@" + username)
        .Foreground(Brushes.accent)

    match flyout with
    | ValueSome source ->
      InlineUIContainer(UserFlyout.attach(source, mentioned username, handle))
    | ValueNone -> InlineUIContainer(handle)

  /// The body text with its mentions wrapped, flowing the way the
  /// site's inline links do.
  let textWithMentions
    (flyout: Source voption, text: string, mentions: Mention list)
    : Control =
    match mentions with
    | [] -> TextBlock().Text(text).TextWrapping(TextWrapping.Wrap)
    | _ ->
      let block = TextBlock().TextWrapping(TextWrapping.Wrap)
      let inlines = InlineCollection()
      block.Inlines <- inlines

      splitMentions(text, mentions |> List.map _.Username)
      |> List.iter(fun piece ->
        match piece with
        | Plain plain -> inlines.Add(Run plain) |> ignore
        | Handle username ->
          inlines.Add(handleInline(flyout, username)) |> ignore)

      block

  let segmentView
    (flyout: Source voption, mentions: Mention list, segment: Segment)
    : Control =
    match segment with
    | TextSegment text -> textWithMentions(flyout, text, mentions)
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

  /// The body over any source: the segments with their mentions, the
  /// plain text when the payload carries no segments.
  let bodyOf(flyout: Source voption, commit: Commit) : Control =
    match commit.BodySegments with
    | [] -> textWithMentions(flyout, commit.Body, commit.Mentions)
    | segments ->
      StackPanel().Children [|
        for segment in segments -> segmentView(flyout, commit.Mentions, segment)
      |]

  let bodyView(props: Props, commit: Commit) : Control =
    bodyOf(props.Flyout, commit)

  /// The image block under the body: one image full width, two or
  /// three in a two-column grid. Each cell is a fixed box with the
  /// site's rounded corners: the reserved box keeps the feed height
  /// stable before the bytes land, and the site crops to fixed boxes
  /// the same way. A handled click opens the commit's gallery.
  let imagesView(cache: ImageCacheService, images: ApiImage list) : Control =
    // The gallery opens over the window the cell lives in; the owner
    // comes from the tree, not the page.
    let gallery(source: Control, index: int) : unit =
      match TopLevel.GetTopLevel source with
      | :? Window as owner ->
        ImageViewer.show {
          Owner = owner
          Cache = cache
          Photos = images
          Start = index
        }
      | _ -> ()

    let cellAt(stretch: Stretch, height: float) : int * ApiImage -> Control =
      fun (index, image) ->
        let loaded =
          AsyncImage.view {
            Cache = cache
            Url = image.Url
            DecodeWidth = 720
            Stretch = stretch
            Placeholder = ValueNone
            ToolTip = image.Description |> ValueOption.ofOption
          }

        let box =
          Border()
            .Background(Brushes.surface)
            .CornerRadius(8.0)
            .Height(height)
            .ClipToBounds(true)
            .Child(loaded.Name("CardImage"))

        Button()
          .Name("CardImageButton")
          .Classes(Theme.name Theme.GhostButton)
          .Padding(0.0)
          .Content(box)
          .OnClickHandler(fun _ args ->
            // The gallery never opens the commit behind the cell.
            args.Handled <- true
            gallery(box, index))

    match images with
    | [ single ] -> cellAt (Stretch.Uniform, 320.0) (0, single)
    | many ->
      UniformGrid().Columns(2).Margin(0.0, 6.0, 0.0, 0.0).Children [|
        for pair in Seq.indexed many ->
          cellAt (Stretch.UniformToFill, 150.0) pair
      |]

  /// The quoted commit: its own surface, a click away from the card it
  /// rides in. The handled click opens the quoted thread and never
  /// falls through to this commit's.
  let quotedView(onEvent: Event -> unit, quoted: CommitNode) : Control =
    match quoted with
    | LiveCommit commit ->
      Button()
        .Name("QuotedCard")
        .Classes(Theme.name Theme.GhostButton)
        .BorderBrush(Brushes.border)
        .BorderThickness(Thickness(1.0))
        .CornerRadius(8.0)
        .Padding(10.0)
        .Margin(0.0, 6.0, 0.0, 0.0)
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .Content(
          StackPanel()
            .Spacing(2.0)
            .Children(
              TextBlock()
                .Classes(Theme.name Theme.Mono)
                .Text($"@{commit.Author.Username}"),
              TextBlock()
                .Text(commit.Body)
                .TextWrapping(TextWrapping.Wrap)
                // The right margin keeps shaped lines inside the box.
                .Margin(0.0, 0.0, 4.0, 0.0)
                .MaxLines(4)
                .TextTrimming(TextTrimming.CharacterEllipsis)
            )
        )
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          onEvent(Opened commit))
    | Tombstone _ ->
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Text("quoting a deleted commit")
        .Margin(0.0, 6.0, 0.0, 0.0)

  /// Everything the poll block needs: the commit it rides, its poll,
  /// the host's event routing, and the viewer — the author sees
  /// results, never a vote.
  [<Struct>]
  type PollViewCtx = {
    Commit: Commit
    Poll: Poll
    OnEvent: Event -> unit
    ViewerId: string
  }

  /// One option while the poll is open and unvoted: one click takes it.
  let votableOptionRow(ctx: PollViewCtx, option: PollOption) : Control =
    Button()
      .Name($"PollOption{option.Id}")
      .Classes(Theme.name Theme.GhostButton)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .HorizontalContentAlignment(HorizontalAlignment.Left)
      .Padding(8.0, 4.0, 8.0, 4.0)
      .Margin(0.0, 4.0, 0.0, 0.0)
      .Content(option.Label)
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        ctx.OnEvent(PollVoted(ctx.Commit, option)))

  /// One option once nobody may vote: the label, the viewer's own mark,
  /// and the share the server publishes.
  let resultOptionRow(option: PollOption) : Control =
    let share =
      [
        match option.Percentage with
        | Some percentage -> $"{percentage}%%"
        | None -> ()

        match option.VotesCount with
        | Some votes -> $"{votes}"
        | None -> ()
      ]
      |> String.concat " · "

    let label =
      if option.Voted then
        $"{option.Label} · yours"
      else
        option.Label

    StackPanel()
      .Spacing(2.0)
      .Margin(0.0, 4.0, 0.0, 0.0)
      .Children(
        TextBlock().Text(label),
        TextBlock().Classes(Theme.name Theme.Mono).Text(share)
      )

  /// The poll block: votable while it is open, unvoted, and not the
  /// viewer's own; the results the server publishes afterwards.
  let pollView(ctx: PollViewCtx) : Control =
    let canVote =
      not ctx.Poll.IsClosed
      && not ctx.Poll.HasVoted
      && ctx.Commit.Author.Id <> ctx.ViewerId

    let title =
      let state = if ctx.Poll.IsClosed then " · closed" else ""

      let votes =
        ctx.Poll.TotalVotes
        |> Option.map(fun count -> $" · {count} votes")
        |> Option.defaultValue ""

      $"poll{votes}{state}"

    let rows: Control array = [|
      TextBlock().Classes(Theme.name Theme.Muted).Text(title)

      for option in ctx.Poll.Options do
        if canVote then
          votableOptionRow(ctx, option)
        else
          resultOptionRow option
    |]

    Border()
      .BorderBrush(Brushes.border)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .Padding(10.0)
      .Margin(0.0, 6.0, 0.0, 0.0)
      .Child(StackPanel().Children(rows))

  /// The link preview: its own surface, a click away from the card it
  /// rides in. The handled click opens the outside resource and never
  /// falls through to the commit's thread.
  let linkPreviewView(preview: LinkPreview) : Control =
    Button()
      .Name("LinkPreviewCard")
      .Classes(Theme.name Theme.GhostButton)
      .BorderBrush(Brushes.border)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .Padding(10.0)
      .Margin(0.0, 6.0, 0.0, 0.0)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Content(
        StackPanel()
          .Spacing(2.0)
          .Children(
            TextBlock()
              .Text(preview.Title |> Option.defaultValue preview.Url)
              .TextWrapping(TextWrapping.Wrap)
              // The right margin keeps shaped lines inside the box.
              .Margin(0.0, 0.0, 4.0, 0.0),
            TextBlock()
              .Classes(Theme.name Theme.Mono)
              .Text(preview.Url)
              .TextTrimming(TextTrimming.CharacterEllipsis)
          )
      )
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        DeviceAuth.startBrowser preview.Url |> ignore)

  /// One quiet action of the row: an icon, an optional mono count, no
  /// border. A handled click never opens the thread as a side effect.
  let iconAction
    (name: string, glyph: Glyph, label: string voption, onClick: Button -> unit)
    : Button =
    let children: Control array = [|
      Icons.iconAt(glyph, 16.0)
      match label with
      | ValueNone -> ()
      | ValueSome count ->
        TextBlock().Classes(Theme.name Theme.Mono).Text(count)
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
  let header(commit: Commit, now: IObservable<DateTimeOffset>) : Control =
    let hash = TextBlock().Classes(Theme.name Theme.Mono).Text(commit.ShortHash)

    let identity =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .Text(commit.Author.Name),
          TextBlock().Classes(Theme.name Theme.Muted).Text("·"),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text($"@{commit.Author.Username}")
            .TextTrimming(TextTrimming.CharacterEllipsis),
          TextBlock().Classes(Theme.name Theme.Muted).Text("·"),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text(
              now
              |> Observable.map(fun moment -> timeAgo(moment, commit.CreatedAt))
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
  /// tail. The full card ends the tail in a dot; a card whose reply
  /// chain shows below runs the tail to its bottom edge instead, so
  /// the chain elbows straight off it. Dense rows shrink the node.
  let railCell
    (avatar: Control, lane: Lane option, dense: bool, continues: bool)
    : Panel =
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
        .Margin(0.0, nodeTop, 0.0, if continues then 0.0 else dotZone)

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

    let pieces: Control array = [|
      tail
      if not continues then
        dot
      ring
    |]

    Panel()
      .Width(railWidth)
      .MinHeight(if dense then 48.0 else 64.0)
      .Children(pieces)

  /// The ringed avatar alone: the rail node of a reply row. A reply is
  /// a node, not a commit card — no tail, no dot.
  let ringCell(avatar: Control, lane: Lane option) : Panel =
    let ring =
      Border()
        .Name("ReplyRing")
        .Background(Brushes.card)
        .BorderBrush(Brushes.laneOf lane)
        .BorderThickness(Thickness(2.0))
        .CornerRadius(28.0)
        .Padding(1.0)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Top)
        .Margin(0.0, 3.0, 0.0, 0.0)
        .Child(avatar)

    Panel().Width(48.0).Children(ring)

  /// The identity line of a reply row: name, handle, time. The hash
  /// stays off; the site keeps replies light this way.
  let replyIdentity
    (commit: Commit, now: IObservable<DateTimeOffset>)
    : Control =
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
            now
            |> Observable.map(fun moment -> timeAgo(moment, commit.CreatedAt))
          )
      )

  let actions(props: Props) : Control =
    let commit = props.Commit
    let onEvent = props.OnEvent
    let starred = commit.Starred |> Option.contains true

    let replyLabel =
      commit.RepliesCount
      |> Option.filter(fun count -> count > 0)
      |> Option.map shortCount
      |> ValueOption.ofOption

    let reply =
      iconAction(
        "ReplyButton",
        Glyph.Reply,
        replyLabel,
        fun _ -> onEvent(ReplyToggled commit)
      )

    let starLabel =
      commit.StarsCount
      |> Option.filter(fun count -> count > 0)
      |> Option.map shortCount
      |> ValueOption.ofOption

    // The server refuses a star on the author's own commit: the site
    // answers with a read-only count, so the toggle never arms.
    let star =
      if commit.Author.Id = props.ViewerId then
        iconAction("StarCount", Glyph.Star, starLabel, fun _ -> ())
          .IsEnabled(false)
          .Foreground(Brushes.muted)
      else
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

      let button =
        iconAction(
          "PastelButton",
          Glyph.Pastel,
          count,
          fun _ -> onEvent(PastelGiven commit)
        )

      // The control exists only while the mode is on: the host rebuilds
      // with the flag, so nothing named PastelButton leaks into the tree.
      ContentControl()
        .Content(
          props.Pastels
          |> Observable.map(fun on -> if on then button :> Control else Panel())
          |> _.ToBinding()
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

    let leftChildren: Control array = [| reply; star; pastel; quote |]

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

  // The site's order: the reply pointer, the body, the images, the
  // poll, the quote, the link preview.
  let bodyChildren: Control array = [|
    match Parts.replyLine commit with
    | Some line -> line
    | None -> ()

    Parts.bodyView(props, commit)

    match commit.Images with
    | [] -> ()
    | images -> Parts.imagesView(props.Images, images)

    match commit.Poll with
    | Some poll ->
      Parts.pollView {
        Commit = commit
        Poll = poll
        OnEvent = props.OnEvent
        ViewerId = props.ViewerId
      }
    | None -> ()

    match commit.QuotedCommit with
    | Some quoted -> Parts.quotedView(props.OnEvent, quoted)
    | None -> ()

    match commit.LinkPreview with
    | Some preview -> Parts.linkPreviewView preview
    | None -> ()
  |]

  let body = StackPanel().Margin(0.0, 2.0, 0.0, 0.0).Children(bodyChildren)

  let plain =
    Avatar.view {
      Name = commit.Author.Name
      Url = commit.Author.AvatarUrl
      Lane = commit.Author.Lane
      Size = if dense then 28.0 else 40.0
      Cache = props.Images
    }

  // The hover card rides the avatar when the host gave it a source.
  let avatar =
    match props.Flyout with
    | ValueSome source -> UserFlyout.attach(source, commit.Author, plain)
    | ValueNone -> plain

  let pad = if dense then 8.0 else 12.0

  let content =
    StackPanel()
      .Margin(0.0, pad, 16.0, pad)
      .Children(Parts.header(commit, props.Now), body, Parts.actions props)

  // The hairline stops at the rail column: the rail runs unbroken.
  let contentColumn =
    Border()
      .BorderBrush(Brushes.border)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Child(content)

  let grid = Grid()

  grid.ColumnDefinitions <- ColumnDefinitions(if dense then "48,*" else "64,*")

  // The tail runs to the card's bottom edge when a visible reply chain
  // hangs under it, so the chain's elbow starts where the tail stops.
  let continues =
    not dense
    && commit.ReplyPreview
       |> Option.map(fun preview -> not preview.Data.IsEmpty)
       |> Option.defaultValue false

  Button()
    .Classes(Theme.name Theme.CommitRow)
    .Content(
      grid.Children(
        Parts.railCell(avatar, commit.Author.Lane, dense, continues).Column(0),
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

/// Everything one card row needs from its host page: where its events
/// go, the live pastéis flag and clock, the row's density, and the
/// image cache.
[<Struct>]
type RowProps = {
  OnEvent: Event -> unit
  ViewerId: string
  Pasteis: IObservable<bool>
  Now: IObservable<DateTimeOffset>
  Dense: bool
  Images: ImageCacheService
  Flyout: Source voption
}

/// One commit row over a feed node: the card with the host's event
/// routing, or the deletion marker. Every feed page shares this;
/// only the routing differs.
let feedRow (props: RowProps) (node: CommitNode) : Control =
  match node with
  | LiveCommit commit ->
    view {
      Commit = commit
      OnEvent = props.OnEvent
      ViewerId = props.ViewerId
      Pastels = props.Pasteis
      Now = props.Now
      Dense = props.Dense
      Images = props.Images
      Flyout = props.Flyout
    }
  | Tombstone _ -> tombstone()

/// The shared context of one reply chain: the row props, the nesting
/// depth, the lane and handle of the parent the chain hangs from, and
/// the host's own host for a parent's children (the thread page grows
/// its nodes through the cursor; the feed reads the payload slice).
[<Struct>]
type ChainCtx = {
  Props: RowProps
  Depth: int
  ParentLane: Lane option
  ParentHandle: string
  Grow: (ChainCtx -> Commit -> Control) voption
}

/// One row's place among its siblings: the lane of the row above, and
/// whether this row opens or closes the run.
[<Struct>]
type ChainSpot = {
  PreviousLane: Lane option
  First: bool
  Last: bool
}

/// The content column of one slim reply row: the identity line, the
/// body, the images. A click opens the reply's own thread.
let slimContent(props: RowProps, commit: Commit) : Control =
  let children: Control array = [|
    Parts.replyIdentity(commit, props.Now)
    Parts.bodyOf(props.Flyout, commit)

    match commit.Images with
    | [] -> ()
    | images -> Parts.imagesView(props.Images, images)
  |]

  Button()
    .Classes(Theme.name Theme.CommitRow)
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .Content(
      StackPanel().Margin(0.0, 0.0, 16.0, 6.0).Spacing(2.0).Children(children)
    )
    .OnClickHandler(fun _ _ -> props.OnEvent(Opened commit))

/// One slim reply row, recursive: the ringed avatar on the rail, the
/// branch or the sibling stub above it, the lane spine below it while
/// the chain continues, and its own replies one step deeper. Past the
/// depth cap the chain flattens with the "replying to" line.
let rec chainRow(ctx: ChainCtx, spot: ChainSpot, node: CommitNode) : Control =
  match node with
  | Tombstone _ ->
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Margin(48.0, 4.0, 0.0, 4.0)
      .Text("a deleted reply")
  | LiveCommit commit ->
    let flat = ctx.Depth >= 3

    let nested =
      if flat then
        []
      else
        commit.ReplyPreview |> Option.map _.Data |> Option.defaultValue []

    // The connector above the ring: the first row branches off its
    // parent's spine through an elbow that starts at the row's top and
    // lands on the ring's center; later siblings chain off the row
    // above through a short stub in that row's lane.
    let branch: Control =
      if spot.First then
        FeedParts.laneElbowAt(ctx.ParentLane, -8.0, 32.0, 20.0)
      else
        Panel()
          .Height(6.0)
          .Children(FeedParts.laneSpineAt(spot.PreviousLane, 24.0))

    let avatar =
      Avatar.view {
        Name = commit.Author.Name
        Url = commit.Author.AvatarUrl
        Lane = commit.Author.Lane
        Size = 28.0
        Cache = ctx.Props.Images
      }

    let ringged =
      match ctx.Props.Flyout with
      | ValueSome source -> UserFlyout.attach(source, commit.Author, avatar)
      | ValueNone -> avatar

    let flatRow =
      StackPanel()
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Muted)
            .Margin(0.0, 4.0, 0.0, 0.0)
            .Text($"replying to @{ctx.ParentHandle}"),
          slimContent(ctx.Props, commit)
        )

    if flat then
      flatRow :> Control
    else
      // The spine below the ring: while a sibling follows, or the
      // row's own replies hang under it.
      let grows =
        match ctx.Grow with
        | ValueSome _ -> true
        | ValueNone -> false

      let chained = not spot.Last || not nested.IsEmpty || grows

      // The rail pieces paint first, the ring last: the lines pass
      // behind the avatar, the way the site's rail does.
      let railChildren: Control array = [|
        branch

        if chained then
          FeedParts
            .laneSpineAt(commit.Author.Lane, 24.0)
            .Margin(23.25, 40.0, 0.0, 0.0)

        Parts.ringCell(ringged, commit.Author.Lane)
      |]

      let child = { ctx with Depth = ctx.Depth + 1 }

      let below: Control =
        match ctx.Grow with
        | ValueSome grow -> grow child commit
        | ValueNone ->
          match nested with
          | [] -> Panel()
          | replies ->
            chainRows(
              {
                child with
                    ParentLane = commit.Author.Lane
                    ParentHandle = commit.Author.Username
              },
              replies
            )

      let grid = Grid()

      grid.ColumnDefinitions <- ColumnDefinitions("48,*")
      grid.RowDefinitions <- RowDefinitions("Auto,Auto")

      let _ =
        grid.Children(
          Panel().Children(railChildren).Column(0).RowSpan(2),
          slimContent(ctx.Props, commit).Column(1),
          below.Column(1).Row(1).Margin(32.0, 0.0, 0.0, 0.0)
        )

      grid :> Control

/// One run of sibling replies: every row knows its neighbors, so the
/// stubs, the spines, and the branches chain into one rail.
and chainRows(ctx: ChainCtx, nodes: CommitNode list) : Control =
  let children: Control array = [|
    for index, node in List.indexed nodes ->
      let previousLane =
        if index = 0 then
          ctx.ParentLane
        else
          match nodes[index - 1] with
          | LiveCommit previous -> previous.Author.Lane
          | Tombstone _ -> ctx.ParentLane

      chainRow(
        ctx,
        {
          PreviousLane = previousLane
          First = index = 0
          Last = index = nodes.Length - 1
        },
        node
      )
  |]

  StackPanel().Children(children)

/// The thread preview under a feed card: the replies to show (the
/// payload slice, or the list a host grew) as the site's slim chain —
/// ringed avatars branched off the parent's lane rail, sibling stubs
/// between them, nested sub-threads one step deeper — with the link
/// that walks the rest.
let replyBlock
  (props: RowProps, commit: Commit, shown: CommitNode list voption)
  : Control =
  let payload =
    commit.ReplyPreview |> Option.map _.Data |> Option.defaultValue []

  let replies = shown |> ValueOption.defaultValue payload

  let remaining =
    commit.RepliesCount
    |> Option.map(fun count -> count - replies.Length)
    |> Option.defaultValue 0

  // The payload attaches a preview slice on the log endpoints even
  // when a commit has no replies; the site draws nothing for those,
  // and so does the client. Where the payload carries no preview at
  // all, the endpoint never promised one.
  match commit.ReplyPreview with
  | None -> Panel() :> Control
  | Some _ when replies.IsEmpty && remaining <= 0 -> Panel() :> Control
  | Some _ ->
    let lane = commit.Author.Lane

    let children: Control array = [|
      // The card above runs its lane tail to its bottom edge when this
      // chain shows, so the first row's elbow starts exactly there —
      // no gap, and nothing reaches back into the card (the feed's
      // virtualizing panel would clip it).
      if not replies.IsEmpty then
        yield
          StackPanel()
            .Margin(40.0, 0.0, 0.0, 0.0)
            .Children(
              chainRows(
                {
                  Props = props
                  Depth = 0
                  ParentLane = lane
                  ParentHandle = commit.Author.Username
                  Grow = ValueNone
                },
                replies
              )
            )

      if remaining > 0 then
        let label =
          match remaining with
          | 1 -> "Show the last reply"
          | count -> $"Show {count} more replies"

        yield
          Button()
            .Name("MoreReplies")
            .Classes(Theme.name Theme.GhostButton)
            .Padding(4.0, 2.0, 4.0, 2.0)
            .Margin(48.0, 4.0, 0.0, 4.0)
            .HorizontalAlignment(HorizontalAlignment.Left)
            .Content(
              StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(6.0)
                .Children(
                  Ellipse()
                    .Width(10.0)
                    .Height(10.0)
                    .StrokeThickness(1.5)
                    .StrokeDashArray(
                      Avalonia.Collections.AvaloniaList<double>(2.0, 2.0)
                    )
                    .Stroke(Brushes.muted),
                  TextBlock().Text(label)
                )
            )
            .OnClickHandler(fun _ args ->
              args.Handled <- true
              props.OnEvent(MoreRepliesRequested commit))
    |]

    StackPanel().Children(children)
