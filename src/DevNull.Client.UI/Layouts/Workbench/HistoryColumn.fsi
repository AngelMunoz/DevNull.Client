module DevNull.Client.Layouts.Workbench.HistoryColumn

open System
open Avalonia.Controls
open MVUish.Avalonia
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache

/// One history column's state: one log's loaded rows, its cursor and
/// head, the last page fetch, the new-commit count, the poll's gate
/// and the newer commits that poll already heard, the clock, and the
/// poll choices and in-flight action the cards follow.
type Model = {
  Loading: bool
  Error: AppError option
  Items: CommitNode list
  NextCursor: string option
  Head: string option
  NewCount: int option
  Ranked: bool
  Polling: bool
  Now: DateTimeOffset
  FetchedAt: DateTimeOffset option
  Busy: string option
  PollChoices: Map<string, string>
  Pasteis: bool
  HeardIds: string list
  Offset: float
}

/// One history column's place on its ref: the scroll offset and the
/// newest commit id it had loaded.
[<Struct>]
type Place = { Offset: float; Head: string option }

/// What a history column reports to its host: the place it holds and
/// the last page fetch time.
[<Struct>]
type Report = {
  Place: Place
  FetchedAt: DateTimeOffset option
}

/// One history column's messages: the first page, the cursor, the
/// since-poll and its pill, the clock, the poll gate, the card's open
/// and quote intents, and the card's star, pastel, and vote actions.
type Msg =
  | Fetched of Result<LogPage, AppError>
  | MoreRequested
  | Appended of Result<LogPage, AppError>
  | CheckNew
  | NewChecked of Result<LogPage, AppError>
  | NewRequested
  | NewFetched of Result<LogPage, AppError>
  | ClockTicked of moment: DateTimeOffset
  | PollingChanged of active: bool
  | Scrolled of offset: float
  | Opened of Commit
  | QuoteRequested of Commit
  | StarToggled of Commit
  | StarDone of Result<Commit, AppError>
  | PastelGiven of Commit
  | PastelDone of Result<PastelResult, AppError>
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | PollVoteRequested of Commit
  | PollVotedDone of Result<Commit, AppError>

/// What one history column needs from its host: the protocol workers,
/// the signed key file and viewer, the pastéis flag, the image cache,
/// the clock ticks, the time zone, and the host's open, quote, and
/// external-link sinks.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
  Log: LogKind
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  OnOpenExternal: string -> unit
  OnOpened: Commit -> unit
  OnQuote: Commit -> unit
  Restore: Place option
  OnReport: Report -> unit
}

/// The column's first state and its first page fetch.
val init: props: Props<'env> -> Model * Cmd<Msg>

/// The column's one update function.
val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The column's inlets: the host's clock ticks and the one-minute
/// since-poll while `Polling` holds.
val subscribe: props: Props<'env> -> model: Model -> Sub<Msg>

/// The column's view: the ref header, the fetch and new-count line,
/// the status line, and the virtualized feed of dense cards.
val view:
  props: Props<'env> ->
  model: IObservable<Model> ->
  dispatch: Dispatch<Msg> ->
    Control

/// The column's program. The host starts it with `Programs.runPage`
/// and keeps the returned handle.
val program: props: Props<'env> -> Program<unit, Model, Msg, Control>
