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
  /// Serves the scroll, and one gated reload: at least `gate` must
  /// have passed since the last accepted reload. Gated-out clicks
  /// still scroll.
  abstract ForceReload: unit -> unit

  /// Scrolls the open timeline to its top, without reloading it: the
  /// answer a fresh first page gives its reader.
  abstract ScrollToTop: unit -> unit

  /// Announces a landed commit; the open page reloads ungated, the way
  /// its own composer's post reloads it.
  abstract CommitLanded: unit -> unit

  /// Announces one deleted commit; the shell answers with its undo
  /// toast. The open log page is not told: its next reload omits the
  /// commit on its own.
  abstract AnnounceDeletion: commitId: string -> unit

  /// The request stream. Emits on the UI thread.
  abstract Requests: IObservable<Request>

  /// The deleted commits worth an undo toast, newest last.
  abstract Deletions: IObservable<string>

  /// Parks one log's scroll place, so the next page of that log
  /// picks it up.
  abstract Remember: log: LogKind * offset: float -> unit

  /// The scroll place a log left, or its top.
  abstract OffsetOf: log: LogKind -> float

  /// Parks the log page the reader last had open, so a thread's back
  /// line returns to it. The log page calls this when it builds.
  abstract RememberLog: log: LogKind -> unit

  /// The last log page built in this session, or none until one was.
  abstract LastLog: LogKind option

  /// Parks one reply draft. An emptied text leaves no entry behind.
  abstract RememberDraft: commitId: string * text: string -> unit

  /// Spends one reply draft: the landed reply consumed it.
  abstract ForgetDraft: commitId: string -> unit

  /// The draft a recycled row's composer seeds from.
  abstract DraftOf: commitId: string -> string

/// Accessor for the timeline service capability.
type IProvideTimeline =
  abstract Timeline: TimelineService

/// The app's timeline service over the subjects and maps it owns.
module TimelineService =

  /// Builds a timeline service with its own subjects and maps; the
  /// env builds the app's one instance.
  val create: unit -> TimelineService
