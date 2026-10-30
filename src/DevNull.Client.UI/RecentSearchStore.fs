module DevNull.Client.RecentSearchStore

open System
open System.Collections.Generic
open System.IO
open System.Reactive.Concurrency
open System.Text.Json
open System.Text.Json.Nodes
open FSharp.Control.Reactive
open JDeck

let directory =
  Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DevNull"
  )

let recentPath = Path.Combine(directory, "recent-searches.json")

/// The file's one entry: whose searches, and which.
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

/// The stored searches; an absent or unreadable file answers none.
/// One synchronous read at startup, the way the settings and last-visit
/// files read theirs.
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

/// The per-user list of the searches the person settled on.
type RecentSearchStore
  (stored: Map<string, string list>, persist: Map<string, string list> -> unit)
  =

  // The list is dialog chrome: a missing or unreadable file only means
  // no recents show, so every write is best effort.
  let entries: Dictionary<string, string list> = Dictionary()

  do
    for user, terms in Map.toSeq stored do
      entries[user] <- terms

  // Each write leaves the calling thread once, serialized in order on
  // the thread pool, so a settled search never costs the interface a
  // disk write and two quick ones never cross.
  let disk = Subject.broadcast

  do
    disk
    |> Observable.observeOn ThreadPoolScheduler.Instance
    |> Observable.add persist

  new() = RecentSearchStore(fileStored(), filePersist)

  member _.Recent(user: string) : string list =
    match entries.TryGetValue user with
    | true, terms -> terms
    | false, _ -> []

  member _.Remember(user: string, term: string) =
    let trimmed = term.Trim()

    if trimmed = "" || String.length trimmed > 100 then
      ()
    else
      // A user's first search has no list yet: the missing key is an
      // empty list, never a null one.
      let known =
        match entries.TryGetValue user with
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

      entries[user] <- kept

      disk.OnNext(
        entries |> Seq.map(fun pair -> pair.Key, pair.Value) |> Map.ofSeq
      )

  member _.Clear(user: string) =
    entries[user] <- []

    disk.OnNext(
      entries |> Seq.map(fun pair -> pair.Key, pair.Value) |> Map.ofSeq
    )

/// Accessor for the recent-search capability.
type IProvideRecentSearches =
  abstract RecentSearches: RecentSearchStore
