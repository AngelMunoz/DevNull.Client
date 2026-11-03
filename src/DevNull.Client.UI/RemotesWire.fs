namespace DevNull.Client.Remotes

open System
open System.Collections.Generic
open FSharp.UMX
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types
open DevNull.Client.Errors
open DevNull.Client.RemotesState
open DevNull.Client.Stores

module internal Wire =

  module CoreRemotes = DevNull.Client.Core.Api.Remotes

  /// The capabilities one signed remotes call needs, as one interface
  /// the calls' constraints check.
  [<Interface>]
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

  /// One inbox page request: the credentials and key it signs with,
  /// the tab, and the cursor before which the page sits.
  [<Struct>]
  type InboxPageRead = {
    SignEnv: SignEnv
    KeyFile: string voption
    Tab: InboxTab
    Before: string option
  }

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
  let readInbox(read: InboxPageRead) : Async<Result<Inbox, AppError>> =
    match read.KeyFile with
    | ValueSome keyFile ->
      CoreRemotes.inbox(
        read.SignEnv,
        {
          KeyFile = keyFile
          Tab = read.Tab
          Search = None
          Before = read.Before
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
    let clearLoading
      (driver: Driver, tab: InboxTab, revision: int)
      : Async<unit> =
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

    [<Struct>]
    type WalkState = {
      Before: string option
      Rows: Conversation list
      Pages: int
      Pins: string option
    }

    /// Rebuilds one tab's loaded depth as one continuous prefix. A pin
    /// that moves mid-rebuild restarts the walk from the first page.
    [<TailCall>]
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
      // A compiler-checked tail call is not expressible here: the binding
      // sits inside a computation expression, where attributes are illegal.
      // Both recursive arms are trailing `return!`, so the loop is tail by
      // shape; the bound is the page depth.
      let rec walk(step: WalkState) : Async<WalkStep> = asyncEx {
        match! fetchPage(driver, tab, step.Before) with
        | Error _ -> return { Rows = None; Pages = step.Pages }
        | Ok read ->
          let! current = driver.Read()

          if current.InboxRevision <> revision then
            return { Rows = None; Pages = step.Pages }
          else
            let snapshot =
              read.Page.Pins |> Map.toList |> List.sortBy fst |> string

            match step.Pins with
            | Some prior when prior <> snapshot ->
              return!
                walk {
                  Before = None
                  Rows = []
                  Pages = 0
                  Pins = None
                }
            | _ ->
              let items = step.Rows @ read.Page.Conversations

              let lastId =
                read.Page.Conversations |> List.tryLast |> Option.map _.Id

              let doneWalking =
                not read.Page.HasMore
                || lastId.IsNone
                || List.length items > depth
                || not more && List.length items = depth

              if doneWalking then
                return {
                  Rows = Some items
                  Pages = step.Pages + 1
                }
              else
                return!
                  walk {
                    Before = lastId
                    Rows = items
                    Pages = step.Pages + 1
                    Pins = Some snapshot
                  }
      }

      let! step =
        walk {
          Before = None
          Rows = []
          Pages = 0
          Pins = None
        }

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

      if
        state.PollingInbox || not state.WindowActive || not(driver.CanRun())
      then
        driver.Log.LogTrace("The inbox poll waits while its gate holds.")
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
      | ValueNone ->
        driver.Log.LogTrace("The thread poll skips while signed out.")
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
          driver.Log.LogWarning(
            "The thread poll failed: {Error}",
            describe error
          )
        | Ok page ->
          driver.Log.LogTrace("The thread poll read a fresh page.")

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
            updateThread key (fun thread -> {
              thread with
                  IsLoadingOlder = true
            })
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
      (driver: Driver, target: ChatTarget, message: Message)
      : Async<unit> = asyncEx {
      let key = ChatTargets.key target

      let! state = driver.Read()

      let wasEmpty =
        state.Threads
        |> Map.tryFind key
        |> Option.map _.Messages.IsEmpty
        |> Option.defaultValue true

      if wasEmpty then
        Async.Start(poll(driver, target))

      driver.Log.LogInformation(
        "The message {ClientId} landed.",
        UMX.untag message.ClientId
      )

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
        | Ok message -> do! landed(driver, target, message)
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

  /// The one-step landing of a pin or an unpin answer: the whole pinned
  /// list and its version take the thread in one posted change, so a
  /// poll that left before the answer carries a version the merge
  /// already treats as stale.
  let landPins
    (
      post: (RemotesState -> RemotesState) -> unit,
      target: ChatTarget,
      result: MessagePinResult
    ) : unit =
    post(
      updateThread (ChatTargets.key target) (fun thread -> {
        thread with
            Pinned = Some result.Pinned
            ReactionsVersion =
              Some(
                max
                  (thread.ReactionsVersion |> Option.defaultValue 0)
                  result.ReactionsVersion
              )
      })
    )
