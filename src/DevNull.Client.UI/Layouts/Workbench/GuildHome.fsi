module DevNull.Client.Workbench.GuildHome

open System
open Avalonia.Controls
open MVUish.Avalonia
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.GuildBoard
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore

/// What the board reports to its host: the guild row the board read
/// answered, and the last fetch time.
[<Struct>]
type Report = {
  Guild: Guild option
  FetchedAt: DateTimeOffset option
}

/// The board home's state: the shared board window, the clock, the
/// last fetch, the failure line a card action answers with, and the
/// card action state.
type Model = {
  Board: BoardState
  Now: DateTimeOffset
  FetchedAt: DateTimeOffset option
  Busy: string option
  Error: AppError option
  PollChoices: Map<string, string>
  Pasteis: bool
}

/// The board home's messages: the board pages, the clock, the named
/// refresh requests, the card's open, reply, and quote intents, and
/// the card's star, pastel, and vote actions.
type Msg =
  | BoardLoad
  | BoardMore
  | BoardArrived of
    page: int *
    Result<DevNull.Client.Core.ApiTypes.GuildBoard, AppError>
  | ClockTicked of moment: DateTimeOffset
  | RefreshRequested
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

/// What one board home needs from its host: the protocol workers, the
/// signed session, the guild's handle, the signed key file and viewer,
/// the pastéis flag, the image cache, the clock ticks, the named
/// refresh requests, the time zone, and the host's open, reply, quote,
/// stage, external, report, and dialog sinks.
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
  Handle: string
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Flyout: DevNull.Client.Components.UserFlyout.Source voption
  Now: IObservable<DateTimeOffset>
  /// One request to read the first page again, such as after a post.
  Refresh: IObservable<unit>
  Zone: DevNull.Client.Formatting.Zone
  OnOpenExternal: string -> unit
  OnOpened: Commit -> unit
  OnReply: Commit -> unit
  OnQuote: Commit -> unit
  /// The stage button's sink: open the staging composer aimed at this
  /// board.
  OnStage: string -> unit
  OnReport: Report -> unit
  OnDialog: RowActions.Dialog -> unit
}

/// The home's first state and its first board page.
val init: props: Props<'env> -> Model * Cmd<Msg>

/// The home's one update function.
val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The home's inlets: the host's clock ticks and its named refresh
/// requests.
val subscribe: props: Props<'env> -> model: Model -> Sub<Msg>

/// The home's program. The host starts it with `Programs.runPage` and
/// keeps the returned handle.
val program: props: Props<'env> -> Program<unit, Model, Msg, Control>
