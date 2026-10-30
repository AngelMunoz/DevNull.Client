module DevNull.Client.Components.CommitCard

open System
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
open FSharp.Control.Reactive
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

/// How a body's mentions read: the accent color over the page surface,
/// or the plain foreground over an accent fill.
type MentionTone =
  | Accent
  | OnFill

/// What one card reports up: open the thread, toggle the star, give a
/// pastel, start a quote, reveal the reply composer, choose a poll
/// option, ask for the vote, walk more replies of a thread preview, or
/// pin the commit to the author's profile.
[<Struct>]
type Event =
  | Opened of Commit
  | ReplyToggled of Commit
  | ThreadRequested of Commit
  | MoreRepliesRequested of Commit
  | StarToggled of Commit
  | PastelGiven of Commit
  | QuoteRequested of Commit
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | PollVoteRequested of Commit
  | PinToggled of Commit

/// The pin affordance one host may offer: the command the toggle runs,
/// and the commit id whose pin call is in flight, so its button
/// disables while the server answers.
[<Struct>]
type PinToggle = {
  Command: Commit -> unit
  Busy: IObservable<string option>
}

/// One commit row. `OnEvent` receives every interaction. Every card
/// rides the graph rail with its author's avatar; dense rows (the
/// replies of a thread) shrink the node. `Now` is the clock the poll's
/// status follows; `Voting` names the commit whose vote is in flight,
/// so the button disables; `PollChoice` is the option the page model
/// holds for this commit; `Pin` offers the profile pin on the author's
/// own top-level commits, where the host decides to show it at all.
[<Struct>]
type Props = {
  Commit: Commit
  OnEvent: Event -> unit
  ViewerId: string
  Pastels: IObservable<bool>
  Now: IObservable<DateTimeOffset>
  Voting: IObservable<string option>
  PollChoice: IObservable<string option>
  Dense: bool
  Images: ImageCacheService
  Flyout: Source voption
  Pin: PinToggle voption
}

/// One poll choice change, in the shape the page model stores it.
[<Struct>]
type ChoiceChange = {
  Choices: Map<string, string>
  CommitId: string
  OptionId: string
  Chosen: bool
}

/// The choice map after one row reports its checked state. Clearing
/// removes only the option that was actually chosen, so the model's
/// uncheck of another row cannot clear the new choice.
let choose(change: ChoiceChange) : Map<string, string> =
  match change.Chosen, change.Choices |> Map.tryFind change.CommitId with
  | true, _ -> change.Choices |> Map.add change.CommitId change.OptionId
  | false, Some current when current = change.OptionId ->
    change.Choices |> Map.remove change.CommitId
  | false, _ -> change.Choices

/// The option a page still holds for a commit, when the poll carries it.
let selectedOption
  (choices: Map<string, string>, commit: Commit)
  : PollOption option =
  choices
  |> Map.tryFind commit.Id
  |> Option.bind(fun optionId ->
    commit.Poll
    |> Option.bind(fun poll ->
      poll.Options |> List.tryFind(fun candidate -> candidate.Id = optionId)))

/// The card's semantic regions; the view only composes these.
module Parts =

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
    Handle = username
    Name = username
    AvatarUrl = None
    Bio = None
    Lane = None
    CommitsCount = None
    FollowersCount = None
    FollowingCount = None
    IsFollowed = None
    FollowsYou = None
  }

  /// One @handle of the body: the accent color, the hover card, and
  /// the click that opens the profile. Over an accent fill the handle
  /// keeps the fill's own foreground, so the accent never sits on
  /// itself.
  let handleInline
    (flyout: Source voption, tone: MentionTone, username: string)
    : Inline =
    let handle = TextBlock().Name("MentionHandle").Text("@" + username)

    match tone with
    | Accent -> handle.Classes(Theme.name Theme.FgAccent) |> ignore
    | OnFill -> ()

    match flyout with
    | ValueSome source ->
      InlineUIContainer(UserFlyout.attach(source, mentioned username, handle))
    | ValueNone -> InlineUIContainer(handle)

  /// The body text with its mentions wrapped, flowing the way the
  /// site's inline links do.
  let textWithMentions
    (
      flyout: Source voption,
      tone: MentionTone,
      text: string,
      mentions: Mention list
    ) : Control =
    match mentions with
    | [] -> TextBlock().Text(text).TextWrapping(TextWrapping.Wrap)
    | _ ->
      let block = TextBlock().TextWrapping(TextWrapping.Wrap)
      let inlines = InlineCollection()
      block.Inlines <- inlines

      splitMentions(text, mentions |> List.map _.Handle)
      |> List.iter(fun piece ->
        match piece with
        | Plain plain -> inlines.Add(Run plain) |> ignore
        | Handle username ->
          inlines.Add(handleInline(flyout, tone, username)) |> ignore)

      block

  let segmentView
    (
      flyout: Source voption,
      tone: MentionTone,
      mentions: Mention list,
      segment: Segment
    ) : Control =
    match segment with
    | TextSegment text -> textWithMentions(flyout, tone, text, mentions)
    | CodeSegment code ->
      Border()
        .Classes(Theme.name Theme.SurfaceBack)
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
        .Classes(Theme.name Theme.SurfaceBack)
        .CornerRadius(6.0)
        .Padding(10.0)
        .Margin(0.0, 4.0, 0.0, 0.0)
        .Child(
          TextBlock()
            .Classes(Theme.name Theme.CodeBlock)
            .Text(plainText html)
            .TextWrapping(TextWrapping.Wrap)
        )

  /// One body from its parts, whatever carries it.
  let bodyGen
    (
      flyout: Source voption,
      tone: MentionTone,
      text: string,
      segments: Segment list,
      mentions: Mention list
    ) : Control =
    match segments with
    | [] -> textWithMentions(flyout, tone, text, mentions)
    | segments ->
      StackPanel().Children [|
        for segment in segments -> segmentView(flyout, tone, mentions, segment)
      |]

  /// The body over any source: the segments with their mentions, the
  /// plain text when the payload carries no segments.
  let bodyOf(flyout: Source voption, commit: Commit) : Control =
    bodyGen(flyout, Accent, commit.Body, commit.BodySegments, commit.Mentions)

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
            // The cell clips to a fixed height, so the decode pays for
            // the pixels it shows: a tall phone photo costs its cell,
            // not its full frame at the column's width.
            DecodeWidth = 720
            DecodeHeight = ValueSome(int height)
            Stretch = stretch
            Placeholder = ValueNone
            ToolTip = image.Description |> ValueOption.ofOption
          }

        let box =
          Border()
            .Classes(Theme.name Theme.SurfaceBack)
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
        .Classes(Theme.name Theme.GhostButton, Theme.name Theme.Hairline)
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
                .Text($"@{commit.Author.Handle}"),
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
  /// the host's event routing, the viewer, the clock, the vote in
  /// flight, the hover source, and the parked choice.
  [<Struct>]
  type PollViewCtx = {
    Commit: Commit
    Poll: Poll
    OnEvent: Event -> unit
    ViewerId: string
    Now: IObservable<DateTimeOffset>
    Voting: IObservable<string option>
    PollChoice: IObservable<string option>
    Flyout: Source voption
  }

  /// The facts one vote decision reads.
  [<Struct>]
  type VoteGate = {
    Now: DateTimeOffset
    Commit: Commit
    Poll: Poll
    ViewerId: string
  }

  /// A poll stops taking votes once its author closes it or its clock
  /// passes the close time the server published.
  let hasExpired(now: DateTimeOffset, poll: Poll) : bool =
    poll.IsClosed
    || (match poll.ClosesAt with
        | Some closesAt -> closesAt <= now
        | None -> false)

  /// The viewer may vote now: the commit is approved, the poll has
  /// opened, the viewer has not voted, it is not the viewer's poll, and
  /// the clock has not passed the close time.
  let canVote(gate: VoteGate) : bool =
    gate.Commit.Status = Approved
    && gate.Poll.ClosesAt.IsSome
    && not gate.Poll.HasVoted
    && not(hasExpired(gate.Now, gate.Poll))
    && gate.Commit.Author.Id <> gate.ViewerId

  /// The site's time-left wording: days, hours, then minutes, never
  /// below one minute.
  let timeLeft(now: DateTimeOffset, closesAt: DateTimeOffset) : string =
    let minutes = Math.Ceiling((closesAt - now).TotalMinutes)

    if minutes >= 1440.0 then $"{int(minutes / 1440.0)}d left"
    elif minutes >= 60.0 then $"{int(minutes / 60.0)}h left"
    else $"{max (int minutes) 1}m left"

  /// The line under the options: why the poll takes no vote, or how
  /// long it stays open, or what the final result is.
  let pollStatus(now: DateTimeOffset, poll: Poll) : string =
    match poll.ClosesAt with
    | None -> "Voting opens once approved"
    | Some closesAt when hasExpired(now, poll) ->
      match poll.TotalVotes with
      | Some _ -> "Final results"
      | None -> "Poll closed"
    | Some closesAt -> timeLeft(now, closesAt)

  /// One count with the site's singular and plural.
  let votesLabel(count: int) : string =
    let noun = if count = 1 then "vote" else "votes"
    $"{shortCount count} {noun}"

  /// One option label's facts: the text, the mentions the body links,
  /// whether the viewer's check follows it, and the hover source.
  [<Struct>]
  type LabelSpec = {
    Text: string
    Mentions: Mention list
    Marked: bool
    Flyout: Source voption
  }

  /// One option label with the mentions the body links and, once the
  /// option is the viewer's, the check mark that follows it inline.
  let optionLabel(spec: LabelSpec) : Control =
    if not spec.Marked && spec.Mentions.IsEmpty then
      TextBlock().Text(spec.Text).TextWrapping(TextWrapping.Wrap)
    else
      let block = TextBlock().TextWrapping(TextWrapping.Wrap)
      let inlines = InlineCollection()
      block.Inlines <- inlines

      match spec.Mentions with
      | [] -> inlines.Add(Run spec.Text) |> ignore
      | _ ->
        splitMentions(spec.Text, spec.Mentions |> List.map _.Handle)
        |> List.iter(fun piece ->
          match piece with
          | Plain plain -> inlines.Add(Run plain) |> ignore
          | Handle username ->
            inlines.Add(handleInline(spec.Flyout, Accent, username)) |> ignore)

      if spec.Marked then
        block.FontWeight <- FontWeight.Medium

        let check =
          Icons.iconAt(Glyph.Check, 16.0).Classes(Theme.name Theme.FgAccent)

        check.Margin <- Thickness(6.0, 0.0, 0.0, 0.0)
        check.Name <- $"PollCheck{spec.Text}"

        let inlineControl = InlineUIContainer(check)
        inlineControl.BaselineAlignment <- BaselineAlignment.Center
        inlines.Add(inlineControl) |> ignore

      block

  /// One result row: the bar the server's share fills, the label, and
  /// the share at the right. The bar sits behind the content, clipped by
  /// the row's rounded border.
  let resultOptionRow(ctx: PollViewCtx, option: PollOption) : Control =
    let share = option.Percentage |> Option.defaultValue 0

    let fillClass =
      if option.Voted then
        Theme.PrimarySoftBack
      else
        Theme.SurfaceBack

    let label =
      optionLabel {
        Text = option.Label
        Mentions = ctx.Commit.Mentions
        Marked = option.Voted
        Flyout = ctx.Flyout
      }

    let percent =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text(if option.Percentage.IsSome then $"{share}%%" else "")
        .VerticalAlignment(VerticalAlignment.Center)
        .Margin(8.0, 0.0, 0.0, 0.0)

    ToolTip.SetTip(
      percent,
      votesLabel(option.VotesCount |> Option.defaultValue 0)
    )

    let content =
      DockPanel()
        .LastChildFill(true)
        .Children(percent.DockRight(), label)
        .Margin(12.0, 8.0, 12.0, 8.0)

    let bar = Grid()

    bar.ColumnDefinitions <- ColumnDefinitions($"{share}*,{100 - share}*")

    bar.Children(
      Border().Classes(Theme.name fillClass).CornerRadius(6.0).Column(0),
      content.ColumnSpan(2)
    )
    |> ignore

    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(6.0)
      .ClipToBounds(true)
      .Child(bar)

  /// One choice row while the poll takes votes: the whole row is the
  /// toggle, with the ring and the label inside it. The page model owns
  /// the checked option, so the row reports the change and does not
  /// keep a second copy.
  let choiceRow(ctx: PollViewCtx, option: PollOption) : ToggleButton =
    let chosen =
      ctx.PollChoice |> Observable.map(fun choice -> choice = Some option.Id)

    let dot =
      Border()
        .Width(8.0)
        .Height(8.0)
        .CornerRadius(4.0)
        .Classes(Theme.name Theme.AccentBack)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Center)
        .IsVisible(chosen)

    let ring =
      Border()
        .Width(16.0)
        .Height(16.0)
        .CornerRadius(8.0)
        .BorderThickness(Thickness(1.0))
        .Classes(Theme.name Theme.Hairline)
        .Child(dot)

    let label =
      optionLabel {
        Text = option.Label
        Mentions = ctx.Commit.Mentions
        Marked = false
        Flyout = ctx.Flyout
      }
      |> _.VerticalAlignment(VerticalAlignment.Center)

    let content =
      let grid = Grid()

      grid.ColumnDefinitions <- ColumnDefinitions("Auto,*")

      grid.Children(ring.Column(0).Margin(0.0, 0.0, 8.0, 0.0), label.Column(1))
      |> ignore

      grid :> Control

    let canVoteNow =
      ctx.Now
      |> Observable.map(fun now ->
        canVote {
          Now = now
          Commit = ctx.Commit
          Poll = ctx.Poll
          ViewerId = ctx.ViewerId
        })

    let idle =
      ctx.Voting
      |> Observable.map(fun commitId -> commitId <> Some ctx.Commit.Id)

    let enabled =
      Observable.combineLatest canVoteNow idle
      |> Observable.map(fun (allowed, ready) -> allowed && ready)

    ToggleButton()
      .Name($"PollChoice{option.Id}")
      .Classes(Theme.name Theme.PollOption)
      .Content(content)
      .IsChecked(chosen |> Observable.map(fun value -> Nullable value))
      .IsEnabled(enabled)
      .OnIsCheckedChangedHandler(fun toggle _ ->
        toggle.IsChecked
        |> Option.ofNullable
        |> Option.iter(fun selected ->
          ctx.OnEvent(PollChoiceChanged(ctx.Commit, option, selected))))
      // The toggle's click must stop here: the card's own row button
      // opens the thread, and an unhandled click bubbles to it.
      .OnClickHandler(fun _ args -> args.Handled <- true)

  /// The poll block: the results once the server publishes them, or
  /// the choices and the Vote button while the poll takes votes.
  let pollView(ctx: PollViewCtx) : Control =
    let body: Control =
      match ctx.Poll.TotalVotes with
      | Some total ->
        let rows: Control array = [|
          for option in ctx.Poll.Options -> resultOptionRow(ctx, option)
        |]

        let summary =
          ctx.Now
          |> Observable.map(fun now ->
            $"{votesLabel total} · {pollStatus(now, ctx.Poll)}")

        let summaryText =
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text(summary)
            .Margin(0.0, 8.0, 0.0, 0.0)

        StackPanel()
          .Spacing(8.0)
          .Children(StackPanel().Spacing(8.0).Children(rows), summaryText)
      | None ->
        let rows: Control array = [|
          for option in ctx.Poll.Options -> choiceRow(ctx, option)
        |]

        let canSubmit =
          Observable.combineLatest
            (ctx.PollChoice |> Observable.map Option.isSome)
            (ctx.Voting
             |> Observable.map(fun commitId -> commitId <> Some ctx.Commit.Id))
          |> Observable.map(fun (chosen, idle) -> chosen && idle)

        let canVoteNow =
          ctx.Now
          |> Observable.map(fun now ->
            canVote {
              Now = now
              Commit = ctx.Commit
              Poll = ctx.Poll
              ViewerId = ctx.ViewerId
            })

        let voteLabel =
          ctx.Voting
          |> Observable.map(fun commitId ->
            if commitId = Some ctx.Commit.Id then "Voting…" else "Vote")

        let vote =
          Button()
            .Name("PollVote")
            .Classes(Theme.name Theme.AccentButton)
            .Content(TextBlock().Text(voteLabel))
            .Padding(12.0, 4.0, 12.0, 4.0)
            .FontSize(14.0)
            .IsVisible(canVoteNow)
            .IsEnabled(canSubmit)
            .OnClickHandler(fun _ args ->
              args.Handled <- true
              ctx.OnEvent(PollVoteRequested ctx.Commit))

        let status =
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text(
              ctx.Now |> Observable.map(fun now -> pollStatus(now, ctx.Poll))
            )

        let footer =
          DockPanel()
            .LastChildFill(true)
            .Children(vote.DockRight(), status)
            .Margin(0.0, 8.0, 0.0, 0.0)

        StackPanel()
          .Spacing(8.0)
          .Children(StackPanel().Spacing(8.0).Children(rows), footer)

    StackPanel().Margin(0.0, 12.0, 0.0, 0.0).Children(body)

  /// The site's own line: the name the page declares, or its host
  /// without "www.". The site's card never shows the whole URL — the
  /// URL's natural width would push the card past its column.
  let siteLine(preview: LinkPreview) : string =
    match preview.SiteName with
    | Some name -> name
    | None ->
      let host =
        match Uri.TryCreate(preview.Url, UriKind.Absolute) with
        | true, NonNull uri -> uri.Host
        | true, _
        | false, _ -> preview.Url

      if host.StartsWith("www.", StringComparison.Ordinal) then
        host.Substring 4
      else
        host

  /// The link preview: its own surface, a click away from the card it
  /// rides in. The handled click opens the outside resource and never
  /// falls through to the commit's thread.
  let linkPreviewView(preview: LinkPreview, title: string) : Control =
    Button()
      .Name("LinkPreviewCard")
      .Classes(Theme.name Theme.GhostButton, Theme.name Theme.Hairline)
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
              .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
              .Text(siteLine preview)
              .TextTrimming(TextTrimming.CharacterEllipsis),
            TextBlock()
              .Text(title)
              .TextWrapping(TextWrapping.Wrap)
              .MaxLines(2)
              // The right margin keeps shaped lines inside the box.
              .Margin(0.0, 0.0, 4.0, 0.0)
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
            .Text($"@{commit.Author.Handle}")
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
          .Text($"replying to @{parent.Author.Handle}")
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
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Stretch)
        .Margin(0.0, nodeTop, 0.0, if continues then 0.0 else dotZone)

    Theme.laneBack(tail, lane)

    // The dot: a lane core inside a card gap inside a lane ring.
    let dotCore = Ellipse().Width(18.0).Height(18.0)

    Theme.laneFill(dotCore, lane)

    let dotGap =
      Ellipse()
        .Width(12.0)
        .Height(12.0)
        .StrokeThickness(3.0)
        .Classes(Theme.name Theme.CardEdge)

    Theme.laneFill(dotGap, lane)

    let dot =
      Grid()
        .Width(18.0)
        .Height(18.0)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Bottom)
        .Margin(0.0, 0.0, 0.0, dotZone - 9.0)
        .Children(dotCore, dotGap)

    let ring =
      Border()
        .Classes(Theme.name Theme.CardBack)
        .BorderThickness(Thickness(2.0))
        .CornerRadius(nodeSize)
        .Padding(1.0)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Top)
        .Margin(0.0, nodeTop - (nodeSize + 6.0) / 2.0, 0.0, 0.0)
        .Child(avatar)

    Theme.laneBorder(ring, lane)

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
        .Classes(Theme.name Theme.CardBack)
        .BorderThickness(Thickness(2.0))
        .CornerRadius(28.0)
        .Padding(1.0)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Top)
        .Margin(0.0, 3.0, 0.0, 0.0)
        .Child(avatar)

    Theme.laneBorder(ring, lane)

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
          .Text($"@{commit.Author.Handle}")
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
          .Classes(Theme.name Theme.FgMuted)
      else
        iconAction(
          "StarButton",
          Glyph.Star,
          starLabel,
          fun _ -> onEvent(StarToggled commit)
        )
          .Classes(
            Theme.name(if starred then Theme.FgAccent else Theme.FgMuted)
          )

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

    // The profile pin: the author's own top-level commits only — the
    // server refuses anything else — and only where the host offers
    // it. The pinned flag flips the glyph and the word, the site's
    // pair, and the flight of the call disables the button.
    let pin: Control =
      match props.Pin with
      | ValueSome toggle when
        commit.Author.Id = props.ViewerId && commit.ParentId = None
        ->
        let pinned = commit.IsPinned
        let glyph = if pinned then Glyph.PinOff else Glyph.Pin
        let word = if pinned then "Unpin" else "Pin"

        let button =
          iconAction(
            "PinButton",
            glyph,
            ValueSome word,
            fun _ -> toggle.Command commit
          )

        ToolTip.SetTip(
          button,
          if pinned then "Unpin from profile" else "Pin to profile"
        )

        button.IsEnabled(
          toggle.Busy |> Observable.map(fun busy -> busy <> Some commit.Id)
        )
      | _ -> Panel()

    let link =
      iconAction(
        "LinkButton",
        Glyph.Link,
        ValueNone,
        fun button -> copyLink(button, commit.HtmlUrl)
      )

    let leftChildren: Control array = [| reply; star; pastel; quote; pin |]

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
        Now = props.Now
        Voting = props.Voting
        PollChoice = props.PollChoice
        Flyout = props.Flyout
      }
    | None -> ()

    match commit.QuotedCommit with
    | Some quoted -> Parts.quotedView(props.OnEvent, quoted)
    | None -> ()

    // A preview the page behind never titled stays absent, the way the
    // site's own card does.
    match commit.LinkPreview with
    | Some({ Title = Some title } as preview) ->
      Parts.linkPreviewView(preview, title)
    | _ -> ()
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
      .Classes(Theme.name Theme.Hairline)
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
    .Focusable(true)
    .Content(
      grid.Children(
        Parts.railCell(avatar, commit.Author.Lane, dense, continues).Column(0),
        contentColumn.Column(1)
      )
    )
    .OnClickHandler(fun _ _ -> props.OnEvent(Opened commit))
    .OnKeyDownHandler(fun sender args ->
      // The focused card answers its own quiet keys: the same events
      // the row's buttons dispatch, the way the site's key shortcuts
      // click the focused commit's buttons.
      match args.Key, args.KeyModifiers with
      | Key.Enter, KeyModifiers.None ->
        args.Handled <- true
        props.OnEvent(Opened commit)
      | Key.R, KeyModifiers.None ->
        args.Handled <- true
        props.OnEvent(ReplyToggled commit)
      | Key.Q, KeyModifiers.None ->
        args.Handled <- true
        props.OnEvent(QuoteRequested commit)
      | Key.Y, KeyModifiers.None ->
        args.Handled <- true
        Parts.copyLink(sender, commit.HtmlUrl)
      | _ -> ())

let tombstone() : Control =
  let line =
    Border()
      .Width(2.0)
      .Classes(Theme.name Theme.RailBack)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Stretch)
      .Margin(0.0, 32.0, 0.0, 0.0)

  let node =
    Ellipse()
      .Width(24.0)
      .Height(24.0)
      .Classes(Theme.name Theme.CardFill, Theme.name Theme.RailEdge)
      .StrokeThickness(2.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Top)
      .Margin(0.0, 20.0, 0.0, 0.0)

  node.StrokeDashArray <-
    Avalonia.Collections.AvaloniaList<double>([ 3.0; 3.0 ])

  let text =
    Border()
      .Classes(Theme.name Theme.Hairline)
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
/// go, the live pastéis flag and clock, the poll choices, the row's
/// density, the image cache, and the profile pin affordance, when the
/// host shows one at all.
[<Struct>]
type RowProps = {
  OnEvent: Event -> unit
  ViewerId: string
  Pasteis: IObservable<bool>
  Now: IObservable<DateTimeOffset>
  Voting: IObservable<string option>
  PollChoices: IObservable<Map<string, string>>
  Dense: bool
  Images: ImageCacheService
  Flyout: Source voption
  Pin: PinToggle voption
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
      Voting = props.Voting

      PollChoice =
        props.PollChoices
        |> Observable.map(fun choices -> choices |> Map.tryFind commit.Id)

      Dense = props.Dense
      Images = props.Images
      Flyout = props.Flyout
      Pin = props.Pin
    }
  | Tombstone _ -> tombstone()

/// One body from its segments — the text with its mentions wrapped,
/// the code blocks boxed — whatever carries it: a commit card or a
/// chat bubble.
let body
  (
    flyout: Source voption,
    text: string,
    segments: Segment list,
    mentions: Mention list,
    tone: MentionTone
  ) : Control =
  Parts.bodyGen(flyout, tone, text, segments, mentions)

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
                    ParentHandle = commit.Author.Handle
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
                  ParentHandle = commit.Author.Handle
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
                    .Classes(Theme.name Theme.MutedEdge),
                  TextBlock().Text(label)
                )
            )
            .OnClickHandler(fun _ args ->
              args.Handled <- true
              props.OnEvent(MoreRepliesRequested commit))
    |]

    StackPanel().Children(children)
