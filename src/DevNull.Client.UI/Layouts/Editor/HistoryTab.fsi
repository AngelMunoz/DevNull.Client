module DevNull.Client.Layouts.Editor.HistoryTab

open System
open Avalonia.Controls
open Avalonia.Threading
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.UiClock
open MVUish.Avalonia

/// What the frame reads from the pinned history tab: the number the
/// dirty dot shows, when the last page landed, and whether a page is
/// on its way. The number is the page's own `NewCommitsCount`.
[<Struct>]
type Snapshot = {
  NewCount: int
  FetchedAt: DateTimeOffset option
  Loading: bool
}

/// What one history tab needs from its host: the signed workers, the
/// signed-in session, the selected ref, the zone the times show in,
/// the host's active stream, and the frame's snapshot sink.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache> = {
  Env: 'env
  Session: SignedInSession
  Log: LogKind
  Zone: Zone
  Active: IObservable<bool>
  Now: unit -> DateTimeOffset
  OnSnapshot: Snapshot -> unit
}

/// The tab's state logic, exported so the update stays testable
/// without a running page.
module State =

  /// One ref's commits, the pager cursor, the head the poll checks
  /// against, and the page's own new count.
  type Model = {
    Loading: bool
    Error: AppError option
    Items: CommitNode list
    NextCursor: string option
    Head: string option
    NewCount: int option
    Ranked: bool
    /// Whether the one-minute poll runs.
    Polling: bool
    FetchedAt: DateTimeOffset option
    /// The shared clock's last tick, for the relative times.
    Now: DateTimeOffset
  }

  /// The messages of one history tab.
  type Msg =
    | Fetched of Result<LogPage, AppError>
    | MoreRequested
    | Appended of Result<LogPage, AppError>
    | ActiveChanged of active: bool
    | ClockTicked of moment: DateTimeOffset
    | CheckNew
    | NewChecked of Result<LogPage, AppError>

  /// The fresh state: the first page is on its way.
  val initial: Model

  /// Whether this model's subscription set carries the one-minute
  /// poll.
  val polling: model: Model -> bool

  /// What the frame reads from this model.
  val snapshot: model: Model -> Snapshot

  /// Applies one message.
  val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

  /// The tab's inlets from outside: the host's active stream and the
  /// shared clock, plus the poll while the model carries it.
  val subscribe: props: Props<'env> * model: Model -> Sub<Msg>

/// The tab's view: one virtualized feed over the loaded commits, the
/// error card over it, and the load-more footer under the last row.
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
/// disposes it when the tab truly closes.
val mount: dispatcher: IDispatcher * props: Props<'env> -> Control * IDisposable
