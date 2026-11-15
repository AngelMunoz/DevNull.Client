module DevNull.Client.TranslationCache

open System
open DevNull.Client.Core.ApiTypes
open Microsoft.Extensions.Logging

/// The translations fetched before, by commit id: one file per commit
/// under the given tier. No member blocks its caller on a file: the reads
/// and writes ride the caller's async. The cache keeps no process-wide
/// dictionary and runs no startup scan; every answer is read on demand.
type TranslationCache =
  /// The translation held for one commit, from the file tier alone.
  /// A missing, expired, or corrupt file answers `None`.
  abstract TryGet: commitId: string -> Async<CommitTranslation option>

  /// Keeps one answer: the server's own JSON on the file tier. An answer
  /// with no translation drops the file instead.
  abstract Set: commitId: string * payload: TranslationPayload -> Async<unit>

  /// Drops one commit's file, when the site says it has none, and sweeps
  /// foreign files away beside it.
  abstract Forget: commitId: string -> Async<unit>

/// Accessor for the translation cache capability.
type IProvideTranslations =
  abstract Translations: TranslationCache

/// The translation cache over one file tier.
module TranslationCache =

  /// The cache's own file tier, injected: read one file with its write
  /// time, keep one file, drop one file, and list the file paths. The
  /// app's tier is the directory the cache names; a test brings its own.
  [<Struct>]
  type Files = {
    Read: string -> Async<(string * DateTime) option>
    Keep: string * string -> Async<unit>
    Drop: string -> unit
    List: unit -> string list
  }

  /// The app's cache: the directory under the local app data.
  val create: directory: string * log: ILogger -> TranslationCache

  /// The cache over the given directory and file tier. Construction reads
  /// no file and lists none.
  val createWith:
    directory: string * files: Files * log: ILogger -> TranslationCache
