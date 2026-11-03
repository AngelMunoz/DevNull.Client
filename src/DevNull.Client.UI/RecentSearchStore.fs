module DevNull.Client.RecentSearchStore

open System
open System.Collections.Generic
open System.IO
open System.Reactive.Concurrency
open System.Text.Json
open System.Text.Json.Nodes
open FSharp.Control.Reactive
open JDeck

type RecentSearchStore =
  abstract Recent: user: string -> string list
  abstract Remember: user: string * term: string -> unit
  abstract Clear: user: string -> unit

type IProvideRecentSearches =
  abstract RecentSearches: RecentSearchStore

module RecentSearchStore =

  let directory =
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DevNull"
    )

  let recentPath = Path.Combine(directory, "recent-searches.json")

  let entryDecoder(element: JsonElement) = decode {
    let! user = Required.Property.get ("user", Required.string) element
    let! terms = Required.Property.list ("terms", Required.string) element
    return user, terms
  }

  let recentsDecoder(element: JsonElement) = decode {
    let! entries = Required.Property.list ("recents", entryDecoder) element
    return Map.ofList entries
  }

  let filePersist(recents: Map<string, string list>) =
    try
      Directory.CreateDirectory directory |> ignore

      let entryOf(user: string, terms: string list) : JsonNode =
        Json.object [
          "user", Encode.string user
          "terms", Encode.sequence (terms, Encode.string) (JsonArray())
        ]

      let root =
        Json.object(
          List.singleton(
            "recents",
            Encode.sequence (Map.toList recents, entryOf) (JsonArray())
          )
        )

      File.WriteAllText(recentPath, root.ToJsonString())
    with
    | :? UnauthorizedAccessException
    | :? IOException -> ()

  let fileStored() : Map<string, string list> =
    try
      match
        JDeck.Decoding.fromString(File.ReadAllText recentPath, recentsDecoder)
      with
      | Ok recents -> recents
      | Error _ -> Map.empty
    with
    | :? FileNotFoundException
    | :? DirectoryNotFoundException
    | :? JsonException
    | :? InvalidOperationException
    | :? IOException -> Map.empty

  type State = {
    Entries: Dictionary<string, string list>
    Publish: Map<string, string list> -> unit
  }

  let recent(state: State, user: string) : string list =
    match state.Entries.TryGetValue user with
    | true, terms -> terms
    | false, _ -> []

  let remember(state: State, user: string, term: string) =
    let trimmed = term.Trim()

    if trimmed = "" || String.length trimmed > 100 then
      ()
    else
      // A user's first search has no list yet: the missing key is an
      // empty list, never a null one.
      let known =
        match state.Entries.TryGetValue user with
        | true, terms -> terms
        | false, _ -> []

      let kept =
        trimmed
        :: List.filter
          (fun knownTerm ->
            not(
              String.Equals(
                knownTerm,
                trimmed,
                StringComparison.OrdinalIgnoreCase
              )
            ))
          known
        |> List.truncate 3

      state.Entries[user] <- kept

      state.Entries
      |> Seq.map(fun pair -> pair.Key, pair.Value)
      |> Map.ofSeq
      |> state.Publish

  let clear(state: State, user: string) =
    state.Entries[user] <- []

    state.Entries
    |> Seq.map(fun pair -> pair.Key, pair.Value)
    |> Map.ofSeq
    |> state.Publish

  let createWith
    (stored: Map<string, string list>, persist: Map<string, string list> -> unit) =
    let entries: Dictionary<string, string list> = Dictionary()

    for user, terms in Map.toSeq stored do
      entries[user] <- terms

    // Each write leaves the calling thread once, serialized in order on
    // the thread pool, so a settled search never costs the interface a
    // disk write and two quick ones never cross.
    let disk = Subject.broadcast

    disk
    |> Observable.observeOn ThreadPoolScheduler.Instance
    |> Observable.add persist

    let state: State = {
      Entries = entries
      Publish = disk.OnNext
    }

    {
      new RecentSearchStore with
        member _.Recent(user) = recent(state, user)
        member _.Remember(user, term) = remember(state, user, term)
        member _.Clear(user) = clear(state, user)
    }

  let create() = createWith(fileStored(), filePersist)
