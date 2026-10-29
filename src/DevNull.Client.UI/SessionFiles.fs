module DevNull.Client.SessionFiles

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Microsoft.Extensions.Logging
open DevNull.Client.Core.ApiTypes

/// The verifier and state one browser approval must come back with.
type PendingAuthorization = { Verifier: string; State: string }

/// The app's own data directory, beside the key and the log files.
let dataDirectory =
  Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DevNull"
  )

let pendingPath = Path.Combine(dataDirectory, "pending.json")

let sessionPath = Path.Combine(dataDirectory, "session.json")

let errorPath = Path.Combine(dataDirectory, "last-error.txt")

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

let savePending
  (log: ILogger, pending: PendingAuthorization)
  : Result<unit, string> =
  try
    Directory.CreateDirectory(dataDirectory) |> ignore
    let json = JsonObject()
    json.["verifier"] <- JsonValue.Create pending.Verifier
    json.["state"] <- JsonValue.Create pending.State
    writeAllTextAtomic pendingPath (json.ToJsonString())
    log.LogDebug("The pending sign-in was stored.")
    Ok()
  with ex ->
    log.LogWarning(ex, "The pending sign-in was not stored.")
    Error ex.Message

let loadPending(log: ILogger) : PendingAuthorization option =
  try
    use document = JsonDocument.Parse(readAllTextShared pendingPath)
    let root = document.RootElement

    match root.TryGetProperty "verifier", root.TryGetProperty "state" with
    | (true, verifier), (true, state) ->
      match
        verifier.GetString() |> Option.ofObj, state.GetString() |> Option.ofObj
      with
      | Some verifier, Some state -> Some { Verifier = verifier; State = state }
      | _ ->
        log.LogWarning("The pending sign-in file names no verifier and state.")
        None
    | _ ->
      log.LogWarning("The pending sign-in file names no verifier and state.")
      None
  with
  | :? FileNotFoundException
  | :? DirectoryNotFoundException -> None
  | ex ->
    log.LogWarning(ex, "The pending sign-in file was not read.")
    None

let deletePending(log: ILogger) =
  try
    File.Delete pendingPath
    log.LogTrace("The pending sign-in file was removed.")
  with ex ->
    log.LogWarning(ex, "The pending sign-in file was not removed.")

let saveSession(log: ILogger, session: DeviceSession) : Result<unit, string> =
  try
    Directory.CreateDirectory(dataDirectory) |> ignore
    let json = JsonObject()
    json.["id"] <- JsonValue.Create session.User.Id
    json.["username"] <- JsonValue.Create session.User.Handle
    json.["name"] <- JsonValue.Create session.User.Name

    session.User.AvatarUrl
    |> Option.iter(fun url -> json.["avatar_url"] <- JsonValue.Create url)

    json.["unread"] <- JsonValue.Create session.UnreadNotificationsCount
    json.["pasteis_mode"] <- JsonValue.Create session.PasteisMode
    json.["terms_version"] <- JsonValue.Create session.TermsVersion

    session.TermsAccepted
    |> Option.iter(fun accepted ->
      json.["terms_accepted"] <- JsonValue.Create accepted)

    match session.Remotes with
    | Some remotes ->
      json.["remotes_from"] <- JsonValue.Create(RemotesFroms.wire remotes.From)
      json.["remotes_pull_receipts"] <- JsonValue.Create remotes.PullReceipts
      json.["remotes_requests"] <- JsonValue.Create remotes.Requests
      json.["remotes_everyone"] <- JsonValue.Create remotes.Everyone
      json.["remotes_unread"] <- JsonValue.Create remotes.UnreadCount
    | None -> ()

    writeAllTextAtomic sessionPath (json.ToJsonString())
    log.LogDebug("The session of {Handle} was stored.", session.User.Handle)
    Ok()
  with ex ->
    log.LogWarning(ex, "The session was not stored.")
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

let inline tryBool (name: string) (root: JsonElement) =
  match root.TryGetProperty name with
  | true, value when value.ValueKind = JsonValueKind.True -> Some true
  | true, value when value.ValueKind = JsonValueKind.False -> Some false
  | _ -> None

let loadSession(log: ILogger) : DeviceSession option =
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
      // A session stored before pastéis mode exists reads as off, and
      // one stored before the direct-message settings reads as off
      // too: the first account poll corrects both.
      let remotes =
        match tryString "remotes_from" root with
        | Some raw ->
          Some {
            From = RemotesFroms.parse raw
            PullReceipts =
              tryBool "remotes_pull_receipts" root |> Option.defaultValue false
            Requests =
              tryBool "remotes_requests" root |> Option.defaultValue false
            Everyone =
              tryBool "remotes_everyone" root |> Option.defaultValue false
            UnreadCount = tryInt "remotes_unread" root |> Option.defaultValue 0
          }
        | None -> None

      log.LogDebug("The stored session of {Handle} was read.", username)

      Some {
        User = {
          Id = id
          Handle = username
          Name = name
          AvatarUrl = tryString "avatar_url" root
        }
        UnreadNotificationsCount = unread
        PasteisMode = tryBool "pasteis_mode" root |> Option.defaultValue false
        // A session stored before the terms state exists is unknown,
        // never unaccepted: the gate stays down until the server says.
        TermsVersion = tryString "terms_version" root |> Option.defaultValue ""
        TermsAccepted = tryBool "terms_accepted" root
        Remotes = remotes
      }
    | _ ->
      log.LogWarning("The stored session file carries no usable session.")
      None
  with
  | :? FileNotFoundException
  | :? DirectoryNotFoundException -> None
  | ex ->
    log.LogWarning(ex, "The stored session file was not read.")
    None

let clearSession(log: ILogger) : unit =
  try
    File.Delete sessionPath
    log.LogTrace("The stored session file was removed.")
  with ex ->
    log.LogWarning(ex, "The stored session file was not removed.")

let saveLastError(log: ILogger, message: string) =
  try
    Directory.CreateDirectory(dataDirectory) |> ignore
    writeAllTextAtomic errorPath message
    log.LogDebug("The sign-in error was stored for the window to read.")
  with ex ->
    log.LogWarning(ex, "The sign-in error was not stored.")

let takeLastError(log: ILogger) : string option =
  try
    let message = readAllTextShared errorPath
    File.Delete errorPath
    log.LogDebug("The stored sign-in error was taken.")
    Some message
  with
  | :? FileNotFoundException
  | :? DirectoryNotFoundException -> None
  | ex ->
    log.LogWarning(ex, "The stored sign-in error was not read.")
    None
