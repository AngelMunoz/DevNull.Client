namespace DevNull.Client.Views

open System
open System.IO
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Shapes
open Avalonia.Controls.Templates
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Threading
open Avalonia.VisualTree
open NXUI.Extensions
open FSharp.Control.Reactive
open FSharp.UMX
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open IcedTasks
open Navs
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Components.ThreadRecorder
open DevNull.Client.Components.ThreadVoice
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types
open DevNull.Client.Errors
open DevNull.Client.Env
open Microsoft.Extensions.Logging
open DevNull.Client.Formatting
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.UiClock

/// The open thread: the detail column's reading pane. The pane itself
/// rebuilds only when the checked-out target moves; everything inside
/// follows the model stream, so a poll refresh never rebuilds a row.
module Thread =

  /// Where the thread list's scroll stands, swapped whole on each
  /// scroll: whether the reader rides the bottom edge, the extent the
  /// last scroll saw, and where a remembered place has landed.
  type ScrollState(target: float) =
    let place: Ref<ScrollPlace> =
      ref {
        Riding = true
        Extent = 0.0
        Target = target
        Landed = target <= 0.0
        Asked = false
      }

    member _.Current = !place

    member _.Move(next: ScrollPlace) = place := next

  and ScrollPlace = {
    Riding: bool
    Extent: float
    Target: float
    Landed: bool
    Asked: bool
  }

  /// What the page's remaining thread code shares with the message
  /// rows it composes: the dispatch and model it still owns, the
  /// viewer, whether the thread is an org's, and the message media.
  type RowCtx = {
    Dispatch: Dispatch<State.Msg>
    Model: IObservable<State.Model>
    ViewerId: string
    IsOrg: bool
    Media: ThreadMessage.Media
  }


  /// The word and shape one message's meta line carries: `pushed`
  /// until the other side pulled, then `pulled` with its own clock
  /// time in a direct thread, or the org's pull count.
  let receiptWord
    (isOrg: bool, thread: ThreadState, message: Message)
    : (bool * string) option =
    if isOrg then
      let others =
        thread.Members
        |> List.filter(fun member' -> member'.User.Id <> message.UserId)

      let pulled =
        others
        |> List.filter(fun member' -> member'.ReadThroughId >= message.Id)

      if List.isEmpty others then
        None
      elif
        List.length pulled = List.length others && List.length others > 3
      then
        Some(true, "pulled by everyone")
      else
        Some(true, $"pulled by {List.length pulled} of {List.length others}")
    elif
      thread.PartnerReadThroughId >= message.Id
      && thread.PartnerReadThroughId > 0
    then
      let pulledAt = message.CreatedAt.LocalDateTime.ToString("h:mm tt")

      Some(true, $"pulled {pulledAt}")
    else
      Some(false, "pushed")


  /// The who-reacted line: the viewer, the partner, a member the
  /// thread carries — each as their handle — or someone the payload
  /// did not name.
  let whoNames
    (thread: ThreadState option, viewerId: string, who: string list)
    : string =
    who
    |> List.map(fun userId ->
      if userId = viewerId then
        "you"
      else
        let named =
          match thread with
          | Some thread ->
            match thread.Target with
            | Partner user when user.Id = userId -> Some("@" + user.Handle)
            | _ ->
              thread.Members
              |> List.tryFind(fun member' -> member'.User.Id = userId)
              |> Option.map(fun member' -> "@" + member'.User.Handle)
          | None -> None

        named |> Option.defaultValue "someone")
    |> String.concat ", "


  /// One message's pin state as the thread holds it: the org's pinned
  /// list once it loaded, the message's own mark before that.
  let pinnedNow(current: ThreadState, message: Message) : bool =
    match current.Pinned with
    | Some pins -> pins |> List.exists(fun pin -> pin.Id = message.Id)
    | None -> message.PinnedAt.IsSome







  /// One bubble's page-side ask: the row context, the live thread,
  /// the reaction usage, the run's side and author, and the message.
  [<Struct>]
  type BubbleAsk = {
    Ctx: RowCtx
    Thread: IObservable<ThreadState option>
    Usage: IObservable<ReactionUse list>
    Mine: bool
    Author: User option
    Message: Message
  }

  /// One run's page-side ask: the row context, the live thread, the
  /// reaction usage, and the run itself.
  [<Struct>]
  type RunAsk = {
    Ctx: RowCtx
    Thread: IObservable<ThreadState option>
    Usage: IObservable<ReactionUse list>
    Run: State.Run
  }

  /// The reaction tray's two lists from the page's usage history.
  let reactionChoices(usage: ReactionUse list) : ThreadMessage.ReactionChoices =
    let quick = State.quickReactions usage

    {
      Quick = quick
      Rest =
        usage |> State.frequentReactions |> List.except quick |> List.distinct
    }

  /// The gallery the images report up to, opened over the clicked
  /// control's own window.
  let openGallery(ctx: RowCtx, message: Message, source: Control) =
    match TopLevel.GetTopLevel(source) with
    | :? Window as owner ->
      ImageViewer.show {
        Owner = owner
        Cache = ctx.Media.Cache
        Photos = message.Images
        Start = 0
      }
    | _ -> ()

  /// One bubble's props: the message and its side, the run's author,
  /// the media, and the live slices the page derives from the thread.
  let bubbleProps(ask: BubbleAsk) : ThreadMessage.BubbleProps =
    let ctx = ask.Ctx
    let message = ask.Message
    let live = ask.Thread

    let reactions =
      live
      |> Observable.map(fun current ->
        current
        |> Option.map(fun thread ->
          thread.Messages
          |> List.tryFind(fun held -> held.Id = message.Id)
          |> Option.map(fun held ->
            State.summarizeReactions(held.Reactions, ctx.ViewerId))
          |> Option.defaultValue []
          |> List.map(fun summary ->
            ({
              Emoji = summary.Emoji
              Count = summary.Count
              Mine = summary.IsMine
              Who = whoNames(Some thread, ctx.ViewerId, summary.Who)
            }
            : ThreadMessage.ReactionChip)))
        |> Option.defaultValue [])
      |> Observable.distinctUntilChanged

    let preview =
      live
      |> Observable.map(fun current ->
        current
        |> Option.bind(fun thread ->
          thread.Messages
          |> List.tryFind(fun held -> held.Id = message.Id)
          |> Option.bind _.LinkPreview))
      |> Observable.distinctUntilChanged

    let pin =
      if ctx.IsOrg then
        ValueSome(
          live
          |> Observable.map(fun current ->
            match current with
            | Some thread ->
              ({
                Pinned = pinnedNow(thread, message)
                Maintainer = thread.Role |> Option.contains Maintainer
              }
              : ThreadMessage.PinState)
            | None ->
              ({ Pinned = false; Maintainer = false }: ThreadMessage.PinState))
          |> Observable.distinctUntilChanged
        )
      else
        ValueNone

    {
      ThreadMessage.Message = message
      Mine = ask.Mine
      ViewerId = ctx.ViewerId
      Author = ask.Author
      Media = ctx.Media
      ShowsStatus =
        live
        |> Observable.map(fun current ->
          current
          |> Option.bind(fun thread ->
            State.statusOf(ctx.IsOrg, ctx.ViewerId, thread))
          |> Option.map _.Id
          |> Option.contains message.Id)
      Receipt =
        live
        |> Observable.map(fun current ->
          current
          |> Option.bind(fun thread -> receiptWord(ctx.IsOrg, thread, message)))
      Preview = preview
      Reactions = reactions
      ReactionChoices = ask.Usage |> Observable.map reactionChoices
      Pin = pin
      Actions = {
        Reply = fun who -> ctx.Dispatch(State.ReplyWanted who)
        React = fun emoji -> ctx.Dispatch(State.ReactWanted(message.Id, emoji))
        Pin =
          fun wanted ->
            ctx.Dispatch(
              if wanted then
                State.PinAskWanted message
              else
                State.MessageUnpinWanted message
            )
        OpenImage = fun (who, source) -> openGallery(ctx, who, source)
        Walk = fun id -> ctx.Dispatch(State.GhostWanted id)
      }
    }

  /// One run's props: the run mapped to the message element's own
  /// shape, the live status message, and the per-bubble factory.
  let runProps(ask: RunAsk) : ThreadMessage.RunProps =
    let ctx = ask.Ctx
    let ofRun = ask.Run

    {
      ThreadMessage.Run = {
        IsMine = ofRun.IsMine
        AuthorId = ofRun.AuthorId
        Author = ofRun.Author
        Bubbles = [
          for carried in ofRun.Bubbles ->
            match carried with
            | State.Sent message -> ThreadMessage.Sent message
            | State.Staged pending -> ThreadMessage.Staged pending
        ]
      }
      IsOrg = ctx.IsOrg
      ViewerId = ctx.ViewerId
      Media = ctx.Media
      Status =
        ask.Thread
        |> Observable.map(fun current ->
          current
          |> Option.bind(fun thread ->
            State.statusOf(ctx.IsOrg, ctx.ViewerId, thread)))
      Bubble =
        fun message ->
          bubbleProps {
            Ctx = ctx
            Thread = ask.Thread
            Usage = ask.Usage
            Mine = ofRun.IsMine
            Author = message.User
            Message = message
          }
      OnRetry = fun pending -> ctx.Dispatch(State.RetryWanted pending.ClientId)
    }

  /// The message list: virtualized, keyed, following its tail.
  let list
    (ctx: RowCtx, key: string, restore: float, thread: IObservable<ThreadState>)
    : Control =
    let scroll = ScrollState(restore)

    let liveThread =
      ctx.Model
      |> Observable.map(fun current -> current.State.Threads |> Map.tryFind key)
      |> Observable.distinctUntilChanged

    let rowItems =
      thread
      |> Observable.map(fun current ->
        State.threadRows {
          State.ViewerId = ctx.ViewerId
          IsOrg = ctx.IsOrg
          Now = DateTimeOffset.UtcNow
          Thread = current
        })

    let usage =
      ctx.Model
      |> Observable.map(fun current ->
        current.State.ReactionUsage |> Option.defaultValue [])
      |> Observable.distinctUntilChanged

    let template =
      FuncDataTemplate<State.ThreadRow>(
        (fun item _ ->
          if isNull(box item) then
            Panel() :> Control
          else
            match item with
            | State.ThreadRow.Day(_, label) -> ThreadMessage.dayRow label
            | State.ThreadRow.Head count -> ThreadMessage.headRow count
            | State.ThreadRow.Event message ->
              ThreadMessage.eventRow(State.eventText(ctx.ViewerId, message))
            | State.ThreadRow.Run ofRun ->
              ThreadMessage.run(
                runProps {
                  Ctx = ctx
                  Thread = liveThread
                  Usage = usage
                  Run = ofRun
                }
              )),
        false
      )

    let items =
      ItemsControl()
        .ItemTemplate(template)
        .ItemsPanel(FuncTemplate<Panel>(fun () -> VirtualizingStackPanel()))
        .BindItemsSource(rowItems, State.threadRowKey)

    let scroller = ScrollViewer().Name("RemotesThread").Content(items)

    scroller.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled

    // The scroll model in one handler: the top asks for older pages,
    // the bottom edge is the tail a riding reader keeps, a remembered
    // place lands once the extent can hold it, and every settled place
    // parks itself in the store. Each pass swaps one whole place.
    scroller.ScrollChanged.Add(fun _ ->
      let extent = scroller.Extent.Height
      let room = extent - scroller.Viewport.Height
      let place = scroll.Current

      if scroller.Offset.Y < 240.0 && room > 0.0 then
        ctx.Dispatch State.OlderWanted

      let fromEnd = room - scroller.Offset.Y

      let riding =
        if fromEnd < 48.0 then
          true
        elif extent > place.Extent + 0.5 && place.Riding then
          scroller.ScrollToEnd()
          place.Riding
        else
          false

      // A whole viewport below the reading is the site's own bar for
      // "far from the present edge".
      let behind = fromEnd > scroller.Viewport.Height

      ctx.Dispatch(State.RideMoved(riding, behind))

      let settled =
        if not place.Landed then
          if room >= place.Target then
            scroller.Offset <- Vector(0.0, place.Target)
            { place with Landed = true }
          elif not place.Asked then
            ctx.Dispatch State.OlderWanted
            { place with Asked = true }
          elif extent > place.Extent then
            // The ask grew nothing the first time; land where the
            // list ends rather than chase forever.
            { place with Landed = true }
          else
            place
        else
          place

      scroll.Move {
        settled with
            Riding = riding
            Extent = extent
      }

      ctx.Dispatch(State.ThreadScrolled scroller.Offset.Y))

    // The landing asks: one message a ghost named, or the present
    // edge. The listener ends with the list's detach.
    let landings =
      ctx.Model |> Observable.map _.Landing |> Observable.distinctUntilChanged

    let land =
      Sub.ofId "landing" (fun _ ->
        landings.Subscribe(function
          | Some(State.Message id) ->
            scroller.Offset <- Vector(0.0, 0.0)

            scroller.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? Border as border when visual.Name = $"ChatMessage{id}" ->
                Some border
              | _ -> None)
            |> Option.iter(fun border ->
              border.Classes.Add(Theme.name Theme.FlashRing) |> ignore

              let timer = DispatcherTimer()
              timer.Interval <- TimeSpan.FromMilliseconds 1200.0

              let stop() =
                timer.Stop()
                border.Classes.Remove(Theme.name Theme.FlashRing) |> ignore

              timer.Tick.Add(fun _ -> stop())
              border.DetachedFromVisualTree.Add(fun _ -> stop())
              timer.Start())

            ctx.Dispatch State.Landed
          | Some State.Present ->
            scroller.ScrollToEnd()
            ctx.Dispatch State.Landed
          | None -> ()))
      |> Sub.untilDetached scroller
      |> List.exactlyOne
      |> snd

    land ignore |> ignore
    scroller

  /// The composer: the meta row with the stash chip and the counter,
  /// the reply bar, the staged images, and the input row. Ctrl+Enter
  /// The floating return to the thread's present edge; while messages
  /// arrived unseen, it doubles as the "N new" pill.
  let present
    (
      dispatch: Dispatch<State.Msg>,
      visible: IObservable<bool>,
      label: IObservable<string>
    ) : Control =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(10.0, 4.0, 10.0, 4.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Bottom)
      .Margin(0.0, 0.0, 0.0, 8.0)
      .Content(label |> _.ToBinding())
      .IsVisible(visible)
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        dispatch State.PresentWanted)

  /// One staged image chip, the commit composer's own shape: the bytes
  /// decoded small, the file name, and its remove. The decoded bitmap
  /// is native memory, so the preview disposes it when the strip
  /// rebuilds and drops it.
  let imageChip
    (dispatch: Dispatch<State.Msg>, image: ImageAttachment, index: int)
    : Control =
    let bitmap =
      use stream = new MemoryStream(image.Bytes)
      Bitmap.DecodeToWidth(stream, 96)

    let preview =
      Avalonia.Controls
        .Image()
        .Source(bitmap)
        .Width(48.0)
        .Height(48.0)
        .Stretch(Stretch.UniformToFill)

    preview.DetachedFromVisualTree.Add(fun _ -> bitmap.Dispose())

    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .Padding(6.0)
      .Child(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(
            preview,
            StackPanel()
              .VerticalAlignment(VerticalAlignment.Center)
              .Children(
                TextBlock()
                  .Classes(Theme.name Theme.Mono)
                  .FontSize(12.0)
                  .Text(image.FileName)
                  .MaxWidth(140.0)
                  .TextTrimming(TextTrimming.CharacterEllipsis),
                Button()
                  .Name($"RemoveImage{index}")
                  .Classes(Theme.name Theme.GhostButton)
                  .Padding(4.0, 0.0, 4.0, 0.0)
                  .FontSize(12.0)
                  .Content("remove")
                  .OnClickHandler(fun _ args ->
                    args.Handled <- true
                    dispatch(State.ImageRemoved index))
              )
          )
      )

  /// The link-card picker: a quiet button whose flyout lists the
  /// draft's links, the first one, or none. The button itself shows
  /// only while the draft carries a link. The flyout is
  /// control-owned: it dies with the pane.
  let linkPicker
    (
      dispatch: Dispatch<State.Msg>,
      draft: IObservable<string>,
      choice: IObservable<LinkCard>
    ) : Control =
    let links = draft |> Observable.map State.linksOf

    let label =
      Observable.combineLatest links choice
      |> Observable.map(fun (found, chosen) ->
        match chosen with
        | LinkCard.CardOf url when found |> List.contains url ->
          State.plainLink url
        | LinkCard.NoCard -> "no card"
        | _ -> if found.IsEmpty then "no links" else "first link")

    let rows =
      Observable.combineLatest links choice
      |> Observable.map(fun (found, chosen) -> [
        yield ("first link", LinkCard.FirstLink)

        for url in found -> State.plainLink url, LinkCard.CardOf url
        yield ("no card", LinkCard.NoCard)
      ])
      |> Observable.distinctUntilChanged

    let host = ContentControl()

    host.Content(
      rows
      |> Observable.map(fun picks ->
        StackPanel().Children [|
          for label', pick in picks ->
            let choice: Control =
              Button()
                .Classes(Theme.name Theme.GhostButton)
                .Padding(6.0, 2.0, 6.0, 2.0)
                .FontSize(12.0)
                .Content(label')
                .OnClickHandler(fun _ args ->
                  args.Handled <- true
                  dispatch(State.LinkChosen pick))

            choice
        |])
      |> _.ToBinding()
    )
    |> ignore

    let flyout =
      Popup()
        .Child(
          Border()
            .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
            .BorderThickness(1.0)
            .CornerRadius(6.0)
            .Padding(4.0)
            .Child(host)
        )

    let quiet =
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(6.0, 0.0, 6.0, 0.0)
        .FontSize(11.0)
        .Content(label |> Observable.distinctUntilChanged |> _.ToBinding())
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          flyout.IsOpen <- not flyout.IsOpen)

    ToolTip.SetTip(quiet, "Choose which link gets a card")

    Panel()
      .Children(quiet, flyout)
      .HorizontalAlignment(HorizontalAlignment.Right)
      .IsVisible(links |> Observable.map(fun found -> not found.IsEmpty))

  /// The composer: the meta row with the typing line and the counter,
  /// the reply bar, the staged images, and the input row. Enter
  /// pushes, Esc cancels the reply.
  let composer
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      solo: Solo,
      target: ChatTarget,
      thread: IObservable<ThreadState>
    ) : Control =
    let isOrg =
      match target with
      | Org _ -> true
      | Partner _ -> false

    let placeholder =
      match target with
      | Partner partner -> $"Message @{partner.Handle}"
      | Org org ->
        let orgName = org.Name |> Option.defaultValue "the org"
        $"Message {orgName}"

    let draft = model |> Observable.map _.Draft
    let images = model |> Observable.map _.Images
    let choice = model |> Observable.map _.LinkChoice

    let voiceRides =
      model |> Observable.map(fun current -> current.Voice.IsSome)

    let counter = draft |> Observable.map(fun text -> text.TrimEnd().Length)

    // The site's own word: quiet until 80%, amber from 95%, danger at
    // the limit, thousands grouped the way the counter reads them.
    let counterBlock =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .FontSize(11.0)
        .Text(
          counter
          |> Observable.map(fun length ->
            FormattableString.Invariant $"""{length:N0}/2,000""")
        )
        .IsVisible(counter |> Observable.map(fun length -> length >= 1600))

    let warning =
      Theme.bindClass(
        counterBlock,
        Theme.FgWarning,
        counter |> Observable.map(fun length -> length >= 1900 && length < 2000)
      )

    // The counter goes quiet-danger at the limit; the class carries
    // the color, never a brush observable.
    let danger =
      Theme.bindClass(
        counterBlock,
        Theme.FgDanger,
        counter |> Observable.map(fun length -> length >= 2000)
      )

    counterBlock.DetachedFromVisualTree.Add(fun _ ->
      warning.Dispose()
      danger.Dispose())

    // The typing line, the site's own slot: the meta row's leading
    // word while the partner or the org types, its three dots chasing
    // each other on the theme's own beat.
    let typingWord =
      let names =
        thread
        |> Observable.map(fun current -> State.typingNames(isOrg, current))
        |> Observable.distinctUntilChanged

      let dot(klass: string) =
        Ellipse()
          .Width(3.0)
          .Height(3.0)
          .Classes(klass)
          .VerticalAlignment(VerticalAlignment.Center)

      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(4.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Children(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(2.0)
            .VerticalAlignment(VerticalAlignment.Center)
            .Children(
              dot(Theme.name Theme.TypingDot1),
              dot(Theme.name Theme.TypingDot2),
              dot(Theme.name Theme.TypingDot3)
            ),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .FontSize(11.0)
            .Text(names |> Observable.map State.typingText)
        )
        .IsVisible(names |> Observable.map(List.isEmpty >> not))

    let metaRow =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8.0)
        .Children(typingWord, linkPicker(dispatch, draft, choice), counterBlock)

    let replyBar =
      ContentControl()
        .Content(
          PageChrome.rebuildOn(
            thread,
            (fun current -> current.ReplyingTo),
            fun current ->
              match current.ReplyingTo with
              | Some parent ->
                StackPanel()
                  .Orientation(Orientation.Horizontal)
                  .Spacing(8.0)
                  .Children(
                    TextBlock()
                      .Classes(Theme.name Theme.Muted)
                      .FontSize(12.0)
                      .Text($"in reply to: {parent.Body}")
                      .TextTrimming(TextTrimming.CharacterEllipsis),
                    Button()
                      .Classes(Theme.name Theme.GhostButton)
                      .Padding(4.0, 0.0, 4.0, 2.0)
                      .FontSize(11.0)
                      .Content("cancel")
                      .OnClickHandler(fun _ args ->
                        args.Handled <- true
                        dispatch State.ReplyCancelled)
                  )
              | None -> Panel()
          )
          |> _.ToBinding()
        )

    // The staged images: the chips rebuild only when the image list
    // itself moves, so typing never re-decodes a preview under the
    // caret.
    let staged =
      ContentControl()
        .Content(
          images
          |> Observable.distinctUntilChanged
          |> Observable.map(fun staged' ->
            let chips: Control array = [|
              for index, image in List.indexed staged' ->
                imageChip(dispatch, image, index)
            |]

            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(8.0)
              .Children(chips)
            :> Control)
          |> _.ToBinding()
        )
        .IsVisible(images |> Observable.map(fun staged' -> not staged'.IsEmpty))

    let input =
      TextBox()
        .Name("RemotesComposer")
        .PlaceholderText(placeholder)
        .MinHeight(40.0)
        .MaxHeight(160.0)
        .AcceptsReturn(true)
        .TextWrapping(TextWrapping.Wrap)
        // The site's own rule: a voice note rides alone, so its draft
        // is closed to typing while one is staged.
        .IsEnabled(voiceRides |> Observable.map not)
        .Text(draft |> Observable.distinctUntilChanged |> _.ToBinding())
        .OnTextChangedHandler(fun box _ ->
          let text = box.Text |> Option.ofObj |> Option.defaultValue ""

          dispatch(State.DraftChanged(text, box.CaretIndex)))
        // The caret is the mention's own place: a move inside the text
        // re-reads what stands at the caret, the way typing does.
        .OnCaretIndexChanged(fun box _ ->
          let text = box.Text |> Option.ofObj |> Option.defaultValue ""

          dispatch(State.DraftChanged(text, box.CaretIndex)))

    // The site's fine-pointer rule, through the tunnel so the box's
    // own key handling never eats it: Enter pushes, Shift+Enter makes
    // the newline, Esc cancels the reply or discards a live recording,
    // and the platform's paste gesture asks the attachment sources
    // first — a clipboard image stages, clipboard text lands in the
    // box.
    input.AddHandler(
      InputElement.KeyDownEvent,
      (fun _ args ->
        let pasteMatches(args: KeyEventArgs) =
          match Application.Current with
          | null -> false
          | app ->
            match app.PlatformSettings with
            | null -> false
            | settings ->
              settings.HotkeyConfiguration.Paste
              |> Seq.exists(fun gesture -> gesture.Matches args)

        let plainEnter =
          args.Key = Key.Enter && args.KeyModifiers = KeyModifiers.None

        let controlEnter =
          args.Key = Key.Enter
          && args.KeyModifiers.HasFlag(KeyModifiers.Control)

        if plainEnter || controlEnter then
          args.Handled <- true
          dispatch State.SendWanted
        elif args.Key = Key.Escape then
          args.Handled <- true
          dispatch State.ReplyCancelled
          dispatch State.RecordCancelled
        elif pasteMatches args then
          args.Handled <- true

          asyncEx {
            let! image = props.Env.Attachments.ReadClipboardImage()

            match image with
            | Some staged ->
              Dispatcher.UIThread.Post(fun () ->
                dispatch(State.Picked [ staged ]))
            | None ->
              let! text = props.Env.Attachments.ReadClipboardText()

              match text with
              | Some pasted ->
                Dispatcher.UIThread.Post(fun () ->
                  input.SelectedText <- pasted)
              | None -> ()
          }
          |> Async.StartImmediate
        else
          ()),
      RoutingStrategies.Tunnel
    )

    // Who a mention can name here: the partner alone, or the org's
    // members beside the viewer.
    let mentionables(current: ThreadState) : User list =
      match target with
      | Partner partner -> [ partner ]
      | Org _ ->
        current.Members
        |> List.map _.User
        |> List.filter(fun user -> user.Id <> props.Session.Session.User.Id)

    let mentionPicks =
      let word = model |> Observable.map _.Mention

      Observable.combineLatest word thread
      |> Observable.map(fun (typed, current) ->
        match typed with
        | Some prefix ->
          mentionables current
          |> List.filter(fun user ->
            user.Handle.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
          |> List.truncate 6
        | None -> [])
      |> Observable.distinctUntilChanged

    // The completion lands in the box itself, so the change rides the
    // same TextChanged path typing does.
    let applyMention(handle: string) =
      let typed = input.Text |> Option.ofObj |> Option.defaultValue ""

      let next, caret = State.mentionCompleted(typed, input.CaretIndex, handle)

      input.Text <- next
      input.CaretIndex <- caret
      dispatch(State.DraftChanged(next, caret))

    let mentions =
      let pick(user: User) : Control =
        Button()
          .Name("MentionPick")
          .Classes(Theme.name Theme.GhostButton)
          .Padding(8.0, 4.0, 8.0, 4.0)
          .FontSize(12.0)
          .HorizontalContentAlignment(HorizontalAlignment.Left)
          .Content(
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(6.0)
              .Children(
                TextBlock()
                  .Classes(Theme.name Theme.Mono)
                  .FontSize(12.0)
                  .Text("@" + user.Handle),
                TextBlock()
                  .Classes(Theme.name Theme.Muted)
                  .FontSize(12.0)
                  .Text(user.Name)
              )
          )
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            applyMention user.Handle)
        :> Control

      ContentControl()
        .Content(
          mentionPicks
          |> Observable.map(fun people ->
            let picks: Control array = [| for user in people -> pick user |]

            Border()
              .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
              .BorderThickness(1.0)
              .CornerRadius(6.0)
              .Padding(4.0)
              .MaxWidth(280.0)
              .HorizontalAlignment(HorizontalAlignment.Left)
              .Child(StackPanel().Children(picks))
            :> Control)
          |> _.ToBinding()
        )
        .IsVisible(mentionPicks |> Observable.map(List.isEmpty >> not))

    let pick =
      let button =
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0)
          .Content(Icons.iconAt(Glyph.Image, 16.0))
          // Images never ride beside a staged voice note either.
          .IsEnabled(voiceRides |> Observable.map not)
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            dispatch State.PickWanted)

      ToolTip.SetTip(button, "Attach images")
      button

    let send =
      Button()
        .Classes(Theme.name Theme.AccentButton)
        .MinWidth(72.0)
        .Content("Push")
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          dispatch State.SendWanted)

    // The staged voice note: the full player over the take just made,
    // so it can be heard before it rides, with its discard beside it.
    // The take is keyed by its length and name, never by comparing
    // the bytes themselves.
    let stagedVoice =
      let host = ContentControl()

      host.Content(
        model
        |> Observable.map(fun current ->
          current.Voice
          |> Option.map(fun (attachment, bars) ->
            (attachment.FileName, attachment.Bytes.Length), (attachment, bars)))
        |> Observable.distinctUntilChanged
        |> Observable.map(function
          | Some(_, (attachment, bars)) ->
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(8.0)
              .Children(
                voiceChip {
                  Audio = props.Env.Natives.Audio
                  Http = props.Env.Http
                  Settings = props.Env.Settings
                  Solo = solo
                  Log =
                    props.Env.LoggerFactory.CreateLogger
                      "DevNull.Client.Views.RemotesVoice"
                  Source = Staged(attachment.Bytes, bars)
                },
                Button()
                  .Classes(Theme.name Theme.GhostButton)
                  .Padding(4.0)
                  .Content(Icons.iconAt(Glyph.Trash, 14.0))
                  .VerticalAlignment(VerticalAlignment.Center)
                  .OnClickHandler(fun _ args ->
                    args.Handled <- true
                    dispatch State.VoiceDiscarded)
              )
            :> Control
          | None -> TextBlock() :> Control)
        |> _.ToBinding()
      )
      |> ignore

      host.IsVisible(
        model |> Observable.map(fun current -> current.Voice.IsSome)
      )
      |> ignore

      host

    // The mic button: the site's own rule — it stays in place and
    // dims while a draft or images ride the push, saying why.
    let mic =
      let button =
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0)
          .Content(Icons.iconAt(Glyph.Mic, 16.0))
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            dispatch State.RecordWanted)

      let free =
        model
        |> Observable.map(fun current ->
          current.Voice.IsNone && current.Images.IsEmpty)

      button.IsEnabled(
        Observable.combineLatest
          free
          (model |> Observable.map(fun current -> current.Draft.Trim() = ""))
        |> Observable.map(fun (free', quiet) -> free' && quiet)
      )
      |> ignore

      ToolTip.SetTip(button, "Record a voice note (max. 1 min)")

      button.IsVisible(
        model
        |> Observable.map(fun _ -> props.Env.Natives.Audio.IsSome)
        |> Observable.distinctUntilChanged
      )
      |> ignore

      button

    // The ping button: a quiet nudge for the partner or the org.
    let ping =
      let button =
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0)
          .Content(Icons.iconAt(Glyph.Vibrate, 16.0))
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            dispatch State.PingWanted)

      let label =
        match target with
        | Partner partner -> $"Ping @{partner.Handle}"
        | Org org ->
          let orgName = org.Name |> Option.defaultValue "the org"

          $"Ping {orgName}"

      ToolTip.SetTip(button, label)
      button

    // The recording lane: the bordered box that stands in for the
    // input row while the take is live. One lane per take — the
    // content is projected when the recording starts, so the program
    // (and the microphone with it) lives only while the take does.
    // The lane reports through the page's own messages.
    let recordLane =
      let report(outcome: Recorder.Report) =
        match outcome with
        | Recorder.Staged staged -> dispatch(State.VoiceStaged staged)
        | Recorder.Cancel -> dispatch State.RecordCancelled

      ContentControl()
        .Name("RemotesRecord")
        .Content(
          model
          |> Observable.map(fun current -> current.Recording)
          |> Observable.distinctUntilChanged
          |> Observable.map(fun recording ->
            if recording then
              RecordLane(props.Env.Natives.Audio, report) :> Control
            else
              null)
          |> _.ToBinding()
        )
        .IsVisible(model |> Observable.map(fun current -> current.Recording))

    // The input row: the tool buttons docked left, Push right, and the
    // box filling every pixel between them. A StackPanel here measured
    // the box to its typed length and pushed Push off the pane's edge;
    // a DockPanel lays no gaps of its own, so each docked edge carries
    // its own.
    let inputRow =
      DockPanel()
        .LastChildFill(true)
        .Children(
          pick
            .Dock(Dock.Left)
            .Margin(0.0, 0.0, 8.0, 0.0)
            .VerticalAlignment(VerticalAlignment.Center),
          mic
            .Dock(Dock.Left)
            .Margin(0.0, 0.0, 8.0, 0.0)
            .VerticalAlignment(VerticalAlignment.Center),
          ping
            .Dock(Dock.Left)
            .Margin(0.0, 0.0, 8.0, 0.0)
            .VerticalAlignment(VerticalAlignment.Center),
          send
            .Dock(Dock.Right)
            .Margin(8.0, 0.0, 0.0, 0.0)
            .VerticalAlignment(VerticalAlignment.Center),
          input
        )

    inputRow.IsVisible(
      model |> Observable.map(fun current -> not current.Recording)
    )
    |> ignore

    StackPanel()
      .Spacing(6.0)
      .Margin(0.0, 8.0, 0.0, 8.0)
      .Children(
        mentions,
        metaRow,
        replyBar,
        staged,
        stagedVoice,
        recordLane,
        inputRow
      )




  /// The pane's footer: the composer while the thread takes pushes,
  /// the access bar when a direct thread does not, nothing for a
  /// closed org. The content rebuilds only when the access itself
  /// moves: the composer's own bindings must survive every poll.
  let footer
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      solo: Solo,
      target: ChatTarget,
      thread: IObservable<ThreadState>
    ) : Control =
    ContentControl()
      .Content(
        PageChrome.rebuildOn(
          thread,
          (fun current -> current.Access),
          (fun current ->
            match current.Access with
            | PushOpen ->
              composer(props, model, dispatch, solo, target, thread)
            | PushRequest
            | PushWaiting
            | PushRestricted
            | PushClosed
            | UnknownAccess _ as refused ->
              match target with
              | Partner partner ->
                ThreadChrome.accessBar {
                  Partner = partner
                  Access = refused
                  OnSettle =
                    fun (partner, merging) ->
                      dispatch(State.SettleWanted(partner, merging))
                }
              | Org _ -> Panel())
        )
        |> _.ToBinding()
      )

  /// The thread pane for one target: the identity header, the message
  /// list over its overlays, and the typing line.
  let pane
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      solo: Solo,
      target: ChatTarget,
      key: string
    ) : Control =
    let isOrg =
      match target with
      | Org _ -> true
      | Partner _ -> false

    // The reading pane's own width, watched before anything reads it:
    // bubbles cap themselves at a fraction of it, the way the site
    // caps theirs at a fraction of the pane.
    let rowsGrid = Grid()

    let paneWidth =
      rowsGrid.GetObservable(Visual.BoundsProperty)
      |> Observable.map(fun bounds -> bounds.Width)

    let media: ThreadMessage.Media = {
      Cache = props.Env.ImageCache
      Audio = props.Env.Natives.Audio
      Http = props.Env.Http
      Settings = props.Env.Settings
      Solo = solo
      Log =
        props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.RemotesVoice"
      PaneWidth = paneWidth
    }

    let ctx: RowCtx = {
      Dispatch = dispatch
      Model = model
      ViewerId = props.Session.Session.User.Id
      IsOrg = isOrg
      Media = media
    }

    let thread =
      model
      |> Observable.map(fun current ->
        current.State.Threads
        |> Map.tryFind key
        |> Option.defaultValue(RemotesState.emptyThread target))

    let restore =
      props.Env.Remotes.ScrollPlaceOf key |> ValueOption.defaultValue 0.0

    let header =
      let identity: Control =
        match target with
        | Partner partner ->
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(10.0)
            .Children(
              Avatar.view {
                Name = partner.Name
                Url = partner.AvatarUrl
                Lane = partner.Lane
                Size = 36.0
                Cache = media.Cache
              },
              StackPanel()
                .VerticalAlignment(VerticalAlignment.Center)
                .Children(
                  TextBlock()
                    .Classes(Theme.name Theme.Subtitle)
                    .Text(partner.Name),
                  TextBlock()
                    .Classes(Theme.name Theme.Mono)
                    .Text("@" + partner.Handle)
                )
            )
          :> Control
        | Org org ->
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(10.0)
            .Children(
              TextBlock()
                .Classes(Theme.name Theme.Subtitle)
                .Text(org.Name |> Option.defaultValue "org")
                .VerticalAlignment(VerticalAlignment.Center),
              ContentControl()
                .Content(
                  PageChrome.rebuildOn(
                    thread,
                    (fun current -> current.Members),
                    (fun current ->
                      ThreadMenus.memberStack(media.Cache, current.Members))
                  )
                  |> _.ToBinding()
                )
            )
          :> Control

      let menuActions: ThreadMenus.MenuActions = {
        ViewProfile =
          fun user ->
            Navigation.go(props.Nav, "/profile/" + user.Handle, ignore)
        Pin = fun wanted -> dispatch(State.PinWanted wanted)
        Archive = fun wanted -> dispatch(State.ArchiveWanted wanted)
        Mute = fun how -> dispatch(State.MuteWanted how)
        Block = fun partner -> dispatch(State.BlockWanted partner)
        Leave =
          fun () ->
            dispatch(
              State.OrgRemoveWanted(props.Session.Session.User.Handle, true)
            )
      }

      let actions =
        ThreadMenus.actionsMenu {
          Target = target
          Pinned =
            thread |> Observable.map(fun current -> current.PinnedAt.IsSome)
          Archived = thread |> Observable.map _.IsArchived
          Actions = menuActions
        }

      let menu: Control =
        match target with
        | Org org ->
          let orgActions: ThreadMenus.OrgActions = {
            RenameChanged = fun name -> dispatch(State.OrgRenameChanged name)
            Rename = fun () -> dispatch State.OrgRenameWanted
            MemberChanged = fun name -> dispatch(State.OrgMemberChanged name)
            Add = fun () -> dispatch State.OrgAddWanted
            ShareChanged = fun shared -> dispatch(State.OrgShareChanged shared)
            Role =
              fun (handle, role) -> dispatch(State.OrgRoleWanted(handle, role))
            Remove =
              fun (handle, leaving) ->
                dispatch(State.OrgRemoveWanted(handle, leaving))
          }

          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Children(
              ThreadMenus.orgMenu {
                Org = org
                ViewerId = props.Session.Session.User.Id
                ViewerHandle = props.Session.Session.User.Handle
                Thread = thread
                Rename = model |> Observable.map _.OrgRename
                Picked = model |> Observable.map _.OrgMembers
                Shared = model |> Observable.map _.OrgShare
                Actions = orgActions
              },
              actions
            )
          :> Control
        | Partner _ -> actions

      DockPanel().LastChildFill(true).Children(menu.Dock(Dock.Right), identity)

    let loadingOlder = thread |> Observable.map _.IsLoadingOlder

    let loadingLine =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text("loading older…")
        .HorizontalAlignment(HorizontalAlignment.Center)
        .Margin(0.0, 8.0, 0.0, 0.0)
        .VerticalAlignment(VerticalAlignment.Top)
        .IsVisible(loadingOlder)

    // The site's own bar: the pill shows when the reading sits a
    // viewport or more behind the present edge, or when messages
    // arrived the reader has not seen; unseen messages name their
    // count.
    let awayFromPresent =
      model
      |> Observable.map(fun current ->
        (not current.Riding && current.Behind) || current.Unseen > 0)
      |> Observable.distinctUntilChanged

    let presentLabel =
      model
      |> Observable.map(fun current ->
        if current.Unseen > 0 then
          $"{current.Unseen} new"
        else
          "back to present")
      |> Observable.distinctUntilChanged

    let presentButton = present(dispatch, awayFromPresent, presentLabel)

    // The pinned panel stands over the reading while it is open; the
    // banner row over the pane's top opens it.
    let pinned =
      model |> Observable.map _.PinnedPanel |> Observable.distinctUntilChanged

    let listHost =
      Panel()
        .Children(
          list(ctx, key, restore, thread),
          loadingLine,
          presentButton,
          ThreadChrome.pinnedPanel {
            Thread = thread
            Shown = pinned
            OnClose = fun () -> dispatch(State.PinnedPanelWanted false)
          }
        )

    let banner =
      ThreadChrome.pinnedBanner {
        Thread = thread
        OnOpen = fun () -> dispatch(State.PinnedPanelWanted true)
      }

    let noticeLine = ThreadChrome.notice(model |> Observable.map _.Notice)

    let rows =
      rowsGrid.RowDefinitions <- RowDefinitions("Auto,Auto,Auto,*,Auto")

      rowsGrid.Children(
        header.Row(0).Margin(0.0, 0.0, 0.0, 8.0),
        banner.Row(1),
        noticeLine.Row(2),
        listHost.Row(3),
        footer(props, model, dispatch, solo, target, thread).Row(4)
      )

    let quietWord =
      match target with
      | Partner partner -> $"empty history. say hi to @{partner.Handle}"
      | Org _ -> "empty history. say hi to the org"

    let quiet =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text(quietWord)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Center)
        .IsVisible(
          thread
          |> Observable.map(fun current ->
            current.IsLoaded
            && current.Messages.IsEmpty
            && current.Pending.IsEmpty)
        )

    Panel()
      .Children(
        rows,
        quiet,
        ThreadChrome.pinDialog {
          Open =
            model
            |> Observable.map(fun current -> current.PinAsk.IsSome)
            |> Observable.distinctUntilChanged
          OnConfirm = fun keep -> dispatch(State.PinConfirmed keep)
          OnCancel = fun () -> dispatch State.PinAskCancelled
        }
      )

  /// The detail column: the pane of the checked-out target — rebuilt
  /// only when the target moves — or the quiet invitation.
  let column
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let solo = Solo()

    // The pane rebuilds only when the checked-out target moves. The
    // guard keys on the store's key, never the whole target: a refresh
    // that rebuilds the target's user payload must not rebuild the
    // pane under it.
    let checkedOut =
      model
      |> Observable.map _.State.OpenTarget
      |> Observable.distinctUntilChangedKey(Option.map ChatTargets.key)

    ContentControl()
      .Content(
        checkedOut
        |> Observable.map(function
          | Some target ->
            pane(props, model, dispatch, solo, target, ChatTargets.key target)
          | None -> Parts.noThread())
        |> _.ToBinding()
      )
