namespace DevNull.Client.Remotes

open System
open System.Collections.Generic
open System.Reactive.Subjects
open System.Threading
open IcedTasks
open Microsoft.Extensions.Logging
open DevNull.Client.RemotesState
open DevNull.Client.Stores

type RemotesWatcher =
  abstract Start: gate: IObservable<bool> -> IDisposable
  abstract WatchThread: target: ChatTarget -> IDisposable
  abstract WatchInbox: unit -> IDisposable

type internal WatcherDeps = {
  LastActivityAt: ChatTarget -> DateTimeOffset voption
  PollThread: ChatTarget -> Async<unit>
  PollInbox: unit -> Async<unit>
  SetWindowActive: bool -> unit
  WatchThread: ChatTarget -> IDisposable
  LoadReactionUsage: unit -> Async<unit>
}

module RemotesWatcher =

  // The poll rates the server throttles for: one thread 1.5 s while it
  // stays lively and 4 s once it goes quiet, the inbox 15 s alone and
  // 6 s while a thread is open, and a thread counts as lively for 60 s
  // after its last activity.
  let activePoll = TimeSpan.FromMilliseconds 1500.0
  let idlePoll = TimeSpan.FromMilliseconds 4000.0
  let inboxPoll = TimeSpan.FromSeconds 15.0
  let busyInboxPoll = TimeSpan.FromSeconds 6.0
  let activeWindow = TimeSpan.FromSeconds 60.0

  type State = {
    Deps: WatcherDeps
    Session: SessionStore
    Log: ILogger
    Active: BehaviorSubject<bool>
    UsageLoaded: BehaviorSubject<bool>
    Loops: Dictionary<string, CancellationTokenSource>
    Watched: Dictionary<string, ChatTarget>
    InboxLoop: BehaviorSubject<CancellationTokenSource option>
  }

  /// The pause every tick consults.
  let canRun(state: State) : bool =
    state.Active.Value && state.Session.SignedIn.IsSome

  let threadCount(state: State) : int = state.Loops.Count

  /// One thread's tick delay: fast while the thread stays lively,
  /// slow once it goes quiet.
  let delayOf(state: State, target: ChatTarget) : TimeSpan =
    match state.Deps.LastActivityAt target with
    | ValueSome at when DateTimeOffset.UtcNow - at < activeWindow -> activePoll
    | _ -> idlePoll

  [<TailCall>]
  let rec tick(state: State, target: ChatTarget) : Async<unit> = asyncEx {
    let! ct = Async.CancellationToken

    if not ct.IsCancellationRequested then
      if canRun state then
        do! state.Deps.PollThread target

      do! Async.Sleep(delayOf(state, target))

      return! tick(state, target)
  }

  [<TailCall>]
  let rec inboxTick(state: State) : Async<unit> = asyncEx {
    let! ct = Async.CancellationToken

    if not ct.IsCancellationRequested then
      if canRun state then
        do! state.Deps.PollInbox()

      do!
        Async.Sleep(if threadCount state > 0 then busyInboxPoll else inboxPoll)

      return! inboxTick state
  }

  /// Starts the loops under the activity gate: true while the window
  /// stands on screen. The gate reopening wakes every loop at once —
  /// the way a visible tab polls the moment it returns — instead of
  /// waiting out the sleep each loop was left in. The disposable stops
  /// every loop.
  let start(state: State, gate: IObservable<bool>) : IDisposable =
    state.Log.LogDebug("The remotes polls started.")

    let subscription =
      gate
      |> Observable.subscribe(fun value ->
        state.Active.OnNext value
        state.Deps.SetWindowActive value

        if value then
          state.Log.LogDebug("The chat polls wake with the window.")

          for KeyValue(_, target) in state.Watched do
            Async.Start(state.Deps.PollThread target)

          Async.Start(state.Deps.PollInbox())
        else
          state.Log.LogTrace("The chat polls pause while the window hides."))

    {
      new IDisposable with
        member _.Dispose() =
          state.Log.LogDebug("The remotes polls stopped.")
          subscription.Dispose()

          for source in state.Loops.Values do
            source.Cancel()
            source.Dispose()

          state.Loops.Clear()
          state.Watched.Clear()

          match state.InboxLoop.Value with
          | Some source ->
            source.Cancel()
            source.Dispose()

            state.InboxLoop.OnNext None
          | None -> ()
    }

  /// Registers one reader of a thread; the last release stops its
  /// loop.
  let watchThread(state: State, target: ChatTarget) : IDisposable =
    let key = ChatTargets.key target

    let release = state.Deps.WatchThread target

    let first = not(state.Loops.ContainsKey key)

    if first then
      let source = new CancellationTokenSource()

      state.Loops[key] <- source
      state.Watched[key] <- target
      Async.Start(tick(state, target), source.Token)

      state.Log.LogDebug("The thread poll for {Key} started.", key)

      if not state.UsageLoaded.Value then
        state.UsageLoaded.OnNext true

        Async.Start(state.Deps.LoadReactionUsage())

    {
      new IDisposable with
        member _.Dispose() =
          release.Dispose()

          match state.Loops.TryGetValue key with
          | true, source ->
            state.Log.LogDebug("The thread poll for {Key} stopped.", key)
            source.Cancel()
            source.Dispose()

            state.Loops.Remove key |> ignore
            state.Watched.Remove key |> ignore
          | false, _ -> ()
    }

  /// Registers one reader of the inbox; the last release stops its
  /// loop.
  let watchInbox(state: State) : IDisposable =
    let first = state.InboxLoop.Value.IsNone

    if first then
      let source = new CancellationTokenSource()

      state.InboxLoop.OnNext(Some source)
      Async.Start(inboxTick state, source.Token)

      state.Log.LogDebug("The inbox poll started.")

    {
      new IDisposable with
        member _.Dispose() =
          match state.InboxLoop.Value with
          | Some source ->
            state.Log.LogDebug("The inbox poll stopped.")
            source.Cancel()
            source.Dispose()

            state.InboxLoop.OnNext None
          | None -> ()
    }

  let create
    (deps: WatcherDeps, session: SessionStore, log: ILogger)
    : RemotesWatcher =
    let state: State = {
      Deps = deps
      Session = session
      Log = log
      Active = new BehaviorSubject<bool>(true)
      UsageLoaded = new BehaviorSubject<bool>(false)
      Loops = Dictionary<string, CancellationTokenSource>()
      Watched = Dictionary<string, ChatTarget>()
      InboxLoop = new BehaviorSubject<CancellationTokenSource option>(None)
    }

    {
      new RemotesWatcher with
        member _.Start(gate) = start(state, gate)
        member _.WatchThread(target) = watchThread(state, target)
        member _.WatchInbox() = watchInbox state
    }
