module DevNull.Client.Timeline

open System
open DevNull.Client.Core.ApiTypes

/// What the shared log chrome asks the open timeline to do.
type Request =
  | Reload
  | ScrollToTop
  | CommitLanded

/// The app's timeline service: the channel the shared log chrome — the
/// tab strip and the composer — talks to the open log page through,
/// and the per-log memory a page parks here before it ends: its scroll
/// place and its reply drafts. One reload passes the anti-abuse gate at
/// most once per window; the scroll is local and always served. Every
/// member runs on the UI thread.
type TimelineService =

  /// One shared instance for the app.
  new: unit -> TimelineService

  /// Serves the scroll, and one gated reload: at least `gate` must
  /// have passed since the last accepted reload. Gated-out clicks
  /// still scroll.
  member ForceReload: unit -> unit

  /// Scrolls the open timeline to its top, without reloading it: the
  /// answer a fresh first page gives its reader.
  member ScrollToTop: unit -> unit

  /// Announces a landed commit; the open page reloads ungated, the way
  /// its own composer's post reloads it.
  member CommitLanded: unit -> unit

  /// The request stream. Emits on the UI thread.
  member Requests: IObservable<Request>

  /// Parks one log's scroll place, so the next page of that log
  /// picks it up.
  member Remember: log: LogKind * offset: float -> unit

  /// The scroll place a log left, or its top.
  member OffsetOf: log: LogKind -> float

  /// Parks one reply draft. An emptied text leaves no entry behind.
  member RememberDraft: commitId: string * text: string -> unit

  /// Spends one reply draft: the landed reply consumed it.
  member ForgetDraft: commitId: string -> unit

  /// The draft a recycled row's composer seeds from.
  member DraftOf: commitId: string -> string

/// Accessor for the timeline service capability.
type IProvideTimeline =
  abstract Timeline: TimelineService
