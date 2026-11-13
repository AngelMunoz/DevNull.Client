module DevNull.Client.Workbench.Inspector

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

/// One inspector's state: the checked-out commit and its parent chain,
/// the replies and their cursor, the show and replies failures, the
/// clock, and the in-flight star or pastel action. The checkout id is
/// fixed by the props, so no message clears the checkout as a side
/// effect.
type Model = {
  CommitId: string
  Loading: bool
  Error: AppError option
  Commit: Commit option
  Ancestors: CommitNode list
  ReplyParticipants: ReplyParticipant list
  Replies: CommitNode list
  RepliesCursor: string option
  RepliesLoading: bool
  RepliesError: AppError option
  Busy: string option
  Pinning: string option
  PollChoices: Map<string, string>
  Now: DateTimeOffset
}

/// One inspector's messages: the checkout read, the replies pages, the
/// clock, the star and pastel actions, and the parent, author, and
/// quote intents.
type Msg =
  | Shown of Result<CommitPage, AppError>
  | RepliesFetched of Result<Page<CommitNode>, AppError>
  | MoreRepliesRequested
  | RepliesAppended of Result<Page<CommitNode>, AppError>
  | ClockTicked of moment: DateTimeOffset
  | StarToggled of Commit
  | StarDone of Result<Commit, AppError>
  | PinToggled of Commit
  | PinDone of pinned: bool * Result<Commit, AppError>
  | PastelGiven of Commit
  | PastelDone of Result<PastelResult, AppError>
  | ParentWalked of commitId: string
  | ChildOpened of commitId: string
  | AuthorOpened of handle: string
  | QuoteRequested of Commit
  | ReplyToggled of Commit
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | PollVoteRequested of Commit
  | PollVotedDone of Result<Commit, AppError>
  | CommitUpdated of Commit
  | CommitDeleted of string
  | CloseRequested

/// What one inspector needs from its host: the protocol workers, the
/// signed key file and viewer, the pastéis flag, the image cache, the
/// clock ticks, the time zone, and the host's parent, child, author,
/// external, and quote sinks. `CommitId` is the checkout: it never
/// changes through a message.
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
  CommitId: string
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Flyout: DevNull.Client.Components.UserFlyout.Source voption
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  OnCheckoutParent: string -> unit
  OnCheckoutChild: string -> unit
  OnOpenAuthor: string -> unit
  OnOpenExternal: string -> unit
  OnQuote: Commit -> unit
  OnOpenThread: Commit -> unit
  OnClose: unit -> unit
  OnDialog: RowActions.Dialog -> unit
}

/// The inspector's first state and its checkout read.
val init: props: Props<'env> -> Model * Cmd<Msg>

/// The inspector's one update function.
val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The inspector's inlet: the host's clock ticks.
val subscribe: props: Props<'env> -> model: Model -> Sub<Msg>

/// The inspector's view: the checkout header, the author block, the
/// checked-out card, the parent walk, and the replies block.
val view:
  props: Props<'env> ->
  model: IObservable<Model> ->
  dispatch: Dispatch<Msg> ->
    Control

/// The inspector's program. The host starts it with `Programs.runPage`
/// and keeps the returned handle.
val program: props: Props<'env> -> Program<unit, Model, Msg, Control>
