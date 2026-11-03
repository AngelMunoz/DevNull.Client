namespace DevNull.Client.Views

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Threading
open Avalonia.VisualTree
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.ThreadComposer
open DevNull.Client.Components.ThreadVoice
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Natives
open DevNull.Client.RemotesState
open DevNull.Client.Stores

/// The open thread: the detail column's reading pane. The pane itself
/// rebuilds only when the checked-out target moves; everything inside
/// follows the model stream, so a poll refresh never rebuilds a row.
module ThreadPane =

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
    let quick = RemotesRows.quickReactions usage

    {
      Quick = quick
      Rest =
        usage
        |> RemotesRows.frequentReactions
        |> List.except quick
        |> List.distinct
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
            RemotesRows.summarizeReactions(held.Reactions, ctx.ViewerId))
          |> Option.defaultValue []
          |> List.map(fun summary ->
            ({
              Emoji = summary.Emoji
              Count = summary.Count
              Mine = summary.IsMine
              Who =
                RemotesRows.whoNames(Some thread, ctx.ViewerId, summary.Who)
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
                Pinned = RemotesRows.pinnedNow(thread, message)
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
            RemotesRows.statusOf(ctx.IsOrg, ctx.ViewerId, thread))
          |> Option.map _.Id
          |> Option.contains message.Id)
      Receipt =
        live
        |> Observable.map(fun current ->
          current
          |> Option.bind(fun thread ->
            RemotesRows.receiptWord(ctx.IsOrg, thread, message)))
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
            RemotesRows.statusOf(ctx.IsOrg, ctx.ViewerId, thread)))
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

  /// What one thread feed needs: the page context, the thread's key,
  /// the remembered place, and the live thread.
  [<Struct>]
  type FeedAsk = {
    Ctx: RowCtx
    Key: string
    Restore: float
    Thread: IObservable<ThreadState>
  }

  /// The message list: the shared feed, riding the bottom edge, with
  /// the landing token and the feed's tail report.
  let feed(ask: FeedAsk) : ScrollViewer =
    let ctx = ask.Ctx
    let key = ask.Key

    let liveThread =
      ctx.Model
      |> Observable.map(fun current -> current.State.Threads |> Map.tryFind key)
      |> Observable.distinctUntilChanged

    let rowItems =
      ask.Thread
      |> Observable.map(fun current ->
        RemotesRows.threadRows {
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

    let row(item: State.ThreadRow) : Control =
      match item with
      | State.ThreadRow.Day(_, label) -> ThreadMessage.dayRow label
      | State.ThreadRow.Head count -> ThreadMessage.headRow count
      | State.ThreadRow.Event message ->
        ThreadMessage.eventRow(RemotesRows.eventText(ctx.ViewerId, message))
      | State.ThreadRow.Run ofRun ->
        ThreadMessage.run(
          runProps {
            Ctx = ctx
            Thread = liveThread
            Usage = usage
            Run = ofRun
          }
        )

    let landings =
      ctx.Model |> Observable.map _.Landing |> Observable.distinctUntilChanged

    let scrollTo =
      landings
      |> Observable.choose(function
        | Some(State.Message _) -> Some 0.0
        | _ -> None)

    let hasOlder = ask.Thread |> Observable.map _.HasOlder

    let scroller =
      (Feed.view {
        Items = rowItems
        KeyOf = RemotesRows.threadRowKey
        Row = row
        Footer = ValueNone
        HasMore = hasOlder
        ScrollTo = ValueSome scrollTo
        Ride = true
        Restore =
          (if ask.Restore > 0.0 then
             ValueSome ask.Restore
           else
             ValueNone)
        OnScroll =
          ValueSome(fun offset -> ctx.Dispatch(State.ThreadScrolled offset))
        OnRide =
          ValueSome(fun ride ->
            if ride.Offset < 240.0 && ride.Room > 0.0 then
              ctx.Dispatch State.OlderWanted

            ctx.Dispatch(
              State.RideMoved(ride.Riding, ride.FromEnd > ride.Viewport)
            ))
        OnNeedMore = ValueSome(fun () -> ctx.Dispatch State.OlderWanted)
      })
        .Name("RemotesThread")

    // The landing asks: one message a ghost named, or the present
    // edge. The message's scroll rides the feed's token; the flash
    // timer is the pane's own. The listener ends with the feed's
    // detach.
    let land =
      Sub.ofId "landing" (fun _ ->
        landings.Subscribe(function
          | Some(State.Message id) ->
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

  /// The page pieces every pane build shares: the model stream and
  /// the dispatch.
  [<Struct>]
  type PaneCore = {
    Model: IObservable<State.Model>
    Dispatch: Dispatch<State.Msg>
  }

  /// What one pane build picks up: the note stage, the target, and its
  /// key.
  [<Struct>]
  type PaneView = {
    Solo: Solo
    Target: ChatTarget
    Key: string
  }

  /// The thread pane for one target: the identity header, the message
  /// list over its overlays, and the typing line.
  let pane(props: Props<'env>, core: PaneCore, view: PaneView) : Control =
    let model = core.Model
    let dispatch = core.Dispatch
    let solo = view.Solo
    let target = view.Target
    let key = view.Key

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
          feed {
            Ctx = ctx
            Key = key
            Restore = restore
            Thread = thread
          },
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

    let composerServices: ThreadComposer.Services = {
      ViewerId = props.Session.Session.User.Id
      Attachments = props.Env.Attachments
      Audio = props.Env.Natives.Audio
      Http = props.Env.Http
      Settings = props.Env.Settings
      Solo = solo
      Log =
        props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.RemotesVoice"
    }

    let composerActions: ThreadComposer.Actions = {
      DraftChanged =
        fun (text, caret) -> dispatch(State.DraftChanged(text, caret))
      ImageRemoved = fun index -> dispatch(State.ImageRemoved index)
      LinkChosen = fun choice -> dispatch(State.LinkChosen choice)
      ReplyCancelled = fun () -> dispatch State.ReplyCancelled
      Send = fun () -> dispatch State.SendWanted
      PickImages = fun () -> dispatch State.PickWanted
      Picked = fun images -> dispatch(State.Picked images)
      Record = fun () -> dispatch State.RecordWanted
      VoiceStaged = fun staged -> dispatch(State.VoiceStaged staged)
      RecordCancelled = fun () -> dispatch State.RecordCancelled
      VoiceDiscarded = fun () -> dispatch State.VoiceDiscarded
      Ping = fun () -> dispatch State.PingWanted
    }

    let composerFooter =
      ContentControl()
        .Content(
          PageChrome.rebuildOn(
            thread,
            (fun current -> current.Access),
            (fun current ->
              match current.Access with
              | PushOpen ->
                ThreadComposer.view {
                  Services = composerServices
                  Target = target
                  Thread = thread
                  Typing =
                    thread
                    |> Observable.map(fun current ->
                      RemotesRows.typingNames(isOrg, current))
                  Draft = model |> Observable.map _.Draft
                  Images = model |> Observable.map _.Images
                  LinkChoice = model |> Observable.map _.LinkChoice
                  Mention = model |> Observable.map _.Mention
                  Voice = model |> Observable.map _.Voice
                  Recording = model |> Observable.map _.Recording
                  Actions = composerActions
                }
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

    let rows =
      rowsGrid.RowDefinitions <- RowDefinitions("Auto,Auto,Auto,*,Auto")

      rowsGrid.Children(
        header.Row(0).Margin(0.0, 0.0, 0.0, 8.0),
        banner.Row(1),
        noticeLine.Row(2),
        listHost.Row(3),
        composerFooter.Row(4)
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

    let core: PaneCore = { Model = model; Dispatch = dispatch }

    ContentControl()
      .Content(
        checkedOut
        |> Observable.map(function
          | Some target ->
            pane(
              props,
              core,
              {
                Solo = solo
                Target = target
                Key = ChatTargets.key target
              }
            )
          | None -> Parts.noThread())
        |> _.ToBinding()
      )
