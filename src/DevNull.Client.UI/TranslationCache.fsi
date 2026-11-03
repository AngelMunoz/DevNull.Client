module DevNull.Client.TranslationCache

open System
open DevNull.Client.Core.ApiTypes
open Microsoft.Extensions.Logging

/// The translations fetched before, by commit id: memory first, then one
/// file per commit under the given tier. No member blocks its caller on
/// a file: the reads and writes ride the caller's async. The log names
/// each answer, so a request trail shows the disk coming first.
type TranslationCache =
  /// The translation held for one commit, from the memory tier or the
  /// file tier. A file answer also joins the memory tier.
  abstract TryGet: commitId: string -> Async<CommitTranslation option>

  /// Keeps one answer: the value in memory, and the server's own JSON on
  /// the file tier. An answer with no translation drops both, and the
  /// drop sweeps foreign files away beside it.
  abstract Set: commitId: string * payload: TranslationPayload -> Async<unit>

  /// Drops one commit's translation, when the site says it has none, and
  /// sweeps foreign files away beside it.
  abstract Forget: commitId: string -> Async<unit>

  /// Every translation still under its seven-day bound, for a page that
  /// mounts. The memory tier only.
  abstract Snapshot: unit -> Map<string, CommitTranslation>

/// Accessor for the translation cache capability.
type IProvideTranslations =
  abstract Translations: TranslationCache

/// The translation cache over its memory tier and one file tier.
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

  /// The app's own cache: the directory under the local app data.
  val create: directory: string * log: ILogger -> TranslationCache

  /// The cache over the given directory and file tier.
  val createWith:
    directory: string * files: Files * log: ILogger -> TranslationCache
