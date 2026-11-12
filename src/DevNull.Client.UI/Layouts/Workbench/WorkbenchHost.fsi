module DevNull.Client.Layouts.Workbench.WorkbenchHost

open System
open Avalonia.Controls
open MVUish.Avalonia
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores

/// One live history program: the ref it shows, the place it restored,
/// its root view, and the handle that ends it.
type HistoryChild = {
  Ref: Ref
  Restore: HistoryColumn.Place option
  View: Control
  Handle: IDisposable
}

/// One live inspector program: the checkout id it serves, its root
/// view, and the handle that ends it.
type InspectorChild = {
  CommitId: string
  View: Control
  Handle: IDisposable
}

/// The host's state: the selected ref and checkout, the per-ref
/// places, the staging flag, the status bar's handle and fetch time,
/// the counts, the fold, the clock, and the live child programs.
type Model = {
  Selected: Ref
  CheckoutId: string option
  Places: Map<Ref, HistoryColumn.Place>
  StagingOpen: bool
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
}

/// The host's messages: ref and checkout selections, the staging flag,
/// the fold, the clock and session, the history place report, and the
/// host's open, external, and quote requests.
[<NoComparison>]
type Msg =
  | RefSelected of ref: Ref
  | CheckoutSet of commitId: string option
  | StagingToggled
  | WidthChanged of width: float
  | ClockTicked of moment: DateTimeOffset
  | SessionChanged of state: SessionState
  | HistoryReported of ref: Ref * report: HistoryColumn.Report
  | OpenAuthor of handle: string
  | OpenExternal of url: string
  | Quote of Commit
  | HistoryStopped
  | InspectorStopped
  | HistoryReady of
    ref: Ref *
    restore: HistoryColumn.Place option *
    view: Control *
    handle: IDisposable
  | InspectorReady of commitId: string * view: Control * handle: IDisposable

/// What the host needs from its integrator: the protocol workers, the
/// dispatcher that starts child programs, the session store, the image
/// cache, the clock and zone, the outbound sinks, and the stored state
/// the host starts from.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession> = {
  Env: 'env
  Initial: WorkbenchState
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  OnOpenAuthor: string -> unit
  OnQuote: Commit -> unit
  OnOpenExternal: string -> unit
  OnStateChanged: WorkbenchState -> unit
}

/// The host's first state, its first history program, and its first
/// inspector program when the stored checkout holds an id.
val init: props: Props<'env> -> Model * Cmd<Msg>

/// The host's one update function.
val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The host's inlets: the clock and the session store.
val subscribe: props: Props<'env> -> model: Model -> Sub<Msg>

/// The three-region layout over the status bar. The inspector is a
/// 380 px right region while the host is wide, and a sheet over the
/// history when `Folded` holds.
val view:
  props: Props<'env> ->
  model: IObservable<Model> ->
  dispatch: Dispatch<Msg> ->
    Control

/// The host program. The integrator starts it with `Programs.runPage`
/// and keeps the returned handle.
val program: props: Props<'env> -> Program<unit, Model, Msg, Control>
