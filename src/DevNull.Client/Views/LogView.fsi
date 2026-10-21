module DevNull.Client.Views.LogView

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Stores
open DevNull.Client.UiClock
open MVUish.Avalonia
open Navs

/// The log page: one of the three logs, its timeline, and its pager.
/// The page needs the signed request workers and the navigation target.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock> = {
  Env: 'env
  Log: LogKind
  Session: SignedInSession
  Nav: INavigable
  /// The route the page lives in; the page's program lifetime lands
  /// in the route's disposable bag, so leaving or evicting the route
  /// stops the program.
  Context: RouteContext
}

/// One row of the log feed: a day separator, a commit on the rail, or
/// a deletion marker.
type LogRow =
  | Day of string
  | Node of Commit
  | TombstoneRow of Tombstone

/// The page's state logic, exported so the update stays testable
/// without a running page.
module State =

  /// The state of one log page: the loaded items, the pager cursor, the
  /// head the since-poll checks against, the new-commit count the pill
  /// holds, and whether the log is ranked.
  type Model = {
    Loading: bool
    Error: AppError option
    Items: CommitNode list
    NextCursor: string option
    Head: string option
    NewCount: int option
    Ranked: bool
    /// Whether the page sits in the visual tree: the cached log page
    /// pauses its since-poll while it is stashed.
    Attached: bool
    /// The shared clock's last tick, for the relative times.
    Now: DateTimeOffset
    /// Whether the viewer's pastel controls show, live from the session.
    Pasteis: bool
  }

  /// The messages of the log page, including the since-poll pair.
  type Msg =
    | Fetched of Result<LogPage, AppError>
    | ReloadRequested
    | Appended of Result<LogPage, AppError>
    | MoreRequested
    | StarToggled of Commit
    | StarDone of Result<Commit, AppError>
    | PastelGiven of Commit
    | PastelDone of Result<PastelResult, AppError>
    | CheckNew
    | NewChecked of Result<LogPage, AppError>
    | NewRequested
    | NewFetched of Result<LogPage, AppError>
    | AttachedChanged of attached: bool
    | ClockTicked of moment: DateTimeOffset
    | SessionChanged of state: SessionState

  /// The fresh page state.
  val initial: Model

  /// Interleaves day separators between the commits of different days,
  /// carrying the day across tombstones. A ranked log is score ordered,
  /// its days interleave, so it rides without separators.
  val toRows: items: CommitNode list * ranked: bool -> LogRow list

  /// The stable identity of one feed row.
  val keyOf: logRow: LogRow -> string

  /// Applies one message.
  val update:
    props: Props<'env> -> msg: Msg -> model: Model -> Model * Cmd<Msg>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and 'env :> IProvideSession
      and 'env :> IProvideUiClock

/// One log timeline, self-contained: mounts the MVUish program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
