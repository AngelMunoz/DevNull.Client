module DevNull.Client.Unread

open System
open Microsoft.Extensions.Logging
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.Stores

/// The app's unread service: one poll loop, paused while the inbox is
/// on screen, and the live count derived from the session store.
type UnreadService =
  new:
    session: SessionStore *
    fetch: (unit -> Async<Result<Me, AppError>>) *
    log: ILogger ->
      UnreadService

  /// Starts the poll loop; the inbox-visible gate pauses it. The gate
  /// is named for what it means (true = inbox on screen) and is
  /// inverted inside the service.
  member Start:
    inboxVisible: IObservable<bool> * interval: TimeSpan -> IDisposable

  /// The live unread count, derived from the session store.
  member Count: IObservable<int>

/// Accessor for the unread service capability.
type IProvideUnread =
  abstract Unread: UnreadService
