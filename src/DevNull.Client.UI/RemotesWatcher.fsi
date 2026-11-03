namespace DevNull.Client.Remotes

open System
open Microsoft.Extensions.Logging
open DevNull.Client.RemotesState
open DevNull.Client.Stores

/// The chat's scheduling: one tick loop per watched thread, one for
/// the inbox, paused while the window is inactive or the session is
/// signed out.
type RemotesWatcher =
  /// Starts the loops under the activity gate: true while the window
  /// stands on screen. The disposable stops every loop.
  abstract Start: gate: IObservable<bool> -> IDisposable

  /// Registers one reader of a thread; the first starts its loop, and
  /// disposing any handle stops it.
  abstract WatchThread: target: ChatTarget -> IDisposable

  /// Registers one reader of the inbox; the first starts its loop, and
  /// disposing any handle stops it.
  abstract WatchInbox: unit -> IDisposable

/// The store operations one watching loop drives.
type internal WatcherDeps = {
  LastActivityAt: ChatTarget -> DateTimeOffset voption
  PollThread: ChatTarget -> Async<unit>
  PollInbox: unit -> Async<unit>
  SetWindowActive: bool -> unit
  WatchThread: ChatTarget -> IDisposable
  LoadReactionUsage: unit -> Async<unit>
}

module RemotesWatcher =

  /// The watcher over the given store operations, session, and
  /// logger.
  val internal create:
    deps: WatcherDeps * session: SessionStore * log: ILogger -> RemotesWatcher
