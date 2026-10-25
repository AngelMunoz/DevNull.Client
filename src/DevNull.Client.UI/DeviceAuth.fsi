module DevNull.Client.DeviceAuth

open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.SessionFiles

/// The app's own Ed25519 key, the one the device approval binds to.
type DeviceKey = {
  PrivatePath: string
  PublicPath: string
  Fingerprint: string
}

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

/// Verifies the callback state, redeems the code, and stores the session.
val complete:
  env: 'env * key: DeviceKey * callbackUri: string ->
    Async<Result<DeviceSession, AppError>>
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp

/// The devnull callback URI in the process arguments, when one is present.
val callbackUri: argv: string array -> string option

/// Opens the system browser at the authorization URL.
val startBrowser: url: string -> Result<unit, string>
