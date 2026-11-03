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
  /// The settings as last read, or none before the first answer.
  abstract Current: UserSettings option

  /// Every answer, starting with the current one; emits on the UI thread.
  abstract Observe: IObservable<UserSettings option>

  /// Starts one read. A read already in flight is not repeated.
  abstract Refresh: unit -> unit

  /// Keeps settings the server just saved, and follows the automatic
  /// translation flag in the session store.
  abstract Apply: settings: UserSettings -> unit

/// Accessor for the user settings capability.
type IProvideUserSettings =
  abstract UserSettings: UserSettingsStore

/// The user settings over one session, read leaf, and logger.
module UserSettingsStore =

  /// The settings store over the given session, read leaf, and logger.
  val create:
    session: SessionStore *
    fetch: (unit -> Async<Result<UserSettings, AppError>>) *
    log: ILogger ->
      UserSettingsStore
