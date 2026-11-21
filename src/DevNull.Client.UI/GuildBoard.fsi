module DevNull.Client.GuildBoard

open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors

/// The board as one surface holds it: the guild row the latest read
/// answered, the commits loaded so far — newest first, the pinned
/// commit keeping its place among them — the pin beside them, the
/// moderator fact, the page the rows hold, whether a further numbered
/// page stands behind them, whether a read is out, the page the last
/// ask named, and the two failure facts: the 404 the flag-off server
/// or an unknown handle answers, and any other failure, which a fresh
/// load retries.
type BoardState = {
  Guild: Guild option
  Commits: CommitNode list
  Pinned: CommitNode option
  CanModerate: bool
  CurrentPage: int
  HasMore: bool
  Loading: bool
  Wanted: int voption
  Refused: bool
  Failed: bool
}

/// The shared board load, the FeedWindow's shape: pure transitions
/// over the state a layout's own program holds. The program is already
/// a serialized state machine — it owns the board in its model, runs
/// `Guilds.show` as a command, and folds the answers through these
/// functions. No service, no subject, no second loop.
///
/// The update arms a layout writes:
///
///     | LoadBoard ->
///       { model with Board = GuildBoard.load model.Board },
///       Cmd.OfAsync.perform Guilds.show (env, { … Page = 1 })
///         (fun outcome -> BoardArrived(1, outcome))
///     | MoreBoard ->
///       let next = GuildBoard.more model.Board
///       let cmd =
///         match next.Wanted with
///         | ValueSome page when next.Wanted <> model.Board.Wanted ->
///           Cmd.OfAsync.perform Guilds.show (env, { … Page = page })
///             (fun outcome -> BoardArrived(page, outcome))
///         | _ -> Cmd.none
///       { model with Board = next }, cmd
///     | BoardArrived(page, outcome) ->
///       { model with Board = GuildBoard.arrived(model.Board, page, outcome) },
///       Cmd.none
///
/// A failed read's cause is the layout's to log; the transitions stay
/// pure.
module GuildBoard =

  /// The state before the first read lands.
  val empty: BoardState

  /// Marks page one asked: loading, the failure facts cleared. The
  /// caller always fires the page-one read — a load is also the retry
  /// of a failed one.
  val load: state: BoardState -> BoardState

  /// Marks the next page asked when the server holds one and no read
  /// is out; otherwise the state unchanged. The caller fires the read
  /// when `Wanted` advanced.
  val more: state: BoardState -> BoardState

  /// Applies one answer: the rows replace on page one, append behind
  /// on a later page. Only the wanted page applies — a stale answer
  /// for a page the surface already left behind leaves the state
  /// unchanged. The 404 marks the state refused; any other failure
  /// marks it failed.
  val arrived:
    state: BoardState *
    page: int *
    outcome: Result<DevNull.Client.Core.ApiTypes.GuildBoard, AppError> ->
      BoardState

  /// The board's rows in display order: the pinned commit first, once,
  /// then the commits the board holds with the pinned one left out.
  /// Each row carries its pinned fact, so a surface draws its own
  /// marker on the rows that ask for one.
  val ordered: state: BoardState -> (CommitNode * bool) list
