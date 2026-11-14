module DevNull.Client.Editor.RemoteThreadTab

open System
open Avalonia.Controls
open Avalonia.Threading
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore
open MVUish.Avalonia

/// What the frame reads from one remote tab: the store key, the title
/// the sidebar row would show, the unread count while inactive, and the
/// first read's state.
[<Struct>]
type Snapshot = {
  Key: string
  Title: string
  Unread: int
  Loading: bool
  Error: AppError option
}

/// What one remote tab needs from its host: the signed workers, the
/// signed-in session, the fixed chat, the zone the times show in, the
/// host's active stream, and the frame's snapshot sink.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideRemotes
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideDispatcher
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideBrowser
  and 'env :> IProvideRecentSearches
  and 'env :> IProvideLogging
  and 'env :> IProvideSettings
  and 'env :> IProvideNatives> = {
  Env: 'env
  Session: SignedInSession
  /// The chat this tab serves. It never changes through a message.
  Target: ChatTarget
  Zone: Zone
  Now: unit -> DateTimeOffset
  /// True while the window stands and this tab is the selected one.
  Active: IObservable<bool>
  OnSnapshot: Snapshot -> unit
  /// The host opens this person's repository tab.
  OnOpenProfile: string -> unit
}

/// The tab's state logic, exported so the update stays testable
/// without a running page. Messages reuse the remotes page messages.
module State =

  /// One remote tab: the thread fields, the copied live slice for its
  /// fixed target, the first read's state, and the host's focus gate.
  type Model = {
    Thread: DevNull.Client.Views.State.ThreadModel
    Live: ThreadState
    Loading: bool
    Error: AppError option
    Active: bool
  }

  /// The fresh state: the parked draft stands, the first read is on
  /// its way through the watch.
  val initial: target: ChatTarget -> Model

  /// What the frame reads from this model.
  val snapshot: props: Props<'env> * model: Model -> Snapshot

  /// The tab's first model: the stashed draft for the chat key.
  val init: props: Props<'env> -> Model * Cmd<DevNull.Client.Views.State.Msg>

  /// Applies one message. Thread messages delegate to the shared
  /// thread update; the focus gate drops mark-read, typing, and record
  /// while inactive; no-change returns the same model reference.
  val update:
    props: Props<'env> * msg: DevNull.Client.Views.State.Msg * model: Model ->
      Model * Cmd<DevNull.Client.Views.State.Msg>

  /// The tab's inlets from outside: the store slice for its target,
  /// the watch handle, the shared clock, the host's active stream,
  /// and the link-preview tick while a card pends.
  val subscribe:
    props: Props<'env> * model: Model -> Sub<DevNull.Client.Views.State.Msg>

/// The tab's view: the fixed-target thread pane over the tab model
/// stream, with the loading and error lines under it.
val view:
  props: Props<'env> *
  model: IObservable<State.Model> *
  dispatch: Dispatch<DevNull.Client.Views.State.Msg> ->
    Control

/// The tab's program, with its update loop bound to the caller's
/// dispatcher.
val program:
  props: Props<'env> ->
    Program<unit, State.Model, DevNull.Client.Views.State.Msg, Control>

/// Starts the tab's program. The caller owns the returned handle and
/// disposes it when the tab closes.
val mount: dispatcher: IDispatcher * props: Props<'env> -> Control * IDisposable

/// The live remote tab programs of this process, so a host can prove
/// that closing a tab disposed its program exactly once.
module Live =

  /// The number of remote tab programs mounted and not disposed.
  val current: unit -> int

  /// Every change of that number.
  val observe: IObservable<int>
