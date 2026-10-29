module DevNull.Client.SessionFiles

open Microsoft.Extensions.Logging
open DevNull.Client.Core.ApiTypes

/// The verifier and state one browser approval must come back with.
type PendingAuthorization = { Verifier: string; State: string }

/// The app's own data directory, beside the key and the log files.
val dataDirectory: string

/// Writes one file through a temporary sibling, off the caller's
/// thread, so a reader never sees a half-written body. The move that
/// publishes the body is directory metadata and stays sync.
val writeAllTextAtomic: path: string -> content: string -> Async<unit>

/// Keeps the verifier and state until the browser returns.
val savePending:
  log: ILogger * pending: PendingAuthorization -> Async<Result<unit, string>>

/// Reads the pending verifier and state, when one exists. A file that
/// exists but cannot serve the pair is a warning; an absent file is
/// the ordinary no-sign-in-in-flight state.
val loadPending: log: ILogger -> Async<PendingAuthorization option>

/// Removes the pending verifier and state. The delete is directory
/// metadata and stays sync.
val deletePending: log: ILogger -> unit

/// Reads the stored session, when one exists. A file that exists but
/// cannot serve a session is a warning; an absent file stays quiet,
/// the way a probe answers.
val loadSession: log: ILogger -> Async<DeviceSession option>

/// Stores the session.
val saveSession:
  log: ILogger * session: DeviceSession -> Async<Result<unit, string>>

/// Removes the stored session. The delete is directory metadata and
/// stays sync.
val clearSession: log: ILogger -> unit

/// Stores the last sign-in error for the window that owns the UI.
val saveLastError: log: ILogger * message: string -> Async<unit>

/// Reads and clears the last sign-in error, when one waits. An absent
/// file stays quiet: the wait loop reads it every wake.
val takeLastError: log: ILogger -> Async<string option>
