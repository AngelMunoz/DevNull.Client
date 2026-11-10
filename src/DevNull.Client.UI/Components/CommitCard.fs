module DevNull.Client.Components.CommitCard

open System
open Avalonia
open Avalonia.Automation
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
open DevNull.Client.Components.CommitMenu
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Formatting
open DevNull.Client.Icons
open DevNull.Client.ImageCache

// The API's image record shares its name with the image control; the
// alias keeps both in reach.
type ApiImage = DevNull.Client.Core.ApiTypes.Image

type MentionTone =
  | Accent
  | OnFill

[<Struct>]
type BodyArgs = {
  Flyout: Source voption
  Tone: MentionTone
  Mentions: Mention list
}

[<Struct>]
type Event =
  | Opened of Commit
  | ReplyToggled of Commit
  | MoreRepliesRequested of Commit
  | QuoteRequested of Commit
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | TranslateRequested of Commit
  | OriginalRequested of Commit
  | MenuOpened of Commit

[<Struct>]
type PinToggle = {
  Command: Commit -> unit
  Busy: IObservable<string option>
}

[<Struct>]
type ActionSlot = {
  Command: Commit -> unit
  Busy: IObservable<string option>
}

type AuthorRelation = DevNull.Client.Components.CommitMenu.AuthorRelation

type MenuSlot = DevNull.Client.Components.CommitMenu.MenuSlot

[<Struct>]
type TranslationView = {
  Body: TranslatedBody option
  SourceName: string option
  CanTranslate: bool
  Busy: bool
  Waiting: bool
  Error: string option
}

[<Struct>]
type Props = {
  Commit: Commit
  OnEvent: Event -> unit
  ViewerId: string
  Pastels: IObservable<bool>
  Now: IObservable<DateTimeOffset>
  PollChoice: IObservable<string option>
  Dense: bool
  Images: ImageCacheService
  Flyout: Source voption
  Pin: PinToggle voption
  Star: ActionSlot voption
  Vote: ActionSlot voption
  Pastel: ActionSlot voption
  Menu: MenuSlot voption
  Translation: IObservable<TranslationView voption>
  Zone: Zone
}

[<Struct>]
type ChoiceChange = {
  Choices: Map<string, string>
  CommitId: string
  OptionId: string
  Chosen: bool
}

let choose(change: ChoiceChange) : Map<string, string> =
  match change.Chosen, change.Choices |> Map.tryFind change.CommitId with
  | true, _ -> change.Choices |> Map.add change.CommitId change.OptionId
  | false, Some current when current = change.OptionId ->
    change.Choices |> Map.remove change.CommitId
  | false, _ -> change.Choices

let selectedOption
  (choices: Map<string, string>, commit: Commit)
  : PollOption option =
  choices
  |> Map.tryFind commit.Id
  |> Option.bind(fun optionId ->
    commit.Poll
    |> Option.bind(fun poll ->
      poll.Options |> List.tryFind(fun candidate -> candidate.Id = optionId)))

// The card's semantic regions; the view only composes these.
module Parts =

  let inline inFlight(commit: Commit, busy: string option) : bool =
    Option.contains commit.Id busy

  // One piece of a text run after the mentions split: plain prose,
  // or one interactive handle carrying the mention that names it.
  type Piece =
    | Plain of string
    | Handle of Mention

  // The split's walk: every round re-finds the earliest handle in
  // what is left, so a handle named twice wraps both times. The walk
  // is a tail call, so a run dense with handles never grows the
  // stack.
  [<TailCall>]
  let rec split
    (rest: string, mentions: Mention list, pieces: Piece list)
    : Piece list =
    let places =
      mentions
      |> List.choose(fun mention ->
        let token = "@" + mention.Handle
        let at = rest.IndexOf(token, StringComparison.Ordinal)

        if at < 0 then None else Some(at, token, mention))
      |> List.sortBy(fun (at, _, _) -> at)

    match places with
    | [] -> Plain rest :: pieces
    | (at, token, mention) :: _ ->
      let before = rest.Substring(0, at)
      let after = rest.Substring(at + token.Length)

      let withHandle = Handle mention :: pieces

      split(
        after,
        mentions,
        if before = "" then
          withHandle
        else
          Plain before :: withHandle
      )

  // Splits one text run at its mentioned handles: plain prose between
  // the @handles the mentions list confirms.
  let splitMentions(text: string, mentions: Mention list) : Piece list =
    split(text, mentions, []) |> List.rev

  // The user a mention names; the payload carries the handle only,
  // the card fills the rest.
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
    FollowedByFollowingCount = None
    IsFollowed = None
    FollowsYou = None
  }

  // One @handle of the body: the accent color, the hover card, and
  // the click that opens the profile. A mention that names an
  // organization skips the hover card — its click opens the
  // organization page through the host's navigation — and an
  // unknown kind stays the accent text alone. Over an accent fill
  // the handle keeps the fill's own foreground, so the accent never
  // sits on itself.
  let handleInline
    (flyout: Source voption, tone: MentionTone, mention: Mention)
    : Inline =
    let handle = TextBlock().Name("MentionHandle").Text("@" + mention.Handle)

    match tone with
    | Accent -> handle.Classes(Theme.name Theme.FgAccent) |> ignore
    | OnFill -> ()

    match mention.Kind, flyout with
    | UserMention, ValueSome source ->
      InlineUIContainer(
        UserFlyout.attach(source, mentioned mention.Handle, handle)
      )
    | OrganizationMention, ValueSome source ->
      InlineUIContainer(
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(0.0)
          .Content(handle)
          .OnClickHandler(fun _ args ->
            // A handled click never opens the thread behind the handle.
            args.Handled <- true

            source.OnOpenOrg mention.Handle)
      )
    | _, ValueNone -> InlineUIContainer(handle)
    | UnknownMention _, ValueSome _ -> InlineUIContainer(handle)

  // The body text with its mentions wrapped, flowing the way the
  // site's inline links do.
  let textWithMentions(args: BodyArgs, text: string) : Control =
    match args.Mentions with
    | [] -> TextBlock().Text(text).TextWrapping(TextWrapping.Wrap)
    | mentions ->
      let block = TextBlock().TextWrapping(TextWrapping.Wrap)
      let inlines = InlineCollection()
      block.Inlines <- inlines

      splitMentions(text, mentions)
      |> List.iter(fun piece ->
        match piece with
        | Plain plain -> inlines.Add(Run plain) |> ignore
        | Handle mention ->
          inlines.Add(handleInline(args.Flyout, args.Tone, mention)) |> ignore)

      block

  let segmentView(args: BodyArgs, segment: Segment) : Control =
    match segment with
    | TextSegment text -> textWithMentions(args, text)
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
    | BlockSegment(language, filename, html) ->
      let code: Control =
        TextBlock()
          .Classes(Theme.name Theme.CodeBlock)
          .Text(plainText html)
          .TextWrapping(TextWrapping.Wrap)

      let border =
        Border()
          .Classes(Theme.name Theme.SurfaceBack)
          .CornerRadius(6.0)
          .Padding(10.0)
          .Margin(0.0, 4.0, 0.0, 0.0)

      match blockTitle(language, filename) with
      | None -> border.Child(code)
      | Some title ->
        border.Child(
          StackPanel()
            .Orientation(Orientation.Vertical)
            .Spacing(4.0)
            .Children(
              TextBlock()
                .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
                .FontSize(11.0)
                .Text(title),
              code
            )
        )

  // One body from its parts, whatever carries it.
  let bodyGen(args: BodyArgs, text: string, segments: Segment list) : Control =
    match segments with
    | [] -> textWithMentions(args, text)
    | segments ->
      StackPanel().Children [|
        for segment in segments -> segmentView(args, segment)
      |]

  // The body over any source: the segments with their mentions, the
  // plain text when the payload carries no segments.
  let bodyOf(flyout: Source voption, commit: Commit) : Control =
    bodyGen(
      {
        Flyout = flyout
        Tone = Accent
        Mentions = commit.Mentions
      },
      commit.Body,
      commit.BodySegments
    )

  // The body a card shows: the translated segments while a translation
  // shows, the commit's own otherwise. Translated segments carry no
  // mention positions, so their handles stay plain text.
  let bodyView(props: Props, commit: Commit) : Control =
    let original = bodyOf(props.Flyout, commit)

    ContentControl()
      .Content(
        PageChrome.rebuildOn(
          props.Translation,
          id,
          fun view ->
            match view with
            | ValueSome { Body = Some translated } ->
              bodyGen(
                {
                  Flyout = props.Flyout
                  Tone = Accent
                  Mentions = []
                },
                commit.Body,
                translated.BodySegments
              )
            | _ -> original
        )
        |> _.ToBinding()
      )

  // The line under a body: the notice with Show original while a
  // translation shows, the Translate button otherwise, and the last
  // failure in the server's words.
  let translationLine(props: Props, commit: Commit) : Control =
    let line(view: TranslationView voption) : Control =
      match view with
      | ValueNone -> Panel()
      | ValueSome view ->
        let notice: Control array =
          match view.Body with
          | None -> [||]
          | Some _ -> [|
              TextBlock()
                .Classes(Theme.name Theme.Muted)
                .Text(
                  view.SourceName
                  |> Option.map(fun name -> $"Translated from {name}")
                  |> Option.defaultValue "Translated"
                )
              TextBlock().Classes(Theme.name Theme.Muted).Text("·")
              Button()
                .Name("ShowOriginalButton")
                .Classes(Theme.name Theme.GhostButton)
                .Padding(4.0, 0.0, 4.0, 0.0)
                .Content("Show original")
                .OnClickHandler(fun _ args ->
                  args.Handled <- true
                  props.OnEvent(OriginalRequested commit))
            |]

        let failure: Control array =
          match view.Error with
          | None -> [||]
          | Some message -> [|
              TextBlock()
                .Classes(Theme.name Theme.FgDanger)
                .TextWrapping(TextWrapping.Wrap)
                .Text(message)
            |]

        StackPanel()
          .Spacing(3.0)
          .Margin(0.0, 3.0, 0.0, 0.0)
          .Children(Array.concat [ notice; failure ])

    ContentControl()
      .Content(
        PageChrome.rebuildOn(props.Translation, id, line) |> _.ToBinding()
      )

  // The image block under the body: one image full width, two or
  // three in a two-column grid. Each cell is a fixed box with the
  // site's rounded corners: the reserved box keeps the feed height
  // stable before the bytes land, and the site crops to fixed boxes
  // the same way. A handled click opens the commit's gallery.
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
            ToolTip = image.Description |> ValueOption.ofOption
          }

        let box =
          Border()
            .Classes(Theme.name Theme.SurfaceBack)
            .CornerRadius(8.0)
            .Height(height)
            .ClipToBounds(true)
            .Child(loaded.Name("CardImage"))

        let imageButton =
          Button()
            .Name("CardImageButton")
            .Classes(Theme.name Theme.GhostButton)
            .Padding(0.0)
            .Content(box)
            .OnClickHandler(fun _ args ->
              // The gallery never opens the commit behind the cell.
              args.Handled <- true
              gallery(box, index))

        AutomationProperties.SetName(imageButton, "Commit image")
        ToolTip.SetTip(imageButton, "Commit image")

        imageButton

    match images with
    | [ single ] -> cellAt (Stretch.Uniform, 320.0) (0, single)
    | many ->
      UniformGrid().Columns(2).Margin(0.0, 6.0, 0.0, 0.0).Children [|
        for pair in Seq.indexed many ->
          cellAt (Stretch.UniformToFill, 150.0) pair
      |]

  // The quoted commit: its own surface, a click away from the card it
  // rides in. The handled click opens the quoted thread and never
  // falls through to this commit's.
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
                .Text($"@{(Author.handle commit.Author)}"),
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

  // Everything the poll block needs: the commit it rides, its poll,
  // the host's event routing, the viewer, the clock, the hover
  // source, the parked choice, and the host's vote action.
  [<Struct>]
  type PollViewCtx = {
    Commit: Commit
    Poll: Poll
    OnEvent: Event -> unit
    ViewerId: string
    Now: IObservable<DateTimeOffset>
    PollChoice: IObservable<string option>
    Vote: ActionSlot voption
    Flyout: Source voption
  }

  // The facts one vote decision reads.
  [<Struct>]
  type VoteGate = {
    Now: DateTimeOffset
    Commit: Commit
    Poll: Poll
    ViewerId: string
  }

  // A poll stops taking votes once its author closes it or its clock
  // passes the close time the server published.
  let hasExpired(now: DateTimeOffset, poll: Poll) : bool =
    poll.IsClosed
    || (match poll.ClosesAt with
        | Some closesAt -> closesAt <= now
        | None -> false)

  // The viewer may vote now: the commit is approved, the poll has
  // opened, the viewer has not voted, it is not the viewer's poll, and
  // the clock has not passed the close time.
  let canVote(gate: VoteGate) : bool =
    gate.Commit.Status = Approved
    && gate.Poll.ClosesAt.IsSome
    && not gate.Poll.HasVoted
    && not(hasExpired(gate.Now, gate.Poll))
    && (Author.id gate.Commit.Author) <> gate.ViewerId
    && not(Author.isOrganization gate.Commit.Author)

  // The site's time-left wording: days, hours, then minutes, never
  // below one minute.
  let timeLeft(now: DateTimeOffset, closesAt: DateTimeOffset) : string =
    let minutes = Math.Ceiling((closesAt - now).TotalMinutes)

    if minutes >= 1440.0 then $"{int(minutes / 1440.0)}d left"
    elif minutes >= 60.0 then $"{int(minutes / 60.0)}h left"
    else $"{max (int minutes) 1}m left"

  // The line under the options: why the poll takes no vote, or how
  // long it stays open, or what the final result is.
  let pollStatus(now: DateTimeOffset, poll: Poll) : string =
    match poll.ClosesAt with
    | None -> "Voting opens once approved"
    | Some closesAt when hasExpired(now, poll) ->
      match poll.TotalVotes with
      | Some _ -> "Final results"
      | None -> "Poll closed"
    | Some closesAt -> timeLeft(now, closesAt)

  // One count with the site's singular and plural.
  let votesLabel(count: int) : string =
    let noun = if count = 1 then "vote" else "votes"
    $"{shortCount count} {noun}"

  // One option label's facts: the text, the mentions the body links,
  // whether the viewer's check follows it, and the hover source.
  [<Struct>]
  type LabelSpec = {
    Text: string
    Mentions: Mention list
    Marked: bool
    Flyout: Source voption
  }

  // One option label with the mentions the body links and, once the
  // option is the viewer's, the check mark that follows it inline.
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
        splitMentions(spec.Text, spec.Mentions)
        |> List.iter(fun piece ->
          match piece with
          | Plain plain -> inlines.Add(Run plain) |> ignore
          | Handle mention ->
            inlines.Add(handleInline(spec.Flyout, Accent, mention)) |> ignore)

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

  // One result row: the bar the server's share fills, the label, and
  // the share at the right. The bar sits behind the content, clipped by
  // the row's rounded border.
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

  // One choice row while the poll takes votes: the whole row is the
  // toggle, with the ring and the label inside it. The page model owns
  // the checked option, so the row reports the change and does not
  // keep a second copy.
  let choiceRow
    (ctx: PollViewCtx, slot: ActionSlot, option: PollOption)
    : ToggleButton =
    let chosen =
      ctx.PollChoice
      |> Observable.map(fun choice -> Option.contains option.Id choice)

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
      slot.Busy |> Observable.map(fun busy -> inFlight(ctx.Commit, busy) |> not)

    let enabled =
      Observable.combineLatest canVoteNow idle
      |> Observable.map(fun (allowed, ready) -> allowed && ready)

    let toggle =
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

    AutomationProperties.SetName(toggle, option.Label)

    toggle

  // The poll block: the results once the server publishes them, the
  // choices and the Vote button while the poll takes votes, or a
  // read-only list of options when the host offers no vote.
  let pollView(ctx: PollViewCtx) : Control =
    let body: Control =
      match ctx.Poll.TotalVotes, ctx.Vote with
      | Some total, _ ->
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
      | None, ValueSome slot ->
        let rows: Control array = [|
          for option in ctx.Poll.Options -> choiceRow(ctx, slot, option)
        |]

        let canSubmit =
          Observable.combineLatest
            (ctx.PollChoice |> Observable.map Option.isSome)
            (slot.Busy
             |> Observable.map(fun busy -> inFlight(ctx.Commit, busy) |> not))
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
          slot.Busy
          |> Observable.map(fun busy ->
            if inFlight(ctx.Commit, busy) then "Voting…" else "Vote")

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
              slot.Command ctx.Commit)

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
      | None, ValueNone ->
        let rows: Control array = [|
          for option in ctx.Poll.Options -> resultOptionRow(ctx, option)
        |]

        let status =
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text(
              ctx.Now |> Observable.map(fun now -> pollStatus(now, ctx.Poll))
            )

        StackPanel()
          .Spacing(8.0)
          .Children(StackPanel().Spacing(8.0).Children(rows), status)

    StackPanel().Margin(0.0, 12.0, 0.0, 0.0).Children(body)

  // The site's own line: the name the page declares, or its host
  // without "www.". The site's card never shows the whole URL — the
  // URL's natural width would push the card past its column.
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

  // The link preview: its own surface, a click away from the card it
  // rides in. The handled click opens the outside resource and never
  // falls through to the commit's thread.
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

  [<Struct>]
  type IconAction = {
    Name: string
    Glyph: Glyph
    Label: string voption
    OnClick: Button -> unit
    /// The word the screen reader and the tooltip say for an
    /// icon-only action, where the card carries no other label.
    Aria: string voption
  }

  // One quiet action of the row: an icon, an optional mono count, no
  // border. A handled click never opens the thread as a side effect.
  let iconAction(action: IconAction) : Button =
    let children: Control array = [|
      Icons.iconAt(action.Glyph, 16.0)
      match action.Label with
      | ValueNone -> ()
      | ValueSome count ->
        TextBlock().Classes(Theme.name Theme.Mono).Text(count)
    |]

    let button =
      Button()
        .Name(action.Name)
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
          action.OnClick sender)

    match action.Aria with
    | ValueSome word ->
      AutomationProperties.SetName(button, word)
      ToolTip.SetTip(button, word)
    | ValueNone -> ()

    button

  // Puts the commit's web URL on the clipboard.
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

  // The identity line: name, handle, and time in the site's rhythm,
  // the short hash docked to the right edge.
  let header
    (commit: Commit, now: IObservable<DateTimeOffset>, zone: Zone)
    : Control =
    let hash = TextBlock().Classes(Theme.name Theme.Mono).Text(commit.ShortHash)

    let identity =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .Text((Author.name commit.Author)),
          TextBlock().Classes(Theme.name Theme.Muted).Text("·"),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text($"@{(Author.handle commit.Author)}")
            .TextTrimming(TextTrimming.CharacterEllipsis),
          TextBlock().Classes(Theme.name Theme.Muted).Text("·"),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text(
              now
              |> Observable.map(fun moment -> timeAgo(moment, commit.CreatedAt))
            )
        )

    ToolTip.SetTip(identity, absolute(zone, commit.CreatedAt))

    DockPanel().Children(hash.DockRight(), identity)

  // The "replying to" line under the identity, when the commit answers
  // one.
  let replyLine(commit: Commit) : Control option =
    match commit.Parent with
    | Some(LiveCommit parent) ->
      Some(
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .Margin(0.0, 2.0, 0.0, 0.0)
          .Text($"replying to @{(Author.handle parent.Author)}")
      )
    | Some(Tombstone _) ->
      Some(
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .Margin(0.0, 2.0, 0.0, 0.0)
          .Text("replying to [deleted]")
      )
    | None -> None

  [<Struct>]
  type RailCell = {
    Avatar: Control
    Lane: Lane option
    Dense: bool
    Continues: bool
    /// The square shape an organization's avatar uses; persons draw
    /// the disc, and the ring follows the avatar's silhouette.
    Square: bool
  }

  // The rail cell: the author's avatar sitting on a short lane-colored
  // tail. The full card ends the tail in a dot; a card whose reply
  // chain shows below runs the tail to its bottom edge instead, so
  // the chain elbows straight off it. Dense rows shrink the node.
  let railCell(cell: RailCell) : Panel =
    let nodeSize = if cell.Dense then 28.0 else 40.0

    let railWidth = if cell.Dense then 48.0 else 64.0

    let nodeTop = if cell.Dense then 20.0 else 32.0

    let dotZone = if cell.Dense then 12.0 else 20.0

    let tail =
      Border()
        .Width(2.0)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Stretch)
        .Margin(0.0, nodeTop, 0.0, if cell.Continues then 0.0 else dotZone)

    Theme.laneBack(tail, cell.Lane)

    // The dot: a lane core inside a card gap inside a lane ring.
    let dotCore = Ellipse().Width(18.0).Height(18.0)

    Theme.laneFill(dotCore, cell.Lane)

    let dotGap =
      Ellipse()
        .Width(12.0)
        .Height(12.0)
        .StrokeThickness(3.0)
        .Classes(Theme.name Theme.CardEdge)

    Theme.laneFill(dotGap, cell.Lane)

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
        .CornerRadius(if cell.Square then 8.0 else nodeSize)
        .Padding(1.0)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Top)
        .Margin(0.0, nodeTop - (nodeSize + 6.0) / 2.0, 0.0, 0.0)
        .Child(cell.Avatar)

    Theme.laneBorder(ring, cell.Lane)

    let pieces: Control array = [|
      tail
      if not cell.Continues then
        dot
      ring
    |]

    Panel()
      .Width(railWidth)
      .MinHeight(if cell.Dense then 48.0 else 64.0)
      .Children(pieces)

  // The ringed avatar alone: the rail node of a reply row. A reply is
  // a node, not a commit card — no tail, no dot.
  let ringCell(avatar: Control, lane: Lane option, square: bool) : Panel =
    let ring =
      Border()
        .Name("ReplyRing")
        .Classes(Theme.name Theme.CardBack)
        .BorderThickness(Thickness(2.0))
        .CornerRadius(if square then 8.0 else 28.0)
        .Padding(1.0)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Top)
        .Margin(0.0, 3.0, 0.0, 0.0)
        .Child(avatar)

    Theme.laneBorder(ring, lane)

    Panel().Width(48.0).Children(ring)

  // The identity line of a reply row: name, handle, time. The hash
  // stays off; the site keeps replies light this way.
  let replyIdentity
    (commit: Commit, now: IObservable<DateTimeOffset>)
    : Control =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(6.0)
      .Children(
        TextBlock()
          .Classes(Theme.name Theme.Subtitle)
          .Text((Author.name commit.Author)),
        TextBlock().Classes(Theme.name Theme.Muted).Text("·"),
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .Text($"@{(Author.handle commit.Author)}")
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
      iconAction {
        Name = "ReplyButton"
        Glyph = Glyph.Reply
        Label = replyLabel
        OnClick = fun _ -> onEvent(ReplyToggled commit)
        Aria = ValueSome "Reply"
      }

    let starLabel =
      commit.StarsCount
      |> Option.filter(fun count -> count > 0)
      |> Option.map shortCount
      |> ValueOption.ofOption

    // The server refuses a star on the author's own commit: the site
    // answers with a read-only count, so the toggle never arms. A host
    // with no star slot draws no star control at all. An organization's
    // commit carries no star at all.
    let star: Control =
      match props.Star with
      | ValueNone -> Panel()
      | ValueSome _ when Author.isOrganization commit.Author -> Panel()
      | ValueSome _ when (Author.id commit.Author) = props.ViewerId ->
        (iconAction {
          Name = "StarCount"
          Glyph = Glyph.Star
          Label = starLabel
          OnClick = fun _ -> ()
          Aria = ValueSome "Stars"
        })
          .IsEnabled(false)
          .Classes(Theme.name Theme.FgMuted)
      | ValueSome slot ->
        (iconAction {
          Name = "StarButton"
          Glyph = Glyph.Star
          Label = starLabel
          OnClick = fun _ -> slot.Command commit
          Aria = ValueSome "Star"
        })
          .IsEnabled(
            slot.Busy
            |> Observable.map(fun busy -> inFlight(commit, busy) |> not)
          )
          .Classes(
            Theme.name(if starred then Theme.FgAccent else Theme.FgMuted)
          )

    let pastel: Control =
      match props.Pastel with
      | ValueSome _ when Author.isOrganization commit.Author -> Panel()
      | ValueSome slot ->
        let count =
          if commit.PasteisCount > 0 then
            ValueSome(shortCount commit.PasteisCount)
          else
            ValueNone

        let button =
          (iconAction {
            Name = "PastelButton"
            Glyph = Glyph.Pastel
            Label = count
            OnClick = fun _ -> slot.Command commit
            Aria = ValueSome "Pastel"
          })
            .IsEnabled(
              slot.Busy
              |> Observable.map(fun busy -> inFlight(commit, busy) |> not)
            )

        // The control exists only while the mode is on: the host
        // rebuilds with the flag, so nothing named PastelButton leaks
        // into the tree.
        ContentControl()
          .Content(
            PageChrome.rebuildOn(
              props.Pastels,
              id,
              (fun on -> if on then button else Panel())
            )
            |> _.ToBinding()
          )
      | ValueNone -> Panel()

    let quote =
      iconAction {
        Name = "QuoteButton"
        Glyph = Glyph.Quote
        Label = ValueNone
        OnClick = fun _ -> onEvent(QuoteRequested commit)
        Aria = ValueSome "Quote"
      }

    // The profile pin: the author's own top-level commits only — the
    // server refuses anything else — and only where the host offers
    // it. The pinned flag flips the glyph and the word, the site's
    // pair, and the flight of the call disables the button.
    let pin: Control =
      match props.Pin with
      | ValueSome toggle when
        (Author.id commit.Author) = props.ViewerId
        && Option.isNone commit.ParentId
        ->
        let pinned = commit.IsPinned
        let glyph = if pinned then Glyph.PinOff else Glyph.Pin
        let word = if pinned then "Unpin" else "Pin"

        let button =
          iconAction {
            Name = "PinButton"
            Glyph = glyph
            Label = ValueSome word
            OnClick = fun _ -> toggle.Command commit
            Aria = ValueNone
          }

        ToolTip.SetTip(
          button,
          if pinned then "Unpin from profile" else "Pin to profile"
        )

        button.IsEnabled(
          toggle.Busy
          |> Observable.map(fun busy -> inFlight(commit, busy) |> not)
        )
      | _ -> Panel()

    // The More actions menu: the card's whole right end. The trigger
    // toggles the site's rows under it; an absent slot draws nothing.
    let menu: Control =
      match props.Menu with
      | ValueSome slot ->
        CommitMenu.view {
          Commit = commit
          ViewerId = props.ViewerId
          Slot = slot
          OnOpened = fun commit -> onEvent(MenuOpened commit)
          CopyLink = fun source -> copyLink(source, commit.HtmlUrl)
        }
      | ValueNone -> Panel()

    // The translate action: the site's Translate, below the body. It
    // shows while the site may translate the commit, and disables while
    // the call runs or the rate limit waits.
    let translate: Control =
      let label =
        props.Translation
        |> Observable.map(fun view ->
          match view with
          | ValueSome view when view.CanTranslate && view.Busy ->
            "Translating…"
          | _ -> "Translate")

      let enabled =
        props.Translation
        |> Observable.map(fun view ->
          match view with
          | ValueSome view ->
            view.CanTranslate && not view.Busy && not view.Waiting
          | ValueNone -> false)

      let button =
        let built =
          iconAction {
            Name = "TranslateButton"
            Glyph = Glyph.Translate
            Label = ValueNone
            OnClick = fun _ -> onEvent(TranslateRequested commit)
            Aria = ValueNone
          }

        built
          .Content(
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(4.0)
              .Children(
                Icons.iconAt(Glyph.Translate, 16.0),
                TextBlock().Classes(Theme.name Theme.Mono).Text(label)
              )
          )
          .IsEnabled(enabled)

      ContentControl()
        .Content(
          PageChrome.rebuildOn(
            props.Translation,
            (fun view ->
              view
              |> ValueOption.map _.CanTranslate
              |> ValueOption.defaultValue false),
            (fun view ->
              match view with
              | ValueSome view when view.CanTranslate -> button
              | _ -> Panel())
          )
          |> _.ToBinding()
        )

    let leftChildren: Control array = [|
      reply
      star
      pastel
      quote
      translate
      pin
    |]

    DockPanel()
      .LastChildFill(true)
      .Margin(0.0, 8.0, 0.0, 0.0)
      .Children(
        menu.DockRight(),
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
    Parts.translationLine(props, commit)

    match commit.Images with
    | [] -> ()
    | images -> Parts.imagesView(props.Images, images)

    match commit.Poll with
    | Some _ when Author.isOrganization commit.Author -> ()
    | Some poll ->
      Parts.pollView {
        Commit = commit
        Poll = poll
        OnEvent = props.OnEvent
        ViewerId = props.ViewerId
        Now = props.Now
        PollChoice = props.PollChoice
        Vote = props.Vote
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
      Name = (Author.name commit.Author)
      Url = (Author.avatarUrl commit.Author)
      Lane = (Author.lane commit.Author)
      Size = if dense then 28.0 else 40.0
      Cache = props.Images
      Square = (Author.isOrganization commit.Author)
    }

  // The hover card rides the avatar when the host gave it a source.
  let avatar =
    match props.Flyout with
    | ValueSome source -> UserFlyout.attachAuthor(source, commit.Author, plain)
    | ValueNone -> plain

  let pad = if dense then 8.0 else 12.0

  let content =
    StackPanel()
      .Margin(0.0, pad, 16.0, pad)
      .Children(
        Parts.header(commit, props.Now, props.Zone),
        body,
        Parts.actions props
      )

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

  let rail =
    Parts.railCell {
      Avatar = avatar
      Lane = (Author.lane commit.Author)
      Dense = dense
      Continues = continues
      Square = (Author.isOrganization commit.Author)
    }

  let rowButton =
    Button()
      .Classes(Theme.name Theme.CommitRow)
      .Focusable(true)
      .Content(grid.Children(rail.Column(0), contentColumn.Column(1)))
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

  AutomationProperties.SetName(
    rowButton,
    $"Commit {commit.ShortHash} by @{(Author.handle commit.Author)}"
  )

  rowButton

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

[<Struct>]
type RowProps = {
  OnEvent: Event -> unit
  ViewerId: string
  Pasteis: IObservable<bool>
  Now: IObservable<DateTimeOffset>
  PollChoices: IObservable<Map<string, string>>
  Dense: bool
  Images: ImageCacheService
  Flyout: Source voption
  Pin: PinToggle voption
  Star: ActionSlot voption
  Vote: ActionSlot voption
  Pastel: ActionSlot voption
  Menu: MenuSlot voption
  Translation: IObservable<Commit -> TranslationView voption>
  Zone: Zone
}

let feedRow (props: RowProps) (node: CommitNode) : Control =
  match node with
  | LiveCommit commit ->
    view {
      Commit = commit
      OnEvent = props.OnEvent
      ViewerId = props.ViewerId
      Pastels = props.Pasteis
      Now = props.Now

      PollChoice =
        props.PollChoices
        |> Observable.map(fun choices -> choices |> Map.tryFind commit.Id)

      Dense = props.Dense
      Images = props.Images
      Flyout = props.Flyout
      Pin = props.Pin
      Star = props.Star
      Vote = props.Vote
      Pastel = props.Pastel
      Menu = props.Menu
      Translation =
        props.Translation |> Observable.map(fun lookup -> lookup commit)
      Zone = props.Zone
    }
  | Tombstone _ -> tombstone()

let body(args: BodyArgs, text: string, segments: Segment list) : Control =
  Parts.bodyGen(args, text, segments)

[<Struct>]
type ChainCtx = {
  Props: RowProps
  Depth: int
  ParentLane: Lane option
  ParentHandle: string
  Grow: (ChainCtx -> Commit -> Control) voption
}

// One row's place among its siblings: the lane of the row above, and
// whether this row opens or closes the run.
[<Struct>]
type ChainSpot = {
  PreviousLane: Lane option
  First: bool
  Last: bool
}

// The content column of one slim reply row: the identity line, the
// body, the images. A click opens the reply's own thread.
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

// One slim reply row, recursive: the ringed avatar on the rail, the
// branch or the sibling stub above it, the lane spine below it while
// the chain continues, and its own replies one step deeper. Past the
// depth cap the chain flattens with the "replying to" line.
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
        FeedParts.laneElbowAt {
          Lane = ctx.ParentLane
          X = -8.0
          Width = 32.0
          Height = 20.0
        }
      else
        Panel()
          .Height(6.0)
          .Children(FeedParts.laneSpineAt(spot.PreviousLane, 24.0))

    let avatar =
      Avatar.view {
        Name = (Author.name commit.Author)
        Url = (Author.avatarUrl commit.Author)
        Lane = (Author.lane commit.Author)
        Size = 28.0
        Cache = ctx.Props.Images
        Square = (Author.isOrganization commit.Author)
      }

    let ringged =
      match ctx.Props.Flyout with
      | ValueSome source ->
        UserFlyout.attachAuthor(source, commit.Author, avatar)
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
            .laneSpineAt((Author.lane commit.Author), 24.0)
            .Margin(23.25, 40.0, 0.0, 0.0)

        Parts.ringCell(
          ringged,
          (Author.lane commit.Author),
          (Author.isOrganization commit.Author)
        )
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
                    ParentLane = (Author.lane commit.Author)
                    ParentHandle = (Author.handle commit.Author)
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

// One run of sibling replies: every row knows its neighbors, so the
// stubs, the spines, and the branches chain into one rail.
and chainRows(ctx: ChainCtx, nodes: CommitNode list) : Control =
  let children: Control array = [|
    for index, node in List.indexed nodes ->
      let previousLane =
        if index = 0 then
          ctx.ParentLane
        else
          match nodes[index - 1] with
          | LiveCommit previous -> (Author.lane previous.Author)
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
    let lane = (Author.lane commit.Author)

    let children: Control array = [|
      // The card above runs its lane tail to its bottom edge when this
      // chain shows, so the first row's elbow starts exactly there —
      // no gap, and nothing reaches back into the card (the feed's
      // virtualizing panel would clip it).
      if not replies.IsEmpty then
        StackPanel()
          .Margin(40.0, 0.0, 0.0, 0.0)
          .Children(
            chainRows(
              {
                Props = props
                Depth = 0
                ParentLane = lane
                ParentHandle = (Author.handle commit.Author)
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
