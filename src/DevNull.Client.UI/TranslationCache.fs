module DevNull.Client.TranslationCache

open System
open System.Collections.Concurrent
open System.IO
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Types
open DevNull.Client.Errors
open IcedTasks
open Microsoft.Extensions.Logging

type TranslationCache =
  abstract TryGet: commitId: string -> Async<CommitTranslation option>
  abstract Set: commitId: string * payload: TranslationPayload -> Async<unit>
  abstract Forget: commitId: string -> Async<unit>
  abstract Snapshot: unit -> Map<string, CommitTranslation>

type IProvideTranslations =
  abstract Translations: TranslationCache

module TranslationCache =

  let ttl = TimeSpan.FromSeconds 604800.0

  [<Struct>]
  type Files = {
    Read: string -> Async<(string * DateTime) option>
    Keep: string * string -> Async<unit>
    Drop: string -> unit
    List: unit -> string list
  }

  type State = {
    Directory: string
    Files: Files
    Log: ILogger
    Held: ConcurrentDictionary<string, DateTime * CommitTranslation>
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

  let pathOf(state: State, commitId: string) =
    Path.Combine(state.Directory, commitId + ".json")

  let tryDisk
    (state: State, commitId: string)
    : Async<(DateTime * CommitTranslation) option> = asyncEx {
    let path = pathOf(state, commitId)

    match! state.Files.Read path with
    | Some(json, writtenAt) ->
      if DateTime.UtcNow - writtenAt <= ttl then
        match CommitTranslation.decode json with
        | Ok(Some translation) -> return Some(writtenAt, translation)
        // A file the decoder refuses is noise: drop it like an expired
        // one, so it never fails twice.
        | _ ->
          state.Files.Drop path
          return None
      else
        state.Files.Drop path
        return None
    | None -> return None
  }

  let forgetDisk(state: State, commitId: string) : Async<unit> = asyncEx {
    state.Files.Drop(pathOf(state, commitId))

    // Foreign files — names no commit id can carry — leave beside the
    // drop, off the caller's path: the sweep never delays the work.
    Async.Start(
      asyncEx {
        for path in state.Files.List() do
          match Path.GetFileNameWithoutExtension path |> Option.ofObj with
          | Some stem when CommitIds.parse<AppError> stem |> Result.isError ->
            state.Files.Drop path
          | _ -> ()
      }
    )
  }

  let recallDisk
    (state: State, commitId: string)
    : Async<CommitTranslation option> = asyncEx {
    match! tryDisk(state, commitId) with
    | Some(writtenAt, translation) ->
      state.Held[commitId] <- (writtenAt, translation)
      return Some translation
    | None ->
      state.Held.TryRemove commitId |> ignore
      return None
  }

  // A restart starts with the disk tier in memory: the scan starts with
  // the cache and runs beside the window. A page that mounts before it
  // settles reads what it needs through TryGet.
  let startScan(state: State) =
    Async.Start(
      asyncEx {
        for path in state.Files.List() do
          match Option.ofObj(Path.GetFileNameWithoutExtension path) with
          | Some commitId -> do! recallDisk(state, commitId) |> Async.Ignore
          | None -> ()
      }
    )

  let tryGet(state: State, commitId: string) : Async<CommitTranslation option> =
    asyncEx {
      match state.Held.TryGetValue commitId with
      | true, (writtenAt, translation) when DateTime.UtcNow - writtenAt <= ttl ->
        state.Log.LogDebug(
          "The translation of {Commit} came from memory.",
          commitId
        )

        return Some translation
      | _ ->
        match! recallDisk(state, commitId) with
        | Some translation ->
          state.Log.LogInformation(
            "The translation of {Commit} came from the disk.",
            commitId
          )

          return Some translation
        | None ->
          state.Log.LogInformation(
            "The disk holds no translation of {Commit}; the page asks the server.",
            commitId
          )

          return None
    }

  let set
    (state: State, commitId: string, payload: TranslationPayload)
    : Async<unit> = asyncEx {
    match payload.Translation with
    | Some translation ->
      state.Held[commitId] <- (DateTime.UtcNow, translation)

      do! state.Files.Keep(pathOf(state, commitId), payload.Json)
    | None ->
      state.Held.TryRemove commitId |> ignore
      do! forgetDisk(state, commitId)
  }

  let forget(state: State, commitId: string) : Async<unit> = asyncEx {
    state.Held.TryRemove commitId |> ignore
    do! forgetDisk(state, commitId)
  }

  let snapshot(state: State) : Map<string, CommitTranslation> =
    state.Held
    |> Seq.filter(fun pair -> DateTime.UtcNow - fst pair.Value <= ttl)
    |> Seq.map(fun pair -> pair.Key, snd pair.Value)
    |> Map.ofSeq

  let createWith(directory: string, files: Files, log: ILogger) =
    let state: State = {
      Directory = directory
      Files = files
      Log = log
      Held = ConcurrentDictionary<string, DateTime * CommitTranslation>()
    }

    startScan state

    {
      new TranslationCache with
        member _.TryGet(commitId) = tryGet(state, commitId)
        member _.Set(commitId, payload) = set(state, commitId, payload)
        member _.Forget(commitId) = forget(state, commitId)
        member _.Snapshot() = snapshot state
    }

  let create(directory: string, log: ILogger) =
    createWith(directory, filesOf directory, log)
