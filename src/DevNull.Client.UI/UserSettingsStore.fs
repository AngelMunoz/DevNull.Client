module DevNull.Client.UserSettingsStore

open System
open System.Reactive.Subjects
open System.Threading
open Microsoft.Extensions.Logging
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.Stores
open IcedTasks

/// What one settings store carries: the session it follows, the read
/// leaf, and the logger of a failed read.
[<Struct>]
type Context = {
  Session: SessionStore
  Fetch: unit -> Async<Result<UserSettings, AppError>>
  Log: ILogger
}

type UserSettingsStore(context: Context) =
  let subject = new BehaviorSubject<UserSettings option>(None)
  let reading = ref 0

  /// Posts on the UI thread, so no page re-enters a control off the
  /// dispatcher.
  let emit(value: UserSettings option) =
    if Avalonia.Threading.Dispatcher.UIThread.CheckAccess() then
      subject.OnNext value
    else
      emitOnUi(subject, value)

  /// Keeps the settings and follows the automatic translation flag in
  /// the session store, so every card page reads the same choice.
  let apply(settings: UserSettings) =
    emit(Some settings)

    match context.Session.SignedIn with
    | ValueSome current ->
      context.Session.Refresh {
        current.Session with
            AutomaticTranslation = settings.AutomaticTranslation
      }
    | ValueNone -> ()

  /// One read, folded into the store: a failure keeps the last settings
  /// and reads again on the next ask.
  let read() = asyncEx {
    try
      match! context.Fetch() with
      | Ok settings ->
        context.Log.LogDebug("The user settings arrived.")
        apply settings
      | Error error ->
        context.Log.LogWarning(
          "The user settings did not arrive: {Error}",
          bounded 300 (describe error)
        )
    with ex ->
      context.Log.LogWarning(ex, "The user settings did not arrive.")
  }

  /// One read at a time: the claim keeps a page's ask and the window's ask
  /// from doubling up, and clears when the read settles.
  let refresh() =
    if Interlocked.CompareExchange(reading, 1, 0) = 0 then
      Async.Start(
        asyncEx {
          try
            do! read()
          finally
            reading.Value <- 0
        }
      )

  /// The state at boot: nothing read yet.
  new(session: SessionStore, fetch, log: ILogger) =
    UserSettingsStore {
      Session = session
      Fetch = fetch
      Log = log
    }

  member _.Current: UserSettings option = subject.Value

  member _.Observe: IObservable<UserSettings option> = subject

  member _.Refresh() = refresh()

  member _.Apply(settings: UserSettings) = apply settings

/// Accessor for the user settings capability.
type IProvideUserSettings =
  abstract UserSettings: UserSettingsStore
