module DevNull.Client.SessionFiles

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
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
    json.["pasteis_mode"] <- JsonValue.Create session.PasteisMode
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

let inline tryBool (name: string) (root: JsonElement) =
  match root.TryGetProperty name with
  | true, value when value.ValueKind = JsonValueKind.True -> Some true
  | true, value when value.ValueKind = JsonValueKind.False -> Some false
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
      // A session stored before pastéis mode exists reads as off.
      Some {
        User = {
          Id = id
          Username = username
          Name = name
          AvatarUrl = tryString "avatar_url" root
        }
        UnreadNotificationsCount = unread
        PasteisMode = tryBool "pasteis_mode" root |> Option.defaultValue false
      }
    | _ -> None
  with _ ->
    None

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
