module DevNull.Client.DeviceAuth

open Microsoft.Extensions.Logging
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

/// Everything one callback completion carries: the device key, the
/// callback URI, and the logger for the file moves and the mismatch.
[<Struct>]
type CompleteArgs = {
  Key: DeviceKey
  CallbackUri: string
  Log: ILogger
}

/// The server the app signs in to.
val baseAddress: string

/// The private key file of the app's own key, the one every signed
/// request after sign-in uses.
val devicePrivateKeyPath: string

/// The SSH fingerprint of one public key file.
val fingerprintOf: publicPath: string -> Async<Result<string, string>>

/// Creates the app key when it is missing, and returns its SSH
/// fingerprint.
val ensureKey: log: ILogger -> Async<Result<DeviceKey, string>>

/// Starts one device authorization and returns the browser URL.
val startAuthorization:
  env: #IProvidePkceSource & #IProvideNonceSource * key: DeviceKey ->
    SignInStart

/// Verifies the callback state, redeems the code, and stores the
/// session.
val complete:
  env: 'env * args: CompleteArgs -> Async<Result<DeviceSession, AppError>>
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp

/// Completes one sign-in callback end to end: prepares the key,
/// redeems the code through `complete`, stores the session, and
/// writes the error file for the waiting sign-in page when any step
/// refuses. The one sequence both the running window and the headless
/// second instance run.
val finish:
  env: 'env * uri: string * log: ILogger ->
    Async<Result<DeviceSession, AppError>>
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp

/// The devnull callback URI in the process arguments, when one is present.
val callbackUri: argv: string array -> string option

/// Opens the system browser at the authorization URL.
val startBrowser: url: string -> Result<unit, string>
