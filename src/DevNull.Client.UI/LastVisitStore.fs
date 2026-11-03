module DevNull.Client.LastVisitStore

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Text.Json.Nodes

type LastVisitStore =
  abstract LastVisit: user: string * log: string -> DateTimeOffset option
  abstract Mark: user: string * log: string * newest: DateTimeOffset -> unit

type IProvideLastVisit =
  abstract LastVisit: LastVisitStore

module LastVisitStore =

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

  type State = {
    Entries: Dictionary<string, DateTimeOffset>
    Persist: Map<string, DateTimeOffset> -> unit
  }

  [<Struct>]
  type Marker = {
    User: string
    Log: string
    Newest: DateTimeOffset
  }

  let keyOf(user: string, log: string) = user + "|" + log

  let lastVisit
    (state: State, user: string, log: string)
    : DateTimeOffset option =
    match state.Entries.TryGetValue(keyOf(user, log)) with
    | true, newest -> Some newest
    | false, _ -> None

  let mark(state: State, marker: Marker) =
    // The marker is feed chrome: a missing or unreadable file only
    // means no marker shows, so every write is best effort.
    state.Entries[keyOf(marker.User, marker.Log)] <- marker.Newest

    state.Entries
    |> Seq.map(fun pair -> pair.Key, pair.Value)
    |> Map.ofSeq
    |> state.Persist

  let createWith
    (
      stored: Map<string, DateTimeOffset>,
      persist: Map<string, DateTimeOffset> -> unit
    ) =
    let entries: Dictionary<string, DateTimeOffset> = Dictionary()

    for key, value in Map.toSeq stored do
      entries[key] <- value

    let state: State = { Entries = entries; Persist = persist }

    {
      new LastVisitStore with
        member _.LastVisit(user, log) = lastVisit(state, user, log)

        member _.Mark(user, log, newest) =
          mark(
            state,
            {
              User = user
              Log = log
              Newest = newest
            }
          )
    }

  let create() = createWith(fileStored(), filePersist)
