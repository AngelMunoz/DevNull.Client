namespace DevNull.Client.Remotes

open System
open System.Reactive.Linq
open System.Reactive.Subjects
open FSharp.UMX
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging
open Avalonia.Threading
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types
open DevNull.Client.Errors
open DevNull.Client.RemotesState
open DevNull.Client.Stores

module CoreRemotes = DevNull.Client.Core.Api.Remotes

type RemotesWorkers = {
  Clock: IClock
  NonceSource: INonceSource
  Signer: ISigner
  Http: IHttp
}

type RemotesStore =
  abstract Current: RemotesState
  abstract Observe: IObservable<RemotesState>
  abstract Watcher: RemotesWatcher
  abstract WatchedCount: int
  abstract LastActivityAt: target: ChatTarget -> DateTimeOffset voption
  abstract SetWindowActive: active: bool -> unit
  abstract SetOpenTarget: target: ChatTarget option -> unit
  abstract RememberScroll: key: string * offset: float -> unit
  abstract ScrollPlaceOf: key: string -> float voption
  abstract RememberTarget: target: ChatTarget -> unit
  abstract WatchThread: target: ChatTarget -> IDisposable
  abstract PollThread: target: ChatTarget -> Async<unit>
  abstract PollInbox: unit -> Async<unit>
  abstract LoadTab: tab: InboxTab -> Async<unit>
  abstract LoadMore: tab: InboxTab -> Async<unit>
  abstract RefreshPages: tab: InboxTab * more: bool -> Async<unit>
  abstract LoadOlder: target: ChatTarget -> Async<unit>
  abstract LoadUntil: target: ChatTarget * messageId: int -> Async<bool>
  abstract Push: target: ChatTarget * message: OutgoingMessage -> Async<unit>
  abstract Retry: target: ChatTarget * clientId: string<ClientId> -> Async<unit>

  abstract ToggleReaction:
    target: ChatTarget * messageId: int * emoji: string -> Async<bool>

  abstract SetReplyingTo:
    target: ChatTarget * reply: MessageParent option -> unit

  abstract MarkRead: target: ChatTarget * focused: bool -> Async<bool>
  abstract PingTyping: target: ChatTarget -> Async<unit>
  abstract SetArchived: target: ChatTarget * archived: bool -> Async<bool>

  abstract SetPinned:
    target: ChatTarget * pinned: bool -> Async<Result<unit, AppError>>

  abstract PinMessage:
    target: ChatTarget * message: Message -> Async<Result<unit, AppError>>

  abstract UnpinMessage:
    target: ChatTarget * message: Message -> Async<Result<unit, AppError>>

  abstract SetMuted:
    target: ChatTarget * duration: MuteDuration -> Async<Result<unit, AppError>>

  abstract SettleRequest: partner: User * merging: bool -> Async<bool>

  abstract CreateOrg:
    name: string option * members: string list ->
      Async<Result<OrgRef, AppError>>

  abstract RenameOrg:
    org: OrgRef * name: string option -> Async<Result<unit, AppError>>

  abstract AddOrgMembers:
    org: OrgRef * members: string list * shareHistory: bool ->
      Async<Result<unit, AppError>>

  abstract SetOrgMemberRole:
    org: OrgRef * username: string * role: OrgRole ->
      Async<Result<unit, AppError>>

  abstract RemoveOrgMember:
    org: OrgRef * username: string * isLeaving: bool ->
      Async<Result<unit, AppError>>

  abstract LoadReactionUsage: unit -> Async<unit>

  abstract RefreshLinkPreview:
    target: ChatTarget * messageId: int -> Async<unit>

  abstract SetEnabled: enabled: bool -> Async<Result<unit, AppError>>

  abstract SetPreferences:
    from: RemotesFrom * pullReceipts: bool -> Async<Result<unit, AppError>>

type IProvideRemotes =
  abstract Remotes: RemotesStore

/// One mailbox message: a pure change, or a read of the state.
type Msg =
  | Apply of change: (RemotesState -> RemotesState)
  | Read of reply: AsyncReplyChannel<RemotesState>

/// The store's own state: the two subjects, the mailbox, and the
/// driver the wire layer calls.
type State = {
  Published: BehaviorSubject<RemotesState>
  Changes: Subject<RemotesState>
  Agent: MailboxProcessor<Msg>
  Session: SessionStore
  Log: ILogger
  SignEnv: Wire.SignEnv
  Driver: Wire.Driver
}

module RemotesStore =

  let typingThrottle = TimeSpan.FromSeconds 3.0

  /// The state the mailbox last published, without a subscription.
  let current(store: State) : RemotesState = store.Published.Value

  /// Every state change, starting with the current one. Emits on the
  /// UI thread.
  let observe(store: State) : IObservable<RemotesState> =
    store.Changes.StartWith store.Published.Value

  /// The number of threads a reader holds open.
  let watchedCount(store: State) : int =
    store.Published.Value.WatchCounts |> Seq.length

  /// When one thread last carried fresh messages or typing.
  let lastActivityAt
    (store: State, target: ChatTarget)
    : DateTimeOffset voption =
    store.Published.Value.Activity
    |> Map.tryFind(ChatTargets.key target)
    |> ValueOption.ofOption

  /// One pure change, applied in order with every other.
  let apply(store: State, change: RemotesState -> RemotesState) : unit =
    store.Agent.Post(Apply change)

  /// The state as the mailbox holds it right now.
  let read(store: State) : Async<RemotesState> =
    store.Agent.PostAndAsyncReply Read

  /// One operation's awaited work, with the mailbox drained behind
  /// it: the state a caller reads on return is current.
  let settled(store: State, work: Async<'t>) : Async<'t> = asyncEx {
    let! result = work

    do! store.Agent.PostAndAsyncReply(Read) |> Async.Ignore

    return result
  }

  let keyFile(store: State) : string voption =
    store.Session.SignedIn |> ValueOption.map _.KeyFile

  let viewerId(store: State) : string voption =
    store.Session.SignedIn |> ValueOption.map _.Session.User.Id

  /// Records whether the window stands on screen; reading state keys
  /// on it.
  let setWindowActive(store: State, active: bool) : unit =
    apply(store, fun state -> { state with WindowActive = active })

  /// Checks one conversation out: the store remembers it, so a page
  /// that returns reopens it. `None` checks nothing out.
  let setOpenTarget(store: State, target: ChatTarget option) : unit =
    apply(
      store,
      fun state ->
        let seeded =
          match target with
          | Some target when
              not(state.Threads |> Map.containsKey(ChatTargets.key target))
              ->
              {
                state with
                    Threads =
                      Map.add
                        (ChatTargets.key target)
                        (emptyThread target)
                        state.Threads
              }
          | _ -> state

        { seeded with OpenTarget = target }
    )

  /// Parks one thread's reading place, so a page that returns restores
  /// its scroll.
  let rememberScroll(store: State, key: string, offset: float) : unit =
    apply(
      store,
      fun state -> {
        state with
            ScrollPlaces =
              (match offset with
               | 0.0 -> Map.remove key state.ScrollPlaces
               | moved -> Map.add key moved state.ScrollPlaces)
      }
    )

  /// Where one thread's reading last stood, or none for a fresh read.
  let scrollPlaceOf(store: State, key: string) : float voption =
    store.Published.Value.ScrollPlaces
    |> Map.tryFind key
    |> ValueOption.ofOption

  /// Opens one target's slot before anything was written in it, so a
  /// composer can park a draft against a thread that never loaded.
  let rememberTarget(store: State, target: ChatTarget) : unit =
    let key = ChatTargets.key target

    apply(
      store,
      fun state ->
        if state.Threads |> Map.containsKey key then
          state
        else
          {
            state with
                Threads = Map.add key (emptyThread target) state.Threads
          }
    )

  /// Registers one reader of a thread: the reading baseline moves to
  /// now, and the store counts the thread as read while the handle
  /// lives.
  let watchThread(store: State, target: ChatTarget) : IDisposable =
    let key = ChatTargets.key target

    apply(
      store,
      fun state ->
        let counted =
          (state.WatchCounts |> Map.tryFind key |> Option.defaultValue 0) + 1

        let thread =
          state.Threads
          |> Map.tryFind key
          |> Option.defaultWith(fun () -> emptyThread target)

        let head, headUntil =
          if thread.IsLoaded then
            thread.ReadThroughId,
            thread.Messages
            |> List.tryLast
            |> Option.map _.Id
            |> Option.defaultValue 0
          else
            thread.Head, thread.HeadUntil

        {
          state with
              WatchCounts = Map.add key counted state.WatchCounts
              Threads =
                Map.add
                  key
                  {
                    thread with
                        Head = head
                        HeadUntil = headUntil
                  }
                  state.Threads
        }
    )

    {
      new IDisposable with
        member _.Dispose() =
          apply(
            store,
            fun state ->
              match state.WatchCounts |> Map.tryFind key with
              | Some 1 -> {
                  state with
                      WatchCounts = Map.remove key state.WatchCounts
                }
              | Some count -> {
                  state with
                      WatchCounts = Map.add key (count - 1) state.WatchCounts
                }
              | None -> state
          )
    }

  /// Reads one thread: the messages after the loaded head, plus the
  /// reactions changed since the last version.
  let pollThread(store: State, target: ChatTarget) : Async<unit> =
    settled(store, Wire.Threads.poll(store.Driver, target))

  /// Reads the inbox and reconciles every loaded tab.
  let pollInbox(store: State) : Async<unit> =
    settled(store, Wire.Inboxes.poll store.Driver)

  /// Loads one tab's first page.
  let loadTab(store: State, tab: InboxTab) : Async<unit> =
    settled(store, Wire.Inboxes.loadTab(store.Driver, tab))

  /// Loads one tab's next page.
  let loadMore(store: State, tab: InboxTab) : Async<unit> =
    settled(store, Wire.Inboxes.loadMore(store.Driver, tab))

  /// Rebuilds one tab's loaded depth as one continuous prefix.
  let refreshPages(store: State, tab: InboxTab, more: bool) : Async<unit> =
    settled(store, Wire.Inboxes.refreshPages(store.Driver, tab, more))

  /// Loads the page above the loaded messages.
  let loadOlder(store: State, target: ChatTarget) : Async<unit> =
    settled(store, Wire.Threads.loadOlder(store.Driver, target))

  /// Loads older pages until one message sits in the thread; answers
  /// whether it was found.
  let loadUntil
    (store: State, target: ChatTarget, messageId: int)
    : Async<bool> = asyncEx {
    let key = ChatTargets.key target
    let! ct = Async.CancellationToken

    let rec walk() : Async<bool> = asyncEx {
      if ct.IsCancellationRequested then
        return false
      else
        let! state = read store

        match state.Threads |> Map.tryFind key with
        | Some thread when
          thread.Messages |> List.exists(fun message -> message.Id = messageId)
          ->
          return true
        | Some thread ->
          let oldest = thread.Messages |> List.tryHead |> Option.map _.Id

          if
            oldest.IsNone || oldest.Value <= messageId || not thread.HasOlder
          then
            return false
          else
            do! Wire.Threads.loadOlder(store.Driver, target)

            let! walked = read store

            let moved =
              walked.Threads
              |> Map.tryFind key
              |> Option.bind(fun thread ->
                thread.Messages |> List.tryHead |> Option.map _.Id)

            match moved with
            | Some moved when moved <> oldest.Value -> return! walk()
            | _ -> return false
        | None -> return false
    }

    return! settled(store, walk())
  }

  /// Shows one message staged at once, then swaps in the stored
  /// message once the server has it.
  let push
    (store: State, target: ChatTarget, message: OutgoingMessage)
    : Async<unit> = asyncEx {
    rememberTarget(store, target)

    let key = ChatTargets.key target

    let pending: PendingSend = {
      Message = message
      ClientId = ClientIds.fresh()
      CreatedAt = DateTimeOffset.UtcNow
      Rejected = false
    }

    apply(
      store,
      updateThread key (fun thread -> {
        thread with
            Pending = thread.Pending @ [ pending ]
      })
    )

    apply(
      store,
      fun state -> {
        state with
            TypingPings = Map.remove key state.TypingPings
      }
    )

    do! settled(store, Wire.Threads.deliver(store.Driver, target, pending))
  }

  /// Sends one refused message again under its own client id, so the
  /// server stores it once at most.
  let retry
    (store: State, target: ChatTarget, clientId: string<ClientId>)
    : Async<unit> = asyncEx {
    let key = ChatTargets.key target
    let! state = read store

    let staged =
      state.Threads
      |> Map.tryFind key
      |> Option.bind(fun thread ->
        thread.Pending |> List.tryFind(fun item -> item.ClientId = clientId))

    match staged with
    | None -> ()
    | Some pending ->
      store.Log.LogDebug(
        "The message {ClientId} was sent again.",
        UMX.untag clientId
      )

      apply(store, fun state -> restageSend(state, key, clientId))

      do! settled(store, Wire.Threads.deliver(store.Driver, target, pending))
  }

  /// Toggles the viewer's reaction on one message: the optimistic
  /// change lands at once, and a live poll cannot clobber it while
  /// the request is out.
  let toggleReaction
    (store: State, target: ChatTarget, messageId: int, emoji: string)
    : Async<bool> = asyncEx {
    match keyFile store, viewerId store with
    | ValueSome keyFile, ValueSome userId ->
      let key = ChatTargets.key target
      let! state = read store

      let message =
        state.Threads
        |> Map.tryFind key
        |> Option.bind(fun thread ->
          thread.Messages
          |> List.tryFind(fun message -> message.Id = messageId))

      match message with
      | None -> return false
      | Some message ->
        let removing =
          message.Reactions
          |> List.exists(fun reaction ->
            reaction.UserId = userId && reaction.Emoji = emoji)

        let toggle: ReactionToggle = {
          Key = key
          MessageId = messageId
          Emoji = emoji
          UserId = userId
        }

        apply(store, fun state -> reactOptimistically(state, toggle))

        let call: CoreRemotes.ReactionRequest = {
          KeyFile = keyFile
          MessageId = messageId
          Emoji = emoji
        }

        let! outcome: Result<Reaction list, AppError> =
          if removing then
            CoreRemotes.unreact(store.SignEnv, call)
          else
            CoreRemotes.react(store.SignEnv, call)

        match outcome with
        | Ok reactions ->
          apply(
            store,
            fun state ->
              settleReaction(
                state,
                {
                  MessageId = messageId
                  Reactions = reactions
                }
              )
          )

          return! settled(store, asyncEx { return true })
        | Error error ->
          store.Log.LogWarning("The reaction failed: {Error}", describe error)

          apply(store, fun state -> revertReaction(state, toggle))

          return! settled(store, asyncEx { return false })
    | _ -> return false
  }

  /// The reply slot of one thread: the message the composer answers.
  let setReplyingTo
    (store: State, target: ChatTarget, reply: MessageParent option)
    : unit =
    apply(
      store,
      updateThread (ChatTargets.key target) (fun thread -> {
        thread with
            ReplyingTo = reply
      })
    )

  /// Pulls one thread through its newest message. Nothing is sent
  /// while `focused` is false; the ahead count and the unread total
  /// fall at once. Answers whether a read went out.
  let markRead(store: State, target: ChatTarget, focused: bool) : Async<bool> =
    asyncEx {
      if not focused then
        return false
      else
        let key = ChatTargets.key target
        let! state = read store

        let thread =
          state.Threads
          |> Map.tryFind key
          |> Option.defaultWith(fun () -> emptyThread target)

        let latest =
          thread.Messages
          |> List.tryLast
          |> Option.map _.Id
          |> Option.defaultValue 0

        if latest <= thread.ReadThroughId then
          return false
        else
          apply(store, fun state -> markThreadRead(state, key))

          for keyFile in keyFile store |> ValueOption.toList do
            asyncEx {
              match!
                CoreRemotes.markRead(
                  store.SignEnv,
                  {
                    KeyFile = keyFile
                    Target = Wire.ThreadTarget.ofChat target
                    Through = latest
                  }
                )
              with
              | Ok() ->
                store.Log.LogTrace("The read went out through {Id}.", latest)
              | Error error ->
                store.Log.LogWarning("The read failed: {Error}", describe error)
            }
            |> Async.Start

          return! settled(store, asyncEx { return true })
    }

  /// Says the user is typing, at most once every three seconds.
  let pingTyping(store: State, target: ChatTarget) : Async<unit> =
    settled(
      store,
      asyncEx {
        match keyFile store |> ValueOption.toList with
        | [ keyFile ] ->
          let key = ChatTargets.key target
          let now = DateTimeOffset.UtcNow
          let! state = read store

          let throttled =
            state.TypingPings
            |> Map.tryFind key
            |> Option.map(fun last -> now - last < typingThrottle)
            |> Option.defaultValue false

          if not throttled then
            apply(
              store,
              fun state -> {
                state with
                    TypingPings = Map.add key now state.TypingPings
              }
            )

            asyncEx {
              match!
                CoreRemotes.typing(
                  store.SignEnv,
                  {
                    KeyFile = keyFile
                    Target = Wire.ThreadTarget.ofChat target
                  }
                )
              with
              | Ok() -> ()
              | Error error ->
                store.Log.LogWarning(
                  "The typing ping failed: {Error}",
                  describe error
                )
            }
            |> Async.Start
        | _ -> ()
      }
    )

  /// Moves one conversation between the inbox and the stash at once,
  /// and back if the server refuses.
  let setArchived
    (store: State, target: ChatTarget, archived: bool)
    : Async<bool> =
    settled(
      store,
      asyncEx {
        let key = ChatTargets.key target
        let! before = read store

        let conversation =
          [
            before.Open.Items |> Option.defaultValue []
            before.Stashed.Items |> Option.defaultValue []
          ]
          |> List.concat
          |> List.tryFind(fun item -> ChatTargets.summaryKey item = key)

        Option.iter
          (fun row ->
            apply(
              store,
              fun state ->
                stashMove(
                  state,
                  {
                    Conversation = row
                    Key = key
                    Archived = archived
                  }
                )
            )

            apply(
              store,
              updateThread key (fun thread -> {
                thread with
                    IsArchived = archived
              })
            ))
          conversation

        let! outcome: Result<unit, AppError> =
          match keyFile store with
          | ValueSome keyFile ->
            if archived then
              CoreRemotes.stash(
                store.SignEnv,
                {
                  KeyFile = keyFile
                  Target = Wire.ThreadTarget.ofChat target
                }
              )
            else
              CoreRemotes.unstash(
                store.SignEnv,
                {
                  KeyFile = keyFile
                  Target = Wire.ThreadTarget.ofChat target
                }
              )
          | ValueNone -> async { return Error(SignInFailed "signed out") }

        match outcome with
        | Error error ->
          store.Log.LogWarning("The stash move failed: {Error}", describe error)

          apply(
            store,
            fun state -> {
              state with
                  Open = before.Open
                  Stashed = before.Stashed
                  Unread = before.Unread
            }
          )

          apply(
            store,
            updateThread key (fun thread -> {
              thread with
                  IsArchived = not archived
            })
          )

          return false
        | Ok() -> return true
      }
    )

  /// Pins one thread to the top of the inbox, or unpins it, then
  /// rebuilds every tab the move invalidates.
  let setPinned
    (store: State, target: ChatTarget, pinned: bool)
    : Async<Result<unit, AppError>> =
    settled(
      store,
      asyncEx {
        match keyFile store with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.pin(
              store.SignEnv,
              {
                KeyFile = keyFile
                Target = Wire.ThreadTarget.ofChat target
                Pinned = pinned
              }
            )
          with
          | Error error -> return Error error
          | Ok result ->
            let key = ChatTargets.key target

            apply(store, invalidatePins)

            apply(
              store,
              fun state ->
                let version = state.PinVersion + 1

                {
                  state with
                      PinVersion = version
                      PinPreferences =
                        writePreference(
                          state.PinPreferences,
                          {
                            Key = key
                            Value = result.PinnedAt
                            Version = version
                          }
                        )
                }
                |> updateThread key (fun thread -> {
                  thread with
                      PinnedAt = result.PinnedAt
                })
            )

            apply(store, Wire.repinLists)

            let! current = read store

            for tab in current.InvalidatedTabs do
              do! Wire.Inboxes.refreshPages(store.Driver, tab, false)

            return Ok()
      }
    )

  /// Pins one message of an org thread. The answer's whole pinned
  /// list and its version land in one step, so a poll sent before the
  /// pin cannot roll it back.
  let pinMessage
    (store: State, target: ChatTarget, message: Message)
    : Async<Result<unit, AppError>> =
    settled(
      store,
      asyncEx {
        match keyFile store with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.pinMessage(
              store.SignEnv,
              {
                KeyFile = keyFile
                MessageId = message.Id
              }
            )
          with
          | Error error -> return Error error
          | Ok result ->
            Wire.landPins((fun change -> apply(store, change)), target, result)

            return Ok()
      }
    )

  /// Takes one pin off a message of an org thread, the same one-step
  /// landing the pin takes.
  let unpinMessage
    (store: State, target: ChatTarget, message: Message)
    : Async<Result<unit, AppError>> =
    settled(
      store,
      asyncEx {
        match keyFile store with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.unpinMessage(
              store.SignEnv,
              {
                KeyFile = keyFile
                MessageId = message.Id
              }
            )
          with
          | Error error -> return Error error
          | Ok result ->
            Wire.landPins((fun change -> apply(store, change)), target, result)

            return Ok()
      }
    )

  /// Mutes one thread for a duration, or unmutes it.
  let setMuted
    (store: State, target: ChatTarget, duration: MuteDuration)
    : Async<Result<unit, AppError>> =
    settled(
      store,
      asyncEx {
        match keyFile store with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.mute(
              store.SignEnv,
              {
                KeyFile = keyFile
                Target = Wire.ThreadTarget.ofChat target
                Duration = duration
              }
            )
          with
          | Error error -> return Error error
          | Ok result ->
            let key = ChatTargets.key target

            apply(
              store,
              fun state ->
                let version = state.MuteVersion + 1

                {
                  state with
                      MuteVersion = version
                      MutePreferences =
                        writePreference(
                          state.MutePreferences,
                          {
                            Key = key
                            Value = result.MutedUntilAt
                            Version = version
                          }
                        )
                      Open = {
                        state.Open with
                            Items =
                              Wire.mutedOnly(
                                state.Open.Items,
                                key,
                                result.MutedUntilAt
                              )
                      }
                      Fetched = {
                        state.Fetched with
                            Items =
                              Wire.mutedOnly(
                                state.Fetched.Items,
                                key,
                                result.MutedUntilAt
                              )
                      }
                      Stashed = {
                        state.Stashed with
                            Items =
                              Wire.mutedOnly(
                                state.Stashed.Items,
                                key,
                                result.MutedUntilAt
                              )
                      }
                }
                |> updateThread key (fun thread -> {
                  thread with
                      MutedUntilAt = result.MutedUntilAt
                })
            )

            return Ok()
      }
    )

  /// Merges one fetched request into the open conversations, or drops
  /// it for good.
  let settleRequest(store: State, partner: User, merging: bool) : Async<bool> =
    settled(
      store,
      asyncEx {
        match keyFile store with
        | ValueNone -> return false
        | ValueSome keyFile ->
          let key = partner.Handle

          let! outcome: Result<unit, AppError> =
            if merging then
              CoreRemotes.merge(
                store.SignEnv,
                { KeyFile = keyFile; Partner = key }
              )
            else
              CoreRemotes.drop(
                store.SignEnv,
                { KeyFile = keyFile; Partner = key }
              )

          match outcome with
          | Error error ->
            store.Log.LogWarning(
              "The request settle failed: {Error}",
              describe error
            )

            return false
          | Ok() ->
            let! state = read store

            let row =
              state.Fetched.Items
              |> Option.defaultValue []
              |> List.tryFind(fun item -> ChatTargets.summaryKey item = key)

            apply(
              store,
              fun state ->
                settleRequestState(
                  state,
                  {
                    Key = key
                    Row = row
                    Merging = merging
                  }
                )
            )

            apply(
              store,
              updateThread key (fun thread -> {
                thread with
                    Access = (if merging then PushOpen else PushWaiting)
              })
            )

            return true
      }
    )

  /// Starts one org with the members picked, and answers it.
  let createOrg
    (store: State, name: string option, members: string list)
    : Async<Result<OrgRef, AppError>> =
    settled(
      store,
      asyncEx {
        match keyFile store with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.createOrg(
              store.SignEnv,
              {
                KeyFile = keyFile
                Name = name
                Members = members
              }
            )
          with
          | Error error -> return Error error
          | Ok created ->
            Async.Start(Wire.Inboxes.poll store.Driver)

            return
              Ok {
                Id = created.Id
                Name = Wire.trimmed name
              }
      }
    )

  /// Renames one org, or takes its name away, and refreshes its
  /// thread.
  let renameOrg
    (store: State, org: OrgRef, name: string option)
    : Async<Result<unit, AppError>> =
    settled(
      store,
      asyncEx {
        match keyFile store with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.renameOrg(
              store.SignEnv,
              {
                KeyFile = keyFile
                OrgId = org.Id
                Name = name
              }
            )
          with
          | Error error -> return Error error
          | Ok() ->
            let renamed = {
              Id = org.Id
              Name = Wire.trimmed name
            }

            apply(
              store,
              updateThread ("org:" + org.Id) (fun thread -> {
                thread with
                    Target = Org renamed
              })
            )

            do! pollThread(store, Org org)
            Async.Start(Wire.Inboxes.poll store.Driver)

            return Ok()
      }
    )

  /// Adds members to one org, sharing its history or starting fresh.
  let addOrgMembers
    (store: State, org: OrgRef, members: string list, shareHistory: bool)
    : Async<Result<unit, AppError>> =
    settled(
      store,
      asyncEx {
        match keyFile store with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.addMembers(
              store.SignEnv,
              {
                KeyFile = keyFile
                OrgId = org.Id
                Members = members
                ShareHistory = shareHistory
              }
            )
          with
          | Error error -> return Error error
          | Ok() ->
            do! pollThread(store, Org org)
            Async.Start(Wire.Inboxes.poll store.Driver)

            return Ok()
      }
    )

  /// Changes one member's role.
  let setOrgMemberRole
    (store: State, org: OrgRef, username: string, role: OrgRole)
    : Async<Result<unit, AppError>> =
    settled(
      store,
      asyncEx {
        match keyFile store with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.setMemberRole(
              store.SignEnv,
              {
                KeyFile = keyFile
                OrgId = org.Id
                Member = username
                Role = role
              }
            )
          with
          | Error error -> return Error error
          | Ok() ->
            do! pollThread(store, Org org)
            Async.Start(Wire.Inboxes.poll store.Driver)

            return Ok()
      }
    )

  /// Takes one member out of an org, or leaves it: leaving takes the
  /// org out of the inbox.
  let removeOrgMember
    (store: State, org: OrgRef, username: string, isLeaving: bool)
    : Async<Result<unit, AppError>> =
    settled(
      store,
      asyncEx {
        match keyFile store with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.removeMember(
              store.SignEnv,
              {
                KeyFile = keyFile
                OrgId = org.Id
                Member = username
              }
            )
          with
          | Error error -> return Error error
          | Ok() ->
            if not isLeaving then
              do! pollThread(store, Org org)
              Async.Start(Wire.Inboxes.poll store.Driver)
            else
              apply(store, fun state -> Wire.leaveOrg(state, org))

            return Ok()
      }
    )

  /// Reads the emoji the user reacts with, most used first.
  let loadReactionUsage(store: State) : Async<unit> =
    settled(
      store,
      asyncEx {
        match store.Published.Value.ReactionUsage, keyFile store with
        | None, ValueSome keyFile ->
          match!
            CoreRemotes.reactionUsage(store.SignEnv, { KeyFile = keyFile })
          with
          | Ok usage ->
            apply(
              store,
              fun state -> {
                state with
                    ReactionUsage = Some usage
              }
            )
          | Error error ->
            store.Log.LogWarning(
              "The usage read failed: {Error}",
              describe error
            )
        | _ -> ()
      }
    )

  /// Reads one message's link card again, and lands it when the server
  /// has it. A still-pending answer lands nothing, so the reader can
  /// keep asking.
  let refreshLinkPreview
    (store: State, target: ChatTarget, messageId: int)
    : Async<unit> =
    settled(
      store,
      asyncEx {
        match keyFile store with
        | ValueNone -> return ()
        | ValueSome keyFile ->
          let key = ChatTargets.key target

          let! answer: Result<LinkPreview option, AppError> =
            CoreRemotes.linkPreview(
              store.SignEnv,
              {
                KeyFile = keyFile
                MessageId = messageId
              }
            )

          match answer with
          | Ok(Some preview) ->
            apply(
              store,
              fun state ->
                landLinkPreview(
                  state,
                  {
                    Key = key
                    MessageId = messageId
                    Preview = preview
                  }
                )
            )
          | _ -> return ()
      }
    )

  /// Turns the direct messages on or off; the account takes the new
  /// settings.
  let setEnabled(store: State, enabled: bool) : Async<Result<unit, AppError>> =
    settled(
      store,
      asyncEx {
        match keyFile store with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.setEnabled(
              store.SignEnv,
              { KeyFile = keyFile; Enabled = enabled }
            )
          with
          | Error error -> return Error error
          | Ok me ->
            Wire.takeAccount(me, store.Session)

            return Ok()
      }
    )

  /// Saves who can push to the user and whether they share pulls; the
  /// account takes the new settings.
  let setPreferences
    (store: State, from: RemotesFrom, pullReceipts: bool)
    : Async<Result<unit, AppError>> =
    settled(
      store,
      asyncEx {
        match keyFile store with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.setPreferences(
              store.SignEnv,
              {
                KeyFile = keyFile
                From = from
                PullReceipts = pullReceipts
              }
            )
          with
          | Error error -> return Error error
          | Ok me ->
            Wire.takeAccount(me, store.Session)

            return Ok()
      }
    )

  let create
    (workers: RemotesWorkers, session: SessionStore, log: ILogger)
    : RemotesStore =
    // The published snapshot: written only by the mailbox, so `Current`
    // and `Observe` always agree.
    let published = new BehaviorSubject<RemotesState>(emptyState)

    // The change stream: every applied change, on the UI thread.
    let changes = new Subject<RemotesState>()

    // One mailbox owns the whole state: every change is one pure
    // function, applied in order. The loop never awaits a call, so no
    // request ever blocks a state change behind it.
    let agent =
      MailboxProcessor.Start(fun inbox ->
        let rec loop(state: RemotesState) = async {
          let! msg = inbox.Receive()

          match msg with
          | Apply change ->
            let next = change state

            if next <> state then
              published.OnNext next

              Dispatcher.UIThread.Post(fun () -> changes.OnNext next)

            return! loop next
          | Read reply ->
            reply.Reply state
            return! loop state
        }

        loop emptyState)

    let signEnv = {
      new Wire.SignEnv with
        member _.Clock = workers.Clock
        member _.NonceSource = workers.NonceSource
        member _.Signer = workers.Signer
        member _.Http = workers.Http
    }

    let keyFileOf() : string voption =
      session.SignedIn |> ValueOption.map _.KeyFile

    let driver: Wire.Driver = {
      Post = fun change -> agent.Post(Apply change)
      Read = fun () -> agent.PostAndAsyncReply Read
      KeyFile = keyFileOf
      CanRun = fun () -> session.SignedIn.IsSome
      CallInbox =
        fun (tab, before) ->
          Wire.readInbox {
            SignEnv = signEnv
            KeyFile = keyFileOf()
            Tab = tab
            Before = before
          }
      CallThread = fun threadRead -> Wire.readThread(signEnv, threadRead)
      Push =
        fun (pending, target) ->
          match keyFileOf() with
          | ValueSome keyFile ->
            CoreRemotes.push(
              signEnv,
              {
                KeyFile = keyFile
                Target = target
                Body = pending.Message.Body
                ClientId = pending.ClientId
                ReplyTo = pending.Message.ReplyTo
                Card = pending.Message.Card
                Images = pending.Message.Images
                ImageDescriptions = pending.Message.ImageDescriptions
                Voice = pending.Message.Voice
              }
            )
          | ValueNone -> async { return Error(SignInFailed "signed out") }
      Log = log
    }

    log.LogDebug("The remotes store started.")

    let store: State = {
      Published = published
      Changes = changes
      Agent = agent
      Session = session
      Log = log
      SignEnv = signEnv
      Driver = driver
    }

    let watcher =
      RemotesWatcher.create(
        {
          LastActivityAt = fun target -> lastActivityAt(store, target)
          PollThread = fun target -> pollThread(store, target)
          PollInbox = fun () -> pollInbox store
          SetWindowActive = fun active -> setWindowActive(store, active)
          WatchThread = fun target -> watchThread(store, target)
          LoadReactionUsage = fun () -> loadReactionUsage store
        },
        session,
        log
      )

    {
      new RemotesStore with
        member _.Current = current store
        member _.Observe = observe store
        member _.Watcher = watcher
        member _.WatchedCount = watchedCount store
        member _.LastActivityAt(target) = lastActivityAt(store, target)
        member _.SetWindowActive(active) = setWindowActive(store, active)
        member _.SetOpenTarget(target) = setOpenTarget(store, target)

        member _.RememberScroll(key, offset) =
          rememberScroll(store, key, offset)

        member _.ScrollPlaceOf(key) = scrollPlaceOf(store, key)
        member _.RememberTarget(target) = rememberTarget(store, target)
        member _.WatchThread(target) = watchThread(store, target)
        member _.PollThread(target) = pollThread(store, target)
        member _.PollInbox() = pollInbox store
        member _.LoadTab(tab) = loadTab(store, tab)
        member _.LoadMore(tab) = loadMore(store, tab)
        member _.RefreshPages(tab, more) = refreshPages(store, tab, more)
        member _.LoadOlder(target) = loadOlder(store, target)

        member _.LoadUntil(target, messageId) =
          loadUntil(store, target, messageId)

        member _.Push(target, message) = push(store, target, message)
        member _.Retry(target, clientId) = retry(store, target, clientId)

        member _.ToggleReaction(target, messageId, emoji) =
          toggleReaction(store, target, messageId, emoji)

        member _.SetReplyingTo(target, reply) =
          setReplyingTo(store, target, reply)

        member _.MarkRead(target, focused) = markRead(store, target, focused)
        member _.PingTyping(target) = pingTyping(store, target)

        member _.SetArchived(target, archived) =
          setArchived(store, target, archived)

        member _.SetPinned(target, pinned) = setPinned(store, target, pinned)

        member _.PinMessage(target, message) =
          pinMessage(store, target, message)

        member _.UnpinMessage(target, message) =
          unpinMessage(store, target, message)

        member _.SetMuted(target, duration) = setMuted(store, target, duration)

        member _.SettleRequest(partner, merging) =
          settleRequest(store, partner, merging)

        member _.CreateOrg(name, members) = createOrg(store, name, members)
        member _.RenameOrg(org, name) = renameOrg(store, org, name)

        member _.AddOrgMembers(org, members, shareHistory) =
          addOrgMembers(store, org, members, shareHistory)

        member _.SetOrgMemberRole(org, username, role) =
          setOrgMemberRole(store, org, username, role)

        member _.RemoveOrgMember(org, username, isLeaving) =
          removeOrgMember(store, org, username, isLeaving)

        member _.LoadReactionUsage() = loadReactionUsage store

        member _.RefreshLinkPreview(target, messageId) =
          refreshLinkPreview(store, target, messageId)

        member _.SetEnabled(enabled) = setEnabled(store, enabled)

        member _.SetPreferences(from, pullReceipts) =
          setPreferences(store, from, pullReceipts)
    }
