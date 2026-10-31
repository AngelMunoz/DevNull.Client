module DevNull.Client.UserSettingsStore

open System
open Microsoft.Extensions.Logging
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.Stores

/// The server settings the whole app reads: the timezone, the language
/// commits are translated into, and whether translations show by
/// themselves. One read starts with the window and never blocks it.
type UserSettingsStore =
  new:
    session: SessionStore *
    fetch: (unit -> Async<Result<UserSettings, AppError>>) *
    log: ILogger ->
      UserSettingsStore

  /// The settings as last read, or none before the first answer.
  member Current: UserSettings option

  /// Every answer, starting with the current one; emits on the UI thread.
  member Observe: IObservable<UserSettings option>

  /// Starts one read. A read already in flight is not repeated.
  member Refresh: unit -> unit

  /// Keeps settings the server just saved, and follows the automatic
  /// translation flag in the session store.
  member Apply: settings: UserSettings -> unit

/// Accessor for the user settings capability.
type IProvideUserSettings =
  abstract UserSettings: UserSettingsStore
