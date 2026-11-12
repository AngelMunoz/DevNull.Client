module DevNull.Client.Layouts.Editor.ThreadTab

open System
open Avalonia.Controls
open Avalonia.Threading
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.ReplyPicker
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.UiClock
open MVUish.Avalonia

/// What the frame reads from one thread tab: the commit, the replies
/// count the last read carried, the people a reply may reach, and the
/// read's state.
[<Struct>]
type Snapshot = {
  CommitId: string
  RepliesCount: int
  /// The people a reply may reach: the thread's author first, then
  /// the read's reply participants.
  Participants: ParticipantRow list
  Loading: bool
  Error: AppError option
}

/// What one thread tab needs from its host: the signed workers, the
/// signed-in session, the commit id, the zone the times show in, the
/// frame's snapshot sink, and the quote and reply intents the terminal
/// panel routes in Phase 3.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache> = {
  Env: 'env
  Session: SignedInSession
  CommitId: string
  Zone: Zone
  Now: unit -> DateTimeOffset
  OnSnapshot: Snapshot -> unit
  /// One read's result, for the frame's output log.
  OnFetch: Result<unit, string> -> unit
  OnQuote: Commit -> unit
  OnReplyRequested: Commit -> unit
}

/// The tab's state logic, exported so the update stays testable
/// without a running page.
module State =

  /// One commit, its ancestors, its replies, and the tab's transient
  /// state.
  type Model = {
    Loading: bool
    Error: AppError option
    Page: CommitPage option
    Replies: CommitNode list
    RepliesCursor: string option
    /// The commit whose star, vote, or pastel call runs now.
    Busy: string option
    /// The option the viewer picked in the checked-out commit's poll.
    PollChoices: Map<string, string>
    /// The shared clock's last tick, for the relative times.
    Now: DateTimeOffset
    /// The last breadcrumb peek read: one commit id and its page.
    Peek: (string * CommitPage) option
  }

  /// The messages of one thread tab.
  type Msg =
    | ThreadFetched of Result<CommitPage, AppError>
    | RepliesFetched of Result<Page<CommitNode>, AppError>
    | MoreReplies
    | StarToggled of Commit
    | StarDone of Result<Commit, AppError>
    | PastelGiven of Commit
    | PastelDone of Result<PastelResult, AppError>
    | PollVoteRequested of Commit
    | PollVotedDone of Result<Commit, AppError>
    | CardEvent of Event
    | PeekRequested of commitId: string
    | Peeked of commitId: string * Result<CommitPage, AppError>
    | ClockTicked of moment: DateTimeOffset

  /// The fresh state: the commit and its first replies page are on
  /// their way.
  val initial: Model

  /// What the frame reads from this model.
  val snapshot: props: Props<'env> * model: Model -> Snapshot

  /// The page cached for one breadcrumb, when the cache holds it.
  val peekCached: model: Model * commitId: string -> CommitPage option

  /// Whether a breadcrumb's hover must read the commit; a held cache
  /// entry means no read.
  val needsPeek: model: Model * commitId: string -> bool

  /// Applies one message.
  val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

  /// The tab's inlets from outside: the shared clock.
  val subscribe: props: Props<'env> * model: Model -> Sub<Msg>

/// The tab's view: the breadcrumb line, the full checked-out card, the
/// replies header, the dense reply rows, and the load-more control.
val view:
  props: Props<'env> *
  model: IObservable<State.Model> *
  dispatch: Dispatch<State.Msg> ->
    Control

/// The tab's program, with its update loop bound to the caller's
/// dispatcher.
val program:
  props: Props<'env> -> Program<unit, State.Model, State.Msg, Control>

/// Starts the tab's program. The caller owns the returned handle and
/// disposes it when the tab closes.
val mount: dispatcher: IDispatcher * props: Props<'env> -> Control * IDisposable

/// The live thread programs of this process, so a host can prove that
/// closing a tab disposed its program exactly once.
module Live =

  /// The number of thread programs mounted and not disposed.
  val current: unit -> int

  /// Every change of that number.
  val observe: IObservable<int>
