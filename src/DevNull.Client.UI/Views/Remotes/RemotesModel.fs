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

  let inline threadModelOf(model: Model) : ThreadModel = model.Thread

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

  // The draft one conversation parked, or none. The thread update
  // keeps its own copy; init keeps this one for the first model.
  let stashedDraft(props: Props<'env>, key: string) : string option =
    props.Env.Settings.Current.ChatDrafts
    |> Map.tryFind key
    |> Option.filter(fun text -> text <> "")

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
      Thread = {
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
      }
    },
    Cmd.none

  // The thread arms live in RemotesThread; this page delegates to
  // them and writes the thread fields back.

  // Thread helpers (push, retry, loadOlder, markRead, pin words, org
  // and voice logic) live in RemotesThread now.

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
      let list = listTab(state, model.Tab)

      let settled =
        match model.AskedMore && not list.LoadingMore with
        | true -> {
            model with
                State = state
                AskedMore = false
          }
        | false -> { model with State = state }

      match state.OpenTarget with
      | Some target ->
        let thread, cmds =
          RemotesThread.update {
            Props = props
            Target = target
            Msg = msg
            Model = settled.Thread
          }

        let key = ChatTargets.key target

        let sound =
          match state.Threads |> Map.tryFind key with
          | Some live when live.IsLoaded ->
            let heard =
              live.Messages
              |> List.tryFindBack(fun message ->
                message.Event |> Option.contains Ping)
              |> Option.map _.Id
              |> Option.defaultValue 0

            let heldBefore =
              model.State.Threads
              |> Map.tryFind key
              |> Option.map _.IsLoaded
              |> Option.defaultValue false

            let own =
              live.Messages
              |> List.tryFind(fun message -> message.Id = heard)
              |> Option.map(fun message ->
                message.UserId = props.Session.Session.User.Id)
              |> Option.defaultValue false

            let muted =
              live.MutedUntilAt
              |> Option.map(fun until -> until > model.Now)
              |> Option.defaultValue false

            match heldBefore && heard > model.Thread.PingHeard with
            | true ->
              match props.Env.Natives.Sounds with
              | ValueSome head when own || not muted ->
                Cmd.OfFunc.attempt
                  (fun (sounds: ISystemSounds) -> sounds.Notify())
                  head
                  (fun failure -> MenuNotice failure.Message)
              | _ -> Cmd.none
            | false -> Cmd.none
          | _ -> Cmd.none

        { settled with Thread = thread }, Cmd.batch [ cmds; sound ]
      | None ->
        {
          settled with
              Thread = {
                settled.Thread with
                    Unseen = 0
                    PingHeard = 0
              }
        },
        Cmd.none
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
    | OlderWanted
    | RideMoved _
    | ThreadScrolled _
    | GhostWanted _
    | Walked _
    | FlashSeen _
    | Landed _
    | PresentWanted
    | PreviewsChecked
    | ReadSettled _
    | StoreWorkDone
    | DraftChanged _
    | ImageRemoved _
    | LinkChosen _
    | ReplyWanted _
    | ReplyCancelled
    | SendWanted
    | RetryWanted _
    | PickWanted
    | Picked _
    | MuteWanted _
    | PinWanted _
    | ArchiveWanted _
    | ArchiveFailed _
    | ActionSettled _
    | PinAskWanted _
    | PinConfirmed _
    | PinAskCancelled
    | MessageUnpinWanted _
    | PinnedPanelWanted _
    | MessagePinSettled _
    | MessageUnpinSettled _
    | BlockWanted _
    | Blocked _
    | OrgCreateWanted
    | OrgNameChanged _
    | OrgCreated _
    | OrgRenameWanted
    | OrgRenameChanged _
    | OrgRenamed _
    | OrgAddWanted
    | OrgMemberChanged _
    | OrgShareChanged _
    | OrgMembersAdded _
    | OrgRoleWanted _
    | OrgRoleSet _
    | OrgRemoveWanted _
    | OrgRemoved _
    | MenuNotice _
    | ReactWanted _
    | ReactSettled _
    | RecordWanted
    | RecordCancelled
    | PingWanted
    | Pinged _
    | VoiceStaged _
    | VoiceDiscarded ->
      match model.State.OpenTarget with
      | Some target ->
        let thread, cmds =
          RemotesThread.update {
            Props = props
            Target = target
            Msg = msg
            Model = model.Thread
          }

        let next = { model with Thread = thread }

        let follow =
          match msg with
          | OrgCreated(Ok org) ->
            Cmd.ofAction(fun () ->
              props.Env.Remotes.SetOpenTarget(Some(Org org)))
          | Blocked(Ok _) ->
            Cmd.ofAction(fun () -> props.Env.Remotes.SetOpenTarget None)
          | OrgRemoved(true, Ok _) ->
            Cmd.ofAction(fun () -> props.Env.Remotes.SetOpenTarget None)
          | _ -> Cmd.none

        next, Cmd.batch [ cmds; follow ]
      | None ->
        match msg with
        | OrgCreateWanted
        | OrgNameChanged _
        | OrgCreated _
        | OrgMemberChanged _
        | OrgShareChanged _ ->
          let dummy: ChatTarget = Org { Id = ""; Name = None }

          let thread, cmds =
            RemotesThread.update {
              Props = props
              Target = dummy
              Msg = msg
              Model = model.Thread
            }

          let next = { model with Thread = thread }

          let follow =
            match msg with
            | OrgCreated(Ok org) ->
              Cmd.ofAction(fun () ->
                props.Env.Remotes.SetOpenTarget(Some(Org org)))
            | _ -> Cmd.none

          next, Cmd.batch [ cmds; follow ]
        | _ -> model, Cmd.none

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
