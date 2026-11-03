module DevNull.Client.ImageCache

open Microsoft.Extensions.Logging
open DevNull.Client.Core.Http

/// The image cache worker: raw image bytes by URL. The memory tier
/// holds the newest entries, the disk folder holds every fetched
/// entry, and one fetch backs both. Commit images never change under
/// their URL, so no entry ever invalidates.
type ImageCacheService =
  /// The bytes of one URL, or `None` when every tier missed. The
  /// caller keeps its placeholder on `None`.
  abstract Get: url: string -> Async<byte array option>

/// Accessor for the image cache capability.
type IProvideImageCache =
  abstract ImageCache: ImageCacheService

/// The image cache worker over its own memory and disk tiers.
module ImageCacheService =

  /// The image cache worker over the given directory, fetch, and log.
  val create:
    directory: string *
    fetch: (string -> Async<Result<byte array, TransportFailure>>) *
    log: ILogger ->
      ImageCacheService
