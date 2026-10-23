module DevNull.Client.Timeline

open System
open System.Collections.Generic
open System.Reactive.Subjects
open DevNull.Client.Core.ApiTypes

/// What the shared log chrome asks the open timeline to do.
type Request =
  | Reload
  | ScrollToTop
  | CommitLanded

/// How long one accepted reload silences the next one.
let gate = TimeSpan.FromSeconds 5.0

/// The app's timeline service: the channel the shared log chrome — the
/// tab strip and the composer — talks to the open log page through, and
/// the per-log memory a page parks here before it ends: its scroll
/// place and its reply drafts. Every member runs on the UI thread.
type TimelineService() =

  let requests = new Subject<Request>()

  // The timestamp cell the gate reads; a subject stands in for a
  // mutable field, the way the shell's layout cell does.
  let lastAccepted =
    new BehaviorSubject<DateTimeOffset>(DateTimeOffset.MinValue)

  // The parked state: one scroll place per log kind, one draft per
  // commit. Plain maps on the UI thread, the way the last-visit store
  // holds its markers.
  let scrolls = Dictionary<LogKind, float>()
  let drafts = Dictionary<string, string>()

  /// Serves the scroll, and one gated reload: at least `gate` must
  /// have passed since the last accepted reload. Gated-out clicks
  /// still scroll.
  member _.ForceReload() =
    requests.OnNext ScrollToTop

    if DateTimeOffset.UtcNow - lastAccepted.Value >= gate then
      lastAccepted.OnNext(DateTimeOffset.UtcNow)
      requests.OnNext Reload

  /// Announces a landed commit; the open page reloads ungated, the way
  /// its own composer's post reloads it.
  member _.CommitLanded() = requests.OnNext CommitLanded

  /// The request stream. Emits on the UI thread.
  member _.Requests: IObservable<Request> = requests

  /// Parks one log's scroll place, so the next page of that log
  /// picks it up.
  member _.Remember(log: LogKind, offset: float) =
    if offset > 0.0 then
      scrolls[log] <- offset
    else
      scrolls.Remove log |> ignore

  /// The scroll place a log left, or its top.
  member _.OffsetOf(log: LogKind) : float =
    match scrolls.TryGetValue log with
    | true, offset -> offset
    | false, _ -> 0.0

  /// Parks one reply draft. An emptied text leaves no entry behind.
  member _.RememberDraft(commitId: string, text: string) =
    if text = "" then
      drafts.Remove commitId |> ignore
    else
      drafts[commitId] <- text

  /// Spends one reply draft: the landed reply consumed it.
  member _.ForgetDraft(commitId: string) = drafts.Remove commitId |> ignore

  /// The draft a recycled row's composer seeds from.
  member _.DraftOf(commitId: string) : string =
    match drafts.TryGetValue commitId with
    | true, text -> text
    | false, _ -> ""

/// Accessor for the timeline service capability.
type IProvideTimeline =
  abstract Timeline: TimelineService
