module DevNull.Client.Workbench.SearchResults

open System
open Avalonia.Controls
open MVUish.Avalonia
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Env
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore

/// The search center's state: its normalized term, the manual read and
/// its failure, the live results, and the clock the cards read against.
type Model = {
  Term: string
  Loading: bool
  Error: AppError option
  Results: CommitNode list
  PollChoices: Map<string, string>
  Now: DateTimeOffset
}

/// The search center's messages: the read, its manual refresh, the
/// clock, the row intents, and the close request.
type Msg =
  | Fetched of Result<CommitNode list, AppError>
  | RefreshRequested
  | FirstResultRequested
  | ClockTicked of moment: DateTimeOffset
  | Opened of Commit
  | QuoteRequested of Commit
  | ExternalOpened of url: string
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | PollVoteRequested of Commit
  | PollVotedDone of Result<Commit, AppError>
  | CommitUpdated of Commit
  | CommitDeleted of string
  | CloseRequested

/// What the search center needs from its host: the protocol workers,
/// the signed key file and viewer, the pastéis flag, the image cache,
/// the clock and zone, the normalized term, and the row and close sinks.
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
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  Term: string
  OnOpenThread: Commit -> unit
  OnQuote: Commit -> unit
  OnOpenExternal: string -> unit
  OnClose: unit -> unit
  OnReportCommit: Commit -> unit
  OnShare: Commit -> unit
}

/// The search center's first state and its one manual commit read.
val init: props: Props<'env> -> Model * Cmd<Msg>

/// The search center's one update function.
val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The search center's inlet: the host's clock ticks.
val subscribe: props: Props<'env> -> model: Model -> Sub<Msg>

/// The search center's list and its manual refresh control.
val view:
  props: Props<'env> ->
  model: IObservable<Model> ->
  dispatch: Dispatch<Msg> ->
    Control

/// The search center's program. It reads once and never polls.
val program: props: Props<'env> -> Program<unit, Model, Msg, Control>
