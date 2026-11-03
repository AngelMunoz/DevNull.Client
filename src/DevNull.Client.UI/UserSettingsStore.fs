module DevNull.Client.UserSettingsStore

open System
open System.Reactive.Subjects
open System.Threading
open Microsoft.Extensions.Logging
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.Stores
open IcedTasks

type UserSettingsStore =
  abstract Current: UserSettings option
  abstract Observe: IObservable<UserSettings option>
  abstract Refresh: unit -> unit
  abstract Apply: settings: UserSettings -> unit

type IProvideUserSettings =
  abstract UserSettings: UserSettingsStore

module UserSettingsStore =

  type State = {
    Session: SessionStore
    Fetch: unit -> Async<Result<UserSettings, AppError>>
    Log: ILogger
    Subject: BehaviorSubject<UserSettings option>
    Reading: int ref
  }

  let emit(state: State, value: UserSettings option) =
    if Avalonia.Threading.Dispatcher.UIThread.CheckAccess() then
      state.Subject.OnNext value
    else
      emitOnUi(state.Subject, value)

  let apply(state: State, settings: UserSettings) =
    emit(state, Some settings)

    match state.Session.SignedIn with
    | ValueSome current ->
      state.Session.Refresh {
        current.Session with
            AutomaticTranslation = settings.AutomaticTranslation
      }
    | ValueNone -> ()

  let read(state: State) = asyncEx {
    try
      match! state.Fetch() with
      | Ok settings ->
        state.Log.LogDebug("The user settings arrived.")
        apply(state, settings)
      | Error error ->
        state.Log.LogWarning(
          "The user settings did not arrive: {Error}",
          bounded 300 (describe error)
        )
    with ex ->
      state.Log.LogWarning(ex, "The user settings did not arrive.")
  }

  let refresh(state: State) =
    if Interlocked.CompareExchange(state.Reading, 1, 0) = 0 then
      Async.Start(
        asyncEx {
          try
            do! read state
          finally
            state.Reading.Value <- 0
        }
      )

  let current(state: State) : UserSettings option = state.Subject.Value

  let observe(state: State) : IObservable<UserSettings option> = state.Subject

  let create
    (
      session: SessionStore,
      fetch: unit -> Async<Result<UserSettings, AppError>>,
      log: ILogger
    ) =
    let state: State = {
      Session = session
      Fetch = fetch
      Log = log
      Subject = new BehaviorSubject<UserSettings option>(None)
      Reading = ref 0
    }

    {
      new UserSettingsStore with
        member _.Current = current state
        member _.Observe = observe state
        member _.Refresh() = refresh state
        member _.Apply(settings) = apply(state, settings)
    }
