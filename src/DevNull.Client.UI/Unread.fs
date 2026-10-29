module DevNull.Client.Unread

open System
open System.Threading
open System.Threading.Tasks
open IcedTasks
open Microsoft.Extensions.Logging
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.Stores

/// Everything one poll loop carries: the store it refreshes, the fetch
/// leaf, the logger of every failed poll, and the inbox gate.
[<Struct>]
type PollContext = {
  Session: SessionStore
  Fetch: unit -> Async<Result<Me, AppError>>
  Log: ILogger
  InboxVisible: IObservable<bool>
  DelayMs: int
}

/// One poll: a signed-in user's account, folded back into the session
/// store. A malformed body can throw past the decode boundary, so the
/// loop survives every failure mode and keeps the last count.
let poll(context: PollContext) : Async<unit> = asyncEx {
  match context.Session.SignedIn with
  | ValueSome _ ->
    context.Log.LogTrace("The unread poll runs.")

    try
      match! context.Fetch() with
      | Ok me ->
        context.Log.LogDebug(
          "The unread poll refreshed ({Count} unread).",
          me.UnreadNotificationsCount
        )

        match context.Session.SignedIn with
        | ValueSome current ->
          context.Session.Refresh {
            current.Session with
                UnreadNotificationsCount = me.UnreadNotificationsCount
                PasteisMode = me.PasteisMode
                Remotes = me.Remotes
          }
        | ValueNone -> ()
      | Error error ->
        context.Log.LogError("The unread poll failed: {Error}", describe error)
    with ex ->
      context.Log.LogError(ex, "The unread poll failed")
  | ValueNone -> context.Log.LogTrace("The unread poll skips while signed out.")
}

/// Waits until the inbox leaves the screen, polls, sleeps, and repeats.
let rec loop(context: PollContext) : Async<unit> = asyncEx {
  let gate =
    TaskCompletionSource<unit>
      TaskCreationOptions.RunContinuationsAsynchronously

  use subscription =
    context.InboxVisible
    |> Observable.filter not
    // A second emission can land before the subscription is disposed;
    // TrySetResult keeps the gate idempotent.
    |> Observable.subscribe(fun _ -> gate.TrySetResult() |> ignore)

  do! gate.Task

  let! cancel = Async.CancellationToken

  if not cancel.IsCancellationRequested then
    do! poll context
    do! Async.Sleep context.DelayMs
    return! loop context
}

/// The app's unread service: one poll loop, paused while the inbox is
/// on screen, and the live count derived from the session store.
type UnreadService
  (
    session: SessionStore,
    fetch: unit -> Async<Result<Me, AppError>>,
    log: ILogger
  ) =

  /// Starts the poll loop; the inbox-visible gate pauses it. The gate
  /// is named for what it means (true = inbox on screen).
  member _.Start
    (inboxVisible: IObservable<bool>, interval: TimeSpan)
    : IDisposable =
    let source = new CancellationTokenSource()

    let context: PollContext = {
      Session = session
      Fetch = fetch
      Log = log
      InboxVisible = inboxVisible
      DelayMs = int interval.TotalMilliseconds
    }

    log.LogInformation(
      "The unread poll started every {Seconds} seconds.",
      interval.TotalSeconds
    )

    Async.Start(loop context, source.Token)

    {
      new IDisposable with
        member _.Dispose() =
          log.LogDebug("The unread poll stopped.")
          source.Cancel()
    }

  /// The live unread count, derived from the session store.
  member _.Count: IObservable<int> =
    session.Observe
    |> Observable.map(function
      | SignedIn entry -> entry.Session.UnreadNotificationsCount
      | SignedOut -> 0)

/// Accessor for the unread service capability.
type IProvideUnread =
  abstract Unread: UnreadService
