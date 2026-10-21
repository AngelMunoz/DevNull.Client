module DevNull.Client.SessionFiles

open DevNull.Client.Core.ApiTypes

/// The verifier and state one browser approval must come back with.
type PendingAuthorization = { Verifier: string; State: string }

/// The app's own data directory, beside the key and the log files.
val dataDirectory: string

/// Writes one file through a temporary sibling, so a reader never sees
/// a half-written body.
val writeAllTextAtomic: path: string -> content: string -> unit

/// Keeps the verifier and state until the browser returns.
val savePending: pending: PendingAuthorization -> Result<unit, string>

/// Reads the pending verifier and state, when one exists.
val loadPending: unit -> PendingAuthorization option

/// Removes the pending verifier and state.
val deletePending: unit -> unit

/// Reads the stored session, when one exists.
val loadSession: unit -> DeviceSession option

/// Stores the session.
val saveSession: session: DeviceSession -> Result<unit, string>

/// Removes the stored session.
val clearSession: unit -> unit

/// Stores the last sign-in error for the window that owns the UI.
val saveLastError: message: string -> unit

/// Reads and clears the last sign-in error.
val takeLastError: unit -> string option
