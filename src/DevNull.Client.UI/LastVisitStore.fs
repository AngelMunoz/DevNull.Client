module DevNull.Client.LastVisitStore

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Text.Json.Nodes

let directory =
  Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DevNull"
  )

let markersPath = Path.Combine(directory, "last-visit.json")

let filePersist(markers: Map<string, DateTimeOffset>) =
  try
    Directory.CreateDirectory directory |> ignore

    let json = JsonObject()

    for key, value in Map.toSeq markers do
      json[key] <- JsonValue.Create(value.ToString "O")

    File.WriteAllText(markersPath, json.ToJsonString())
  with
  | :? UnauthorizedAccessException
  | :? IOException -> ()

/// The stored markers; an absent or unreadable file answers none.
let fileStored() : Map<string, DateTimeOffset> =
  try
    use document = JsonDocument.Parse(File.ReadAllText markersPath)

    [|
      for pair in document.RootElement.EnumerateObject() do
        match pair.Value.ValueKind with
        | JsonValueKind.String ->
          let parsed = DateTimeOffset.TryParse(pair.Value.GetString())

          match parsed with
          | true, newest -> pair.Name, newest
          | false, _ -> ()
        | _ -> ()
    |]
    |> Map.ofArray
  with
  | :? FileNotFoundException
  | :? DirectoryNotFoundException
  | :? JsonException
  | :? IOException -> Map.empty

/// The per-user, per-log marker of the newest commit the last visit
/// saw.
type LastVisitStore
  (
    stored: Map<string, DateTimeOffset>,
    persist: Map<string, DateTimeOffset> -> unit
  ) =

  // The marker is feed chrome: a missing or unreadable file only
  // means no marker shows, so every read and write is best effort.
  let entries: Dictionary<string, DateTimeOffset> = Dictionary()

  do
    for key, value in Map.toSeq stored do
      entries[key] <- value

  let keyOf(user: string, log: string) = user + "|" + log

  new() = LastVisitStore(fileStored(), filePersist)

  member _.LastVisit(user: string, log: string) : DateTimeOffset option =
    match entries.TryGetValue(keyOf(user, log)) with
    | true, newest -> Some newest
    | false, _ -> None

  member _.Mark(user: string, log: string, newest: DateTimeOffset) =
    entries[keyOf(user, log)] <- newest

    let asMap =
      entries |> Seq.map(fun pair -> pair.Key, pair.Value) |> Map.ofSeq

    persist asMap

/// Accessor for the last-visit capability.
type IProvideLastVisit =
  abstract LastVisit: LastVisitStore
