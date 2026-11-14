namespace DevNull.Client.Views

open System
open Avalonia.Controls
open FSharp.UMX
open MVUish.Avalonia
open DevNull.Client.AttachmentSources
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.UiClock

/// The open thread: the detail column's reading pane. The pane itself
/// rebuilds only when the checked-out target moves; everything inside
/// follows the model stream, so a poll refresh never rebuilds a row.
module ThreadPane =

  /// One tab's slice: the thread state for its key, and the thread
  /// fields the composer and the reading position share.
  type TabModel = {
    Thread: ThreadState
    Model: State.ThreadModel
  }

  /// What one fixed-target pane needs: the page props, the target it
  /// serves, its own tab model stream, and the dispatch.
  type ForTargetArgs<'env
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp
    and 'env :> IProvideDispatcher
    and 'env :> IProvideSession
    and 'env :> IProvideUiClock
    and 'env :> IProvideImageCache
    and 'env :> IProvideProfiles
    and 'env :> IProvideSettings
    and 'env :> IProvideAttachmentSources
    and 'env :> IProvideNatives
    and 'env :> IProvideRemotes
    and 'env :> IProvideBrowser
    and 'env :> IProvideLogging> = {
    Props: Props<'env>
    Target: ChatTarget
    Model: IObservable<TabModel>
    Dispatch: Dispatch<State.Msg>
  }

  /// The detail column: the pane of the checked-out target — rebuilt
  /// only when the target moves — or the quiet invitation.
  val column:
    props: Props<'env> *
    model: IObservable<State.Model> *
    dispatch: Dispatch<State.Msg> ->
      Control

  /// The thread pane for one fixed target, over its own tab model.
  /// TabModel holds the thread slice for the key plus State.ThreadModel.
  val forTarget: args: ForTargetArgs<'env> -> Control
