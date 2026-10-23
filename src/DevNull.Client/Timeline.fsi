module DevNull.Client.Timeline

open System

/// What the shared log chrome asks the open timeline to do.
type Request =
  | Reload
  | ScrollToTop
  | CommitLanded

/// The app's timeline service: the channel the shared log chrome — the
/// tab strip and the composer — talks to the open log page through.
/// One reload passes the anti-abuse gate at most once per window; the
/// scroll is local and always served.
type TimelineService =

  /// One shared instance for the app.
  new: unit -> TimelineService

  /// Serves the scroll, and one gated reload: at least `gate` must
  /// have passed since the last accepted reload. Gated-out clicks
  /// still scroll.
  member ForceReload: unit -> unit

  /// Announces a landed commit; the open page reloads ungated, the way
  /// its own composer's post reloads it.
  member CommitLanded: unit -> unit

  /// The request stream. Emits on the UI thread.
  member Requests: IObservable<Request>

/// Accessor for the timeline service capability.
type IProvideTimeline =
  abstract Timeline: TimelineService
