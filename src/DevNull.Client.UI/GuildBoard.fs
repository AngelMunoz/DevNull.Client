module DevNull.Client.GuildBoard

open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors

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

module GuildBoard =

  let empty = {
    Guild = None
    Commits = []
    Pinned = None
    CanModerate = false
    CurrentPage = 0
    HasMore = false
    Loading = false
    Wanted = ValueNone
    Refused = false
    Failed = false
  }

  let load(state: BoardState) : BoardState = {
    state with
        Loading = true
        Refused = false
        Failed = false
        Wanted = ValueSome 1
  }

  let more(state: BoardState) : BoardState =
    if state.HasMore && not state.Loading then
      {
        state with
            Loading = true
            Wanted = ValueSome(state.CurrentPage + 1)
      }
    else
      state

  let arrived
    (
      state: BoardState,
      page: int,
      outcome: Result<ApiTypes.GuildBoard, AppError>
    ) : BoardState =
    // A stale answer: the surface already asked past this page.
    if state.Wanted <> ValueSome page then
      state
    else
      match outcome with
      | Ok board ->
        let commits =
          if page <= 1 then
            board.Items
          else
            state.Commits @ board.Items

        {
          Guild = Some board.Guild
          Commits = commits
          Pinned = board.PinnedCommit
          CanModerate = board.CanModerate
          CurrentPage = page
          HasMore = board.HasMore
          Loading = false
          Wanted = ValueNone
          Refused = false
          Failed = false
        }
      // Only the 404 the flag-off server or an unknown handle answers
      // marks the state refused; any other failure stays retryable.
      | Error(Refused(status, _, _)) when status = 404 -> {
          state with
              Loading = false
              Wanted = ValueNone
              Refused = true
        }
      | Error _ -> {
          state with
              Loading = false
              Wanted = ValueNone
              Failed = true
        }
