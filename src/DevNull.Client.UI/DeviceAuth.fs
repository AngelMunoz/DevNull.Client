module DevNull.Client.DeviceAuth

open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.SessionFiles

type DeviceKey = {
  PrivatePath: string
  PublicPath: string
  Fingerprint: string
}

type SignInStart = {
  Pending: PendingAuthorization
  Url: string
}

// Everything one callback completion carries: the device key, the
// callback URI, and the logger for the file moves and the mismatch.
[<Struct>]
type CompleteArgs = {
  Key: DeviceKey
  CallbackUri: string
  Log: ILogger
}

let baseAddress = "https://dev-null.sh"

module Helpers =

  let keyDirectory = Path.Combine(dataDirectory, "keys")

  let devicePrivateKeyPath = Path.Combine(keyDirectory, "device")

  let publicKeyPath = devicePrivateKeyPath + ".pub"

  let appName = "DevNull Client"

  // The server shapes an app id as a platform prefix and a bundle id
  // (its rule is ^[a-z]+:[A-Za-z0-9._-]+$, one hundred characters at
  // most), and one app's id stays the same on every install.
  let appId =
    let platform =
      if OperatingSystem.IsWindows() then "windows"
      elif OperatingSystem.IsMacOS() then "macos"
      else "linux"

    $"{platform}:me.tunaxor.devnull.client"

  let parseQuery(query: string) =
    query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
    |> Array.map(fun part ->
      match part.Split([| '=' |], 2) with
      | [| name; value |] ->
        name, Uri.UnescapeDataString(value.Replace('+', ' '))
      | _ -> part, "")
    |> Map.ofArray

  type Callback = { Code: string; State: string }

  // Reads and checks one `devnull://` callback URI.
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

let devicePrivateKeyPath = Helpers.devicePrivateKeyPath

// The SSH fingerprint of one public key file.
let fingerprintOf(publicPath: string) : Async<Result<string, string>> =
  asyncResult {
    let! output = SshKeygen.run $"-lf \"{publicPath}\""

    let found =
      output.Split(
        [| ' '; '\t'; '\r'; '\n' |],
        StringSplitOptions.RemoveEmptyEntries
      )
      |> Array.tryFind(fun token ->
        token.StartsWith("SHA256:", StringComparison.Ordinal))

    let! token =
      found
      |> Result.requireSome "ssh-keygen did not report a SHA256 fingerprint."

    if Regex.IsMatch(token, "^SHA256:[A-Za-z0-9+/]{43}$") then
      return token
    else
      return! Error $"Unexpected fingerprint: {token}"
  }

let ensureKey
  (log: ILogger, deviceName: string)
  : Async<Result<DeviceKey, string>> = asyncResult {
  try
    Directory.CreateDirectory(Helpers.keyDirectory) |> ignore

    if File.Exists devicePrivateKeyPath then
      if File.Exists Helpers.publicKeyPath then
        log.LogDebug("The device key already exists.")
      else
        log.LogWarning(
          "The device key lost its public file; deriving it again."
        )

        let! derived = SshKeygen.run($"-y -f \"{devicePrivateKeyPath}\"")

        do! writeAllTextAtomic Helpers.publicKeyPath derived
    else
      log.LogInformation("The device key is being generated.")

      do!
        SshKeygen.run(
          $"-t ed25519 -f \"{devicePrivateKeyPath}\" -N \"\" -C \"{deviceName}\""
        )
        |> Async.Ignore

    let! fingerprint = fingerprintOf Helpers.publicKeyPath

    return {
      PrivatePath = devicePrivateKeyPath
      PublicPath = Helpers.publicKeyPath
      Fingerprint = fingerprint
    }
  with ex ->
    log.LogWarning(ex, "The device key was not prepared.")

    return! Error ex.Message
}

let startAuthorization
  (
    env: #IProvidePkceSource & #IProvideNonceSource & #IProvideMachineName,
    key: DeviceKey
  ) : SignInStart =
  let request: Device.AuthorizationRequest = {
    BaseAddress = baseAddress
    Fingerprint = key.Fingerprint
    DeviceName = env.MachineName
    AppName = Helpers.appName
    AppId = Helpers.appId
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

// Verifies the callback state, redeems the code, and stores the
// session.
let complete
  (
    env: #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp,
    args: CompleteArgs
  ) : Async<Result<DeviceSession, AppError>> = asyncEx {
  let log = args.Log

  match Helpers.parseCallback args.CallbackUri with
  | Error message ->
    log.LogWarning("The sign-in callback was refused: {Error}", message)
    return Error(SignInFailed message)
  | Ok callback ->
    match! loadPending(log) with
    | None ->
      log.LogWarning("The callback arrived with no sign-in pending.")
      return Error(SignInFailed "No sign-in is pending. Start again.")
    | Some pending when pending.State <> callback.State ->
      log.LogWarning(
        "The sign-in state does not match; the callback is dropped."
      )

      return
        Error(SignInFailed "The sign-in state does not match. Start again.")
    | Some pending ->
      log.LogDebug("The callback state matched; the code is being redeemed.")

      let! (result: Result<Me, AppError>) =
        Device.redeem(
          env,
          {
            KeyFile = args.Key.PrivatePath
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
          DeviceFingerprint = args.Key.Fingerprint
          UnreadNotificationsCount = me.UnreadNotificationsCount
          PasteisMode = me.PasteisMode
          AutomaticTranslation = false
          TermsVersion = me.TermsVersion
          TermsAccepted = Some me.TermsAccepted
          Remotes = me.Remotes
        }

        match! saveSession(log, session) with
        | Ok() ->
          deletePending(log)

          log.LogInformation(
            "The device was approved for {Handle}.",
            session.User.Handle
          )

          return Ok session
        | Error message ->
          log.LogWarning("The session was not stored: {Error}", message)
          return Error(SignInFailed message)
      | Error error ->
        log.LogWarning(
          "The device code was not redeemed: {Error}",
          bounded 300 (describe error)
        )

        return Error error
}

let callbackUri(argv: string array) : string option =
  argv
  |> Array.tryFind(fun argument ->
    argument.StartsWith("devnull://", StringComparison.OrdinalIgnoreCase))

let finish
  (
    env:
      #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp & #IProvideMachineName,
    uri: string,
    log: ILogger
  ) : Async<Result<DeviceSession, AppError>> = asyncEx {
  match! ensureKey(log, env.MachineName) with
  | Error message ->
    log.LogWarning(
      "The sign-in callback could not prepare the key: {Error}",
      message
    )

    do! saveLastError(log, message)

    return Error(SignInFailed message)
  | Ok key ->
    let! outcome =
      complete(
        env,
        {
          Key = key
          CallbackUri = uri
          Log = log
        }
      )

    match outcome with
    | Ok _ -> return outcome
    | Error error ->
      let message = describe error

      log.LogWarning(
        "The sign-in callback failed: {Error}",
        bounded 300 message
      )

      do! saveLastError(log, message)

      return outcome
}

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
