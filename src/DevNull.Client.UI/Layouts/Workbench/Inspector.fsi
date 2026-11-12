module DevNull.Client.Layouts.Workbench.Inspector

open System
open Avalonia.Controls
open MVUish.Avalonia
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache

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
  | PastelGiven of Commit
  | PastelDone of Result<PastelResult, AppError>
  | ParentWalked of commitId: string
  | AuthorOpened of handle: string
  | QuoteRequested of Commit

/// What one inspector needs from its host: the protocol workers, the
/// signed key file and viewer, the pastéis flag, the image cache, the
/// clock ticks, the time zone, and the host's parent, author, external,
/// and quote sinks. `CommitId` is the checkout: it never changes
/// through a message.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
  CommitId: string
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  OnCheckoutParent: string -> unit
  OnOpenAuthor: string -> unit
  OnOpenExternal: string -> unit
  OnQuote: Commit -> unit
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
