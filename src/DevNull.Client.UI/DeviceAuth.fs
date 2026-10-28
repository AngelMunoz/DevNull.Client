module DevNull.Client.DeviceAuth

open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions
open IcedTasks
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.SessionFiles
open DevNull.Client.SshKeygen

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
let baseAddress = "https://dev-null.sh"

let keyDirectory = Path.Combine(dataDirectory, "keys")

/// The private key file of the app's own key, the one every signed
/// request after sign-in uses.
let devicePrivateKeyPath = Path.Combine(keyDirectory, "device")

let publicKeyPath = devicePrivateKeyPath + ".pub"

/// The SSH fingerprint of one public key file.
let fingerprintOf(publicPath: string) : Result<string, string> =
  SshKeygen.run("-lf \"" + publicPath + "\"")
  |> Result.bind(fun output ->
    output.Split(
      [| ' '; '\t'; '\r'; '\n' |],
      StringSplitOptions.RemoveEmptyEntries
    )
    |> Array.tryFind(fun token ->
      token.StartsWith("SHA256:", StringComparison.Ordinal))
    |> function
      | Some token when Regex.IsMatch(token, "^SHA256:[A-Za-z0-9+/]{43}$") ->
        Ok token
      | Some token -> Error $"Unexpected fingerprint: {token}"
      | None -> Error "ssh-keygen did not report a SHA256 fingerprint.")

/// Creates the app key when it is missing, and returns its SSH
/// fingerprint.
let ensureKey() : Result<DeviceKey, string> =
  try
    Directory.CreateDirectory(keyDirectory) |> ignore

    let generation =
      match File.Exists devicePrivateKeyPath, File.Exists publicKeyPath with
      | true, true -> Ok()
      | true, false ->
        SshKeygen.run("-y -f \"" + devicePrivateKeyPath + "\"")
        |> Result.bind(fun publicKey ->
          writeAllTextAtomic publicKeyPath publicKey
          Ok())
      | false, _ ->
        SshKeygen.run(
          "-t ed25519 -f \""
          + devicePrivateKeyPath
          + "\" -N \"\" -C \"devnull on Windows\""
        )
        |> Result.map ignore

    generation
    |> Result.bind(fun () ->
      fingerprintOf publicKeyPath
      |> Result.map(fun fingerprint -> {
        PrivatePath = devicePrivateKeyPath
        PublicPath = publicKeyPath
        Fingerprint = fingerprint
      }))
  with ex ->
    Error ex.Message

/// Starts one device authorization and returns the browser URL.
let startAuthorization
  (env: #IProvidePkceSource & #IProvideNonceSource, key: DeviceKey)
  : SignInStart =
  let request: Device.AuthorizationRequest = {
    BaseAddress = baseAddress
    Fingerprint = key.Fingerprint
    DeviceName = "devnull on Windows"
    RedirectUri = "devnull://auth/callback"
  }

  let authorization = Device.beginAuthorization(env, request)

  {
    Pending = {
      Verifier = authorization.Verifier
      State = authorization.State
    }
    Url = authorization.Url
  }

let parseQuery(query: string) =
  query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
  |> Array.map(fun part ->
    match part.Split([| '=' |], 2) with
    | [| name; value |] ->
      name, Uri.UnescapeDataString(value.Replace('+', ' '))
    | _ -> part, "")
  |> Map.ofArray

type Callback = { Code: string; State: string }

/// Reads and checks one `devnull://` callback URI.
let parseCallback(uri: string) : Result<Callback, string> =
  try
    let parsed = Uri(uri)

    if
      not(parsed.Scheme.Equals("devnull", StringComparison.OrdinalIgnoreCase))
    then
      Error "The callback is not a devnull link."
    else
      let query = parseQuery parsed.Query

      match Map.tryFind "error" query with
      | Some error -> Error $"The sign-in was denied ({error})."
      | None ->
        match Map.tryFind "code" query, Map.tryFind "state" query with
        | Some code, Some state when code <> "" && state <> "" ->
          Ok { Code = code; State = state }
        | _ -> Error "The sign-in callback is missing the code or state."
  with ex ->
    Error $"The sign-in callback is invalid: {ex.Message}"

/// Verifies the callback state, redeems the code, and stores the
/// session.
let complete
  (
    env: #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp,
    key: DeviceKey,
    callbackUri: string
  ) : Async<Result<DeviceSession, AppError>> = asyncEx {
  match parseCallback callbackUri with
  | Error message -> return Error(SignInFailed message)
  | Ok callback ->
    match loadPending() with
    | None -> return Error(SignInFailed "No sign-in is pending. Start again.")
    | Some pending when pending.State <> callback.State ->
      return
        Error(SignInFailed "The sign-in state does not match. Start again.")
    | Some pending ->
      let! (result: Result<Me, AppError>) =
        Device.redeem(
          env,
          {
            KeyFile = key.PrivatePath
            Code = callback.Code
            Verifier = pending.Verifier
          }
        )

      match result with
      | Ok me ->
        let session: DeviceSession = {
          User = {
            Id = me.Profile.User.Id
            Handle = me.Profile.User.Handle
            Name = me.Profile.User.Name
            AvatarUrl = me.Profile.User.AvatarUrl
          }
          UnreadNotificationsCount = me.UnreadNotificationsCount
          PasteisMode = me.PasteisMode
          Remotes = me.Remotes
        }

        match saveSession session with
        | Ok() ->
          deletePending()
          return Ok session
        | Error message -> return Error(SignInFailed message)
      | Error error -> return Error error
}

/// The devnull callback URI in the process arguments, when one is
/// present.
let callbackUri(argv: string array) : string option =
  argv
  |> Array.tryFind(fun argument ->
    argument.StartsWith("devnull://", StringComparison.OrdinalIgnoreCase))

/// Opens the system browser at the authorization URL.
let startBrowser(url: string) : Result<unit, string> =
  try
    match
      Process.Start(ProcessStartInfo(FileName = url, UseShellExecute = true))
    with
    | null -> Error "Could not open the browser."
    | started ->
      started.Dispose()
      Ok()
  with ex ->
    Error ex.Message
