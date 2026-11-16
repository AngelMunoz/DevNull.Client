namespace DevNull.Client.Views

open System
open FSharp.UMX
open MVUish.Avalonia
open Microsoft.Extensions.Logging
open DevNull.Client.AttachmentSources
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.UiClock

module CoreRemotes = DevNull.Client.Core.Api.Remotes

module RemotesThread =

  type UpdateArgs<'env
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp
    and 'env :> IProvideDispatcher
    and 'env :> IProvideSession
    and 'env :> IProvideUiClock
    and 'env :> IProvideImageCache
    and 'env :> IProvideProfiles
    and 'env :> IProvideSettings
    and 'env :> IProvideAttachmentSources
    and 'env :> IProvideNatives
    and 'env :> IProvideRemotes
    and 'env :> IProvideBrowser
    and 'env :> IProvideLogging> = {
    Props: Props<'env>
    Target: ChatTarget
    Msg: State.Msg
    Model: State.ThreadModel
  }

  [<Struct>]
  type Ctx<'env
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp
    and 'env :> IProvideDispatcher
    and 'env :> IProvideSession
    and 'env :> IProvideUiClock
    and 'env :> IProvideImageCache
    and 'env :> IProvideProfiles
    and 'env :> IProvideSettings
    and 'env :> IProvideAttachmentSources
    and 'env :> IProvideNatives
    and 'env :> IProvideRemotes
    and 'env :> IProvideBrowser
    and 'env :> IProvideLogging> = {
    Props: Props<'env>
    Target: ChatTarget
  }

  let inline loadOlder(store: RemotesStore, target: ChatTarget) : Async<unit> =
    store.LoadOlder target

  let inline walkTo
    (store: RemotesStore, target: ChatTarget, messageId: int)
    : Async<bool> =
    store.LoadUntil(target, messageId)

  let inline preview
    (store: RemotesStore, target: ChatTarget, messageId: int)
    : Async<unit> =
    store.RefreshLinkPreview(target, messageId)

  let inline markRead
    (store: RemotesStore, target: ChatTarget, focused: bool)
    : Async<bool> =
    store.MarkRead(target, focused)

  let inline push
    (store: RemotesStore, target: ChatTarget, message: OutgoingMessage)
    : Async<unit> =
    store.Push(target, message)

  let inline retry
    (store: RemotesStore, target: ChatTarget, clientId: string<ClientId>)
    : Async<unit> =
    store.Retry(target, clientId)

  let stashedDraft(props: Props<'env>, key: string) : string option =
    props.Env.Settings.Current.ChatDrafts
    |> Map.tryFind key
    |> Option.filter(fun text -> text <> "")

  let inline liveOf(state: RemotesState, target: ChatTarget) : ThreadState =
    state.Threads
    |> Map.tryFind(ChatTargets.key target)
    |> Option.defaultValue(emptyThread target)

  let inline liveOpt
    (state: RemotesState, target: ChatTarget)
    : ThreadState option =
    state.Threads |> Map.tryFind(ChatTargets.key target)

  let inline lastPingOf(thread: ThreadState) =
    thread.Messages
    |> List.tryFindBack(fun message -> message.Event |> Option.contains Ping)
    |> Option.map _.Id
    |> Option.defaultValue 0

  let inline atPinCap(thread: ThreadState) : bool =
    thread.Pinned |> Option.defaultValue [] |> List.length >= CoreRemotes.pinCap

  let pinWords(pinning: bool, outcome: Result<unit, AppError>) : string option =
    match outcome with
    | Ok() -> None
    | Error(Refused(403, _, message)) -> Some message
    | Error _ ->
      Some(
        match pinning with
        | true -> "Could not pin. Try again."
        | false -> "Could not unpin. Try again."
      )

  let inline loggerOf(props: Props<'env>) =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Remotes"

  let seenOf
    (viewerId: string, live: ThreadState option, thread: State.ThreadModel)
    : State.ThreadModel =
    match live, thread.Riding with
    | Some live, true ->
      let latest =
        live.Messages
        |> List.tryLast
        |> Option.map _.Id
        |> Option.defaultValue 0

      {
        thread with
            SeenThrough = latest
            Unseen = 0
      }
    | Some live, false ->
      let unseen =
        live.Messages
        |> List.filter(fun message ->
          message.Id > thread.SeenThrough
          && message.UserId <> viewerId
          && message.Event.IsNone)
        |> List.length

      { thread with Unseen = unseen }
    | None, _ -> { thread with Unseen = 0 }

  let readCmd
    (ctx: Ctx<'env>, thread: State.ThreadModel)
    : State.ThreadModel * Cmd<State.Msg> =
    let state = ctx.Props.Env.Remotes.Current
    let key = ChatTargets.key ctx.Target

    match state.Threads |> Map.tryFind key with
    | Some live when live.IsLoaded ->
      let latest =
        live.Messages
        |> List.tryLast
        |> Option.map _.Id
        |> Option.defaultValue 0

      match
        latest > thread.MarkedThrough && thread.Riding && state.WindowActive
      with
      | true ->
        { thread with MarkedThrough = latest },
        Cmd.OfAsync.perform
          markRead
          (ctx.Props.Env.Remotes, ctx.Target, true)
          State.ReadSettled
      | false -> thread, Cmd.none
    | _ -> thread, Cmd.none

  let pingCmd
    (ctx: Ctx<'env>, thread: State.ThreadModel)
    : State.ThreadModel * Cmd<State.Msg> =
    let state = ctx.Props.Env.Remotes.Current
    let key = ChatTargets.key ctx.Target
    let now = DateTimeOffset.UtcNow

    match state.Threads |> Map.tryFind key with
    | Some live when live.IsLoaded ->
      let heard = lastPingOf live
      let heldBefore = thread.PingHeard <> 0

      let own =
        live.Messages
        |> List.tryFind(fun message -> message.Id = heard)
        |> Option.map(fun message ->
          message.UserId = ctx.Props.Session.Session.User.Id)
        |> Option.defaultValue false

      let muted =
        live.MutedUntilAt
        |> Option.map(fun until -> until > now)
        |> Option.defaultValue false

      let sound =
        match heldBefore && heard > thread.PingHeard with
        | true ->
          match ctx.Props.Env.Natives.Sounds with
          | ValueSome head when own || not muted ->
            Cmd.OfFunc.attempt
              (fun (sounds: ISystemSounds) -> sounds.Notify())
              head
              (fun failure -> State.MenuNotice failure.Message)
          | _ -> Cmd.none
        | false -> Cmd.none

      { thread with PingHeard = heard }, sound
    | _ -> thread, Cmd.none

  let startPin
    (ctx: Ctx<'env>, thread: State.ThreadModel, message: Message)
    : State.ThreadModel * Cmd<State.Msg> =
    match thread.Busy with
    | Some _ -> thread, Cmd.none
    | None ->
      {
        thread with
            Notice = None
            Busy = Some State.PinFlight
      },
      Cmd.OfAsync.perform
        (fun (store: RemotesStore, who: ChatTarget, which: Message) ->
          store.PinMessage(who, which))
        (ctx.Props.Env.Remotes, ctx.Target, message)
        State.MessagePinSettled

  let startUnpin
    (ctx: Ctx<'env>, thread: State.ThreadModel, message: Message)
    : State.ThreadModel * Cmd<State.Msg> =
    match thread.Busy with
    | Some _ -> thread, Cmd.none
    | None ->
      {
        thread with
            Notice = None
            Busy = Some State.UnpinFlight
      },
      Cmd.OfAsync.perform
        (fun (store: RemotesStore, who: ChatTarget, which: Message) ->
          store.UnpinMessage(who, which))
        (ctx.Props.Env.Remotes, ctx.Target, message)
        State.MessageUnpinSettled

  let update(args: UpdateArgs<'env>) : State.ThreadModel * Cmd<State.Msg> =
    let props = args.Props
    let target = args.Target
    let msg = args.Msg
    let thread = args.Model
    let ctx: Ctx<'env> = { Props = props; Target = target }
    let log = loggerOf props
    let key = ChatTargets.key target
    let store = props.Env.Remotes
    let viewerId = props.Session.Session.User.Id

    let failed what error =
      Cmd.ofAction(fun () ->
        log.LogWarning(
          "The {What} failed: {Error}",
          what,
          bounded 300 (describe error)
        ))

    let same: State.ThreadModel * Cmd<State.Msg> = thread, Cmd.none

    let withThread
      (next: State.ThreadModel, cmd: Cmd<State.Msg>)
      : State.ThreadModel * Cmd<State.Msg> =
      match box next with
      | _ when obj.ReferenceEquals(next, thread) -> thread, cmd
      | _ when next = thread -> thread, cmd
      | _ -> next, cmd

    match msg with
    | State.StoreChanged state ->
      let keyed = key

      let reset =
        match keyed <> thread.DraftKey with
        | true ->
          let draft = stashedDraft(props, keyed)

          {
            thread with
                DraftKey = keyed
                Draft = draft |> Option.defaultValue ""
                Mention = None
                Images = []
                LinkChoice = LinkCard.FirstLink
                Voice = None
                Recording = false
                PinAsk = None
                PinnedPanel = false
          }
        | false -> thread

      let seen = seenOf(viewerId, state.Threads |> Map.tryFind keyed, reset)
      let carried, reading = readCmd({ ctx with Target = target }, seen)

      let answered =
        match state.Threads |> Map.tryFind keyed with
        | Some live when live.IsLoaded ->
          let heard = lastPingOf live
          { carried with PingHeard = heard }
        | _ -> carried

      withThread(answered, reading)
    | State.ClockTicked _ -> same
    | State.SessionChanged _ -> same
    | State.TabChosen _ -> same
    | State.Searched _ -> same
    | State.SearchLanded _ -> same
    | State.MoreWanted -> same
    | State.Opened _ -> same
    | State.Closed -> same
    | State.SettleWanted _ -> same
    | State.Settled _ -> same
    | State.OlderWanted ->
      let asks =
        store.Current.Threads
        |> Map.tryFind key
        |> Option.map(fun live ->
          live.IsLoaded && live.HasOlder && not live.IsLoadingOlder)
        |> Option.defaultValue false

      match asks with
      | true ->
        thread,
        Cmd.OfAsync.perform loadOlder (store, target) (fun () ->
          State.StoreWorkDone)
      | false -> same
    | State.RideMoved(riding, behind) ->
      let moved = {
        thread with
            Riding = riding
            Behind = behind
      }

      let seen =
        seenOf(viewerId, store.Current.Threads |> Map.tryFind key, moved)

      let carried, reading = readCmd(ctx, seen)
      withThread(carried, reading)
    | State.ThreadAnchorMoved anchor ->
      thread, Cmd.ofAction(fun () -> store.RememberPlace(key, anchor))
    | State.GhostWanted messageId ->
      { thread with Landing = None },
      Cmd.OfAsync.perform walkTo (store, target, messageId) (fun found ->
        State.Walked(messageId, found))
    | State.Walked(messageId, found) ->
      match found with
      | true ->
        let beat = thread.LandingBeat + 1

        {
          thread with
              LandingBeat = beat
              Landing = Some(State.Message(messageId, beat))
        },
        Cmd.none
      | false ->
        {
          thread with
              Notice = Some "Could not load this message. Try again."
        },
        Cmd.ofAction(fun () ->
          log.LogWarning(
            "The walk to message {Message} found nothing.",
            messageId
          ))
    | State.FlashSeen landing ->
      match thread.Landing = Some landing with
      | true ->
        thread,
        Cmd.OfAsync.perform (fun () -> Async.Sleep 1200) () (fun () ->
          State.Landed landing)
      | false -> same
    | State.Landed landing ->
      match thread.Landing = Some landing with
      | true -> { thread with Landing = None }, Cmd.none
      | false -> same
    | State.PresentWanted ->
      {
        thread with
            Landing = Some State.Present
            Riding = true
      },
      Cmd.OfAsync.perform (fun () -> Async.Sleep 1200) () (fun () ->
        State.Landed State.Present)
    | State.PreviewsChecked ->
      let live = liveOf(store.Current, target)

      let asks =
        live.Messages
        |> List.filter(fun message ->
          message.LinkPreview
          |> Option.map _.IsPending
          |> Option.defaultValue false)
        |> List.map(fun message -> target, message.Id)

      thread,
      Cmd.batch [
        for target, messageId in asks ->
          Cmd.OfAsync.perform preview (store, target, messageId) (fun () ->
            State.StoreWorkDone)
      ]
    | State.ReadSettled _ -> same
    | State.StoreWorkDone -> same
    | State.DraftChanged(text, caret) ->
      let next = {
        thread with
            Draft = text
            Mention = RemotesText.mentionOf(text, caret)
      }

      let typing =
        match text.Trim() <> "" with
        | true ->
          Cmd.OfAsync.perform
            (fun (store: RemotesStore, who: ChatTarget) -> store.PingTyping who)
            (store, target)
            (fun () -> State.StoreWorkDone)
        | false -> Cmd.none

      let stash =
        Cmd.ofAction(fun () ->
          props.Env.Settings.SetChatDraft(thread.DraftKey, text))

      withThread(next, Cmd.batch [ stash; typing ])
    | State.ImageRemoved index ->
      match index >= 0 && index < List.length thread.Images with
      | true ->
        {
          thread with
              Images = List.removeAt index thread.Images
        },
        Cmd.none
      | false -> same
    | State.LinkChosen choice -> { thread with LinkChoice = choice }, Cmd.none
    | State.ReplyWanted message ->
      let parent: MessageParent = {
        Id = message.Id
        UserId = message.UserId
        Body = message.Body
        ImagesCount = List.length message.Images
        HasVoice = message.Voice.IsSome
      }

      thread, Cmd.ofAction(fun () -> store.SetReplyingTo(target, Some parent))
    | State.ReplyCancelled ->
      thread, Cmd.ofAction(fun () -> store.SetReplyingTo(target, None))
    | State.SendWanted ->
      let body = thread.Draft.Trim()

      let ridingAlone =
        thread.Voice.IsSome && (body <> "" || not thread.Images.IsEmpty)

      let canSend =
        (body <> "" || not thread.Images.IsEmpty || thread.Voice.IsSome)
        && not ridingAlone

      match canSend with
      | false ->
        match ridingAlone with
        | true ->
          {
            thread with
                Notice =
                  Some "A voice note rides alone. Discard it to type a message."
          },
          Cmd.none
        | false -> same
      | true ->
        let replyTo =
          store.Current.Threads
          |> Map.tryFind key
          |> Option.bind _.ReplyingTo
          |> Option.map _.Id

        let message: OutgoingMessage = {
          Body =
            (match body with
             | "" -> None
             | _ -> Some body)
          ReplyTo = replyTo
          Card = thread.LinkChoice
          Images = thread.Images
          ImageDescriptions = [ for _ in thread.Images -> None ]
          Voice = thread.Voice
        }

        let cleared = {
          thread with
              Draft = ""
              Mention = None
              Images = []
              LinkChoice = LinkCard.FirstLink
              Voice = None
        }

        cleared,
        Cmd.batch [
          Cmd.ofAction(fun () ->
            store.SetReplyingTo(target, None)
            props.Env.Settings.SetChatDraft(thread.DraftKey, ""))

          Cmd.OfAsync.perform push (store, target, message) (fun () ->
            State.StoreWorkDone)

          Cmd.ofAction(fun () ->
            log.LogInformation(
              "The message for {Key} goes out ({Images} images, {Voice}).",
              key,
              List.length thread.Images,
              match thread.Voice with
              | Some _ -> "voice attached"
              | None -> "no voice"
            ))
        ]
    | State.RetryWanted clientId ->
      thread,
      Cmd.OfAsync.perform retry (store, target, clientId) (fun () ->
        State.StoreWorkDone)
    | State.PickWanted ->
      thread,
      Cmd.OfAsync.perform
        (fun (sources: IAttachmentSources) -> sources.PickFiles())
        props.Env.Attachments
        (fun picked -> State.Picked picked)
    | State.Picked picked ->
      match thread.Voice with
      | Some _ -> same
      | None ->
        {
          thread with
              Images = (thread.Images @ picked) |> List.truncate 4
        },
        Cmd.none
    | State.MuteWanted duration ->
      match thread.Busy with
      | Some _ -> same
      | None ->
        {
          thread with
              Notice = None
              Busy = Some State.MuteFlight
        },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, who: ChatTarget, how: MuteDuration) ->
            store.SetMuted(who, how))
          (store, target, duration)
          State.ActionSettled
    | State.PinWanted pinned ->
      match thread.Busy with
      | Some _ -> same
      | None ->
        {
          thread with
              Notice = None
              Busy = Some State.PinFlight
        },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, who: ChatTarget, pin: bool) ->
            store.SetPinned(who, pin))
          (store, target, pinned)
          State.ActionSettled
    | State.ArchiveWanted archived ->
      match thread.Busy with
      | Some _ -> same
      | None ->
        {
          thread with
              Notice = None
              Busy = Some State.ArchiveFlight
        },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, who: ChatTarget, away: bool) ->
            store.SetArchived(who, away))
          (store, target, archived)
          (fun moved ->
            match moved with
            | true -> State.ActionSettled(Ok())
            | false ->
              State.ArchiveFailed
                "Could not update the conversation. Try again.")
    | State.ArchiveFailed message ->
      {
        thread with
            Notice = Some message
            Busy = None
      },
      Cmd.none
    | State.ActionSettled outcome ->
      let next = { thread with Busy = None }

      match outcome with
      | Ok() -> { next with Notice = None }, Cmd.none
      | Error error ->
        {
          next with
              Notice = Some(describe error)
        },
        failed "the conversation action" error
    | State.PinAskWanted message ->
      match thread.Busy with
      | Some _ -> same
      | None ->
        match liveOpt(store.Current, target) with
        | Some held when atPinCap held ->
          {
            thread with
                Notice = Some CoreRemotes.pinCapMessage
          },
          Cmd.none
        | Some _ when props.Env.Settings.Current.PinPromptHidden ->
          startPin(ctx, thread, message)
        | _ -> { thread with PinAsk = Some message }, Cmd.none
    | State.PinConfirmed keep ->
      match thread.Busy with
      | Some _ -> same
      | None ->
        let remember =
          match keep with
          | true ->
            Cmd.ofAction(fun () -> props.Env.Settings.SetPinPromptHidden true)
          | false -> Cmd.none

        let next, pin =
          match thread.PinAsk with
          | Some message ->
            startPin(ctx, { thread with PinAsk = None }, message)
          | None -> { thread with PinAsk = None }, Cmd.none

        next, Cmd.batch [ remember; pin ]
    | State.PinAskCancelled -> { thread with PinAsk = None }, Cmd.none
    | State.MessageUnpinWanted message -> startUnpin(ctx, thread, message)
    | State.PinnedPanelWanted shown ->
      { thread with PinnedPanel = shown }, Cmd.none
    | State.MessagePinSettled outcome ->
      {
        thread with
            Notice = pinWords(true, outcome)
            Busy = None
      },
      (match outcome with
       | Ok() -> Cmd.none
       | Error error -> failed "the message pin" error)
    | State.MessageUnpinSettled outcome ->
      {
        thread with
            Notice = pinWords(false, outcome)
            Busy = None
      },
      (match outcome with
       | Ok() -> Cmd.none
       | Error error -> failed "the message unpin" error)
    | State.BlockWanted partner ->
      match thread.Busy with
      | Some _ -> same
      | None ->
        {
          thread with
              Notice = None
              Busy = Some State.BlockFlight
        },
        Cmd.OfAsync.perform
          (fun (env, session: SignedInSession, who: User) -> async {
            let! blocked =
              DevNull.Client.Core.Api.Users.block(
                env,
                {
                  KeyFile = session.KeyFile
                  Username = who.Handle
                }
              )

            return blocked |> Result.map ignore
          })
          (props.Env, props.Session, partner)
          State.Blocked
    | State.Blocked outcome ->
      match outcome with
      | Ok _ ->
        {
          thread with
              Notice = None
              Busy = None
        },
        Cmd.ofAction(fun () -> log.LogInformation "The block changed.")
      | Error error ->
        {
          thread with
              Notice = Some(describe error)
              Busy = None
        },
        failed "the block" error
    | State.OrgCreateWanted ->
      let name =
        thread.OrgName.Trim()
        |> fun trimmed ->
          match trimmed with
          | "" -> None
          | _ -> Some trimmed

      { thread with Notice = None },
      Cmd.OfAsync.perform
        (fun (store: RemotesStore, orgName, members) ->
          store.CreateOrg(orgName, members))
        (store, name, thread.OrgMembers)
        State.OrgCreated
    | State.OrgNameChanged name -> { thread with OrgName = name }, Cmd.none
    | State.OrgCreated outcome ->
      match outcome with
      | Ok org ->
        {
          thread with
              OrgName = ""
              OrgMembers = []
              Notice = None
        },
        Cmd.ofAction(fun () ->
          log.LogInformation("The org {Org} was created.", org.Id))
      | Error error ->
        {
          thread with
              Notice = Some(describe error)
        },
        failed "the org creation" error
    | State.OrgRenameWanted ->
      match target with
      | Org org ->
        let name =
          thread.OrgRename.Trim()
          |> fun trimmed ->
            match trimmed with
            | "" -> None
            | _ -> Some trimmed

        {
          thread with
              Notice = None
              OrgRename = ""
        },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, which: OrgRef, orgName) ->
            store.RenameOrg(which, orgName))
          (store, org, name)
          State.OrgRenamed
      | _ -> same
    | State.OrgRenameChanged name -> { thread with OrgRename = name }, Cmd.none
    | State.OrgRenamed outcome ->
      match outcome with
      | Ok() -> { thread with Notice = None }, Cmd.none
      | Error error ->
        {
          thread with
              Notice = Some(describe error)
        },
        Cmd.none
    | State.OrgAddWanted ->
      match target with
      | Org org ->
        let members =
          thread.OrgMembers
          |> List.map _.Trim()
          |> List.filter(fun m -> m <> "")

        { thread with Notice = None },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, which: OrgRef, added, shared) ->
            store.AddOrgMembers(which, added, shared))
          (store, org, members, thread.OrgShare)
          State.OrgMembersAdded
      | _ -> same
    | State.OrgMemberChanged username ->
      let members =
        match List.contains username thread.OrgMembers with
        | true -> List.except [ username ] thread.OrgMembers
        | false -> thread.OrgMembers @ [ username ]

      { thread with OrgMembers = members }, Cmd.none
    | State.OrgShareChanged shared ->
      { thread with OrgShare = shared }, Cmd.none
    | State.OrgMembersAdded outcome ->
      match outcome with
      | Ok() ->
        {
          thread with
              OrgMembers = []
              Notice = None
        },
        Cmd.none
      | Error error ->
        {
          thread with
              Notice = Some(describe error)
        },
        Cmd.none
    | State.OrgRoleWanted(username, role) ->
      match target with
      | Org org ->
        { thread with Notice = None },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, which: OrgRef, member', part) ->
            store.SetOrgMemberRole(which, member', part))
          (store, org, username, role)
          State.OrgRoleSet
      | _ -> same
    | State.OrgRoleSet outcome ->
      match outcome with
      | Ok() -> { thread with Notice = None }, Cmd.none
      | Error error ->
        {
          thread with
              Notice = Some(describe error)
        },
        Cmd.none
    | State.OrgRemoveWanted(username, leaving) ->
      match thread.Busy with
      | Some _ -> same
      | None ->
        match target with
        | Org org ->
          {
            thread with
                Notice = None
                Busy = Some State.RemoveFlight
          },
          Cmd.OfAsync.perform
            (fun (store: RemotesStore, which: OrgRef, member', gone) ->
              store.RemoveOrgMember(which, member', gone))
            (store, org, username, leaving)
            (fun outcome -> State.OrgRemoved(leaving, outcome))
        | _ -> same
    | State.OrgRemoved(_, outcome) ->
      match outcome with
      | Ok() ->
        {
          thread with
              Notice = None
              Busy = None
        },
        Cmd.none
      | Error error ->
        {
          thread with
              Notice = Some(describe error)
              Busy = None
        },
        Cmd.none
    | State.MenuNotice message ->
      { thread with Notice = Some message }, Cmd.none
    | State.ReactWanted(messageId, emoji) ->
      thread,
      Cmd.OfAsync.perform
        (fun (store: RemotesStore, who: ChatTarget, id: int, mark: string) ->
          store.ToggleReaction(who, id, mark))
        (store, target, messageId, emoji)
        State.ReactSettled
    | State.ReactSettled _ -> same
    | State.RecordWanted -> { thread with Recording = true }, Cmd.none
    | State.RecordCancelled -> { thread with Recording = false }, Cmd.none
    | State.PingWanted ->
      thread,
      Cmd.OfAsync.perform
        (fun (env, session: SignedInSession, who: ChatTarget) -> async {
          let threadTarget =
            match who with
            | Partner user -> CoreRemotes.Partner user.Handle
            | Org org -> CoreRemotes.Org org.Id

          return!
            CoreRemotes.ping(
              env,
              {
                KeyFile = session.KeyFile
                Target = threadTarget
              }
            )
        })
        (props.Env, props.Session, target)
        State.Pinged
    | State.Pinged outcome ->
      let said, logged =
        match outcome with
        | Ok() ->
          None, Cmd.ofAction(fun () -> log.LogDebug "The ping went out.")
        | Error error -> Some(describe error), failed "the ping" error

      { thread with Notice = said }, logged
    | State.VoiceStaged outcome ->
      match outcome with
      | Ok(voice, waveform) ->
        {
          thread with
              Recording = false
              Voice = Some(voice, waveform)
              Notice = None
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The voice note staged ({Bytes} bytes).",
            voice.Bytes.Length
          ))
      | Error failure ->
        {
          thread with
              Recording = false
              Notice = Some(describe failure)
        },
        Cmd.ofAction(fun () ->
          log.LogWarning(
            "The voice note did not stage: {Error}",
            bounded 300 (describe failure)
          ))
    | State.VoiceDiscarded -> { thread with Voice = None }, Cmd.none
