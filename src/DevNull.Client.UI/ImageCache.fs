module DevNull.Client.ImageCache

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open IcedTasks
open Microsoft.Extensions.Logging
open DevNull.Client.Core.Http

type ImageCacheService =
  abstract Get: url: string -> Async<byte array option>

type IProvideImageCache =
  abstract ImageCache: ImageCacheService

module ImageCacheService =

  let memoryCapacity = 64

  // The byte budget, sized from what the server really serves: PNG
  // payloads averaging ~60 KB with a ~800 KB worst case (measured over
  // 370 cached entries), and a visible window of 6-9 images. The disk
  // tier serves every eviction with one local read, so the memory tier
  // only needs the visible window's worth.
  let memoryBudget = 5L * 1024L * 1024L

  type State = {
    Gate: obj
    Memory: Dictionary<string, byte array>
    Order: List<string>
    Directory: string
    Fetch: string -> Async<Result<byte array, TransportFailure>>
    Log: ILogger
  }

  let budgeted(state: State) =
    state.Memory.Values |> Seq.sumBy(fun bytes -> int64 bytes.Length)

  let forget(state: State, url: string) =
    state.Order.Remove url |> ignore
    state.Memory.Remove url |> ignore

  let overCaps(state: State) =
    state.Order.Count > memoryCapacity
    || (budgeted state > memoryBudget && state.Order.Count > 0)

  let remember(state: State, url: string, bytes: byte array) =
    lock state.Gate (fun () ->
      if state.Memory.ContainsKey url then
        forget(state, url)

      // A payload over the whole budget never enters: caching it would
      // evict everything else for one image.
      if int64 bytes.Length <= memoryBudget then
        state.Order.Add url
        state.Memory[url] <- bytes

        // Evict oldest first until both caps hold: entries, bytes.
        while overCaps state do
          forget(state, state.Order[0])
      else
        state.Log.LogTrace(
          "The image of {Bytes} bytes stays out of the memory tier.",
          bytes.Length
        ))

  let tryMemory(state: State, url: string) =
    lock state.Gate (fun () ->
      match state.Memory.TryGetValue url with
      | true, bytes ->
        state.Order.Remove url |> ignore
        state.Order.Add url
        Some bytes
      | false, _ -> None)

  let fileNameOf(url: string) =
    let hash = SHA256.HashData(Encoding.UTF8.GetBytes url)

    // The hex name is a valid file name on every platform and never
    // collides between URLs.
    Convert.ToHexString(hash)[0..31]

  // The disk tier is a performance cache: a read or write failure
  // falls through to the network, never to an error the caller sees.
  let tryDisk(state: State, url: string) : byte array option =
    try
      Some(File.ReadAllBytes(Path.Combine(state.Directory, fileNameOf url)))
    with
    | :? FileNotFoundException
    | :? DirectoryNotFoundException -> None
    | ex ->
      state.Log.LogWarning(ex, "The cached image file was not read.")
      None

  let rememberDisk(state: State, url: string, bytes: byte array) =
    try
      Directory.CreateDirectory state.Directory |> ignore

      File.WriteAllBytes(Path.Combine(state.Directory, fileNameOf url), bytes)
    with
    | :? UnauthorizedAccessException
    | :? IOException ->
      state.Log.LogWarning("The image was not stored in the disk tier.")

  let get(state: State, url: string) : Async<byte array option> = asyncEx {
    match tryMemory(state, url) with
    | Some bytes ->
      state.Log.LogTrace("The image came from the memory tier.")
      return Some bytes
    | None ->
      let disk = tryDisk(state, url)

      match disk with
      | Some bytes ->
        state.Log.LogDebug("The image came from the disk tier.")
        remember(state, url, bytes)
        return Some bytes
      | None ->
        state.Log.LogTrace("The image leaves for the network.")

        match! state.Fetch url with
        | Ok bytes ->
          state.Log.LogDebug("The image arrived ({Bytes} bytes).", bytes.Length)

          remember(state, url, bytes)
          rememberDisk(state, url, bytes)
          return Some bytes
        | Error failure ->
          state.Log.LogWarning(
            "The image was not fetched: {Error}",
            failure.Message.Trim()
          )

          return None
  }

  let create
    (
      directory: string,
      fetch: string -> Async<Result<byte array, TransportFailure>>,
      log: ILogger
    ) =
    let state: State = {
      Gate = obj()
      Memory = Dictionary<string, byte array>()
      Order = List<string>()
      Directory = directory
      Fetch = fetch
      Log = log
    }

    {
      new ImageCacheService with
        member _.Get(url) = get(state, url)
    }
