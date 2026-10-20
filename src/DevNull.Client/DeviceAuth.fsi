module DevNull.Client.DeviceAuth

open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors

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

/// The private key file of the app's own key, the one every signed
/// request after sign-in uses.
val devicePrivateKeyPath: string

/// Creates the app key when it is missing, and returns its SSH fingerprint.
val ensureKey: unit -> Result<DeviceKey, string>

/// Starts one device authorization and returns the browser URL.
val startAuthorization:
  env: #IProvidePkceSource & #IProvideNonceSource * key: DeviceKey ->
    SignInStart

/// Keeps the verifier and state until the browser returns.
val savePending: pending: PendingAuthorization -> Result<unit, string>

/// Reads the pending verifier and state, when one exists.
val loadPending: unit -> PendingAuthorization option

/// Removes the pending verifier and state.
val deletePending: unit -> unit

/// Verifies the callback state, redeems the code, and stores the session.
val complete:
  env: 'env * key: DeviceKey * callbackUri: string ->
    Async<Result<DeviceSession, AppError>>
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp

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

/// The devnull callback URI in the process arguments, when one is present.
val callbackUri: argv: string array -> string option

/// Opens the system browser at the authorization URL.
val startBrowser: url: string -> Result<unit, string>
