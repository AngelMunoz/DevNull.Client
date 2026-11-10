namespace DevNull.Client.Views

open System
open FSharp.Control.Reactive
open FSharp.UMX
open MVUish.Avalonia
open IcedTasks
open DevNull.Client.AttachmentSources
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open Microsoft.Extensions.Logging
open DevNull.Client.Natives
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.Views.State

module CoreRemotes = DevNull.Client.Core.Api.Remotes

module RemotesModel =

  // The search term the raw box text stands for: trimmed, with the
  // leading `@` signs taken off.
  let inline termOf(query: string) : string = query.Trim().TrimStart('@')

  // Whether one tab's list never loaded.
  let inline isLoading(state: RemotesState, tab: InboxTab) : bool =
    (listTab(state, tab)).Items.IsNone

  // One debounced search read: the conversations the term matches in
  // the tab, and the people a new remote can start with while the
  // open tab stands.
  [<Struct>]
  type SearchQuery = {
    Session: SignedInSession
    Tab: InboxTab
    Term: string
  }

  let search
    (
      env:
        #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp,
      query: SearchQuery
    ) : Async<Result<SearchFound, AppError>> = asyncEx {
    do! Async.Sleep 150

    let! page =
      CoreRemotes.inbox(
        env,
        {
          KeyFile = query.Session.KeyFile
          Tab = query.Tab
          Search = Some query.Term
          Before = None
        }
      )

    match page with
    | Error error -> return Error error
    | Ok page ->
      if query.Tab <> TabOpen then
        return
          Ok {
            Term = query.Term
            Conversations = page.Conversations
            Candidates = []
          }
      else
        let! people =
          CoreRemotes.candidates(
            env,
            {
              KeyFile = query.Session.KeyFile
              Search = Some query.Term
              Org = None
              For = Some CoreRemotes.ForRemote
            }
          )

        return
          match people with
          | Error error -> Error error
          | Ok people ->
            Ok {
              Term = query.Term
              Conversations = page.Conversations
              Candidates = List.truncate 5 people
            }
  }

  // One tab's next page, through the store.
  let inline loadTab(store: RemotesStore, tab: InboxTab) : Async<unit> =
    store.LoadTab tab

  // One fetched request settled, through the store.
  let inline settle
    (store: RemotesStore, partner: User, merging: bool)
    : Async<bool> =
    store.SettleRequest(partner, merging)

  // The page above the loaded messages, through the store.
  let inline loadOlder(store: RemotesStore, target: ChatTarget) : Async<unit> =
    store.LoadOlder target

  // Older pages until one message sits in the thread, through the
  // store.
  let inline walkTo
    (store: RemotesStore, target: ChatTarget, messageId: int)
    : Async<bool> =
    store.LoadUntil(target, messageId)

  // One message's link card read again, through the store.
  let inline preview
    (store: RemotesStore, target: ChatTarget, messageId: int)
    : Async<unit> =
    store.RefreshLinkPreview(target, messageId)

  // One thread pulled through its newest message, through the store.
  // Nothing goes out while the reading does not ride the bottom edge
  // or the window stands inactive.
  let inline markRead
    (store: RemotesStore, target: ChatTarget, focused: bool)
    : Async<bool> =
    store.MarkRead(target, focused)

  // The draft one conversation parked, or none.
  let stashedDraft(props: Props<'env>, key: string) : string option =
    props.Env.Settings.Current.ChatDrafts
    |> Map.tryFind key
    |> Option.filter(fun text -> text <> "")

  // One push, through the store.
  let inline push
    (store: RemotesStore, target: ChatTarget, message: OutgoingMessage)
    : Async<unit> =
    store.Push(target, message)

  // One refused send staged again, through the store.
  let inline retry
    (store: RemotesStore, target: ChatTarget, clientId: string<ClientId>)
    : Async<unit> =
    store.Retry(target, clientId)

  let init (props: Props<'env>) () : Model * Cmd<Msg> =
    let requestsOn =
      props.Session.Session.Remotes
      |> Option.map _.Requests
      |> Option.defaultValue false

    let key =
      props.Env.Remotes.Current.OpenTarget
      |> Option.map ChatTargets.key
      |> Option.defaultValue ""

    let draft = stashedDraft(props, key)

    {
      State = props.Env.Remotes.Current
      Tab = TabOpen
      Query = ""
      Found = None
      AskedMore = false
      RequestsOn = requestsOn
      Now = DateTimeOffset.UtcNow
      Riding = true
      Landing = None
      LandingBeat = 0
      MarkedThrough = 0
      DraftKey = key
      Draft = draft |> Option.defaultValue ""
      Mention = None
      Images = []
      LinkChoice = LinkCard.FirstLink
      Voice = None
      Recording = false
      OrgName = ""
      OrgMembers = []
      OrgShare = true
      OrgRename = ""
      Notice = None
      PinAsk = None
      Busy = None
      PinnedPanel = false
      SeenThrough = 0
      Unseen = 0
      Behind = false
      PingHeard = 0
    },
    Cmd.none

  // The open thread's state, when one stands and has loaded.
  let openThread(model: Model) : ThreadState option =
    model.State.OpenTarget
    |> Option.map ChatTargets.key
    |> Option.bind(fun key -> model.State.Threads |> Map.tryFind key)

  // The reading's seen bookkeeping: riding the bottom edge marks
  // everything seen; away from it, arrivals past the mark count as
  // unseen.
  let seenOf(viewerId: string, model: Model) : Model =
    match openThread model, model.Riding with
    | Some thread, true ->
      let latest =
        thread.Messages
        |> List.tryLast
        |> Option.map _.Id
        |> Option.defaultValue 0

      {
        model with
            SeenThrough = latest
            Unseen = 0
      }
    | Some thread, false ->
      let unseen =
        thread.Messages
        |> List.filter(fun message ->
          message.Id > model.SeenThrough
          && message.UserId <> viewerId
          && message.Event.IsNone)
        |> List.length

      { model with Unseen = unseen }
    | None, _ -> { model with Unseen = 0 }

  // The read one riding reader owes the open thread: one pull
  // through the newest message per arrival, never per scroll tick.
  let readCmd(props: Props<'env>, model: Model) : Model * Cmd<Msg> =
    match model.State.OpenTarget with
    | Some target when model.Riding && model.State.WindowActive ->
      let key = ChatTargets.key target

      match model.State.Threads |> Map.tryFind key with
      | Some thread when thread.IsLoaded ->
        let latest =
          thread.Messages
          |> List.tryLast
          |> Option.map _.Id
          |> Option.defaultValue 0

        if latest > model.MarkedThrough then
          { model with MarkedThrough = latest },
          Cmd.OfAsync.perform
            markRead
            (props.Env.Remotes, target, true)
            ReadSettled
        else
          model, Cmd.none
      | _ -> model, Cmd.none
    | _ -> model, Cmd.none

  // The newest ping one thread carries, by its message id.
  let inline lastPingOf(thread: ThreadState) =
    thread.Messages
    |> List.tryFindBack(fun message -> message.Event |> Option.contains Ping)
    |> Option.map _.Id
    |> Option.defaultValue 0

  // A ping that just landed answers with the system's own note: the
  // viewer's own ping sounds even muted, a partner's only while the
  // thread is not — the site's own rule. A ping sounds only in a
  // thread the model already held loaded: pings already there when a
  // thread opens stay still.
  let pingCmd
    (props: Props<'env>, model: Model, next: Model)
    : Model * Cmd<Msg> =
    match next.State.OpenTarget with
    | Some target ->
      let key = ChatTargets.key target

      match next.State.Threads |> Map.tryFind key with
      | Some thread when thread.IsLoaded ->
        let heard = lastPingOf thread

        let heldBefore =
          match model.State.Threads |> Map.tryFind key with
          | Some before -> before.IsLoaded
          | None -> false

        let own =
          thread.Messages
          |> List.tryFind(fun message -> message.Id = heard)
          |> Option.map(fun message ->
            message.UserId = props.Session.Session.User.Id)
          |> Option.defaultValue false

        let muted =
          thread.MutedUntilAt
          |> Option.map(fun until -> until > next.Now)
          |> Option.defaultValue false

        let sound =
          if heldBefore && heard > model.PingHeard then
            match props.Env.Natives.Sounds with
            | ValueSome head when own || not muted ->
              Cmd.OfFunc.attempt
                (fun (sounds: ISystemSounds) -> sounds.Notify())
                head
                (fun failure -> MenuNotice(failure.Message))
            | _ -> Cmd.none
          else
            Cmd.none

        { next with PingHeard = heard }, sound
      | _ -> next, Cmd.none
    | None -> { next with PingHeard = 0 }, Cmd.none

  // Whether the org's held pinned list already stands at the cap the
  // server counts against.
  let inline atPinCap(thread: ThreadState) : bool =
    thread.Pinned |> Option.defaultValue [] |> List.length >= CoreRemotes.pinCap

  // The words one settled pin says: the server's 403 refusal
  // verbatim, the quiet line for every other failure.
  let pinWords(pinning: bool, outcome: Result<unit, AppError>) : string option =
    match outcome with
    | Ok() -> None
    | Error(Refused(403, _, message)) -> Some message
    | Error _ ->
      Some(
        if pinning then
          "Could not pin. Try again."
        else
          "Could not unpin. Try again."
      )

  // One pin through the store, once the dialog or the cap let it
  // pass.
  let startPin
    (props: Props<'env>, model: Model, message: Message)
    : Model * Cmd<Msg> =
    match model.Busy with
    | Some _ -> model, Cmd.none
    | None ->
      match model.State.OpenTarget with
      | Some target ->
        {
          model with
              Notice = None
              Busy = Some PinFlight
        },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, who: ChatTarget, which: Message) ->
            store.PinMessage(who, which))
          (props.Env.Remotes, target, message)
          MessagePinSettled
      | None -> model, Cmd.none

  // One unpin through the store; it never asks first.
  let startUnpin
    (props: Props<'env>, model: Model, message: Message)
    : Model * Cmd<Msg> =
    match model.Busy with
    | Some _ -> model, Cmd.none
    | None ->
      match model.State.OpenTarget with
      | Some target ->
        {
          model with
              Notice = None
              Busy = Some UnpinFlight
        },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, who: ChatTarget, which: Message) ->
            store.UnpinMessage(who, which))
          (props.Env.Remotes, target, message)
          MessageUnpinSettled
      | None -> model, Cmd.none

  let loggerOf(props: Props<'env>) =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Remotes"

  let updateCore
    (log: ILogger, props: Props<'env>)
    (msg: Msg)
    (model: Model)
    : Model * Cmd<Msg> =
    // Every log line is an effect: update returns it as a command,
    // and the loop writes it after the computation.
    let failed what error =
      Cmd.ofAction(fun () ->
        log.LogWarning(
          "The {What} failed: {Error}",
          what,
          bounded 300 (describe error)
        ))

    match msg with
    | StoreChanged state ->
      let next = { model with State = state }

      let list = listTab(state, model.Tab)

      let settled =
        if model.AskedMore && not list.LoadingMore then
          { next with AskedMore = false }
        else
          next

      // A new conversation checks its own draft out of the stash; the
      // restore is silent, the way the site's chat is.
      let keyed =
        state.OpenTarget |> Option.map ChatTargets.key |> Option.defaultValue ""

      if keyed <> model.DraftKey then
        let draft = stashedDraft(props, keyed)

        let carried, reading =
          readCmd(
            props,
            seenOf(
              props.Session.Session.User.Id,
              {
                settled with
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
            )
          )

        let answered, sound = pingCmd(props, model, carried)
        answered, Cmd.batch [ reading; sound ]
      else
        let carried, reading =
          readCmd(props, seenOf(props.Session.Session.User.Id, settled))

        let answered, sound = pingCmd(props, model, carried)
        answered, Cmd.batch [ reading; sound ]
    | ClockTicked moment -> { model with Now = moment }, Cmd.none
    | SessionChanged state ->
      let requestsOn =
        match state with
        | SignedIn entry ->
          entry.Session.Remotes
          |> Option.map _.Requests
          |> Option.defaultValue false
        | SignedOut -> false

      { model with RequestsOn = requestsOn }, Cmd.none
    | TabChosen tab ->
      let next = {
        model with
            Tab = tab
            AskedMore = false
      }

      let load =
        if isLoading(model.State, tab) then
          Cmd.OfAsync.perform loadTab (props.Env.Remotes, tab) (fun () ->
            MoreWanted)
        else
          Cmd.none

      let term = termOf model.Query

      let searchAgain =
        if term = "" then
          Cmd.none
        else
          Cmd.OfAsync.perform
            search
            (props.Env,
             {
               Session = props.Session
               Tab = tab
               Term = term
             })
            (fun result -> SearchLanded(term, result))

      next, Cmd.batch [ load; searchAgain ]
    | Searched text ->
      let term = termOf text

      if Some term = (model.Found |> Option.map _.Term) then
        { model with Query = text }, Cmd.none
      else
        {
          model with
              Query = text
              Found = None
        },
        (if term = "" then
           Cmd.none
         else
           Cmd.OfAsync.perform
             search
             (props.Env,
              {
                Session = props.Session
                Tab = model.Tab
                Term = term
              })
             (fun result -> SearchLanded(term, result)))
    | SearchLanded(term, result) ->
      if term <> termOf model.Query then
        model, Cmd.none
      else

      match result with
      | Ok found -> { model with Found = Some found }, Cmd.none
      | Error error ->
        // A failed search keeps the empty answer: the search's own
        // rows stay quiet rather than noisy. The log keeps the reason.
        {
          model with
              Found =
                Some {
                  Term = term
                  Conversations = []
                  Candidates = []
                }
        },
        failed $"the search for {term}" error
    | MoreWanted ->
      let list = listTab(model.State, model.Tab)

      if
        model.Found.IsSome
        || model.AskedMore
        || not list.HasMore
        || list.LoadingMore
      then
        model, Cmd.none
      else
        { model with AskedMore = true },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, tab: InboxTab) -> store.LoadMore tab)
          (props.Env.Remotes, model.Tab)
          (fun () -> MoreWanted)
    | Opened target ->
      model,
      Cmd.ofAction(fun () -> props.Env.Remotes.SetOpenTarget(Some target))
    | Closed ->
      model, Cmd.ofAction(fun () -> props.Env.Remotes.SetOpenTarget None)
    | SettleWanted(partner, merging) ->
      model,
      Cmd.OfAsync.perform
        settle
        (props.Env.Remotes, partner, merging)
        (fun taken -> Settled taken)
    | Settled _ -> model, Cmd.none
    | OlderWanted ->
      let asks =
        model.State.OpenTarget
        |> Option.bind(fun target ->
          model.State.Threads
          |> Map.tryFind(ChatTargets.key target)
          |> Option.map(fun thread ->
            target,
            thread.IsLoaded && thread.HasOlder && not thread.IsLoadingOlder))

      match asks with
      | Some(target, true) ->
        model,
        Cmd.OfAsync.perform loadOlder (props.Env.Remotes, target) (fun () ->
          StoreWorkDone)
      | _ -> model, Cmd.none
    | RideMoved(riding, behind) ->
      readCmd(
        props,
        seenOf(
          props.Session.Session.User.Id,
          {
            model with
                Riding = riding
                Behind = behind
          }
        )
      )
    | ThreadScrolled offset ->
      model,
      model.State.OpenTarget
      |> Option.map(fun target ->
        Cmd.ofAction(fun () ->
          props.Env.Remotes.RememberScroll(ChatTargets.key target, offset)))
      |> Option.defaultValue Cmd.none
    | GhostWanted messageId ->
      match model.State.OpenTarget with
      | Some target ->
        { model with Landing = None },
        Cmd.OfAsync.perform
          walkTo
          (props.Env.Remotes, target, messageId)
          (fun found -> Walked(messageId, found))
      | None -> model, Cmd.none
    | Walked(messageId, found) ->
      if found then
        let beat = model.LandingBeat + 1

        // No beat starts here: the row may not stand yet, and a slow
        // layout would spend a beat that starts at the ask before the
        // row exists. FlashSeen starts it when the row realizes.
        {
          model with
              LandingBeat = beat
              Landing = Some(Message(messageId, beat))
        },
        Cmd.none
      else
        {
          model with
              Notice = Some "Could not load this message. Try again."
        },
        Cmd.ofAction(fun () ->
          log.LogWarning(
            "The walk to message {Message} found nothing.",
            messageId
          ))
    | FlashSeen landing ->
      if model.Landing = Some landing then
        model,
        Cmd.OfAsync.perform (fun () -> Async.Sleep 1200) () (fun () ->
          Landed landing)
      else
        model, Cmd.none
    | Landed landing ->
      if model.Landing = Some landing then
        { model with Landing = None }, Cmd.none
      else
        model, Cmd.none
    | PresentWanted ->
      {
        model with
            Landing = Some Present
            Riding = true
      },
      Cmd.OfAsync.perform (fun () -> Async.Sleep 1200) () (fun () ->
        Landed Present)
    | PreviewsChecked ->
      let asks =
        model.State.OpenTarget
        |> Option.map(fun target ->
          let thread =
            model.State.Threads
            |> Map.tryFind(ChatTargets.key target)
            |> Option.defaultValue(emptyThread target)

          thread.Messages
          |> List.filter(fun message ->
            message.LinkPreview
            |> Option.map _.IsPending
            |> Option.defaultValue false)
          |> List.map(fun message -> target, message.Id))
        |> Option.defaultValue []

      model,
      Cmd.batch [
        for target, messageId in asks ->
          Cmd.OfAsync.perform
            preview
            (props.Env.Remotes, target, messageId)
            (fun () -> StoreWorkDone)
      ]
    | ReadSettled _ -> model, Cmd.none
    | StoreWorkDone -> model, Cmd.none
    | DraftChanged(text, caret) ->
      let next = {
        model with
            Draft = text
            Mention = RemotesText.mentionOf(text, caret)
      }

      // The typing ping rides the change; the store throttles it.
      let typing =
        model.State.OpenTarget
        |> Option.filter(fun _ -> text.Trim() <> "")
        |> Option.map(fun target ->
          Cmd.OfAsync.perform
            (fun (store: RemotesStore, who: ChatTarget) ->
              store.PingTyping who)
            (props.Env.Remotes, target)
            (fun () -> StoreWorkDone))
        |> Option.defaultValue Cmd.none

      // The stash parks with the change itself: the file's own copy
      // rests inside the store, so a fast section switch — or a page
      // that ends mid-word — never loses what was typed.
      let stash =
        Cmd.ofAction(fun () ->
          props.Env.Settings.SetChatDraft(model.DraftKey, text))

      next, Cmd.batch [ stash; typing ]
    | ImageRemoved index ->
      if index >= 0 && index < List.length model.Images then
        {
          model with
              Images = List.removeAt index model.Images
        },
        Cmd.none
      else
        model, Cmd.none
    | LinkChosen choice -> { model with LinkChoice = choice }, Cmd.none
    | ReplyWanted message ->
      let parent: MessageParent = {
        Id = message.Id
        UserId = message.UserId
        Body = message.Body
        ImagesCount = List.length message.Images
        HasVoice = message.Voice.IsSome
      }

      model,
      model.State.OpenTarget
      |> Option.map(fun target ->
        Cmd.ofAction(fun () ->
          props.Env.Remotes.SetReplyingTo(target, Some parent)))
      |> Option.defaultValue Cmd.none
    | ReplyCancelled ->
      model,
      model.State.OpenTarget
      |> Option.map(fun target ->
        Cmd.ofAction(fun () -> props.Env.Remotes.SetReplyingTo(target, None)))
      |> Option.defaultValue Cmd.none
    | SendWanted ->
      let body = model.Draft.Trim()

      // The site's own rule: a voice note rides alone, so its push
      // carries nothing beside it.
      let ridingAlone =
        model.Voice.IsSome && (body <> "" || not model.Images.IsEmpty)

      let canSend =
        (body <> "" || not model.Images.IsEmpty || model.Voice.IsSome)
        && model.State.OpenTarget.IsSome
        && not ridingAlone

      match model.State.OpenTarget with
      | Some target when canSend ->
        let message: OutgoingMessage = {
          Body = (if body = "" then None else Some body)
          ReplyTo =
            (model.State.Threads
             |> Map.tryFind(ChatTargets.key target)
             |> Option.bind _.ReplyingTo
             |> Option.map _.Id)
          Card = model.LinkChoice
          Images = model.Images
          ImageDescriptions = [ for _ in model.Images -> None ]
          Voice = model.Voice
        }

        {
          model with
              Draft = ""
              Mention = None
              Images = []
              LinkChoice = LinkCard.FirstLink
              Voice = None
        },
        Cmd.batch [
          Cmd.ofAction(fun () ->
            props.Env.Remotes.SetReplyingTo(target, None)
            props.Env.Settings.SetChatDraft(model.DraftKey, ""))

          Cmd.OfAsync.perform
            push
            (props.Env.Remotes, target, message)
            (fun () -> StoreWorkDone)

          Cmd.ofAction(fun () ->
            log.LogInformation(
              "The message for {Key} goes out ({Images} images, {Voice}).",
              ChatTargets.key target,
              List.length model.Images,
              match model.Voice with
              | Some _ -> "voice attached"
              | None -> "no voice"
            ))
        ]
      | Some _ when ridingAlone ->
        {
          model with
              Notice =
                Some "A voice note rides alone. Discard it to type a message."
        },
        Cmd.none
      | _ -> model, Cmd.none
    | RetryWanted clientId ->
      match model.State.OpenTarget with
      | Some target ->
        model,
        Cmd.OfAsync.perform
          retry
          (props.Env.Remotes, target, clientId)
          (fun () -> StoreWorkDone)
      | None -> model, Cmd.none
    | PickWanted ->
      model,
      Cmd.OfAsync.perform
        (fun (sources: IAttachmentSources) -> sources.PickFiles())
        props.Env.Attachments
        (fun picked -> Picked picked)
    | Picked picked ->
      // A voice note rides alone: while one is staged, no image lands.
      if model.Voice.IsSome then
        model, Cmd.none
      else
        {
          model with
              Images = (model.Images @ picked) |> List.truncate 4
        },
        Cmd.none
    | MuteWanted duration ->
      match model.Busy with
      | Some _ -> model, Cmd.none
      | None ->
        match model.State.OpenTarget with
        | Some target ->
          {
            model with
                Notice = None
                Busy = Some MuteFlight
          },
          Cmd.OfAsync.perform
            (fun (store: RemotesStore, who: ChatTarget, how: MuteDuration) ->
              store.SetMuted(who, how))
            (props.Env.Remotes, target, duration)
            ActionSettled
        | None -> model, Cmd.none
    | PinWanted pinned ->
      match model.Busy with
      | Some _ -> model, Cmd.none
      | None ->
        match model.State.OpenTarget with
        | Some target ->
          {
            model with
                Notice = None
                Busy = Some PinFlight
          },
          Cmd.OfAsync.perform
            (fun (store: RemotesStore, who: ChatTarget, pin: bool) ->
              store.SetPinned(who, pin))
            (props.Env.Remotes, target, pinned)
            ActionSettled
        | None -> model, Cmd.none
    | ArchiveWanted archived ->
      match model.Busy with
      | Some _ -> model, Cmd.none
      | None ->
        match model.State.OpenTarget with
        | Some target ->
          {
            model with
                Notice = None
                Busy = Some ArchiveFlight
          },
          Cmd.OfAsync.perform
            (fun (store: RemotesStore, who: ChatTarget, away: bool) ->
              store.SetArchived(who, away))
            (props.Env.Remotes, target, archived)
            (fun moved ->
              // The store already rolled the row back and logged the
              // server's word; the notice says only what happened.
              if moved then
                ActionSettled(Ok())
              else
                ArchiveFailed "Could not update the conversation. Try again.")
        | None -> model, Cmd.none
    | ArchiveFailed message ->
      {
        model with
            Notice = Some message
            Busy = None
      },
      Cmd.none
    | ActionSettled outcome ->
      let next = { model with Busy = None }

      match outcome with
      | Ok() -> { next with Notice = None }, Cmd.none
      | Error error ->
        {
          next with
              Notice = Some(describe error)
        },
        failed "the conversation action" error
    | PinAskWanted message ->
      match model.Busy with
      | Some _ -> model, Cmd.none
      | None ->
        match openThread model with
        | Some held when atPinCap held ->
          // At the cap the action answers with the server's own
          // sentence and never calls.
          {
            model with
                Notice = Some CoreRemotes.pinCapMessage
          },
          Cmd.none
        | Some _ when props.Env.Settings.Current.PinPromptHidden ->
          startPin(props, model, message)
        | _ -> { model with PinAsk = Some message }, Cmd.none
    | PinConfirmed keep ->
      match model.Busy with
      | Some _ -> model, Cmd.none
      | None ->
        let remember =
          if keep then
            Cmd.ofAction(fun () -> props.Env.Settings.SetPinPromptHidden true)
          else
            Cmd.none

        let next, pin =
          match model.PinAsk with
          | Some message ->
            startPin(props, { model with PinAsk = None }, message)
          | None -> { model with PinAsk = None }, Cmd.none

        next, Cmd.batch [ remember; pin ]
    | PinAskCancelled -> { model with PinAsk = None }, Cmd.none
    | MessageUnpinWanted message -> startUnpin(props, model, message)
    | PinnedPanelWanted shown -> { model with PinnedPanel = shown }, Cmd.none
    | MessagePinSettled outcome ->
      {
        model with
            Notice = pinWords(true, outcome)
            Busy = None
      },
      (match outcome with
       | Ok() -> Cmd.none
       | Error error -> failed "the message pin" error)
    | MessageUnpinSettled outcome ->
      {
        model with
            Notice = pinWords(false, outcome)
            Busy = None
      },
      (match outcome with
       | Ok() -> Cmd.none
       | Error error -> failed "the message unpin" error)
    | BlockWanted partner ->
      match model.Busy with
      | Some _ -> model, Cmd.none
      | None ->
        {
          model with
              Notice = None
              Busy = Some BlockFlight
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
          Blocked
    | Blocked outcome ->
      match outcome with
      | Ok _ ->
        {
          model with
              Notice = None
              Busy = None
        },
        Cmd.batch [
          Cmd.ofAction(fun () -> props.Env.Remotes.SetOpenTarget None)
          Cmd.ofAction(fun () -> log.LogInformation("The block changed."))
        ]
      | Error error ->
        {
          model with
              Notice = Some(describe error)
              Busy = None
        },
        failed "the block" error
    | OrgCreateWanted ->
      let name =
        model.OrgName.Trim()
        |> fun trimmed -> if trimmed = "" then None else Some trimmed

      { model with Notice = None },
      Cmd.OfAsync.perform
        (fun (store: RemotesStore, orgName, members) ->
          store.CreateOrg(orgName, members))
        (props.Env.Remotes, name, model.OrgMembers)
        OrgCreated
    | OrgNameChanged name -> { model with OrgName = name }, Cmd.none
    | OrgCreated outcome ->
      match outcome with
      | Ok org ->
        {
          model with
              OrgName = ""
              OrgMembers = []
              Notice = None
        },
        Cmd.batch [
          Cmd.ofAction(fun () -> props.Env.Remotes.SetOpenTarget(Some(Org org)))

          Cmd.ofAction(fun () ->
            log.LogInformation("The org {Org} was created.", org.Id))
        ]
      | Error error ->
        {
          model with
              Notice = Some(describe error)
        },
        failed "the org creation" error
    | OrgRenameWanted ->
      match model.State.OpenTarget with
      | Some(Org org) ->
        let name =
          model.OrgRename.Trim()
          |> fun trimmed -> if trimmed = "" then None else Some trimmed

        {
          model with
              Notice = None
              OrgRename = ""
        },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, which: OrgRef, orgName) ->
            store.RenameOrg(which, orgName))
          (props.Env.Remotes, org, name)
          OrgRenamed
      | _ -> model, Cmd.none
    | OrgRenameChanged name -> { model with OrgRename = name }, Cmd.none
    | OrgRenamed outcome ->
      match outcome with
      | Ok() -> { model with Notice = None }, Cmd.none
      | Error error ->
        {
          model with
              Notice = Some(describe error)
        },
        Cmd.none
    | OrgAddWanted ->
      match model.State.OpenTarget with
      | Some(Org org) ->
        let members =
          model.OrgMembers
          |> List.map _.Trim()
          |> List.filter(fun member' -> member' <> "")

        { model with Notice = None },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, which: OrgRef, added, shared) ->
            store.AddOrgMembers(which, added, shared))
          (props.Env.Remotes, org, members, model.OrgShare)
          OrgMembersAdded
      | _ -> model, Cmd.none
    | OrgMemberChanged username ->
      let members =
        if List.contains username model.OrgMembers then
          List.except [ username ] model.OrgMembers
        else
          model.OrgMembers @ [ username ]

      { model with OrgMembers = members }, Cmd.none
    | OrgShareChanged shared -> { model with OrgShare = shared }, Cmd.none
    | OrgMembersAdded outcome ->
      match outcome with
      | Ok() ->
        {
          model with
              OrgMembers = []
              Notice = None
        },
        Cmd.none
      | Error error ->
        {
          model with
              Notice = Some(describe error)
        },
        Cmd.none
    | OrgRoleWanted(username, role) ->
      match model.State.OpenTarget with
      | Some(Org org) ->
        { model with Notice = None },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, which: OrgRef, member', part) ->
            store.SetOrgMemberRole(which, member', part))
          (props.Env.Remotes, org, username, role)
          OrgRoleSet
      | _ -> model, Cmd.none
    | OrgRoleSet outcome ->
      match outcome with
      | Ok() -> { model with Notice = None }, Cmd.none
      | Error error ->
        {
          model with
              Notice = Some(describe error)
        },
        Cmd.none
    | OrgRemoveWanted(username, leaving) ->
      match model.Busy with
      | Some _ -> model, Cmd.none
      | None ->
        match model.State.OpenTarget with
        | Some(Org org) ->
          {
            model with
                Notice = None
                Busy = Some RemoveFlight
          },
          Cmd.OfAsync.perform
            (fun (store: RemotesStore, which: OrgRef, member', gone) ->
              store.RemoveOrgMember(which, member', gone))
            (props.Env.Remotes, org, username, leaving)
            (fun outcome -> OrgRemoved(leaving, outcome))
        | Some(Partner _)
        | None -> model, Cmd.none
    | OrgRemoved(left, outcome) ->
      match outcome with
      | Ok() ->
        // Leaving takes the org out of the state; the pane steps back
        // to the quiet invitation.
        {
          model with
              Notice = None
              Busy = None
        },
        (if left then
           Cmd.ofAction(fun () -> props.Env.Remotes.SetOpenTarget None)
         else
           Cmd.none)
      | Error error ->
        {
          model with
              Notice = Some(describe error)
              Busy = None
        },
        Cmd.none
    | MenuNotice message -> { model with Notice = Some message }, Cmd.none
    | ReactWanted(messageId, emoji) ->
      match model.State.OpenTarget with
      | Some target ->
        model,
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, who: ChatTarget, id: int, mark: string) ->
            store.ToggleReaction(who, id, mark))
          (props.Env.Remotes, target, messageId, emoji)
          ReactSettled
      | None -> model, Cmd.none
    | ReactSettled _ -> model, Cmd.none
    | RecordWanted -> { model with Recording = true }, Cmd.none
    | RecordCancelled -> { model with Recording = false }, Cmd.none
    | PingWanted ->
      match model.State.OpenTarget with
      | Some target ->
        model,
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
          Pinged
      | None -> model, Cmd.none
    | Pinged outcome ->
      let said, logged =
        match outcome with
        | Ok() ->
          None, Cmd.ofAction(fun () -> log.LogDebug("The ping went out."))
        | Error error -> Some(describe error), failed "the ping" error

      { model with Notice = said }, logged
    | VoiceStaged outcome ->
      match outcome with
      | Ok(voice, waveform) ->
        {
          model with
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
          model with
              Recording = false
              Notice = Some(describe failure)
        },
        Cmd.ofAction(fun () ->
          log.LogWarning(
            "The voice note did not stage: {Error}",
            bounded 300 (describe failure)
          ))
    | VoiceDiscarded -> { model with Voice = None }, Cmd.none

  let update(props: Props<'env>) = updateCore(loggerOf props, props)

  let subscribe (props: Props<'env>) (model: Model) : Sub<Msg> =
    let watcher = props.Env.Remotes.Watcher

    let threadWatch =
      model.State.OpenTarget
      |> Option.map(fun target ->
        Sub.ofId ("watch:" + ChatTargets.key target) (fun _ ->
          watcher.WatchThread target))
      |> Option.toList

    Sub.batch [
      Sub.fromObservable
        "clock"
        (props.Env.UiClock.Minute |> Observable.map ClockTicked)

      Sub.fromObservable
        "session"
        (props.Env.Session.Observe |> Observable.map SessionChanged)

      Sub.fromObservable
        "store"
        (props.Env.Remotes.Observe |> Observable.map StoreChanged)

      yield! threadWatch

      // A card still being read asks again on a slow tick; the sub
      // lives only while a pending card stands, so a settled thread
      // never ticks.
      let previewsPending =
        model.State.OpenTarget
        |> Option.map(fun target ->
          model.State.Threads
          |> Map.tryFind(ChatTargets.key target)
          |> Option.map(fun thread ->
            thread.Messages
            |> List.exists(fun message ->
              message.LinkPreview
              |> Option.map _.IsPending
              |> Option.defaultValue false))
          |> Option.defaultValue false)
        |> Option.defaultValue false

      if previewsPending then
        Sub.interval "link-previews" (TimeSpan.FromSeconds 5.0) (fun () ->
          PreviewsChecked)

      Sub.ofId "inbox" (fun _ -> watcher.WatchInbox())
    ]
