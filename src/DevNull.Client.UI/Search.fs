module DevNull.Client.Search

open System
open System.Reactive.Subjects
open FSharp.Control.Reactive
open IcedTasks
open Microsoft.Extensions.Logging
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors

// The site's own pauses: people answers are cheap and land while the
// person types; every commit search pays for an embedding, so it waits
// for a pause first.
let peopleWindow = TimeSpan.FromMilliseconds 120.0

let commitWindow = TimeSpan.FromMilliseconds 350.0

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

/// Everything one search channel reads.
[<Struct>]
type Channel<'items, 'found> = {
  Gate: BehaviorSubject<string voption>
  Call: string -> Async<Result<'items, AppError>>
  Found: IObserver<'found>
  /// The answer that carries the items the call found.
  Build: string -> 'items -> 'found
  /// The answer a failed read settles as.
  Empty: string -> 'found
  /// The answer a slow-down request settles as.
  Throttled: string -> 'found
  Log: ILogger
  What: string
}

/// Runs one gated term: the call, the latest-wins check, and the
/// publish. A slower answer for an older term never shows, and a
/// publish on a completed subject — after the host went away — is a
/// no-op.
let run(channel: Channel<'items, 'found>, gated: string voption) =
  match gated with
  | ValueNone -> ()
  | ValueSome term ->
    asyncEx {
      let! answer = channel.Call term

      if channel.Gate.Value = gated then
        match answer with
        | Ok items ->
          channel.Found.OnNext(channel.Build term items)

          channel.Log.LogDebug(
            "The {What} search for {Term} arrived.",
            channel.What,
            term
          )
        | Error(AppError.Refused(429, _, _)) ->
          channel.Found.OnNext(channel.Throttled term)

          channel.Log.LogWarning(
            "The {What} search for {Term} was asked to slow down.",
            channel.What,
            term
          )
        | Error error ->
          // A failed search counts as an empty one, the way the site's
          // own never waits on one forever.
          channel.Found.OnNext(channel.Empty term)

          channel.Log.LogWarning(
            "The {What} search for {Term} failed: {Error}",
            channel.What,
            term,
            bounded 300 (describe error)
          )
    }
    |> Async.Start

/// One surface's searches.
type Session
  (
    people: string -> Async<Result<User list, AppError>>,
    commits: string -> Async<Result<CommitNode list, AppError>>,
    log: ILogger
  ) =

  // The gate holds the newest term that passed the server's own rules;
  // it is both the throttle's source and the latest-wins check, so an
  // answer for a term the gate has moved past never shows.
  let peopleGate = new BehaviorSubject<string voption>(ValueNone)

  let commitGate = new BehaviorSubject<string voption>(ValueNone)

  // The answers replay their latest to late subscribers: a page's
  // first search fires from its init, and the program's subscription
  // can attach after the answer landed.
  let peopleFound =
    new BehaviorSubject<PeopleFound>(
      {
        Term = ""
        Users = []
        Throttled = false
      }
    )

  let commitsFound =
    new BehaviorSubject<CommitsFound>(
      {
        Term = ""
        Commits = []
        Throttled = false
      }
    )

  let peopleChannel: Channel<User list, PeopleFound> = {
    Gate = peopleGate
    Call = people
    Found = peopleFound
    Build =
      fun term users -> {
        Term = term
        Users = users
        Throttled = false
      }
    Empty =
      fun term -> {
        Term = term
        Users = []
        Throttled = false
      }
    Throttled =
      fun term -> {
        Term = term
        Users = []
        Throttled = true
      }
    Log = log
    What = "people"
  }

  let commitChannel: Channel<CommitNode list, CommitsFound> = {
    Gate = commitGate
    Call = commits
    Found = commitsFound
    Build =
      fun term found -> {
        Term = term
        Commits = found
        Throttled = false
      }
    Empty =
      fun term -> {
        Term = term
        Commits = []
        Throttled = false
      }
    Throttled =
      fun term -> {
        Term = term
        Commits = []
        Throttled = true
      }
    Log = log
    What = "commit"
  }

  let pipeOf(channel: Channel<'items, 'found>, window: TimeSpan) =
    channel.Gate
    |> Observable.distinctUntilChanged
    |> Observable.throttle window
    |> Observable.subscribe(fun gated -> run(channel, gated))

  let peoplePipe = pipeOf(peopleChannel, peopleWindow)

  let commitPipe = pipeOf(commitChannel, commitWindow)

  /// The term as typed. The same term again asks nothing.
  member _.Set(term: string) =
    peopleGate.OnNext(SearchTerms.peopleTerm term)
    commitGate.OnNext(SearchTerms.commitTerm term)

  /// The term as typed, for the commit channel alone: the results page
  /// searches commits the way its own submit does.
  member _.SetCommits(term: string) =
    commitGate.OnNext(SearchTerms.commitTerm term)

  /// Every settled people answer, newest term only.
  member _.People: IObservable<PeopleFound> = peopleFound :> _

  /// Every settled commit answer, newest term only.
  member _.Commits: IObservable<CommitsFound> = commitsFound :> _

  interface IDisposable with
    member _.Dispose() =
      peoplePipe.Dispose()
      commitPipe.Dispose()
      peopleFound.OnCompleted()
      commitsFound.OnCompleted()
