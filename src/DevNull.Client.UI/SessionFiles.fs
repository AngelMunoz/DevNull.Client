module DevNull.Client.SessionFiles

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open IcedTasks
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

// The content of one file, read off the caller's thread. The share
// flags keep the read working beside a writer's atomic move.
let private readAllTextShared(path: string) : Async<string> = asyncEx {
  use stream =
    new FileStream(
      path,
      FileMode.Open,
      FileAccess.Read,
      FileShare.ReadWrite ||| FileShare.Delete
    )

  use reader = new StreamReader(stream)
  return! reader.ReadToEndAsync()
}

// One file written through a temporary sibling, so a reader never
// sees a half-written body. The body write leaves the caller's
// thread; the move that publishes it is directory metadata, and
// staying sync is the deliberate line.
let writeAllTextAtomic (path: string) (content: string) : Async<unit> = asyncEx {
  let temporary = path + ".tmp"
  do! File.WriteAllTextAsync(temporary, content)
  File.Move(temporary, path, true)
}

let savePending
  (log: ILogger, pending: PendingAuthorization)
  : Async<Result<unit, string>> = asyncEx {
  try
    do Directory.CreateDirectory(dataDirectory) |> ignore

    let json = JsonObject()
    json.["verifier"] <- JsonValue.Create pending.Verifier
    json.["state"] <- JsonValue.Create pending.State

    do! writeAllTextAtomic pendingPath (json.ToJsonString())

    log.LogDebug("The pending sign-in was stored.")
    return Ok()
  with ex ->
    log.LogWarning(ex, "The pending sign-in was not stored.")
    return Error ex.Message
}

let loadPending(log: ILogger) : Async<PendingAuthorization option> = asyncEx {
  try
    let! text = readAllTextShared pendingPath

    use document = JsonDocument.Parse(text)
    let root = document.RootElement

    match root.TryGetProperty "verifier", root.TryGetProperty "state" with
    | (true, verifier), (true, state) ->
      match
        verifier.GetString() |> Option.ofObj, state.GetString() |> Option.ofObj
      with
      | Some verifier, Some state ->
        return Some { Verifier = verifier; State = state }
      | _ ->
        log.LogWarning("The pending sign-in file names no verifier and state.")

        return None
    | _ ->
      log.LogWarning("The pending sign-in file names no verifier and state.")

      return None
  with
  | :? FileNotFoundException
  | :? DirectoryNotFoundException -> return None
  | ex ->
    log.LogWarning(ex, "The pending sign-in file was not read.")

    return None
}

let deletePending(log: ILogger) =
  try
    File.Delete pendingPath
    log.LogTrace("The pending sign-in file was removed.")
  with ex ->
    log.LogWarning(ex, "The pending sign-in file was not removed.")

let saveSession
  (log: ILogger, session: DeviceSession)
  : Async<Result<unit, string>> = asyncEx {
  try
    do Directory.CreateDirectory(dataDirectory) |> ignore

    let json = JsonObject()
    json.["id"] <- JsonValue.Create session.User.Id
    json.["username"] <- JsonValue.Create session.User.Handle
    json.["name"] <- JsonValue.Create session.User.Name

    session.User.AvatarUrl
    |> Option.iter(fun url -> json.["avatar_url"] <- JsonValue.Create url)

    json.["unread"] <- JsonValue.Create session.UnreadNotificationsCount
    json.["pasteis_mode"] <- JsonValue.Create session.PasteisMode
    json.["device_fingerprint"] <- JsonValue.Create session.DeviceFingerprint
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

    do! writeAllTextAtomic sessionPath (json.ToJsonString())

    log.LogDebug("The session of {Handle} was stored.", session.User.Handle)
    return Ok()
  with ex ->
    log.LogWarning(ex, "The session was not stored.")
    return Error ex.Message
}

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

let loadSession(log: ILogger) : Async<DeviceSession option> = asyncEx {
  try
    let! text = readAllTextShared sessionPath

    use document = JsonDocument.Parse(text)
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

      return
        Some {
          User = {
            Id = id
            Handle = username
            Name = name
            AvatarUrl = tryString "avatar_url" root
          }
          UnreadNotificationsCount = unread
          PasteisMode = tryBool "pasteis_mode" root |> Option.defaultValue false
          // A session stored before the fingerprint exists reads as
          // unknown; the next sign-in stamps it.
          DeviceFingerprint =
            tryString "device_fingerprint" root |> Option.defaultValue ""
          // A session stored before the terms state exists is unknown,
          // never unaccepted: the gate stays down until the server says.
          TermsVersion =
            tryString "terms_version" root |> Option.defaultValue ""
          TermsAccepted = tryBool "terms_accepted" root
          Remotes = remotes
        }
    | _ ->
      log.LogWarning("The stored session file carries no usable session.")

      return None
  with
  | :? FileNotFoundException
  | :? DirectoryNotFoundException -> return None
  | ex ->
    log.LogWarning(ex, "The stored session file was not read.")

    return None
}

let clearSession(log: ILogger) : unit =
  try
    File.Delete sessionPath
    log.LogTrace("The stored session file was removed.")
  with ex ->
    log.LogWarning(ex, "The stored session file was not removed.")

let saveLastError(log: ILogger, message: string) : Async<unit> = asyncEx {
  try
    do Directory.CreateDirectory(dataDirectory) |> ignore

    do! writeAllTextAtomic errorPath message

    log.LogDebug("The sign-in error was stored for the window to read.")
  with ex ->
    log.LogWarning(ex, "The sign-in error was not stored.")
}

let takeLastError(log: ILogger) : Async<string option> = asyncEx {
  try
    let! message = readAllTextShared errorPath

    // The delete is directory metadata: taking the message does not
    // wait on the disk for it.
    File.Delete errorPath

    log.LogDebug("The stored sign-in error was taken.")
    return Some message
  with
  | :? FileNotFoundException
  | :? DirectoryNotFoundException -> return None
  | ex ->
    log.LogWarning(ex, "The stored sign-in error was not read.")

    return None
}
