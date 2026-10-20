module DevNull.Client.DeviceAuth

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open IcedTasks
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open Microsoft.Win32

type DeviceKey = {
  PrivatePath: string
  PublicPath: string
  Fingerprint: string
}

type PendingAuthorization = { Verifier: string; State: string }

type SignInStart = {
  Pending: PendingAuthorization
  Url: string
}

let baseAddress = "https://dev-null.sh"

let dataDirectory =
  Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DevNull"
  )

let keyDirectory = Path.Combine(dataDirectory, "keys")

let devicePrivateKeyPath = Path.Combine(keyDirectory, "device")

let privateKeyPath = devicePrivateKeyPath

let publicKeyPath = privateKeyPath + ".pub"
let pendingPath = Path.Combine(dataDirectory, "pending.json")
let sessionPath = Path.Combine(dataDirectory, "session.json")
let errorPath = Path.Combine(dataDirectory, "last-error.txt")

let sshKeygenPath =
  let system =
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.System),
      "OpenSSH",
      "ssh-keygen.exe"
    )

  if File.Exists system then system else "ssh-keygen"

let readAllTextShared(path: string) =
  use stream =
    new FileStream(
      path,
      FileMode.Open,
      FileAccess.Read,
      FileShare.ReadWrite ||| FileShare.Delete
    )

  use reader = new StreamReader(stream)
  reader.ReadToEnd()

let writeAllTextAtomic (path: string) (content: string) =
  let temporary = path + ".tmp"
  File.WriteAllText(temporary, content)
  File.Move(temporary, path, true)

let runSshKeygen(arguments: string) : Result<string, string> =
  try
    let start =
      ProcessStartInfo(
        FileName = sshKeygenPath,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      )

    match Process.Start(start) with
    | null -> Error "ssh-keygen did not start."
    | started ->
      use signing = started
      let output = signing.StandardOutput.ReadToEnd()
      let error = signing.StandardError.ReadToEnd()
      signing.WaitForExit()

      if signing.ExitCode = 0 then
        Ok output
      else
        Error(
          if error.Trim() = "" then
            $"ssh-keygen failed ({signing.ExitCode})."
          else
            error.Trim()
        )
  with ex ->
    Error ex.Message

let fingerprintOf(publicPath: string) : Result<string, string> =
  runSshKeygen("-lf \"" + publicPath + "\"")
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

let ensureKey() : Result<DeviceKey, string> =
  try
    Directory.CreateDirectory(keyDirectory) |> ignore

    let generation =
      match File.Exists privateKeyPath, File.Exists publicKeyPath with
      | true, true -> Ok()
      | true, false ->
        runSshKeygen("-y -f \"" + privateKeyPath + "\"")
        |> Result.bind(fun publicKey ->
          writeAllTextAtomic publicKeyPath publicKey
          Ok())
      | false, _ ->
        runSshKeygen(
          "-t ed25519 -f \""
          + privateKeyPath
          + "\" -N \"\" -C \"devnull on Windows\""
        )
        |> Result.map ignore

    generation
    |> Result.bind(fun () ->
      fingerprintOf publicKeyPath
      |> Result.map(fun fingerprint -> {
        PrivatePath = privateKeyPath
        PublicPath = publicKeyPath
        Fingerprint = fingerprint
      }))
  with ex ->
    Error ex.Message

let executablePath() =
  match Environment.ProcessPath with
  | null -> Path.Combine(AppContext.BaseDirectory, "DevNull.Client.exe")
  | path when
    path.EndsWith("DevNull.Client.exe", StringComparison.OrdinalIgnoreCase)
    ->
    path
  | _ -> Path.Combine(AppContext.BaseDirectory, "DevNull.Client.exe")

// The registry APIs return null when they cannot create the key; the
// BCL does not annotate the return as nullable.
#nowarn "3261"

let registerScheme() : Result<unit, string> =
  try
    let command = "\"" + executablePath() + "\" \"%1\""

    match Registry.CurrentUser.CreateSubKey(@"Software\Classes\devnull") with
    | null -> Error "Could not open the devnull registry key."
    | key ->
      use key = key
      key.SetValue("", "URL:devnull Protocol")
      key.SetValue("URL Protocol", "")

      match key.CreateSubKey(@"shell\open\command") with
      | null -> Error "Could not open the devnull command key."
      | commandKey ->
        use commandKey = commandKey
        commandKey.SetValue("", command)
        Ok()
  with ex ->
    Error ex.Message

#warnon "3261"

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

let savePending(pending: PendingAuthorization) : Result<unit, string> =
  try
    Directory.CreateDirectory(dataDirectory) |> ignore
    let json = JsonObject()
    json.["verifier"] <- JsonValue.Create pending.Verifier
    json.["state"] <- JsonValue.Create pending.State
    writeAllTextAtomic pendingPath (json.ToJsonString())
    Ok()
  with ex ->
    Error ex.Message

let loadPending() : PendingAuthorization option =
  try
    use document = JsonDocument.Parse(readAllTextShared pendingPath)
    let root = document.RootElement

    match root.TryGetProperty "verifier", root.TryGetProperty "state" with
    | (true, verifier), (true, state) ->
      match
        verifier.GetString() |> Option.ofObj, state.GetString() |> Option.ofObj
      with
      | Some verifier, Some state -> Some { Verifier = verifier; State = state }
      | _ -> None
    | _ -> None
  with _ ->
    None

let deletePending() =
  try
    File.Delete pendingPath
  with _ ->
    ()

let parseQuery(query: string) =
  query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
  |> Array.map(fun part ->
    match part.Split([| '=' |], 2) with
    | [| name; value |] ->
      name, Uri.UnescapeDataString(value.Replace('+', ' '))
    | _ -> part, "")
  |> Map.ofArray

type Callback = { Code: string; State: string }

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

let saveSession(session: DeviceSession) : Result<unit, string> =
  try
    Directory.CreateDirectory(dataDirectory) |> ignore
    let json = JsonObject()
    json.["id"] <- JsonValue.Create session.User.Id
    json.["username"] <- JsonValue.Create session.User.Username
    json.["name"] <- JsonValue.Create session.User.Name

    session.User.AvatarUrl
    |> Option.iter(fun url -> json.["avatar_url"] <- JsonValue.Create url)

    json.["unread"] <- JsonValue.Create session.UnreadNotificationsCount
    writeAllTextAtomic sessionPath (json.ToJsonString())
    Ok()
  with ex ->
    Error ex.Message

let inline tryString (name: string) (root: JsonElement) =
  match root.TryGetProperty name with
  | true, value when value.ValueKind = JsonValueKind.String ->
    value.GetString() |> Option.ofObj
  | _ -> None

let inline tryInt (name: string) (root: JsonElement) =
  match root.TryGetProperty name with
  | true, value when value.ValueKind = JsonValueKind.Number ->
    match value.TryGetInt32() with
    | true, number -> Some number
    | false, _ -> None
  | _ -> None

let loadSession() : DeviceSession option =
  try
    use document = JsonDocument.Parse(readAllTextShared sessionPath)
    let root = document.RootElement

    match
      tryString "id" root,
      tryString "username" root,
      tryString "name" root,
      tryInt "unread" root
    with
    | Some id, Some username, Some name, Some unread ->
      Some {
        User = {
          Id = id
          Username = username
          Name = name
          AvatarUrl = tryString "avatar_url" root
        }
        UnreadNotificationsCount = unread
      }
    | _ -> None
  with _ ->
    None

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
            Username = me.Profile.User.Username
            Name = me.Profile.User.Name
            AvatarUrl = me.Profile.User.AvatarUrl
          }
          UnreadNotificationsCount = me.UnreadNotificationsCount
        }

        match saveSession session with
        | Ok() ->
          deletePending()
          return Ok session
        | Error message -> return Error(SignInFailed message)
      | Error error -> return Error error
}

let clearSession() : unit =
  try
    File.Delete sessionPath
  with _ ->
    ()

let saveLastError(message: string) =
  try
    Directory.CreateDirectory(dataDirectory) |> ignore
    writeAllTextAtomic errorPath message
  with _ ->
    ()

let takeLastError() : string option =
  try
    let message = readAllTextShared errorPath
    File.Delete errorPath
    Some message
  with _ ->
    None

let callbackUri(argv: string array) : string option =
  argv
  |> Array.tryFind(fun argument ->
    argument.StartsWith("devnull://", StringComparison.OrdinalIgnoreCase))

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
