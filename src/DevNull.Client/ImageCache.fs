module DevNull.Client.ImageCache

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open IcedTasks
open DevNull.Client.Core.Http

/// The image cache worker: raw image bytes by URL, memory first, then
/// the disk folder, then one fetch.
type ImageCacheService
  (
    directory: string,
    fetch: string -> Async<Result<byte array, TransportFailure>>
  ) =

  let gate = obj()
  let memory = Dictionary<string, byte array>()
  let order = List<string>()
  let memoryCapacity = 64

  let remember(url: string, bytes: byte array) =
    lock gate (fun () ->
      if memory.ContainsKey url then
        order.Remove url |> ignore

      order.Add url
      memory[url] <- bytes

      while order.Count > memoryCapacity do
        memory.Remove(order[0]) |> ignore
        order.RemoveAt 0)

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
    | :? DirectoryNotFoundException
    | :? IOException -> None

  let rememberDisk(url: string, bytes: byte array) =
    try
      Directory.CreateDirectory directory |> ignore

      File.WriteAllBytes(Path.Combine(directory, fileNameOf url), bytes)
    with
    | :? UnauthorizedAccessException
    | :? IOException -> ()

  member _.Get(url: string) : Async<byte array option> = asyncEx {
    match tryMemory url with
    | Some bytes -> return Some bytes
    | None ->
      let disk = tryDisk url

      match disk with
      | Some bytes ->
        remember(url, bytes)
        return Some bytes
      | None ->
        match! fetch url with
        | Ok bytes ->
          remember(url, bytes)
          rememberDisk(url, bytes)
          return Some bytes
        | Error _ -> return None
  }

/// Accessor for the image cache capability.
type IProvideImageCache =
  abstract ImageCache: ImageCacheService
