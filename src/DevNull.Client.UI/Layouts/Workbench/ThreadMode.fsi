module DevNull.Client.Workbench.ThreadMode

open System
open Avalonia.Controls
open MVUish.Avalonia
open DevNull.Client.AttachmentSources
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

/// One thread's state: the checked-out commit and its parent chain, the
/// replies and their cursor, the show and replies failures, the clock,
/// and the reply participants the composer picks from. The commit id is
/// fixed by the props, so no message changes the thread behind it.
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
  PollChoices: Map<string, string>
  Now: DateTimeOffset
}

/// One thread's messages: the thread read, the replies pages, the clock,
/// the posted reply, and the back, parent, and quote intents.
type Msg =
  | Shown of Result<CommitPage, AppError>
  | RepliesFetched of Result<Page<CommitNode>, AppError>
  | MoreRepliesRequested
  | RepliesAppended of Result<Page<CommitNode>, AppError>
  | Posted of commit: Commit
  | ClockTicked of moment: DateTimeOffset
  | BackRequested
  | Opened of Commit
  | QuoteRequested of Commit
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | PollVoteRequested of Commit
  | PollVotedDone of Result<Commit, AppError>
  | CommitUpdated of Commit
  | CommitDeleted of string

/// What one thread needs from its host: the protocol workers, the
/// signed key file and viewer, the pastéis flag, the image cache, the
/// clock ticks, the time zone, and the back, parent, external, and
/// quote sinks. `CommitId` is the thread's commit.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  CommitId: string
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  OnBack: unit -> unit
  OnOpenThread: Commit -> unit
  OnOpenExternal: string -> unit
  OnQuote: Commit -> unit
  OnReportCommit: Commit -> unit
  OnShare: Commit -> unit
}

/// The thread's two regions: the center thread and the PARENTS chain
/// the host places above the inspector checkout.
[<Struct>]
type View = { Center: Control; Parents: Control }

/// The thread's first state and its first thread read.
val init: props: Props<'env> -> Model * Cmd<Msg>

/// The thread's one update function.
val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The thread's inlet: the host's clock ticks.
val subscribe: props: Props<'env> -> model: Model -> Sub<Msg>

/// The thread's two views: the center and the PARENTS chain.
val view:
  props: Props<'env> ->
  model: IObservable<Model> ->
  dispatch: Dispatch<Msg> ->
    View

/// The thread's program. The host starts it with `Programs.runPage` and
/// keeps the returned handle.
val program: props: Props<'env> -> Program<unit, Model, Msg, View>
