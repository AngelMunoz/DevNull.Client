module DevNull.Client.Remotes

open System
open System.Collections.Generic
open System.Reactive.Linq
open System.Reactive.Subjects
open System.Threading
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

// The poll rates the server throttles for: one thread 1.5 s while it
// stays lively and 4 s once it goes quiet, the inbox 15 s alone and
// 6 s while a thread is open, the typing ping at most every 3 s, and
// a thread counts as lively for 60 s after its last activity.
let activePoll = TimeSpan.FromMilliseconds 1500.0
let idlePoll = TimeSpan.FromMilliseconds 4000.0
let inboxPoll = TimeSpan.FromSeconds 15.0
let busyInboxPoll = TimeSpan.FromSeconds 6.0
let activeWindow = TimeSpan.FromSeconds 60.0
let typingThrottle = TimeSpan.FromSeconds 3.0

/// The capabilities one signed remotes call needs, as one interface
/// the calls' constraints check.
type SignEnv =
  inherit IProvideClock
  inherit IProvideNonceSource
  inherit IProvideSigner
  inherit IProvideHttp

/// The account takes one saved settings answer.
let takeAccount(me: Me, session: SessionStore) : unit =
  session.SignedIn
  |> ValueOption.iter(fun current ->
    session.Refresh {
      current.Session with
          UnreadNotificationsCount = me.UnreadNotificationsCount
          Remotes = me.Remotes
    })

/// One thread page read: which thread, with which credentials, and
/// the window of the poll.
type ThreadRead = {
  Target: ChatTarget
  KeyFile: string
  After: int option
  Before: int option
  ReactionsAfter: int option
}

/// One inbox page read, with whether the pin state moved.
type InboxRead = { Page: Inbox; PinsMoved: bool }

/// One step of a tab rebuild's walk: the rows it gathered, and how
/// many pages it took.
type WalkStep = {
  Rows: Conversation list option
  Pages: int
}

/// One thread poll's ask: the messages after the loaded head, and the
/// reactions changed since the version.
type ThreadCursor = {
  After: int option
  ReactionsAfter: int option
}

/// One optional name as the org keeps it: trimmed, and absent when
/// the trim leaves nothing.
let trimmed(name: string option) : string option =
  name
  |> Option.map _.Trim()
  |> Option.bind(fun trimmed -> if trimmed = "" then None else Some trimmed)

/// One list with the mute of one key's rows replaced.
let mutedOnly
  (
    items: Conversation list option,
    key: string,
    mutedUntil: DateTimeOffset option
  ) : Conversation list option =
  items
  |> Option.map(fun items ->
    items
    |> List.map(fun item ->
      if ChatTargets.summaryKey item = key then
        { item with MutedUntilAt = mutedUntil }
      else
        item))

/// The three tab lists under the saved pins, sorted back into the
/// inbox's order.
let repinLists(state: RemotesState) : RemotesState = {
  state with
      Open = {
        state.Open with
            Items =
              state.Open.Items
              |> Option.map(fun items ->
                withConversationPins(state.PinPreferences, items))
      }
      Fetched = {
        state.Fetched with
            Items =
              state.Fetched.Items
              |> Option.map(fun items ->
                withConversationPins(state.PinPreferences, items))
      }
      Stashed = {
        state.Stashed with
            Items =
              state.Stashed.Items
              |> Option.map(fun items ->
                withConversationPins(state.PinPreferences, items))
      }
}

/// One chat target as the push API addresses it.
module ThreadTarget =

  /// The wire form of one target.
  let ofChat(target: ChatTarget) : CoreRemotes.ThreadTarget =
    match target with
    | Partner user -> CoreRemotes.Partner user.Handle
    | Org org -> CoreRemotes.Org org.Id

/// One thread page read: the call, mapped onto whichever kind of
/// thread it came from.
let readThread
  (signEnv: SignEnv, read: ThreadRead)
  : Async<Result<FreshThread, AppError>> =
  match read.Target with
  | Partner user ->
    CoreRemotes.thread(
      signEnv,
      {
        KeyFile = read.KeyFile
        Partner = user.Handle
        After = read.After
        Before = read.Before
        ReactionsAfter = read.ReactionsAfter
      }
    )
    |> AsyncResult.map DirectFresh
  | Org org ->
    CoreRemotes.orgThread(
      signEnv,
      {
        KeyFile = read.KeyFile
        OrgId = org.Id
        After = read.After
        Before = read.Before
        ReactionsAfter = read.ReactionsAfter
      }
    )
    |> AsyncResult.map OrgFresh

/// One inbox page read, refused when the session holds no key.
let readInbox
  (
    signEnv: SignEnv,
    keyFile: string voption,
    tab: InboxTab,
    before: string option
  ) : Async<Result<Inbox, AppError>> =
  match keyFile with
  | ValueSome keyFile ->
    CoreRemotes.inbox(
      signEnv,
      {
        KeyFile = keyFile
        Tab = tab
        Search = None
        Before = before
      }
    )
  | ValueNone -> async { return Error(SignInFailed "signed out") }

/// How one driver reaches its store: post one pure change, read the
/// state, the session's key file, whether polling may run, the three
/// page callers it drives, and the logger of every failed call.
type Driver = {
  Post: (RemotesState -> RemotesState) -> unit
  Read: unit -> Async<RemotesState>
  KeyFile: unit -> string voption
  CanRun: unit -> bool
  CallInbox: InboxTab * string option -> Async<Result<Inbox, AppError>>
  CallThread: ThreadRead -> Async<Result<FreshThread, AppError>>
  Push:
    PendingSend * CoreRemotes.ThreadTarget -> Async<Result<Message, AppError>>
  Log: ILogger
}

/// One driver's shared moves: posting, version reservation, and
/// revision guards.
module Guards =

  /// Posts one change.
  let post(driver: Driver, change: RemotesState -> RemotesState) : unit =
    driver.Post change

  /// Reserves the preference versions one request takes when it
  /// leaves, so a save that lands while the request is in flight
  /// still writes a newer one.
  let reserveVersions(driver: Driver, state: RemotesState) : VersionPair =
    let versions = {
      Mute = state.MuteVersion + 1
      Pin = state.PinVersion + 1
    }

    post(
      driver,
      fun state -> {
        state with
            MuteVersion = max state.MuteVersion versions.Mute
            PinVersion = max state.PinVersion versions.Pin
      }
    )

    versions

  /// The rows every loaded tab held when a request left.
  let knownRows(state: RemotesState) : Conversation list =
    [
      state.Open.Items |> Option.defaultValue []
      state.Fetched.Items |> Option.defaultValue []
      state.Stashed.Items |> Option.defaultValue []
    ]
    |> List.concat

  /// Runs one change only while the inbox revision still holds, so a
  /// rebuild that superseded this answer drops it.
  let postIfFresh
    (driver: Driver, revision: int, change: RemotesState -> RemotesState)
    : Async<unit> = asyncEx {
    let! current = driver.Read()

    if current.InboxRevision = revision then
      post(driver, change)
  }

  /// Clears one tab's loading flag only while the revision holds.
  let clearLoading(driver: Driver, tab: InboxTab, revision: int) : Async<unit> =
    postIfFresh(
      driver,
      revision,
      withTab tab (fun list -> { list with LoadingMore = false })
    )

/// The inbox drivers: one tab's pages, the poll, and the rebuilds a
/// moved pin forces.
module Inboxes =

  /// One tab's page read, with whether the pin state moved.
  let fetchPage
    (driver: Driver, tab: InboxTab, before: string option)
    : Async<Result<InboxRead, AppError>> = asyncEx {
    let! state = driver.Read()
    let versions = Guards.reserveVersions(driver, state)

    match! driver.CallInbox(tab, before) with
    | Error error -> return Error error
    | Ok page ->
      let known = Guards.knownRows state

      Guards.post(
        driver,
        fun state ->
          mergeInbox(
            state,
            {
              Tab = tab
              Page = page
              Known = known
              Versions = versions
            }
          )
      )

      let! current = driver.Read()

      return
        Ok {
          Page = page
          PinsMoved = pinsMoved(current, page, known)
        }
  }

  /// One finished walk's tail: whether more rows may sit behind it.
  /// The walk carries no page answer of its own, so a failed walk
  /// conservatively says no.
  let walkHasMore(rebuilt: Conversation list option, pages: int) : bool =
    rebuilt.IsSome && pages > 0

  /// One tab's next-page ask: the driver, the tab, the state as the
  /// load read it, the tab's list, and its last row.
  [<Struct>]
  type PageAsk = {
    Driver: Driver
    Tab: InboxTab
    State: RemotesState
    List: InboxList
    Last: Conversation
  }

  /// Rebuilds one tab's loaded depth as one continuous prefix. A pin
  /// that moves mid-rebuild restarts the walk from the first page.
  let rec refreshPages
    (driver: Driver, tab: InboxTab, more: bool)
    : Async<unit> = asyncEx {
    let! state = driver.Read()

    let depth =
      (listTab(state, tab)).Items
      |> Option.map List.length
      |> Option.defaultValue 0

    let revision = state.InboxRevision

    Guards.post(
      driver,
      withTab tab (fun list -> { list with LoadingMore = true })
    )

    // One step of the walk: a page, plus how many pages came before
    // it in this rebuild.
    let rec walk
      (
        before: string option,
        accumulated: Conversation list,
        pages: int,
        pins: string option
      ) : Async<WalkStep> = asyncEx {
      match! fetchPage(driver, tab, before) with
      | Error _ -> return { Rows = None; Pages = pages }
      | Ok read ->
        let! current = driver.Read()

        if current.InboxRevision <> revision then
          return { Rows = None; Pages = pages }
        else
          let snapshot =
            read.Page.Pins |> Map.toList |> List.sortBy fst |> string

          match pins with
          | Some prior when prior <> snapshot -> return! walk(None, [], 0, None)
          | _ ->
            let items = accumulated @ read.Page.Conversations

            let lastId =
              read.Page.Conversations |> List.tryLast |> Option.map _.Id

            let doneWalking =
              not read.Page.HasMore
              || lastId.IsNone
              || List.length items > depth
              || not more && List.length items = depth

            if doneWalking then
              return { Rows = Some items; Pages = pages + 1 }
            else
              return! walk(lastId, items, pages + 1, Some snapshot)
    }

    let! step = walk(None, [], 0, None)
    let! current = driver.Read()

    if current.InboxRevision = revision then
      Guards.post(
        driver,
        fun state ->
          {
            state with
                InvalidatedTabs = List.except [ tab ] state.InvalidatedTabs
          }
          |> withTab tab (fun list -> {
            list with
                Items =
                  step.Rows
                  |> Option.map(fun items ->
                    withConversationPins(state.PinPreferences, items))
          })
          |> withTab tab (fun list -> {
            list with
                HasMore = walkHasMore(step.Rows, step.Pages)
          })
      )

      do! Guards.clearLoading(driver, tab, revision)
  }

  /// Loads one tab's first page. A slower answer that races a pin
  /// move is dropped and the load runs again.
  and loadTab(driver: Driver, tab: InboxTab) : Async<unit> = asyncEx {
    let! state = driver.Read()
    let revision = state.InboxRevision

    match! fetchPage(driver, tab, None) with
    | Error _ ->
      Guards.post(
        driver,
        withTab tab (fun list -> {
          list with
              Items = Some(list.Items |> Option.defaultValue [])
        })
      )
    | Ok read ->
      let! current = driver.Read()

      if current.InboxRevision <> revision then
        do! loadTab(driver, tab)
      else
        Guards.post(
          driver,
          fun state ->
            {
              state with
                  Requests = Some read.Page.RequestsCount
                  InvalidatedTabs = List.except [ tab ] state.InvalidatedTabs
            }
            |> withTab tab (fun list -> {
              list with
                  Items =
                    Some(
                      withConversationPins(
                        state.PinPreferences,
                        read.Page.Conversations
                      )
                    )
            })
            |> withTab tab (fun list -> {
              list with
                  HasMore = read.Page.HasMore
            })
        )
  }

  /// Loads one tab's next page, once the reader reaches the end of
  /// the loaded rows.
  and loadMore(driver: Driver, tab: InboxTab) : Async<unit> = asyncEx {
    let! state = driver.Read()
    let list = listTab(state, tab)
    let last = list.Items |> Option.bind List.tryLast

    match
      state.InvalidatedTabs |> List.contains tab,
      list.HasMore && not list.LoadingMore,
      last
    with
    | true, _, _ -> do! refreshPages(driver, tab, true)
    | false, true, Some last ->
      do!
        pageAfter {
          Driver = driver
          Tab = tab
          State = state
          List = list
          Last = last
        }
    | _ -> ()
  }

  /// One tab's next page: a moved pin rebuilds the depth instead.
  /// One tab's next page: a moved pin rebuilds the depth instead.
  and pageAfter(ask: PageAsk) : Async<unit> = asyncEx {
    let {
          Driver = driver
          Tab = tab
          State = state
          List = list
          Last = last
        } =
      ask

    let revision = state.InboxRevision

    Guards.post(
      driver,
      withTab tab (fun list -> { list with LoadingMore = true })
    )

    match! fetchPage(driver, tab, Some last.Id) with
    | Ok { PinsMoved = true } ->
      Guards.post(driver, invalidatePins)
      do! Guards.clearLoading(driver, tab, revision)
      do! refreshPages(driver, tab, true)
    | Ok read ->
      do!
        Guards.postIfFresh(
          driver,
          revision,
          fun state ->
            appendInboxPage(
              state,
              {
                Tab = tab
                Page = read.Page
                Known = (list.Items |> Option.defaultValue [])
              }
            )
        )

      do! Guards.clearLoading(driver, tab, revision)
    | Error _ -> do! Guards.clearLoading(driver, tab, revision)
  }

  /// Reads the inbox and reconciles every loaded tab.
  and poll(driver: Driver) : Async<unit> = asyncEx {
    let! state = driver.Read()

    if state.PollingInbox || not state.WindowActive || not(driver.CanRun()) then
      ()
    else
      Guards.post(driver, fun state -> { state with PollingInbox = true })

      try
        match! fetchPage(driver, TabOpen, None) with
        | Error error ->
          driver.Log.LogWarning(
            "The inbox poll failed: {Error}",
            describe error
          )
        | Ok read -> do! reconcile(driver, read)
      finally
        Guards.post(driver, fun state -> { state with PollingInbox = false })
  }

  /// One read poll's reconciliation: the rebuilds a moved pin forces,
  /// the watched aheads, and the counts.
  and reconcile(driver: Driver, read: InboxRead) : Async<unit> = asyncEx {
    if read.PinsMoved then
      Guards.post(driver, invalidatePins)

    let! current = driver.Read()

    let reconcileOpen = current.InvalidatedTabs |> List.contains TabOpen

    for tab in current.InvalidatedTabs do
      do! refreshPages(driver, tab, false)

    let! settled = driver.Read()

    let watched = zeroWatched(settled, read.Page.Conversations)

    Guards.post(
      driver,
      fun state ->
        refreshInbox(
          state,
          {
            Page = read.Page
            Rows = watched.Rows
            Watched = watched.Watched
            ReconcileOpen = reconcileOpen
          }
        )
    )

    let requestsChanged =
      settled.Requests.IsSome
      && Some read.Page.RequestsCount <> settled.Requests

    if requestsChanged then
      do! loadTab(driver, TabFetched)
  }

/// The thread drivers: one thread's polls, older pages, and pushes.
module Threads =

  /// One thread's poll ask: the messages after the loaded head, and
  /// the reactions changed since the version.
  let cursorOf(state: RemotesState, key: string) : ThreadCursor =
    match state.Threads |> Map.tryFind key with
    | Some thread when thread.IsLoaded ->
      let lastId =
        thread.Messages
        |> List.tryLast
        |> Option.map _.Id
        |> Option.defaultValue 0

      {
        After = Some lastId
        ReactionsAfter = thread.ReactionsVersion
      }
    | _ -> { After = None; ReactionsAfter = None }

  /// The page above the loaded messages, under the loader's guards:
  /// the oldest loaded id, when the guards pass.
  let olderPageAsk(state: RemotesState, key: string) : int option =
    match state.Threads |> Map.tryFind key with
    | Some thread when
      thread.IsLoaded
      && not thread.IsLoadingOlder
      && thread.HasOlder
      && not thread.Messages.IsEmpty
      ->
      Some(thread.Messages |> List.head |> fun message -> message.Id)
    | _ -> None

  /// Reads one thread: the messages after the loaded head, plus the
  /// reactions changed since the last version.
  let poll(driver: Driver, target: ChatTarget) : Async<unit> = asyncEx {
    match driver.KeyFile() with
    | ValueNone -> ()
    | ValueSome keyFile ->
      let key = ChatTargets.key target
      let! state = driver.Read()
      let versions = Guards.reserveVersions(driver, state)
      let cursor = cursorOf(state, key)
      let polledAt = DateTimeOffset.UtcNow

      match!
        driver.CallThread {
          Target = target
          KeyFile = keyFile
          After = cursor.After
          Before = None
          ReactionsAfter = cursor.ReactionsAfter
        }
      with
      | Error error ->
        driver.Log.LogWarning("The thread poll failed: {Error}", describe error)
      | Ok page ->
        Guards.post(
          driver,
          fun state ->
            mergeThread(
              state,
              {
                Key = key
                Target = target
                Page = page
                PolledAt = polledAt
                Versions = versions
              }
            )
        )
  }

  /// Loads the page above the loaded messages, under the loader's
  /// guards.
  let loadOlder(driver: Driver, target: ChatTarget) : Async<unit> = asyncEx {
    match driver.KeyFile() with
    | ValueNone -> ()
    | ValueSome keyFile ->
      let key = ChatTargets.key target
      let! state = driver.Read()

      match olderPageAsk(state, key) with
      | None -> ()
      | Some oldest ->
        Guards.post(
          driver,
          updateThread key (fun thread -> { thread with IsLoadingOlder = true })
        )

        match!
          driver.CallThread {
            Target = target
            KeyFile = keyFile
            After = None
            Before = Some oldest
            ReactionsAfter = None
          }
        with
        | Error error ->
          driver.Log.LogWarning(
            "The older page failed: {Error}",
            describe error
          )

          Guards.post(
            driver,
            updateThread key (fun thread -> {
              thread with
                  IsLoadingOlder = false
            })
          )
        | Ok page ->
          Guards.post(driver, fun state -> mergeOlderPage(state, key, page))
  }

  /// One message the server took: the thread takes it and the inbox
  /// row moves to the top.
  let landed
    (driver: Driver, target: ChatTarget, key: string, message: Message)
    : Async<unit> = asyncEx {
    let! state = driver.Read()

    let wasEmpty =
      state.Threads
      |> Map.tryFind key
      |> Option.map _.Messages.IsEmpty
      |> Option.defaultValue true

    if wasEmpty then
      Async.Start(poll(driver, target))

    Guards.post(
      driver,
      fun state ->
        landPush(
          state,
          {
            Key = key
            Target = target
            Message = message
          }
        )
    )
  }

  /// One message the server holds: swapped into the thread, the
  /// staged copy dropped, and the inbox touched.
  let deliver
    (driver: Driver, target: ChatTarget, pending: PendingSend)
    : Async<unit> = asyncEx {
    match driver.KeyFile() with
    | ValueNone -> ()
    | ValueSome _ ->
      let key = ChatTargets.key target

      match! driver.Push(pending, ThreadTarget.ofChat target) with
      | Error error ->
        driver.Log.LogWarning("The push failed: {Error}", describe error)

        Guards.post(
          driver,
          fun state -> refuseSend(state, key, pending.ClientId)
        )
      | Ok message -> do! landed(driver, target, key, message)
  }

/// One org left: its rows and its thread leave the state.
let leaveOrg(state: RemotesState, org: OrgRef) : RemotesState = {
  state with
      Open = {
        state.Open with
            Items = withoutKey(state.Open.Items, "org:" + org.Id)
      }
      Stashed = {
        state.Stashed with
            Items = withoutKey(state.Stashed.Items, "org:" + org.Id)
      }
      Threads = Map.remove ("org:" + org.Id) state.Threads
}

/// One mailbox message: a pure change, or a read of the state.
type Msg =
  | Apply of change: (RemotesState -> RemotesState)
  | Read of reply: AsyncReplyChannel<RemotesState>

/// The workers every signed remotes call needs.
type RemotesWorkers = {
  Clock: IClock
  NonceSource: INonceSource
  Signer: ISigner
  Http: IHttp
}

/// The chat's state machine: one mailbox owns the whole state, every
/// operation awaits its call and posts one pure merge, and the merges
/// live as plain functions in `RemotesState`. The timers that drive
/// the polls live in `RemotesWatcher`; the store never schedules.
type RemotesStore(workers: RemotesWorkers, session: SessionStore, log: ILogger) as this
  =

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

  /// One pure change, applied in order with every other.
  let apply(change: RemotesState -> RemotesState) : unit =
    agent.Post(Apply change)

  /// The state as the mailbox holds it right now.
  let read() : Async<RemotesState> = agent.PostAndAsyncReply Read

  /// One operation's awaited work, with the mailbox drained behind
  /// it: the state a caller reads on return is current.
  let settled(work: Async<'t>) : Async<'t> = asyncEx {
    let! result = work

    do! agent.PostAndAsyncReply(Read) |> Async.Ignore

    return result
  }

  let keyFile() : string voption =
    session.SignedIn |> ValueOption.map _.KeyFile

  let viewerId() : string voption =
    session.SignedIn |> ValueOption.map _.Session.User.Id

  let signEnv = {
    new SignEnv with
      member _.Clock = workers.Clock
      member _.NonceSource = workers.NonceSource
      member _.Signer = workers.Signer
      member _.Http = workers.Http
  }

  let driver: Driver = {
    Post = apply
    Read = read
    KeyFile = keyFile
    CanRun = fun () -> session.SignedIn.IsSome
    CallInbox = fun (tab, before) -> readInbox(signEnv, keyFile(), tab, before)
    CallThread = fun threadRead -> readThread(signEnv, threadRead)
    Push =
      fun (pending, target) ->
        match keyFile() with
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

  let watcher = RemotesWatcher(this, session)

  /// The state the mailbox last published, without a subscription.
  member _.Current: RemotesState = published.Value

  /// Every state change, starting with the current one. Emits on the
  /// UI thread.
  member _.Observe: IObservable<RemotesState> =
    changes.StartWith published.Value

  /// The watcher that drives this store's polls.
  member _.Watcher: RemotesWatcher = watcher

  /// The number of threads a reader holds open.
  member _.WatchedCount: int = published.Value.WatchCounts |> Seq.length

  /// When one thread last carried fresh messages or typing.
  member _.LastActivityAt(target: ChatTarget) : DateTimeOffset voption =
    published.Value.Activity
    |> Map.tryFind(ChatTargets.key target)
    |> ValueOption.ofOption

  /// Records whether the window stands on screen; reading state keys
  /// on it.
  member _.SetWindowActive(active: bool) : unit =
    apply(fun state -> { state with WindowActive = active })

  /// Checks one conversation out: the store remembers it, so a page
  /// that returns reopens it. `None` checks nothing out.
  member _.SetOpenTarget(target: ChatTarget option) : unit =
    apply(fun state ->
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

      { seeded with OpenTarget = target })

  /// Parks one thread's reading place, so a page that returns restores
  /// its scroll.
  member _.RememberScroll(key: string, offset: float) : unit =
    apply(fun state -> {
      state with
          ScrollPlaces =
            (match offset with
             | 0.0 -> Map.remove key state.ScrollPlaces
             | moved -> Map.add key moved state.ScrollPlaces)
    })

  /// Where one thread's reading last stood, or none for a fresh read.
  member _.ScrollPlaceOf(key: string) : float voption =
    published.Value.ScrollPlaces |> Map.tryFind key |> ValueOption.ofOption

  /// Opens one target's slot before anything was written in it, so a
  /// composer can park a draft against a thread that never loaded.
  member _.RememberTarget(target: ChatTarget) : unit =
    let key = ChatTargets.key target

    apply(fun state ->
      if state.Threads |> Map.containsKey key then
        state
      else
        {
          state with
              Threads = Map.add key (emptyThread target) state.Threads
        })

  /// Registers one reader of a thread: the reading baseline moves to
  /// now, and the store counts the thread as read while the handle
  /// lives.
  member _.WatchThread(target: ChatTarget) : IDisposable =
    let key = ChatTargets.key target

    apply(fun state ->
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
      })

    {
      new IDisposable with
        member _.Dispose() =
          apply(fun state ->
            match state.WatchCounts |> Map.tryFind key with
            | Some 1 -> {
                state with
                    WatchCounts = Map.remove key state.WatchCounts
              }
            | Some count -> {
                state with
                    WatchCounts = Map.add key (count - 1) state.WatchCounts
              }
            | None -> state)
    }

  /// Reads one thread: the messages after the loaded head, plus the
  /// reactions changed since the last version.
  member _.PollThread(target: ChatTarget) : Async<unit> =
    settled(Threads.poll(driver, target))

  /// Reads the inbox and reconciles every loaded tab.
  member _.PollInbox() : Async<unit> = settled(Inboxes.poll driver)

  /// Loads one tab's first page.
  member _.LoadTab(tab: InboxTab) : Async<unit> =
    settled(Inboxes.loadTab(driver, tab))

  /// Loads one tab's next page.
  member _.LoadMore(tab: InboxTab) : Async<unit> =
    settled(Inboxes.loadMore(driver, tab))

  /// Rebuilds one tab's loaded depth as one continuous prefix.
  member _.RefreshPages(tab: InboxTab, more: bool) : Async<unit> =
    settled(Inboxes.refreshPages(driver, tab, more))

  /// Loads the page above the loaded messages.
  member _.LoadOlder(target: ChatTarget) : Async<unit> =
    settled(Threads.loadOlder(driver, target))

  /// Loads older pages until one message sits in the thread; answers
  /// whether it was found.
  member _.LoadUntil(target: ChatTarget, messageId: int) : Async<bool> = asyncEx {
    let key = ChatTargets.key target
    let! ct = Async.CancellationToken

    let rec walk() : Async<bool> = asyncEx {
      if ct.IsCancellationRequested then
        return false
      else
        let! state = read()

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
            do! Threads.loadOlder(driver, target)

            let! walked = read()

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

    return! settled(walk())
  }

  /// Shows one message staged at once, then swaps in the stored
  /// message once the server has it.
  member _.Push(target: ChatTarget, message: OutgoingMessage) : Async<unit> =
    asyncEx {
      this.RememberTarget target

      let key = ChatTargets.key target

      let pending: PendingSend = {
        Message = message
        ClientId = ClientIds.fresh()
        CreatedAt = DateTimeOffset.UtcNow
        Rejected = false
      }

      apply(
        updateThread key (fun thread -> {
          thread with
              Pending = thread.Pending @ [ pending ]
        })
      )

      apply(fun state -> {
        state with
            TypingPings = Map.remove key state.TypingPings
      })

      do! settled(Threads.deliver(driver, target, pending))
    }

  /// Sends one refused message again under its own client id, so the
  /// server stores it once at most.
  member _.Retry(target: ChatTarget, clientId: string<ClientId>) : Async<unit> =
    asyncEx {
      let key = ChatTargets.key target
      let! state = read()

      let staged =
        state.Threads
        |> Map.tryFind key
        |> Option.bind(fun thread ->
          thread.Pending |> List.tryFind(fun item -> item.ClientId = clientId))

      match staged with
      | None -> ()
      | Some pending ->
        apply(fun state -> restageSend(state, key, clientId))

        do! settled(Threads.deliver(driver, target, pending))
    }

  /// Toggles the viewer's reaction on one message: the optimistic
  /// change lands at once, and a live poll cannot clobber it while
  /// the request is out.
  member _.ToggleReaction
    (target: ChatTarget, messageId: int, emoji: string)
    : Async<bool> = asyncEx {
    match keyFile(), viewerId() with
    | ValueSome keyFile, ValueSome userId ->
      let key = ChatTargets.key target
      let! state = read()

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

        apply(fun state -> reactOptimistically(state, toggle))

        let call: CoreRemotes.ReactionRequest = {
          KeyFile = keyFile
          MessageId = messageId
          Emoji = emoji
        }

        let! outcome: Result<Reaction list, AppError> =
          if removing then
            CoreRemotes.unreact(signEnv, call)
          else
            CoreRemotes.react(signEnv, call)

        match outcome with
        | Ok reactions ->
          apply(fun state ->
            settleReaction(
              state,
              {
                MessageId = messageId
                Reactions = reactions
              }
            ))

          return! settled(asyncEx { return true })
        | Error error ->
          driver.Log.LogWarning("The reaction failed: {Error}", describe error)

          apply(fun state -> revertReaction(state, toggle))

          return! settled(asyncEx { return false })
    | _ -> return false
  }

  /// The reply slot of one thread: the message the composer answers.
  member _.SetReplyingTo
    (target: ChatTarget, reply: MessageParent option)
    : unit =
    apply(
      updateThread (ChatTargets.key target) (fun thread -> {
        thread with
            ReplyingTo = reply
      })
    )

  /// Pulls one thread through its newest message. Nothing is sent
  /// while `focused` is false; the ahead count and the unread total
  /// fall at once. Answers whether a read went out.
  member _.MarkRead(target: ChatTarget, focused: bool) : Async<bool> = asyncEx {
    if not focused then
      return false
    else
      let key = ChatTargets.key target
      let! state = read()

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
        apply(fun state -> markThreadRead(state, key))

        for keyFile in keyFile() |> ValueOption.toList do
          asyncEx {
            match!
              CoreRemotes.markRead(
                signEnv,
                {
                  KeyFile = keyFile
                  Target = ThreadTarget.ofChat target
                  Through = latest
                }
              )
            with
            | Ok() -> ()
            | Error error ->
              driver.Log.LogWarning("The read failed: {Error}", describe error)
          }
          |> Async.Start

        return! settled(asyncEx { return true })
  }

  /// Says the user is typing, at most once every three seconds.
  member _.PingTyping(target: ChatTarget) : Async<unit> =
    settled(
      asyncEx {
        match keyFile() |> ValueOption.toList with
        | [ keyFile ] ->
          let key = ChatTargets.key target
          let now = DateTimeOffset.UtcNow
          let! state = read()

          let throttled =
            state.TypingPings
            |> Map.tryFind key
            |> Option.map(fun last -> now - last < typingThrottle)
            |> Option.defaultValue false

          if not throttled then
            apply(fun state -> {
              state with
                  TypingPings = Map.add key now state.TypingPings
            })

            asyncEx {
              match!
                CoreRemotes.typing(
                  signEnv,
                  {
                    KeyFile = keyFile
                    Target = ThreadTarget.ofChat target
                  }
                )
              with
              | Ok() -> ()
              | Error error ->
                driver.Log.LogWarning(
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
  member _.SetArchived(target: ChatTarget, archived: bool) : Async<bool> =
    settled(
      asyncEx {
        let key = ChatTargets.key target
        let! before = read()

        let conversation =
          [
            before.Open.Items |> Option.defaultValue []
            before.Stashed.Items |> Option.defaultValue []
          ]
          |> List.concat
          |> List.tryFind(fun item -> ChatTargets.summaryKey item = key)

        Option.iter
          (fun row ->
            apply(fun state ->
              stashMove(
                state,
                {
                  Conversation = row
                  Key = key
                  Archived = archived
                }
              ))

            apply(
              updateThread key (fun thread -> {
                thread with
                    IsArchived = archived
              })
            ))
          conversation

        let! outcome: Result<unit, AppError> =
          match keyFile() with
          | ValueSome keyFile ->
            if archived then
              CoreRemotes.stash(
                signEnv,
                {
                  KeyFile = keyFile
                  Target = ThreadTarget.ofChat target
                }
              )
            else
              CoreRemotes.unstash(
                signEnv,
                {
                  KeyFile = keyFile
                  Target = ThreadTarget.ofChat target
                }
              )
          | ValueNone -> async { return Error(SignInFailed "signed out") }

        match outcome with
        | Error error ->
          driver.Log.LogWarning(
            "The stash move failed: {Error}",
            describe error
          )

          apply(fun state -> {
            state with
                Open = before.Open
                Stashed = before.Stashed
                Unread = before.Unread
          })

          apply(
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
  member _.SetPinned
    (target: ChatTarget, pinned: bool)
    : Async<Result<unit, AppError>> =
    settled(
      asyncEx {
        match keyFile() with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.pin(
              signEnv,
              {
                KeyFile = keyFile
                Target = ThreadTarget.ofChat target
                Pinned = pinned
              }
            )
          with
          | Error error -> return Error error
          | Ok result ->
            let key = ChatTargets.key target

            apply invalidatePins

            apply(fun state ->
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
              }))

            apply repinLists

            let! current = read()

            for tab in current.InvalidatedTabs do
              do! Inboxes.refreshPages(driver, tab, false)

            return Ok()
      }
    )

  /// Mutes one thread for a duration, or unmutes it.
  member _.SetMuted
    (target: ChatTarget, duration: MuteDuration)
    : Async<Result<unit, AppError>> =
    settled(
      asyncEx {
        match keyFile() with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.mute(
              signEnv,
              {
                KeyFile = keyFile
                Target = ThreadTarget.ofChat target
                Duration = duration
              }
            )
          with
          | Error error -> return Error error
          | Ok result ->
            let key = ChatTargets.key target

            apply(fun state ->
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
                            mutedOnly(
                              state.Open.Items,
                              key,
                              result.MutedUntilAt
                            )
                    }
                    Fetched = {
                      state.Fetched with
                          Items =
                            mutedOnly(
                              state.Fetched.Items,
                              key,
                              result.MutedUntilAt
                            )
                    }
                    Stashed = {
                      state.Stashed with
                          Items =
                            mutedOnly(
                              state.Stashed.Items,
                              key,
                              result.MutedUntilAt
                            )
                    }
              }
              |> updateThread key (fun thread -> {
                thread with
                    MutedUntilAt = result.MutedUntilAt
              }))

            return Ok()
      }
    )

  /// Merges one fetched request into the open conversations, or drops
  /// it for good.
  member _.SettleRequest(partner: User, merging: bool) : Async<bool> =
    settled(
      asyncEx {
        match keyFile() with
        | ValueNone -> return false
        | ValueSome keyFile ->
          let key = partner.Handle

          let! outcome: Result<unit, AppError> =
            if merging then
              CoreRemotes.merge(signEnv, { KeyFile = keyFile; Partner = key })
            else
              CoreRemotes.drop(signEnv, { KeyFile = keyFile; Partner = key })

          match outcome with
          | Error error ->
            driver.Log.LogWarning(
              "The request settle failed: {Error}",
              describe error
            )

            return false
          | Ok() ->
            let! state = read()

            let row =
              state.Fetched.Items
              |> Option.defaultValue []
              |> List.tryFind(fun item -> ChatTargets.summaryKey item = key)

            apply(fun state ->
              settleRequestState(
                state,
                {
                  Key = key
                  Row = row
                  Merging = merging
                }
              ))

            apply(
              updateThread key (fun thread -> {
                thread with
                    Access = (if merging then PushOpen else PushWaiting)
              })
            )

            return true
      }
    )

  /// Starts one org with the members picked, and answers it.
  member _.CreateOrg
    (name: string option, members: string list)
    : Async<Result<OrgRef, AppError>> =
    settled(
      asyncEx {
        match keyFile() with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.createOrg(
              signEnv,
              {
                KeyFile = keyFile
                Name = name
                Members = members
              }
            )
          with
          | Error error -> return Error error
          | Ok created ->
            Async.Start(Inboxes.poll driver)

            return Ok { Id = created.Id; Name = trimmed name }
      }
    )

  /// Renames one org, or takes its name away, and refreshes its
  /// thread.
  member _.RenameOrg
    (org: OrgRef, name: string option)
    : Async<Result<unit, AppError>> =
    settled(
      asyncEx {
        match keyFile() with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.renameOrg(
              signEnv,
              {
                KeyFile = keyFile
                OrgId = org.Id
                Name = name
              }
            )
          with
          | Error error -> return Error error
          | Ok() ->
            let renamed = { Id = org.Id; Name = trimmed name }

            apply(
              updateThread ("org:" + org.Id) (fun thread -> {
                thread with
                    Target = Org renamed
              })
            )

            do! this.PollThread(Org org)
            Async.Start(Inboxes.poll driver)

            return Ok()
      }
    )

  /// Adds members to one org, sharing its history or starting fresh.
  member _.AddOrgMembers
    (org: OrgRef, members: string list, shareHistory: bool)
    : Async<Result<unit, AppError>> =
    settled(
      asyncEx {
        match keyFile() with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.addMembers(
              signEnv,
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
            do! this.PollThread(Org org)
            Async.Start(Inboxes.poll driver)

            return Ok()
      }
    )

  /// Changes one member's role.
  member _.SetOrgMemberRole
    (org: OrgRef, username: string, role: OrgRole)
    : Async<Result<unit, AppError>> =
    settled(
      asyncEx {
        match keyFile() with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.setMemberRole(
              signEnv,
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
            do! this.PollThread(Org org)
            Async.Start(Inboxes.poll driver)

            return Ok()
      }
    )

  /// Takes one member out of an org, or leaves it: leaving takes the
  /// org out of the inbox.
  member _.RemoveOrgMember
    (org: OrgRef, username: string, isLeaving: bool)
    : Async<Result<unit, AppError>> =
    settled(
      asyncEx {
        match keyFile() with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.removeMember(
              signEnv,
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
              do! this.PollThread(Org org)
              Async.Start(Inboxes.poll driver)
            else
              apply(fun state -> leaveOrg(state, org))

            return Ok()
      }
    )

  /// Reads the emoji the user reacts with, most used first.
  member _.LoadReactionUsage() : Async<unit> =
    settled(
      asyncEx {
        match published.Value.ReactionUsage, keyFile() with
        | None, ValueSome keyFile ->
          match! CoreRemotes.reactionUsage(signEnv, { KeyFile = keyFile }) with
          | Ok usage ->
            apply(fun state -> {
              state with
                  ReactionUsage = Some usage
            })
          | Error error ->
            driver.Log.LogWarning(
              "The usage read failed: {Error}",
              describe error
            )
        | _ -> ()
      }
    )

  /// Reads one message's link card again, and lands it when the server
  /// has it. A still-pending answer lands nothing, so the reader can
  /// keep asking.
  member _.RefreshLinkPreview
    (target: ChatTarget, messageId: int)
    : Async<unit> =
    settled(
      asyncEx {
        match keyFile() with
        | ValueNone -> return ()
        | ValueSome keyFile ->
          let key = ChatTargets.key target

          let! answer: Result<LinkPreview option, AppError> =
            CoreRemotes.linkPreview(
              signEnv,
              {
                KeyFile = keyFile
                MessageId = messageId
              }
            )

          match answer with
          | Ok(Some preview) ->
            apply(fun state -> landLinkPreview(state, key, messageId, preview))
          | _ -> return ()
      }
    )

  /// Turns the direct messages on or off; the account takes the new
  /// settings.
  member _.SetEnabled(enabled: bool) : Async<Result<unit, AppError>> =
    settled(
      asyncEx {
        match keyFile() with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.setEnabled(
              signEnv,
              { KeyFile = keyFile; Enabled = enabled }
            )
          with
          | Error error -> return Error error
          | Ok me ->
            takeAccount(me, session)

            return Ok()
      }
    )

  /// Saves who can push to the user and whether they share pulls; the
  /// account takes the new settings.
  member _.SetPreferences
    (from: RemotesFrom, pullReceipts: bool)
    : Async<Result<unit, AppError>> =
    settled(
      asyncEx {
        match keyFile() with
        | ValueNone -> return Error(SignInFailed "signed out")
        | ValueSome keyFile ->
          match!
            CoreRemotes.setPreferences(
              signEnv,
              {
                KeyFile = keyFile
                From = from
                PullReceipts = pullReceipts
              }
            )
          with
          | Error error -> return Error error
          | Ok me ->
            takeAccount(me, session)

            return Ok()
      }
    )

/// The chat's scheduling: one reference-counted tick loop per thread,
/// one for the inbox, paused while the window is inactive or the
/// session is signed out.
and RemotesWatcher(store: RemotesStore, session: SessionStore) =

  let active = new BehaviorSubject<bool>(true)

  let usageLoaded = new BehaviorSubject<bool>(false)

  // One cancellation source per thread loop, and the inbox loop's own.
  let loops = Dictionary<string, CancellationTokenSource>()

  // The target each loop polls, so a reopened gate can wake it.
  let watched = Dictionary<string, ChatTarget>()

  let inboxLoop = new BehaviorSubject<CancellationTokenSource option>(None)

  let threadCount() : int = loops.Count

  /// The pause every tick consults.
  let canRun() : bool = active.Value && session.SignedIn.IsSome

  /// One thread's tick delay: fast while the thread stays lively,
  /// slow once it goes quiet.
  let delayOf(target: ChatTarget) : TimeSpan =
    match store.LastActivityAt target with
    | ValueSome at when DateTimeOffset.UtcNow - at < activeWindow -> activePoll
    | _ -> idlePoll

  let rec tick(target: ChatTarget) : Async<unit> = asyncEx {
    let! ct = Async.CancellationToken

    if not ct.IsCancellationRequested then
      if canRun() then
        do! store.PollThread target

      do! Async.Sleep(delayOf target)

      return! tick target
  }

  let rec inboxTick() : Async<unit> = asyncEx {
    let! ct = Async.CancellationToken

    if not ct.IsCancellationRequested then
      if canRun() then
        do! store.PollInbox()

      do! Async.Sleep(if threadCount() > 0 then busyInboxPoll else inboxPoll)

      return! inboxTick()
  }

  /// Starts the loops under the activity gate: true while the window
  /// stands on screen. The gate reopening wakes every loop at once —
  /// the way a visible tab polls the moment it returns — instead of
  /// waiting out the sleep each loop was left in. The disposable stops
  /// every loop.
  member _.Start(gate: IObservable<bool>) : IDisposable =
    let subscription =
      gate
      |> Observable.subscribe(fun value ->
        active.OnNext value
        store.SetWindowActive value

        if value then
          for KeyValue(_, target) in watched do
            Async.Start(store.PollThread target)

          Async.Start(store.PollInbox()))

    {
      new IDisposable with
        member _.Dispose() =
          subscription.Dispose()

          for source in loops.Values do
            source.Cancel()
            source.Dispose()

          loops.Clear()
          watched.Clear()

          match inboxLoop.Value with
          | Some source ->
            source.Cancel()
            source.Dispose()

            inboxLoop.OnNext None
          | None -> ()
    }

  /// Registers one reader of a thread; the last release stops its
  /// loop.
  member _.WatchThread(target: ChatTarget) : IDisposable =
    let key = ChatTargets.key target

    let release = store.WatchThread(target)

    let first = not(loops.ContainsKey key)

    if first then
      let source = new CancellationTokenSource()

      loops[key] <- source
      watched[key] <- target
      Async.Start(tick target, source.Token)

      if not usageLoaded.Value then
        usageLoaded.OnNext true

        Async.Start(store.LoadReactionUsage())

    {
      new IDisposable with
        member _.Dispose() =
          release.Dispose()

          match loops.TryGetValue key with
          | true, source ->
            source.Cancel()
            source.Dispose()

            loops.Remove key |> ignore
            watched.Remove key |> ignore
          | false, _ -> ()
    }

  /// Registers one reader of the inbox; the last release stops its
  /// loop.
  member _.WatchInbox() : IDisposable =
    let first = inboxLoop.Value.IsNone

    if first then
      let source = new CancellationTokenSource()

      inboxLoop.OnNext(Some source)
      Async.Start(inboxTick(), source.Token)

    {
      new IDisposable with
        member _.Dispose() =
          match inboxLoop.Value with
          | Some source ->
            source.Cancel()
            source.Dispose()

            inboxLoop.OnNext None
          | None -> ()
    }

/// Accessor for the remotes store capability.
type IProvideRemotes =
  abstract Remotes: RemotesStore
