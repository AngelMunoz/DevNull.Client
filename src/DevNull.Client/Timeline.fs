module DevNull.Client.Timeline

open System
open System.Reactive.Subjects

/// What the shared log chrome asks the open timeline to do.
type Request =
  | Reload
  | ScrollToTop
  | CommitLanded

/// How long one accepted reload silences the next one.
let gate = TimeSpan.FromSeconds 5.0

/// The app's timeline service: the channel the shared log chrome — the
/// tab strip and the composer — talks to the open log page through.
type TimelineService() =

  let requests = new Subject<Request>()

  // The timestamp cell the gate reads; a subject stands in for a
  // mutable field, the way the shell's layout cell does.
  let lastAccepted =
    new BehaviorSubject<DateTimeOffset>(DateTimeOffset.MinValue)

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

/// Accessor for the timeline service capability.
type IProvideTimeline =
  abstract Timeline: TimelineService
