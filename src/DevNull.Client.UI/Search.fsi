module DevNull.Client.Search

open System
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors

/// One settled people answer: the term it answers, the people found —
/// empty when nothing matched or the read failed — and whether the
/// server asked to slow down.
[<Struct>]
type PeopleFound = {
  Term: string
  Users: User list
  Throttled: bool
}

/// One settled commit answer: the term it answers, the commits found —
/// empty when nothing matched, the read failed, or the server left the
/// irrelevant ones out — and whether the server asked to slow down.
[<Struct>]
type CommitsFound = {
  Term: string
  Commits: CommitNode list
  Throttled: bool
}

/// One surface's searches: the site's dialog and its results page each
/// own one. `Set` takes the term as typed; the session trims it, gates
/// it by the server's own rules, waits out the typing, and answers for
/// the newest term only — a slower answer for an older term never
/// shows. The people channel waits 120 milliseconds and the commit
/// channel 350, the site's own pauses, because every commit search
/// pays for an embedding.
type Session =
  /// The calls are the two searches with their host's own limits
  /// closed in; the logger carries the session's lines.
  new:
    people: (string -> Async<Result<User list, AppError>>) *
    commits: (string -> Async<Result<CommitNode list, AppError>>) *
    log: Microsoft.Extensions.Logging.ILogger ->
      Session

  /// The term as typed. The same term again asks nothing.
  member Set: term: string -> unit

  /// The term as typed, for the commit channel alone: the results page
  /// searches commits the way its own submit does.
  member SetCommits: term: string -> unit

  /// Every settled people answer, newest term only.
  member People: IObservable<PeopleFound>

  /// Every settled commit answer, newest term only.
  member Commits: IObservable<CommitsFound>

  interface IDisposable
