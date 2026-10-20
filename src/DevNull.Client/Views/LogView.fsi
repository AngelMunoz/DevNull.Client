module DevNull.Client.Views.LogView

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Stores
open MVUish.Avalonia
open Navs

/// The log page: one of the three logs, its timeline, and its pager.
/// The page needs the signed request workers and the navigation target.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
  Log: LogKind
  Session: SignedInSession
  Nav: INavigable
}

/// One row of the log feed: a day separator, a commit on the rail, or
/// a deletion marker.
type LogRow =
  | Day of string
  | Node of Commit * prevLane: Lane option
  | TombstoneRow of Tombstone

/// The state of one log page: the loaded items, the pager cursor, the
/// head the since-poll checks against, and the commits the pill holds.
type Model = {
  Loading: bool
  Error: AppError option
  Items: CommitNode list
  NextCursor: string option
  Head: string option
  New: CommitNode list option
}

/// The messages of the log page, including the since-poll pair.
type Msg =
  | Fetched of Result<LogPage, AppError>
  | ReloadRequested
  | Appended of Result<LogPage, AppError>
  | MoreRequested
  | StarToggled of Commit
  | StarDone of Result<Commit, AppError>
  | CheckNew
  | NewChecked of Result<LogPage, AppError>
  | NewRequested

/// The fresh page state.
val initial: Model

/// Interleaves day separators and carries the previous row's lane, so
/// every commit paints the rail segment above its node.
val toRows: items: CommitNode list -> LogRow list

/// The stable identity of one feed row.
val keyOf: logRow: LogRow -> string

/// Applies one message. Exported so the since-poll stays testable
/// without a running page.
val update:
  props: Props<'env> -> msg: Msg -> model: Model -> Model * Cmd<Msg>
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp

/// One log timeline, self-contained: mounts the MVUish program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
