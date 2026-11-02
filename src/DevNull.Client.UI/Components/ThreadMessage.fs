module DevNull.Client.Components.ThreadMessage

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Shapes
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.ImageCache
open DevNull.Client.Icons
open DevNull.Client.Natives
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Components.ThreadVoice
open Microsoft.Extensions.Logging

/// The services one message row draws on: the image cache for
/// pictures and avatars, the voice player's dependencies, the logger
/// of its failures, and the reading pane's live width.
[<Struct>]
type Media = {
  Cache: ImageCacheService
  Audio: IAudioEngine voption
  Http: IHttp
  Settings: AppSettingsStore
  Solo: Solo
  Log: ILogger
  PaneWidth: IObservable<float>
}

/// One reaction chip's display data: the emoji, its count, whether the
/// viewer left one, and the joined names of who did.
[<Struct>]
type ReactionChip = {
  Emoji: string
  Count: int
  Mine: bool
  Who: string
}

/// The reactions one tray offers: the quick picks on top, the ranked
/// rest under the divider.
[<Struct>]
type ReactionChoices = {
  Quick: string list
  Rest: string list
}

/// One message's pin affordance: whether it is pinned, and whether
/// the viewer may move the pin.
[<Struct>]
type PinState = { Pinned: bool; Maintainer: bool }

/// What one bubble reports up: answer it, react to it, pin or unpin
/// it, open its gallery, or walk to the message it answers.
[<Struct>]
type BubbleActions = {
  Reply: Message -> unit
  React: string -> unit
  Pin: bool -> unit
  OpenImage: Message * Control -> unit
  Walk: int -> unit
}

/// One message bubble's inputs: the message, its side, the author
/// payload the reply ghost names, the media services, and the live
/// slices the row follows.
[<Struct>]
type BubbleProps = {
  Message: Message
  Mine: bool
  ViewerId: string
  Author: User option
  Media: Media
  ShowsStatus: IObservable<bool>
  Receipt: IObservable<(bool * string) option>
  Preview: IObservable<LinkPreview option>
  Reactions: IObservable<ReactionChip list>
  ReactionChoices: IObservable<ReactionChoices>
  Pin: IObservable<PinState> voption
  Actions: BubbleActions
}

/// One carried bubble: a sent message, or one still on its way in.
type RunBubble =
  | Sent of Message
  | Staged of PendingSend

/// One author's run of bubbles: consecutive messages by one side that
/// render as one stacked group.
[<Struct>]
type Run = {
  IsMine: bool
  AuthorId: string
  Author: User option
  Bubbles: RunBubble list
}

/// One run's inputs: the run, whether the thread is an org's, the
/// viewer, the services its label draws on, the live status message,
/// the bubble factory for the messages that arrived, and the staged
/// retry.
[<Struct>]
type RunProps = {
  Run: Run
  IsOrg: bool
  ViewerId: string
  Media: Media
  Status: IObservable<Message option>
  Bubble: Message -> BubbleProps
  OnRetry: PendingSend -> unit
}

/// One author's name as the receipt and ghost lines know it.
let authorName(viewerId: string, who: User option, authorId: string) : string =
  who
  |> Option.map _.Name
  |> Option.defaultValue(if authorId = viewerId then "You" else "Someone")

/// The reply ghost: the message a bubble answers, peeking out above
/// it. A click walks the thread to the original.
let ghost
  (walk: int -> unit, reply: MessageParent, mine: bool, author: string)
  : Control =
  let snippet =
    let body = reply.Body.Replace("\r", " ").Replace("\n", " ")

    // The site's snippet: links fall away, so the words carry it.
    let words =
      body.Split(' ')
      |> Array.filter(fun word ->
        not(word.StartsWith "http://" || word.StartsWith "https://"))

    let trimmed = String.concat " " words |> _.Trim()

    if trimmed <> "" then
      trimmed
    elif reply.HasVoice then
      "voice note"
    elif reply.ImagesCount > 1 then
      $"{reply.ImagesCount} images"
    else
      "image"

  Button()
    .Classes(Theme.name Theme.GhostButton)
    .Padding(8.0, 3.0, 8.0, 3.0)
    .FontSize(12.0)
    .Opacity(0.9)
    .HorizontalAlignment(
      if mine then
        HorizontalAlignment.Right
      else
        HorizontalAlignment.Left
    )
    .Content(
      StackPanel()
        .Spacing(1.0)
        .Children(
          TextBlock().Classes(Theme.name Theme.Mono).FontSize(11.0).Text(author),
          TextBlock()
            .Classes(Theme.name Theme.Muted)
            .FontSize(12.0)
            .Text(snippet)
            .TextTrimming(TextTrimming.CharacterEllipsis)
            .MaxWidth(260.0)
        )
    )
    .OnClickHandler(fun _ args ->
      args.Handled <- true
      walk reply.Id)

/// The images of one message: fixed boxes on the signed URLs, the
/// click opening the gallery through the host.
let images
  (
    cache: ImageCacheService,
    message: Message,
    openImage: Message * Control -> unit
  ) : Control voption =
  if message.Images.IsEmpty then
    ValueNone
  else
    let boxes: Control array = [|
      for image in message.Images ->
        AsyncImage
          .view(
            {
              Cache = cache
              Url = image.Url
              DecodeWidth = 512
              DecodeHeight = ValueSome 320
              Stretch = Stretch.UniformToFill
              ToolTip = image.Description |> Option.toValueOption
            }
          )
          .Width(232.0)
          .Height(150.0)
    |]

    let gallery = WrapPanel().Children(boxes)

    let open' =
      Button()
        .Classes(Theme.name Theme.CommitRow)
        .Padding(0.0)
        .CornerRadius(10.0)
        .Content(gallery)
        .OnClickHandler(fun sender _ -> openImage(message, sender))

    ValueSome open'

/// One message's link card, live from the thread: the card once the
/// server read it, the quiet word while it reads.
let linkCard
  (preview: IObservable<LinkPreview option>, mine: bool)
  : Control voption =
  let cardOf(preview: LinkPreview) : Control =
    if preview.IsPending then
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .FontSize(12.0)
        .Margin(0.0, 4.0, 0.0, 0.0)
        .Text("reading the link…")
    else
      Border()
        .CornerRadius(10.0)
        .BorderThickness(1.0)
        .Classes(Theme.name Theme.Hairline)
        .Padding(10.0, 8.0, 10.0, 8.0)
        .Margin(0.0, 6.0, 0.0, 0.0)
        .MaxWidth(280.0)
        .Child(
          StackPanel()
            .Spacing(2.0)
            .Children(
              TextBlock()
                .Classes(
                  if mine then
                    Theme.name Theme.FgOnAccent
                  else
                    Theme.name Theme.Subtitle
                )
                .Text(preview.Title |> Option.defaultValue preview.Url)
                .TextWrapping(TextWrapping.Wrap),
              TextBlock()
                .Classes(
                  if mine then
                    Theme.name Theme.FgOnAccent
                  else
                    Theme.name Theme.Mono
                )
                .FontSize(11.0)
                .Text(preview.Url)
                .TextTrimming(TextTrimming.CharacterEllipsis)
            )
        )

  let card = preview |> Observable.distinctUntilChanged

  ValueSome(
    ContentControl()
      .Content(
        card
        |> Observable.map(function
          | Some preview -> cardOf preview
          | None -> null)
        |> _.ToBinding()
      )
      .IsVisible(card |> Observable.map Option.isSome)
  )

/// The receipts under one message, the site's own meta line: the
/// status glyph — a filled dot once pushed, two dots joined by a
/// rail once pulled — beside the word. Live from the thread, because
/// the read position moves under a built row.
let receipts
  (line: IObservable<(bool * string) option>, message: Message, mine: bool)
  : Control voption =
  if not mine then
    ValueNone
  else
    let partnerLane = message.User |> Option.bind _.Lane

    // The glyph: my filled dot while only pushed; once pulled, my
    // dot joined by the rail to the partner's lane-colored one.
    let glyph(pulled: bool) : Control =
      let mine =
        Ellipse().Classes(Theme.name Theme.AccentDot).Width(8.0).Height(8.0)

      if pulled then
        let theirs = Ellipse().Width(8.0).Height(8.0)

        Theme.laneFill(theirs, partnerLane)

        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(3.0)
          .VerticalAlignment(VerticalAlignment.Center)
          .Children(
            mine,
            Border()
              .Classes(Theme.name Theme.Hairline)
              .Width(8.0)
              .Height(1.5)
              .VerticalAlignment(VerticalAlignment.Center),
            theirs
          )
      else
        mine

    let readings = line |> Observable.distinctUntilChanged

    ValueSome(
      ContentControl()
        .Content(
          readings
          |> Observable.map(function
            | Some(pulled, word) ->
              StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(5.0)
                .VerticalAlignment(VerticalAlignment.Center)
                .Children(
                  glyph pulled,
                  TextBlock()
                    .Classes(Theme.name Theme.Mono)
                    .FontSize(11.0)
                    .Text(word)
                )
              :> Control
            | None -> null)
          |> _.ToBinding()
        )
        .Margin(4.0, 2.0, 4.0, 0.0)
        .HorizontalAlignment(HorizontalAlignment.Right)
        .IsVisible(readings |> Observable.map Option.isSome)
    )

/// One reaction chip: the emoji and its count in the site's own
/// shape — no button chrome, just the bordered count a pointer
/// press toggles. Worn on the mine wash when the viewer's reaction
/// is among them; the tip names who left it.
let reactionChip(chip: ReactionChip, onReact: string -> unit) : Control =
  let control =
    Border()
      .Classes(
        Theme.name(
          if chip.Mine then
            Theme.ReactionChipMine
          else
            Theme.ReactionChip
        )
      )
      .Child(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(4.0)
          .Children(
            TextBlock().Text(chip.Emoji),
            TextBlock()
              .Classes(Theme.name Theme.Mono)
              .FontSize(11.0)
              .Text(string chip.Count)
          )
      )

  ToolTip.SetTip(control, chip.Who)

  control.PointerPressed.Add(fun args ->
    args.Handled <- true
    onReact chip.Emoji)

  control :> Control

/// The chips row under one message: one chip per emoji the message
/// carries, live from the thread. The message carries none until it
/// does, and the row hides then.
let reactionChips
  (chips: IObservable<ReactionChip list>, onReact: string -> unit)
  : Control =
  let summaries = chips |> Observable.distinctUntilChanged

  ContentControl()
    .Content(
      summaries
      |> Observable.map(fun rows ->
        let controls: Control array = [|
          for row in rows -> reactionChip(row, onReact)
        |]

        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(4.0)
          .Margin(2.0, 2.0, 0.0, 0.0)
          .Children(controls))
      |> _.ToBinding()
    )
    .IsVisible(summaries |> Observable.map(fun rows -> not rows.IsEmpty))

/// The pin control beside one bubble of an org thread: the
/// maintainer's pin and unpin, the contributor's static glyph on a
/// pinned message, nothing otherwise. Live from the thread, because
/// a pin or a poll moves it under a built row.
let pinAction
  (state: IObservable<PinState>, message: Message, onPin: bool -> unit)
  : Control =
  let current = state |> Observable.distinctUntilChanged

  ContentControl()
    .Content(
      current
      |> Observable.map(fun pin ->
        if pin.Maintainer then
          Button()
            .Name($"PinMessage{message.Id}")
            .Classes(Theme.name Theme.GhostButton)
            .Padding(4.0, 0.0, 4.0, 2.0)
            .FontSize(11.0)
            .Content(if pin.Pinned then "Unpin message" else "Pin message")
            .OnClickHandler(fun _ args ->
              args.Handled <- true
              onPin(not pin.Pinned))
          :> Control
        elif pin.Pinned then
          let glyph = Icons.iconAt(Glyph.Pin, 12.0)
          ToolTip.SetTip(glyph, "Pinned message")
          glyph :> Control
        else
          null)
      |> _.ToBinding()
    )

/// The reaction tray the hover react button opens: the quick picks
/// on top, the ranked rest under a divider — the site's toolbar
/// shape, one flyout, no nested picker. A pick closes it, the site's
/// own tray rule.
let reactionTray
  (choices: IObservable<ReactionChoices>, onReact: string -> unit)
  : Popup =
  let tray = Popup()
  let ranked = choices |> Observable.distinctUntilChanged

  let host =
    ContentControl()
      .Content(
        ranked
        |> Observable.map(fun choices ->
          let pick(size: float, mark: string) : Control =
            Button()
              .Classes(Theme.name Theme.GhostButton)
              .Padding(4.0)
              .FontSize(size)
              .Content(mark)
              .OnClickHandler(fun _ args ->
                args.Handled <- true
                tray.IsOpen <- false
                onReact mark)

          let quickRow: Control array = [|
            for mark in choices.Quick -> pick(17.0, mark)
          |]

          let rest: Control array = [|
            for mark in choices.Rest -> pick(15.0, mark)
          |]

          StackPanel()
            .Spacing(4.0)
            .Children(
              WrapPanel().Children(quickRow),
              Border()
                .Classes(Theme.name Theme.Hairline)
                .Height(1.0)
                .HorizontalAlignment(HorizontalAlignment.Stretch),
              WrapPanel().MaxWidth(220.0).Children(rest)
            ))
        |> _.ToBinding()
      )

  tray.Child <-
    Border()
      .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
      .BorderThickness(1.0)
      .CornerRadius(8.0)
      .Padding(6.0)
      .Child(host)

  tray

/// The hover-revealed actions beside one bubble: reply and react,
/// 28px icons outside the bubble edge, vertically centered. They
/// take no room in the thread; the pointer over the message row is
/// what shows them, the site's group-hover rule.
let hoverActions
  (message: Message, mine: bool, tray: Popup, actions: BubbleActions)
  : Control =
  let react =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(0.0)
      .Width(28.0)
      .Height(28.0)
      .Content(Icons.iconAt(Glyph.Reaction, 16.0))
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        tray.IsOpen <- not tray.IsOpen)

  ToolTip.SetTip(react, "Add reaction")

  let reply =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(0.0)
      .Width(28.0)
      .Height(28.0)
      .Content(Icons.iconAt(Glyph.Reply, 16.0))
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        actions.Reply message)

  ToolTip.SetTip(reply, "Reply")

  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(2.0)
    .VerticalAlignment(VerticalAlignment.Center)
    .Classes(Theme.name Theme.HoverActions)
    .Children(reply, react)
    .Name((if mine then "BubbleActionsMine" else "BubbleActionsTheirs"))

/// One bubble: the message body in a rounded fill, mine on the
/// accent, theirs on the card surface. A voice note rides in a box
/// of its own, the way the site gives it one, and the flash target
/// is whichever box stands alone at the message's edge.
let bubble(props: BubbleProps) : Control =
  let {
        Message = who
        Mine = mine
        ViewerId = viewerId
        Author = authorUser
        Media = media
        ShowsStatus = showsStatus
        Receipt = receipt
        Preview = preview
        Reactions = reactions
        ReactionChoices = choices
        Pin = pin
        Actions = actions
      } =
    props

  let body =
    CommitCard.body(
      ValueNone,
      who.Body,
      who.BodySegments,
      who.Mentions,
      (if mine then CommitCard.OnFill else CommitCard.Accent)
    )

  let tray = reactionTray(choices, actions.React)

  let shown = images(media.Cache, who, actions.OpenImage)
  let card = linkCard(preview, mine)

  // The card host binds to the thread and is always built, so the
  // body box's gate reads the message itself: a voice-only note
  // carries nothing beside the note, and an empty body box would
  // render as a bare padding pill above the player.
  let carriesMore = who.Body <> "" || shown.IsSome || who.LinkPreview.IsSome

  let voiceBox =
    who.Voice
    |> Option.map(fun clip ->
      let box =
        Border()
          .Classes(
            Theme.name(if mine then Theme.AccentBack else Theme.SurfaceBack)
          )
          .CornerRadius(12.0)
          .Padding(8.0)
          .Margin(0.0, 4.0, 0.0, 0.0)
          .Child(
            voiceChip {
              Audio = media.Audio
              Http = media.Http
              Settings = media.Settings
              Solo = media.Solo
              Log = media.Log
              Source = Remote clip
            }
          )

      if carriesMore then
        box
      else
        box.Name($"ChatMessage{who.Id}"))

  let bodyBox =
    Border()
      .Name(
        if carriesMore || voiceBox.IsNone then
          $"ChatMessage{who.Id}"
        else
          "ChatMessageBody"
      )
      .Classes(Theme.name(if mine then Theme.AccentBack else Theme.SurfaceBack))
      .CornerRadius(12.0)
      .Padding(10.0, 6.0, 10.0, 6.0)
      .MaxWidth(media.PaneWidth |> Observable.map(fun w -> w * 0.85))
      .Child(
        StackPanel().Spacing(4.0).Children [|
          if who.Body <> "" then
            body
          yield! ValueOption.toArray shown
          yield! ValueOption.toArray card
        |]
      )

  // The double-click heart: the site's double-tap, on the desktop's
  // own pair of clicks.
  let heart(args: RoutedEventArgs) =
    args.Handled <- true
    actions.React "❤️"

  let hover = hoverActions(who, mine, tray, actions)

  let ghostRows: Control array =
    who.ReplyTo
    |> Option.map(fun reply ->
      let ghostAuthor =
        authorName(
          viewerId,
          Option.filter (fun user -> reply.UserId <> viewerId) authorUser,
          reply.UserId
        )

      [| ghost(actions.Walk, reply, mine, ghostAuthor) |])
    |> Option.defaultValue [||]

  let bodyRows: Control array =
    if carriesMore || voiceBox.IsNone then
      [| bodyBox :> Control |]
    else
      [||]

  let voiceRows: Control array =
    voiceBox |> Option.map(fun box -> box :> Control) |> Option.toArray

  let pinRows: Control array =
    match pin with
    | ValueSome state -> [| pinAction(state, who, actions.Pin) |]
    | ValueNone -> [||]

  let meta =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(4.0)
      .Margin(2.0, 2.0, 0.0, 0.0)
      .Children(
        Array.append [| reactionChips(reactions, actions.React) |] pinRows
      )

  let column =
    StackPanel()
      .Spacing(2.0)
      .Children([| yield! ghostRows; yield! bodyRows; yield! voiceRows; meta |])

  column.DoubleTapped.Add heart

  // The actions hang outside the bubble edge: on the far side for
  // mine, on the near side for theirs, the way the site floats
  // them off the bubble. The HoverActions style carries the
  // hide-and-show on the row's own pointer-over state.
  let hoverDocked =
    if mine then
      hover.Dock(Dock.Left)
    else
      hover.Dock(Dock.Right)

  // The row's whole lane must hold the pointer: a backgroundless
  // panel passes the samples between the action buttons through to
  // the page behind, and the pointer-over state drops mid-crossing
  // before the far icon is reached.
  let row =
    DockPanel()
      .Classes(Theme.name Theme.MsgRow)
      .Background(Brushes.Transparent)
      .LastChildFill(true)
      .HorizontalAlignment(
        if mine then
          HorizontalAlignment.Right
        else
          HorizontalAlignment.Left
      )
      .Children(hoverDocked, column.Dock(Dock.Left))

  row.Children.Add(tray)

  match receipts(receipt, who, mine) with
  | ValueSome line ->
    StackPanel()
      .Spacing(2.0)
      .HorizontalAlignment(
        if mine then
          HorizontalAlignment.Right
        else
          HorizontalAlignment.Left
      )
      .Children(row, line.IsVisible(showsStatus))
  | ValueNone -> row

/// One staged bubble: the message on its way in, or one the server
/// refused — refused once more with its retry. The staged shape is
/// hollow with a dashed accent edge, the refused one a solid danger
/// edge.
let stagedBubble(onRetry: PendingSend -> unit, pending: PendingSend) : Control =
  let bodyRows: Control array =
    pending.Message.Body
    |> Option.toArray
    |> Array.map(fun text ->
      CommitCard.body(ValueNone, text, [], [], CommitCard.Accent))

  let statusRows: Control array = [|
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(6.0)
      .Children(
        TextBlock()
          .Classes(
            Theme.name(if pending.Rejected then Theme.FgDanger else Theme.Muted)
          )
          .FontSize(11.0)
          .Text((if pending.Rejected then "rejected · retry" else "staged")),
        (if pending.Rejected then
           Button()
             .Classes(Theme.name Theme.GhostButton)
             .Padding(4.0, 0.0, 4.0, 2.0)
             .FontSize(11.0)
             .Content("retry")
             .OnClickHandler(fun _ args ->
               args.Handled <- true
               onRetry pending)
           :> Control
         else
           TextBlock() :> Control)
      )
  |]

  let children = Array.append bodyRows statusRows

  let inner =
    StackPanel().Spacing(4.0).Margin(10.0, 6.0, 10.0, 6.0).Children(children)

  if pending.Rejected then
    Border()
      .Classes(Theme.name Theme.RejectedBubble)
      .CornerRadius(12.0)
      .Padding(0.0)
      .Child(inner)
      .HorizontalAlignment(HorizontalAlignment.Right)
  else
    // The dashed edge the site draws around a message on its way
    // in: a rectangle under the content, the one dashed line the
    // chat carries.
    let edge = Rectangle().Classes(Theme.name Theme.StagedEdge)

    edge.RadiusX <- 11.0
    edge.RadiusY <- 11.0
    edge.StrokeDashArray <- Avalonia.Collections.AvaloniaList<float>(2.0, 2.0)

    Grid().Children(edge, inner).HorizontalAlignment(HorizontalAlignment.Right)

/// One run's org label: the author's avatar and name, or the bare
/// name when the payload is gone.
let runLabel(media: Media, viewerId: string, ofRun: Run) : Control =
  match ofRun.Author with
  | Some user ->
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(4.0)
      .Children(
        Avatar.view {
          Name = user.Name
          Url = user.AvatarUrl
          Lane = user.Lane
          Size = 16.0
          Cache = media.Cache
        },
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .FontSize(11.0)
          .Text(authorName(viewerId, ofRun.Author, ofRun.AuthorId))
          .VerticalAlignment(VerticalAlignment.Center)
      )
  | None ->
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .FontSize(11.0)
      .Text(authorName(viewerId, ofRun.Author, ofRun.AuthorId))

/// One author's run: the org label, the bubbles stacked, and under
/// the last bubble the clock time the site prints for the other
/// side's runs.
let run(props: RunProps) : Control =
  let {
        Run = ofRun
        IsOrg = isOrg
        ViewerId = viewerId
        Media = media
        Status = statusMessage
        Bubble = bubbleOf
        OnRetry = onRetry
      } =
    props

  let labelRows: Control array =
    if isOrg && not ofRun.IsMine then
      [|
        runLabel(media, viewerId, ofRun)
          .Margin(4.0, 6.0, 4.0, 0.0)
          .HorizontalAlignment(HorizontalAlignment.Left)
      |]
    else
      [||]

  let bubbleRows: Control array = [|
    for carried in ofRun.Bubbles ->
      match carried with
      | Sent message -> bubble(bubbleOf message)
      | Staged pending -> stagedBubble(onRetry, pending)
  |]

  // Every run ends in its clock time, the site's own meta line —
  // mine's too, right-aligned, except the one run whose line is
  // the receipt instead (the newest message of the conversation).
  let clockRows: Control array =
    match
      ofRun.Bubbles
      |> List.tryFindBack(function
        | Sent _ -> true
        | Staged _ -> false)
    with
    | Some(Sent last) ->
      let at = last.CreatedAt.LocalDateTime.ToString("h:mm tt")

      let line =
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .FontSize(11.0)
          .Margin(4.0, 0.0, 4.0, 0.0)
          .Text(at)
          .HorizontalAlignment(
            if ofRun.IsMine then
              HorizontalAlignment.Right
            else
              HorizontalAlignment.Left
          )

      [|
        line.IsVisible(
          statusMessage
          |> Observable.map(fun status ->
            not(
              ofRun.IsMine
              && status |> Option.map _.Id |> Option.contains last.Id
            ))
        )
      |]
    | _ -> [||]

  StackPanel()
    .Spacing(2.0)
    .Margin(0.0, 6.0, 0.0, 0.0)
    .HorizontalAlignment(
      if ofRun.IsMine then
        HorizontalAlignment.Right
      else
        HorizontalAlignment.Left
    )
    .Children([| yield! labelRows; yield! bubbleRows; yield! clockRows |])

/// One quiet org event line.
let eventRow(text: string) : Control =
  TextBlock()
    .Classes(Theme.name Theme.Mono)
    .FontSize(12.0)
    .Margin(0.0, 8.0, 0.0, 0.0)
    .HorizontalAlignment(HorizontalAlignment.Center)
    .Text(text)

/// One calendar-day break: the centered quiet pill with its leading
/// dot, and no rule.
let dayRow(label: string) : Control =
  Border()
    .Classes(Theme.name Theme.DayMarker)
    .Margin(0.0, 12.0, 0.0, 0.0)
    .HorizontalAlignment(HorizontalAlignment.Center)
    .Child(
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(
          Ellipse().Classes(Theme.name Theme.DayMarker),
          TextBlock().Text(label)
        )
    )

/// The unread divider: the "HEAD · N new" word on the left with the
/// hairline running off after it, the way the site's head rule
/// reads.
let headRow(count: int) : Control =
  DockPanel()
    .Margin(0.0, 10.0, 0.0, 0.0)
    .Children(
      TextBlock()
        .Classes(Theme.name Theme.HeadRule)
        .Dock(Dock.Left)
        .Text($"HEAD · {count} new"),
      Border()
        .Classes(Theme.name Theme.HeadRule)
        .Height(1.0)
        .VerticalAlignment(VerticalAlignment.Center)
    )
