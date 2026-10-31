module DevNull.Client.TranslationCache

open System
open System.Collections.Concurrent
open System.IO
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Types
open DevNull.Client.Errors
open IcedTasks
open Microsoft.Extensions.Logging

/// One translation as the server keeps it for a week.
let ttl = TimeSpan.FromSeconds 604800.0

[<Struct>]
type Files = {
  Read: string -> Async<(string * DateTime) option>
  Keep: string * string -> Async<unit>
  Drop: string -> unit
  List: unit -> string list
}

// The app's own file tier: one directory, with every failure a cache
// survives turned into a quiet answer — a read that cannot happen reads
// as nothing, and a write or drop that cannot happen never happened.
let filesOf(directory: string) : Files = {
  Read =
    fun path -> asyncEx {
      try
        let! token = Async.CancellationToken
        let! json = File.ReadAllTextAsync(path, token)
        return Some(json, File.GetLastWriteTimeUtc path)
      with
      | :? FileNotFoundException
      | :? DirectoryNotFoundException -> return None
      | :? IOException
      | :? UnauthorizedAccessException -> return None
    }
  Keep =
    fun (path, json) -> asyncEx {
      try
        let! token = Async.CancellationToken
        Directory.CreateDirectory directory |> ignore

        do! File.WriteAllTextAsync(path, json, token)
      with
      | :? IOException
      | :? UnauthorizedAccessException -> ()
    }
  Drop =
    fun path ->
      try
        File.Delete path
      with
      | :? FileNotFoundException
      | :? DirectoryNotFoundException -> ()
      | :? IOException
      | :? UnauthorizedAccessException -> ()
  List =
    fun () ->
      try
        Directory.EnumerateFiles(directory, "*.json") |> Seq.toList
      with
      | :? DirectoryNotFoundException -> []
      | :? IOException
      | :? UnauthorizedAccessException -> []
}

type TranslationCache(directory: string, files: Files, log: ILogger) =
  let held = ConcurrentDictionary<string, DateTime * CommitTranslation>()

  let tryDisk(commitId: string) : Async<(DateTime * CommitTranslation) option> =
    asyncEx {
      let path = Path.Combine(directory, commitId + ".json")

      match! files.Read path with
      | Some(json, writtenAt) ->
        if DateTime.UtcNow - writtenAt <= ttl then
          match CommitTranslation.decode json with
          | Ok(Some translation) -> return Some(writtenAt, translation)
          // A file the decoder refuses is noise: drop it like an expired
          // one, so it never fails twice.
          | _ ->
            files.Drop path
            return None
        else
          files.Drop path
          return None
      | None -> return None
    }

  let forgetDisk(commitId: string) : Async<unit> = asyncEx {
    files.Drop(Path.Combine(directory, commitId + ".json"))

    // Foreign files — names no commit id can carry — leave beside the
    // drop, off the caller's path: the sweep never delays the work.
    Async.Start(
      asyncEx {
        for path in files.List() do
          match Path.GetFileNameWithoutExtension path |> Option.ofObj with
          | Some stem when CommitIds.parse<AppError> stem |> Result.isError ->
            files.Drop path
          | _ -> ()
      }
    )
  }

  let recallDisk(commitId: string) : Async<CommitTranslation option> = asyncEx {
    match! tryDisk commitId with
    | Some(writtenAt, translation) ->
      held[commitId] <- (writtenAt, translation)
      return Some translation
    | None ->
      held.TryRemove commitId |> ignore
      return None
  }

  // A restart starts with the disk tier in memory: the scan starts with
  // the cache and runs beside the window. A page that mounts before it
  // settles reads what it needs through TryGet.
  do
    Async.Start(
      asyncEx {
        for path in files.List() do
          match Option.ofObj(Path.GetFileNameWithoutExtension path) with
          | Some commitId -> do! recallDisk commitId |> Async.Ignore
          | None -> ()
      }
    )

  /// The app's own cache: the directory under the local app data.
  new(directory: string, log: ILogger) =
    TranslationCache(directory, filesOf directory, log)

  member _.TryGet(commitId: string) : Async<CommitTranslation option> = asyncEx {
    match held.TryGetValue commitId with
    | true, (writtenAt, translation) when DateTime.UtcNow - writtenAt <= ttl ->
      log.LogDebug("The translation of {Commit} came from memory.", commitId)

      return Some translation
    | _ ->
      match! recallDisk commitId with
      | Some translation ->
        log.LogInformation(
          "The translation of {Commit} came from the disk.",
          commitId
        )

        return Some translation
      | None ->
        log.LogInformation(
          "The disk holds no translation of {Commit}; the page asks the server.",
          commitId
        )

        return None
  }

  member _.Set(commitId: string, payload: TranslationPayload) : Async<unit> =
    asyncEx {
      match payload.Translation with
      | Some translation ->
        held[commitId] <- (DateTime.UtcNow, translation)

        do!
          files.Keep(Path.Combine(directory, commitId + ".json"), payload.Json)
      | None ->
        held.TryRemove commitId |> ignore
        do! forgetDisk commitId
    }

  member _.Forget(commitId: string) : Async<unit> = asyncEx {
    held.TryRemove commitId |> ignore
    do! forgetDisk commitId
  }

  member _.Snapshot() : Map<string, CommitTranslation> =
    held
    |> Seq.filter(fun pair -> DateTime.UtcNow - fst pair.Value <= ttl)
    |> Seq.map(fun pair -> pair.Key, snd pair.Value)
    |> Map.ofSeq

/// Accessor for the translation cache capability.
type IProvideTranslations =
  abstract Translations: TranslationCache
