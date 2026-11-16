module DevNull.Client.Workbench.HistoryColumn

open System
open Avalonia.Controls
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore

/// One history column's state: one log's loaded rows, its cursor and
/// head, the last page fetch, the new-commit count, the poll's gate
/// and the newer commits that poll already heard, the clock, and the
/// poll choices and in-flight action the cards follow.
type Model = {
  Loading: bool
  Error: AppError option
  Feed: FeedWindow.Window<CommitNode>
  Anchor: FeedWindow.Anchor voption
  Head: string option
  NewCount: int option
  Ranked: bool
  Polling: bool
  Now: DateTimeOffset
  FetchedAt: DateTimeOffset option
  Busy: string option
  PollChoices: Map<string, string>
  Pasteis: bool
}

/// One history column's place on its ref: the reading anchor and the
/// newest commit id it had loaded.
[<Struct>]
type Place = {
  Anchor: FeedWindow.Anchor voption
  Head: string option
}

/// What a history column reports to its host: the place it holds and
/// the last page fetch time.
[<Struct>]
type Report = {
  Place: Place
  FetchedAt: DateTimeOffset option
}

/// One history column's messages: the first page, the cursor, the
/// since-poll and its pill, the clock, the poll gate, the card's open,
/// reply, and quote intents, and the card's star, pastel, and vote
/// actions.
type Msg =
  | Fetched of cursor: string option * Result<LogPage, AppError>
  | MoreRequested
  | NewerRequested
  | Appended of cursor: string option * Result<LogPage, AppError>
  | Prepended of cursor: string option * Result<LogPage, AppError>
  | AnchorMoved of FeedWindow.Anchor
  | CheckNew
  | NewChecked of Result<LogPage, AppError>
  | NewRequested
  | NewFetched of Result<LogPage, AppError>
  | ClockTicked of moment: DateTimeOffset
  | RefreshRequested
  | PollingChanged of active: bool
  | Opened of Commit
  | QuoteRequested of Commit
  | ReplyRequested of Commit
  | StarToggled of Commit
  | StarDone of Result<Commit, AppError>
  | PastelGiven of Commit
  | PastelDone of Result<PastelResult, AppError>
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | PollVoteRequested of Commit
  | PollVotedDone of Result<Commit, AppError>
  | CommitUpdated of Commit
  | CommitDeleted of string

/// What one history column needs from its host: the source list, the
/// protocol workers, the signed session, the pastéis flag, the image
/// cache, the clock ticks, the named refresh requests, the time zone,
/// the last-visit marker the log draws, and the host's open, reply,
/// quote, report, share, and external-link sinks.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession option
  Source: HistorySource
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Flyout: DevNull.Client.Components.UserFlyout.Source voption
  Now: IObservable<DateTimeOffset>
  /// One request to read the first page again, such as after a post.
  Refresh: IObservable<unit>
  Zone: Zone
  /// The newest commit time the last visit saw, when the log has one.
  LastVisit: DateTimeOffset option
  OnOpenExternal: string -> unit
  OnOpened: Commit -> unit
  OnReply: Commit -> unit
  OnQuote: Commit -> unit
  Restore: Place option
  OnReport: Report -> unit
  OnDialog: RowActions.Dialog -> unit
}

/// The rows the window holds, in display order.
val internal itemsOf: model: Model -> CommitNode list

/// The older cursor of the window.
val internal olderOf: model: Model -> string voption

/// A model with one loaded page, for tests and fixtures.
val internal withPage:
  items: CommitNode list * nextCursor: string option * model: Model -> Model

/// The column's first state and its first page fetch.
val init: props: Props<'env> -> Model * Cmd<Msg>

/// The column's one update function.
val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The column's inlets: the host's clock ticks, its named refresh
/// requests, and the one-minute since-poll while `Polling` holds.
val subscribe: props: Props<'env> -> model: Model -> Sub<Msg>

/// The column's view: the ref header, the fetch and new-count line,
/// the status line, the day separators and last-visit marker, and the
/// virtualized feed of dense cards.
val view:
  props: Props<'env> ->
  model: IObservable<Model> ->
  dispatch: Dispatch<Msg> ->
    Control

/// The column's program. The host starts it with `Programs.runPage`
/// and keeps the returned handle.
val program: props: Props<'env> -> Program<unit, Model, Msg, Control>
