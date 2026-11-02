module DevNull.Client.Views.TranslationModel

open System
open System.Globalization
open FSharp.UMX
open DevNull.Client.Components.CommitCard
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Types
open DevNull.Client.Errors
open DevNull.Client.TranslationCache
open IcedTasks

/// Which body one card shows for a commit.
type Display =
  | Automatic
  | Translated
  | Original

/// One commit's translation, as a page keeps it.
[<Struct>]
type State = {
  Fetched: CommitTranslation option
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

/// What one card asks about a commit.
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

let empty: State = {
  Fetched = None
  Available = None
  Display = Automatic
  Error = None
  CooldownUntil = None
}

let initial: Book = { States = Map.empty; Busy = Set.empty }

let seed(translations: Map<string, CommitTranslation>) : Book = {
  States =
    translations
    |> Map.map(fun _ translation -> {
      empty with
          Fetched = Some translation
          Available = Some translation.Available
    })
  Busy = Set.empty
}

let inline stateOf(book: Book, commitId: string) : State =
  book.States |> Map.tryFind commitId |> Option.defaultValue empty

let view(surface: Surface) : TranslationView voption =
  let state = stateOf(surface.Book, surface.Commit.Id)

  let translation = state.Fetched |> Option.orElse surface.Commit.Translation

  let shown =
    translation
    |> Option.bind(fun translation -> translation.Translated)
    |> Option.filter(fun _ ->
      match state.Display with
      | Original -> false
      | Translated -> true
      | Automatic -> surface.Automatic)

  let available =
    state.Available
    |> Option.defaultValue(
      match surface.Commit.Status with
      | Approved
      | Pending -> surface.Commit.Body <> ""
      | Rejected
      | UnknownStatus _ -> false
    )

  let busy = Set.contains surface.Commit.Id surface.Book.Busy

  let waiting =
    state.CooldownUntil |> Option.exists(fun until -> surface.Now < until)

  let renders =
    shown.IsSome || available || busy || waiting || state.Error.IsSome

  if not renders then
    ValueNone
  else
    ValueSome {
      Body = shown
      SourceName =
        translation |> Option.bind(fun translation -> translation.SourceName)
      CanTranslate = available && shown.IsNone
      Busy = busy
      Waiting = waiting
      Error = state.Error
    }

let request(book: Book, commitId: string) : Book = {
  book with
      Busy = Set.add commitId book.Busy
}

let held(state: State, translation: CommitTranslation) : State = {
  state with
      Fetched = Some translation
      Available = Some translation.Available
      Display = if translation.Available then Translated else Automatic
      Error = None
      CooldownUntil = None
}

let recall
  (cache: TranslationCache, book: Book, commitId: string)
  : Async<Book option> = asyncEx {
  match! cache.TryGet commitId with
  | Some translation when
    translation.Translated.IsSome || not translation.Available
    ->
    return
      Some {
        book with
            States =
              Map.add
                commitId
                (held(stateOf(book, commitId), translation))
                book.States
      }
  | _ -> return None
}

let finish(args: Finish) : Book =
  let state = stateOf(args.Book, args.CommitId)

  let next =
    match args.Result with
    | Ok payload ->
      payload.Translation
      |> Option.map(fun translation -> held(state, translation))
      |> Option.defaultValue {
        state with
            Available = Some false
            Error = None
            CooldownUntil = None
      }
    | Error(Refused(429, _, message)) -> {
        state with
            Error = Some message
            CooldownUntil = Some(args.Now.AddSeconds 60.0)
      }
    | Error(Refused(404, _, _)) ->
        // The site has nothing to translate here; the next visit shows no
        // button.
        {
          state with
              Available = Some false
              Error = None
        }
    | Error(Refused(_, _, message)) -> { state with Error = Some message }
    | Error error -> {
        state with
            Error = Some(describe error)
      }

  {
    args.Book with
        States = Map.add args.CommitId next args.Book.States
        Busy = Set.remove args.CommitId args.Book.Busy
  }

let store(cache: TranslationCache, args: Finish) : Async<unit> = asyncEx {
  match args.Result with
  | Ok payload -> do! cache.Set(args.CommitId, payload)
  | Error(Refused(404, _, _)) -> do! cache.Forget args.CommitId
  | _ -> ()
}

let showOriginal(book: Book, commitId: string) : Book =
  let state = stateOf(book, commitId)

  {
    book with
        States = Map.add commitId { state with Display = Original } book.States
  }

let canReuse(book: Book, commit: Commit) : bool =
  let state = stateOf(book, commit.Id)

  state.Fetched
  |> Option.orElse commit.Translation
  |> Option.bind(fun translation -> translation.Translated)
  |> Option.isSome

let showTranslated(book: Book, commitId: string) : Book =
  let state = stateOf(book, commitId)

  {
    book with
        States =
          Map.add commitId { state with Display = Translated } book.States
  }

let pending(book: Book, commits: Commit list) : Commit list =
  commits
  |> List.filter(fun commit ->
    let translatable =
      match commit.Status with
      | Approved
      | Pending -> commit.Body <> ""
      | Rejected
      | UnknownStatus _ -> false

    translatable
    && Map.tryFind commit.Id book.States
       |> Option.map(fun state -> state.Available.IsNone)
       |> Option.defaultValue true)

/// Walks the pending commits one ask at a time, gathering answers until
/// the list ends or the site's rate limit answers. The walk is a tail
/// call, so a long feed never grows the stack.
[<TailCall>]
let rec walk
  (
    attempt: Commit -> Async<Result<TranslationPayload, AppError>>,
    remaining: Commit list,
    acc: (Commit * Result<TranslationPayload, AppError>) list
  ) : Async<(Commit * Result<TranslationPayload, AppError>) list> = asyncEx {
  match remaining with
  | [] -> return List.rev acc
  | commit :: rest ->
    let! result = attempt commit

    match result with
    | Error(Refused(429, _, _)) ->
      // The budget is spent: stop, and leave the cards to the reader.
      return List.rev((commit, result) :: acc)
    | _ -> return! walk(attempt, rest, (commit, result) :: acc)
}

let auto
  (
    attempt: Commit -> Async<Result<TranslationPayload, AppError>>,
    limit: int,
    commits: Commit list
  ) : Async<(Commit * Result<TranslationPayload, AppError>) list> =
  walk(attempt, commits |> List.truncate limit, [])

let retryDelays = [ 2000; 4000; 8000 ]

[<TailCall>]
let rec execute
  (
    delays: int list,
    attempt: unit -> Async<Result<TranslationPayload, AppError>>
  ) : Async<Result<TranslationPayload, AppError>> = asyncEx {
  let! result = attempt()

  match result with
  | Error(Refused(503, _, _)) when not delays.IsEmpty ->
    do! Async.Sleep delays.Head
    return! execute(delays.Tail, attempt)
  | result -> return result
}

let deviceLanguage =
  let name = CultureInfo.CurrentUICulture.Name

  if String.IsNullOrWhiteSpace name then
    "en"
  else
    name.Split('-').[0].ToLowerInvariant()

/// One server-given code as a language, or none when it is not one.
let ofCode(code: string) : string<LanguageCode> option =
  match LanguageCodes.parse<AppError> code with
  | Ok parsed -> Some parsed
  | Error _ -> None

let languageFor
  (settings: UserSettings option, commit: Commit)
  : string<LanguageCode> =
  let chosen =
    settings
    |> Option.bind(fun settings -> settings.TranslationLanguage)
    |> Option.bind(fun language -> ofCode language.Code)

  let listed =
    settings
    |> Option.bind(fun settings ->
      settings.Languages
      |> List.tryFind(fun language -> language.Code = deviceLanguage))
    |> Option.bind(fun language -> ofCode language.Code)

  commit.Translation
  |> Option.bind(fun translation -> ofCode translation.Language)
  |> Option.orElse chosen
  |> Option.orElse listed
  |> Option.defaultValue LanguageCodes.english
