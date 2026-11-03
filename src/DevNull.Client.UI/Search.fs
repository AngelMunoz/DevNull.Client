module DevNull.Client.Search

open System
open System.Collections.Concurrent
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

[<Struct>]
type PeopleFound = {
  Term: string
  Users: User list
  Throttled: bool
}

[<Struct>]
type CommitsFound = {
  Term: string
  Commits: CommitNode list
  Throttled: bool
}

// Everything one search channel reads.
[<Struct>]
type Channel<'items, 'found> = {
  Gate: BehaviorSubject<string voption>
  Call: string -> Async<Result<'items, AppError>>
  Found: IObserver<'found>
  // The answer that carries the items the call found.
  Build: string -> 'items -> 'found
  // The answer a failed read settles as.
  Empty: string -> 'found
  // The answer a slow-down request settles as.
  Throttled: string -> 'found
  Log: ILogger
  What: string
}

// Runs one gated term: the call, the latest-wins check, and the
// publish. A slower answer for an older term never shows, and a
// publish on a completed subject — after the host went away — is a
// no-op.
let run(channel: Channel<'items, 'found>, gated: string voption) =
  match gated with
  | ValueNone -> ()
  | ValueSome term ->
    asyncEx {
      try
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
      with ex ->
        if channel.Gate.Value = gated then
          channel.Found.OnNext(channel.Empty term)

        channel.Log.LogWarning(
          ex,
          "The {What} search for {Term} failed.",
          channel.What,
          term
        )
    }
    |> Async.Start

// The newest term after its window. The same term again is quiet.
let timed (window: TimeSpan) (source: IObservable<string voption>) =
  source |> Observable.distinctUntilChanged |> Observable.throttle window

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
    |> timed window
    |> Observable.subscribe(fun gated -> run(channel, gated))

  let peoplePipe = pipeOf(peopleChannel, peopleWindow)

  let commitPipe = pipeOf(commitChannel, commitWindow)

  member _.Set(term: string) =
    peopleGate.OnNext(SearchTerms.peopleTerm term)
    commitGate.OnNext(SearchTerms.commitTerm term)

  member _.SetCommits(term: string) =
    commitGate.OnNext(SearchTerms.commitTerm term)

  member _.People: IObservable<PeopleFound> = peopleFound :> _

  member _.Commits: IObservable<CommitsFound> = commitsFound :> _

  interface IDisposable with
    member _.Dispose() =
      peoplePipe.Dispose()
      commitPipe.Dispose()
      peopleFound.OnCompleted()
      commitsFound.OnCompleted()

[<Struct>]
type MentionsFound = {
  Query: string
  Results: MentionResults
  Throttled: bool
}

[<Struct>]
type IssueFound = {
  Digits: string
  Issue: Issue option
  Throttled: bool
}

[<Struct>]
type CompletionCalls = {
  Mentions: string -> Async<Result<MentionResults, AppError>>
  Issue: string -> Async<Result<Issue option, AppError>>
  Remember: bool
}

type CompletionSession(calls: CompletionCalls, log: ILogger) =

  let mentionGate = new BehaviorSubject<string voption>(ValueNone)
  let issueGate = new BehaviorSubject<string voption>(ValueNone)

  let emptyMentions = {
    Query = ""
    Results = { Users = []; Organizations = [] }
    Throttled = false
  }

  let mentionFound = new BehaviorSubject<MentionsFound>(emptyMentions)

  let issueFound =
    new BehaviorSubject<IssueFound> {
      Digits = ""
      Issue = None
      Throttled = false
    }

  let memory = ConcurrentDictionary<string, MentionResults>()

  // The call remembers its own answer before it publishes, so the
  // pipe below can serve the repeat from memory.
  let callMentions(query: string) = async {
    match! calls.Mentions query with
    | Ok results as answer ->
      // A reply's composer answers every query fresh.
      if calls.Remember then
        memory[query] <- results

      return answer
    | Error _ as answer -> return answer // a failed read is not remembered
  }

  let mentionChannel: Channel<MentionResults, MentionsFound> = {
    Gate = mentionGate
    Call = callMentions
    Found = mentionFound
    Build =
      fun query results -> {
        Query = query
        Results = results
        Throttled = false
      }
    Empty = fun query -> { emptyMentions with Query = query }
    Throttled =
      fun query -> {
        emptyMentions with
            Query = query
            Throttled = true
      }
    Log = log
    What = "mention completion"
  }

  let issueChannel: Channel<Issue option, IssueFound> = {
    Gate = issueGate
    Call = calls.Issue
    Found = issueFound
    Build =
      fun digits issue -> {
        Digits = digits
        Issue = issue
        Throttled = false
      }
    Empty =
      fun digits -> {
        Digits = digits
        Issue = None
        Throttled = false
      }
    Throttled =
      fun digits -> {
        Digits = digits
        Issue = None
        Throttled = true
      }
    Log = log
    What = "issue completion"
  }

  // The mention pipe answers from memory first; the issue pipe is the
  // plain gated pipe the search session runs.
  let mentionPipe =
    mentionGate
    |> Observable.filter(fun gated -> gated <> ValueSome "")
    |> timed peopleWindow
    |> Observable.subscribe(fun gated ->
      match gated with
      | ValueSome query when mentionGate.Value = gated && calls.Remember ->
        match memory.TryGetValue query with
        | true, results ->
          mentionFound.OnNext(mentionChannel.Build query results)

          log.LogDebug(
            "The mention completion for {Query} answered from memory.",
            query
          )
        | false, _ -> run(mentionChannel, gated)
      | _ -> run(mentionChannel, gated))

  let issuePipe =
    issueGate
    |> timed peopleWindow
    |> Observable.subscribe(fun gated -> run(issueChannel, gated))

  member _.Set(query: string voption) =
    if mentionGate.Value <> query then
      mentionGate.OnNext query

      match query with
      | ValueSome "" -> run(mentionChannel, query)
      | _ -> ()

  member _.SetIssue(digits: string voption) = issueGate.OnNext digits

  member _.Mentions: IObservable<MentionsFound> = mentionFound :> _

  member _.Issues: IObservable<IssueFound> = issueFound :> _

  interface IDisposable with
    member _.Dispose() =
      mentionPipe.Dispose()
      issuePipe.Dispose()
      mentionFound.OnCompleted()
      issueFound.OnCompleted()
