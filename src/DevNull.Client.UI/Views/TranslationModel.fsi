module DevNull.Client.Views.TranslationModel

open System
open FSharp.UMX
open DevNull.Client.Components.CommitCard
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.TranslationCache

/// Which body one card shows for a commit.
type Display =
  | Automatic
  | Translated
  | Original

/// One commit's translation, as a page keeps it: the answer it holds, the
/// display the reader chose, the last failure, and the moment the server's
/// rate limit lets the next ask through. Pages fold answers in through
/// `finish`; they never build a `State` by hand.
[<Struct>]
type State = {
  Fetched: CommitTranslation option
  /// What the server said about this commit: none before the first ask,
  /// `Some false` when it has nothing to translate.
  Available: bool option
  Display: Display
  Error: string option
  CooldownUntil: DateTimeOffset option
}

/// Every commit's translation on one page, with the commits whose calls run.
[<Struct>]
type Book = {
  States: Map<string, State>
  Busy: Set<string>
}

/// What one card asks about a commit: the page's automatic choice, its
/// book, the clock, and the commit.
[<Struct>]
type Surface = {
  Automatic: bool
  Book: Book
  Now: DateTimeOffset
  Commit: Commit
}

/// One translate answer to fold into the book.
[<Struct>]
type Finish = {
  Book: Book
  Now: DateTimeOffset
  CommitId: string
  Result: Result<TranslationPayload, AppError>
}

/// No commit translated yet, nothing running.
val initial: Book

/// The book a page starts with: every translation fetched before, ready
/// to show under the automatic choice.
val seed: translations: Map<string, CommitTranslation> -> Book

/// What one card shows about a commit's translation, or none when it has
/// nothing to say.
val view: surface: Surface -> TranslationView voption

/// Marks one commit's translate call as running.
val request: book: Book * commitId: string -> Book

/// Folds one answer in: a held translation shows, a 429 blocks the button
/// for the route's minute, a 404 leaves the commit to the next visit, and
/// every other failure keeps the last body with the server's words.
val finish: args: Finish -> Book

/// Keeps one answer in the cache: an answer stays for the next visit, a
/// not-found drops it. The answer itself folds into the book through
/// `finish`.
val store: cache: TranslationCache * args: Finish -> Async<unit>

/// The book holding a translation read from the cache, or none when the
/// cache has no answer or still owes the body. A manual Translate asks
/// this before it posts; the disk read runs off the UI thread.
val recall:
  cache: TranslationCache * book: Book * commitId: string -> Async<Book option>

/// Returns one commit to its original body.
val showOriginal: book: Book * commitId: string -> Book

/// True when the commit's translation is already at hand, so showing it
/// asks the server for nothing.
val canReuse: book: Book * commit: Commit -> bool

/// Shows the translation already at hand.
val showTranslated: book: Book * commitId: string -> Book

/// The commits a page still asks about on its own: the ones the site
/// may translate and no answer has settled yet, in page order.
val pending: book: Book * commits: Commit list -> Commit list

/// Asks for the commits' translations one at a time, up to the limit, and
/// stops at the first rate limit. The answers arrive as one list.
val auto:
  attempt: (Commit -> Async<Result<TranslationPayload, AppError>>) *
  limit: int *
  commits: Commit list ->
    Async<(Commit * Result<TranslationPayload, AppError>) list>

/// How long each 503 retry waits, in milliseconds.
val retryDelays: int list

/// Runs one translate call, and tries again at growing intervals while the
/// server answers a 503. Any other refusal comes back at once.
val execute:
  delays: int list *
  attempt: (unit -> Async<Result<TranslationPayload, AppError>>) ->
    Async<Result<TranslationPayload, AppError>>

/// The device's primary language, lowercased: the UI culture's first part.
val deviceLanguage: string

/// The language to ask for: the commit's own, the chosen one, the device's
/// when the server lists it, or English.
val languageFor:
  settings: UserSettings option * commit: Commit -> string<LanguageCode>
