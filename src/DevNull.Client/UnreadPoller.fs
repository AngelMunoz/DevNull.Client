module DevNull.Client.UnreadPoller

open System
open System.Reactive.Subjects
open System.Threading
open System.Threading.Tasks
open IcedTasks
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Stores

/// Completes as soon as the gate holds false; a BehaviorSubject answers
/// with its current value on subscribe, so an open gate never blocks.
let waitUntilOpen(paused: BehaviorSubject<bool>) : Async<unit> = asyncEx {
  let gate =
    TaskCompletionSource<unit>
      TaskCreationOptions.RunContinuationsAsynchronously

  use subscription =
    paused
    |> Observable.filter(fun open' -> not open')
    // A second emission can land before the subscription is disposed;
    // TrySetResult keeps the gate idempotent.
    |> Observable.subscribe(fun _ -> gate.TrySetResult() |> ignore)

  do! gate.Task
}

let rec loop
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp & #IProvideSession,
    paused: BehaviorSubject<bool>,
    intervalMs: int
  ) : Async<unit> = asyncEx {
  do! waitUntilOpen paused
  let! cancel = Async.CancellationToken

  if not cancel.IsCancellationRequested then
    match env.Session.SignedIn with
    | ValueSome session ->
      // A malformed body can throw past the decode boundary; the
      // loop survives every failure mode and keeps the last count.
      try
        let! (result: Result<Me, AppError>) =
          Me.get(env, { KeyFile = session.KeyFile })

        match result with
        | Ok me ->
          match env.Session.SignedIn with
          | ValueSome current ->
            env.Session.Refresh {
              current.Session with
                  UnreadNotificationsCount = me.UnreadNotificationsCount
                  PasteisMode = me.PasteisMode
            }
          | ValueNone -> ()
        | Error _ -> ()
      with _ ->
        ()
    | ValueNone -> ()

    do! Async.Sleep intervalMs

    return! loop(env, paused, intervalMs)
}

let run
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp & #IProvideSession,
    paused: BehaviorSubject<bool>,
    intervalMs: int
  ) : IDisposable =
  let source = new CancellationTokenSource()
  let token = source.Token

  Async.Start(loop(env, paused, intervalMs), token)

  {
    new IDisposable with
      member _.Dispose() = source.Cancel()
  }

let inline start
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp & #IProvideSession,
    paused: BehaviorSubject<bool>
  ) : unit =
  run(env, paused, 45_000) |> ignore
