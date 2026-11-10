module DevNull.Client.Search

open System
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors

/// One settled profile answer: the term it answers, the people and the
/// organizations found — empty when nothing matched or the read failed
/// — and whether the server asked to slow down.
[<Struct>]
type ProfilesFound = {
  Term: string
  Users: User list
  Organizations: Organization list
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
/// shows. The profile channel waits 120 milliseconds and the commit
/// channel 350, the site's own pauses, because every commit search
/// pays for an embedding.
type Session =
  /// The calls are the two searches with their host's own limits
  /// closed in; the logger carries the session's lines.
  new:
    profiles: (string -> Async<Result<ProfileMatches, AppError>>) *
    commits: (string -> Async<Result<CommitNode list, AppError>>) *
    log: Microsoft.Extensions.Logging.ILogger ->
      Session

  /// The term as typed. The same term again asks nothing.
  member Set: term: string -> unit

  /// The term as typed, for the commit channel alone: the results page
  /// searches commits the way its own submit does.
  member SetCommits: term: string -> unit

  /// Every settled profile answer, newest term only.
  member Profiles: IObservable<ProfilesFound>

  /// Every settled commit answer, newest term only.
  member Commits: IObservable<CommitsFound>

  interface IDisposable

/// One settled mention answer: the query it answers, the people and
/// organizations found — empty when nothing matched or the read
/// failed — and whether the server asked to slow down.
[<Struct>]
type MentionsFound = {
  Query: string
  Results: MentionResults
  Throttled: bool
}

/// One settled issue answer: the digits it answers, the issue when
/// the site knows one, and whether the server asked to slow down.
[<Struct>]
type IssueFound = {
  Digits: string
  Issue: Issue option
  Throttled: bool
}

/// Everything one composer's completions read: the two calls with
/// their host's own context closed in, and whether repeat queries
/// answer from the session's own memory.
[<Struct>]
type CompletionCalls = {
  Mentions: string -> Async<Result<MentionResults, AppError>>
  Issue: string -> Async<Result<Issue option, AppError>>
  Remember: bool
}

/// One composer's completions, on the same gated pipes the search
/// session runs: both channels wait out the site's own pause — the
/// same one hundred and twenty milliseconds the people search waits —
/// answer for the newest query only, and settle a refusal to slow
/// down as the throttled answer. A remembered query answers from
/// memory without a call, the way the site's composer caches its
/// suggestions; a reply's composer asks every time, because its
/// participants change.
type CompletionSession =
  new:
    calls: CompletionCalls * log: Microsoft.Extensions.Logging.ILogger ->
      CompletionSession

  /// The mention query as typed; the same query again asks nothing, and
  /// a bare `@` asks without the pause.
  member Set: query: string voption -> unit

  /// The issue digits as typed; the same digits again ask nothing.
  member SetIssue: digits: string voption -> unit

  /// Every settled mention answer, newest query only.
  member Mentions: IObservable<MentionsFound>

  /// Every settled issue answer, newest digits only.
  member Issues: IObservable<IssueFound>

  interface IDisposable
