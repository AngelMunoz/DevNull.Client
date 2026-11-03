module DevNull.Client.Unread

open System
open System.Threading
open System.Threading.Tasks
open IcedTasks
open Microsoft.Extensions.Logging
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.Stores

type UnreadService =
  abstract Start:
    inboxVisible: IObservable<bool> * interval: TimeSpan -> IDisposable

  abstract Count: IObservable<int>

type IProvideUnread =
  abstract Unread: UnreadService

module UnreadService =

  type State = {
    Session: SessionStore
    Fetch: unit -> Async<Result<Me, AppError>>
    Log: ILogger
  }

  [<Struct>]
  type PollContext = {
    Session: SessionStore
    Fetch: unit -> Async<Result<Me, AppError>>
    Log: ILogger
    InboxVisible: IObservable<bool>
    DelayMs: int
  }

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
                  TermsVersion = me.TermsVersion
                  TermsAccepted = Some me.TermsAccepted
                  Remotes = me.Remotes
            }
          | ValueNone -> ()
        | Error error ->
          context.Log.LogError(
            "The unread poll failed: {Error}",
            describe error
          )
      with ex ->
        context.Log.LogError(ex, "The unread poll failed")
    | ValueNone ->
      context.Log.LogTrace("The unread poll skips while signed out.")
  }

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

  let start
    (state: State, inboxVisible: IObservable<bool>, interval: TimeSpan)
    : IDisposable =
    let source = new CancellationTokenSource()

    let context: PollContext = {
      Session = state.Session
      Fetch = state.Fetch
      Log = state.Log
      InboxVisible = inboxVisible
      DelayMs = int interval.TotalMilliseconds
    }

    state.Log.LogInformation(
      "The unread poll started every {Seconds} seconds.",
      interval.TotalSeconds
    )

    Async.Start(loop context, source.Token)

    {
      new IDisposable with
        member _.Dispose() =
          state.Log.LogDebug("The unread poll stopped.")
          source.Cancel()
    }

  let count(state: State) : IObservable<int> =
    state.Session.Observe
    |> Observable.map(function
      | SignedIn entry -> entry.Session.UnreadNotificationsCount
      | SignedOut -> 0)

  let create
    (
      session: SessionStore,
      fetch: unit -> Async<Result<Me, AppError>>,
      log: ILogger
    ) =
    let state: State = {
      Session = session
      Fetch = fetch
      Log = log
    }

    {
      new UnreadService with
        member _.Start(inboxVisible, interval) =
          start(state, inboxVisible, interval)

        member _.Count = count state
    }
