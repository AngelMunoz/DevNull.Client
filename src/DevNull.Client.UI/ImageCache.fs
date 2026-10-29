module DevNull.Client.ImageCache

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open IcedTasks
open Microsoft.Extensions.Logging
open DevNull.Client.Core.Http

/// The image cache worker: raw image bytes by URL, memory first, then
/// the disk folder, then one fetch.
type ImageCacheService
  (
    directory: string,
    fetch: string -> Async<Result<byte array, TransportFailure>>,
    log: ILogger
  ) =

  let gate = obj()
  let memory = Dictionary<string, byte array>()
  let order = List<string>()
  let memoryCapacity = 64

  // The byte budget, sized from what the server really serves: PNG
  // payloads averaging ~60 KB with a ~800 KB worst case (measured over
  // 370 cached entries), and a visible window of 6-9 images. The disk
  // tier serves every eviction with one local read, so the memory tier
  // only needs the visible window's worth.
  let memoryBudget = 5L * 1024L * 1024L

  let budgeted() =
    memory.Values |> Seq.sumBy(fun bytes -> int64 bytes.Length)

  let forget(url: string) =
    order.Remove url |> ignore
    memory.Remove url |> ignore

  let overCaps() =
    order.Count > memoryCapacity
    || (budgeted() > memoryBudget && order.Count > 0)

  let remember(url: string, bytes: byte array) =
    lock gate (fun () ->
      if memory.ContainsKey url then
        forget url

      // A payload over the whole budget never enters: caching it would
      // evict everything else for one image.
      if int64 bytes.Length <= memoryBudget then
        order.Add url
        memory[url] <- bytes

        // Evict oldest first until both caps hold: entries, bytes.
        while overCaps() do
          forget order[0]
      else
        log.LogTrace(
          "The image of {Bytes} bytes stays out of the memory tier.",
          bytes.Length
        ))

  let tryMemory(url: string) =
    lock gate (fun () ->
      match memory.TryGetValue url with
      | true, bytes ->
        order.Remove url |> ignore
        order.Add url
        Some bytes
      | false, _ -> None)

  let fileNameOf(url: string) =
    let hash = SHA256.HashData(Encoding.UTF8.GetBytes url)

    // The hex name is a valid file name on every platform and never
    // collides between URLs.
    Convert.ToHexString(hash)[0..31]

  // The disk tier is a performance cache: a read or write failure
  // falls through to the network, never to an error the caller sees.
  let tryDisk(url: string) : byte array option =
    try
      Some(File.ReadAllBytes(Path.Combine(directory, fileNameOf url)))
    with
    | :? FileNotFoundException
    | :? DirectoryNotFoundException -> None
    | ex ->
      log.LogWarning(ex, "The cached image file was not read.")
      None

  let rememberDisk(url: string, bytes: byte array) =
    try
      Directory.CreateDirectory directory |> ignore

      File.WriteAllBytes(Path.Combine(directory, fileNameOf url), bytes)
    with
    | :? UnauthorizedAccessException
    | :? IOException ->
      log.LogWarning("The image was not stored in the disk tier.")

  member _.Get(url: string) : Async<byte array option> = asyncEx {
    match tryMemory url with
    | Some bytes ->
      log.LogTrace("The image came from the memory tier.")
      return Some bytes
    | None ->
      let disk = tryDisk url

      match disk with
      | Some bytes ->
        log.LogDebug("The image came from the disk tier.")
        remember(url, bytes)
        return Some bytes
      | None ->
        log.LogTrace("The image leaves for the network.")

        match! fetch url with
        | Ok bytes ->
          log.LogDebug("The image arrived ({Bytes} bytes).", bytes.Length)
          remember(url, bytes)
          rememberDisk(url, bytes)
          return Some bytes
        | Error failure ->
          log.LogWarning(
            "The image was not fetched: {Error}",
            failure.Message.Trim()
          )

          return None
  }

/// Accessor for the image cache capability.
type IProvideImageCache =
  abstract ImageCache: ImageCacheService
