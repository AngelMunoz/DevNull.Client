module DevNull.Client.DeviceAuth

open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.Env

/// The app's own Ed25519 key, the one the device approval binds to.
type DeviceKey = {
  PrivatePath: string
  PublicPath: string
  Fingerprint: string
}

/// The verifier and state one browser approval must come back with.
type PendingAuthorization = { Verifier: string; State: string }

/// One started sign-in: what to keep, and where to send the browser.
type SignInStart = {
  Pending: PendingAuthorization
  Url: string
}

/// The server the app signs in to.
val baseAddress: string

/// Creates the app key when it is missing, and returns its SSH fingerprint.
val ensureKey: unit -> Result<DeviceKey, string>

/// Registers devnull:// for the current user, pointing at this executable.
val registerScheme: unit -> Result<unit, string>

/// Starts one device authorization and returns the browser URL.
val startAuthorization: env: AppEnv * key: DeviceKey -> SignInStart

/// Keeps the verifier and state until the browser returns.
val savePending: pending: PendingAuthorization -> Result<unit, string>

/// Reads the pending verifier and state, when one exists.
val loadPending: unit -> PendingAuthorization option

/// Removes the pending verifier and state.
val deletePending: unit -> unit

/// Verifies the callback state, redeems the code, and stores the session.
val complete:
  env: AppEnv * key: DeviceKey * callbackUri: string ->
    Async<Result<DeviceSession, AppError>>

/// Reads the stored session, when one exists.
val loadSession: unit -> DeviceSession option

/// Stores the session.
val saveSession: session: DeviceSession -> Result<unit, string>

/// Stores the last sign-in error for the window that owns the UI.
val saveLastError: message: string -> unit

/// Reads and clears the last sign-in error.
val takeLastError: unit -> string option

/// The devnull callback URI in the process arguments, when one is present.
val callbackUri: argv: string array -> string option

/// Opens the system browser at the authorization URL.
val startBrowser: url: string -> Result<unit, string>
