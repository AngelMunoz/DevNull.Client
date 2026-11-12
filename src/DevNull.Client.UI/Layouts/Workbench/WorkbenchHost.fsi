module DevNull.Client.Workbench.WorkbenchHost

open System
open Avalonia.Controls
open Avalonia.Threading
open MVUish.Avalonia
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores

/// One live history program: the ref it shows, the place it restored,
/// its root view, the handle that ends it, and the named refresh inlet
/// a post uses.
type HistoryChild = {
  Ref: Ref
  Restore: HistoryColumn.Place option
  View: Control
  Handle: IDisposable
  Refresh: unit -> unit
}

/// One live inspector program: the checkout id it serves, its root
/// view, and the handle that ends it.
type InspectorChild = {
  CommitId: string
  View: Control
  Handle: IDisposable
}

/// One live thread program: the commit it shows, its center and
/// parents views, and the handle that ends it.
type ThreadChild = {
  CommitId: string
  View: ThreadMode.View
  Handle: IDisposable
}

/// One live repository home: the handle it shows, its center and
/// people-list views, and the handle that ends it.
type RepositoryChild = {
  RepositoryHandle: string
  View: RepositoryHome.View
  Handle: IDisposable
}

/// The host's state: the selected ref and checkout, the per-ref
/// places, the staging flag and state, the open thread, the open
/// repository home and its people list, the status bar's handle and
/// fetch time, the counts, the fold, the clock, and the live child
/// programs.
type Model = {
  Selected: Ref
  CheckoutId: string option
  Places: Map<Ref, HistoryColumn.Place>
  StagingOpen: bool
  Staging: Staging.Model
  ThreadId: string option
  Thread: ThreadChild option
  Repository: RepositoryChild option
  RepositoryListOpen: bool
  Handle: string
  FetchedAt: DateTimeOffset option
  SavedCount: int
  UnreadCount: int
  Folded: bool
  Now: DateTimeOffset
  Pasteis: bool
  History: HistoryChild option
  Inspector: InspectorChild option
  LiveHistory: int
  LiveInspector: int
  LiveThread: int
  LiveRepository: int
}

/// The host's messages: ref and checkout selections, the staging flag
/// and its draft, post, schedule, and queue messages, the open thread
/// and its back request, the repository home and its people-list
/// report, the fold, the clock and session, the history place report,
/// and the host's open, external, and quote requests.
[<NoComparison>]
type Msg =
  | RefSelected of ref: Ref
  | CheckoutSet of commitId: string option
  | OpenThread of commitId: string
  | ThreadClosed
  | StagingToggled
  | DraftChanged of text: string
  | StagingPosted of commit: Commit
  | StagingScheduled of entry: ScheduledCommit
  | QueueRequested
  | QueueFetched of Result<ScheduledPage, AppError>
  | WidthChanged of width: float
  | ClockTicked of moment: DateTimeOffset
  | SessionChanged of state: SessionState
  | HistoryReported of ref: Ref * report: HistoryColumn.Report
  | OpenAuthor of handle: string
  | OpenExternal of url: string
  | Quote of Commit
  | HistoryStopped
  | InspectorStopped
  | ThreadStopped
  | RepositoryStopped
  | RepositoryListChanged of open': bool
  | HistoryReady of
    ref: Ref *
    restore: HistoryColumn.Place option *
    view: Control *
    handle: IDisposable *
    refresh: (unit -> unit)
  | InspectorReady of commitId: string * view: Control * handle: IDisposable
  | ThreadReady of
    commitId: string *
    view: ThreadMode.View *
    handle: IDisposable
  | RepositoryReady of
    handle: string *
    view: RepositoryHome.View *
    lifetime: IDisposable

/// What the host needs from its integrator: the protocol workers, the
/// dispatcher that starts child programs, the session store, the image
/// cache, the attachment sources the composer stages into, the clock
/// and zone, the outbound sinks, and the stored state the host starts
/// from.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLogging> = {
  Env: 'env
  Initial: WorkbenchState
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  OnOpenAuthor: string -> unit
  OnOpenExternal: string -> unit
  OnStateChanged: WorkbenchState -> unit
}

/// One child program handle the mount wrapper tracks. The wrapper
/// marks it when it ends, so a test can prove every seen handle is
/// disposed.
[<Sealed>]
type internal TrackedHandle =
  member Disposed: bool
  interface IDisposable

/// One mounted host: the child handles it has seen, the live counts,
/// and the send path the tests use to drive the host.
[<Sealed>]
type internal MountedHost =
  member Send: msg: Msg -> unit
  member Created: TrackedHandle list
  member LiveHistory: int
  member LiveInspector: int
  member LiveThread: int
  member LiveRepository: int
  member HostDisposed: bool
  interface IDisposable

/// The host's first state, its first history program, its first
/// inspector program when the stored checkout holds an id, and its
/// first scheduled queue read.
val init: props: Props<'env> -> Model * Cmd<Msg>

/// The host's one update function.
val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The host's inlets: the clock and the session store.
val subscribe: props: Props<'env> -> model: Model -> Sub<Msg>

/// The three-region layout, the staging strip and its open area, and
/// the status bar. The inspector is a 380 px right region while the
/// host is wide, and a sheet over the history when `Folded` holds.
/// An open thread replaces the history center and adds its PARENTS
/// chain above the inspector checkout. A selected repository ref
/// replaces the history center with its home; an open people list
/// takes the inspector's place.
val view:
  props: Props<'env> ->
  model: IObservable<Model> ->
  dispatch: Dispatch<Msg> ->
    Control

/// The host program. The integrator starts it with `Programs.runPage`
/// and keeps the returned handle.
val program: props: Props<'env> -> Program<unit, Model, Msg, Control>

/// Starts the host and returns the one handle the integrator disposes.
/// Disposing it ends the host program and every live child program it
/// recorded, exactly once.
val mount: dispatcher: IDispatcher * props: Props<'env> -> Control * IDisposable
