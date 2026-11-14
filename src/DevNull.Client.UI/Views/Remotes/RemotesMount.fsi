namespace DevNull.Client.Views

open System
open Avalonia.Controls
open Avalonia.Threading
open MVUish.Avalonia
open DevNull.Client.AttachmentSources
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.UiClock

/// The remotes mount: the same program as the Website page, without
/// the router. A host with no routes gets the inbox and thread
/// controls apart, disposes the program with one handle, and learns
/// of target moves through a sink.
module RemotesMount =

  /// What a host gives the mount: the page props without the router,
  /// plus the answers to the two things the router did.
  type Props<'env
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
    Env: 'env
    Session: SignedInSession
    /// The host opens this person's repository or profile its own way.
    OnOpenProfile: string -> unit
    /// The open chat moved: a target, or none. Fires on the UI thread.
    OnTargetChanged: ChatTarget option -> unit
  }

  /// What the mount returns: the two controls and the one lifetime.
  type Mount = {
    Inbox: Control
    Thread: Control
    Lifetime: IDisposable
    /// Sends one message into the program, for example Closed.
    Send: State.Msg -> unit
  }

  /// Mounts the remotes program on the dispatcher: the inbox and
  /// thread controls stand apart; disposing the lifetime ends the
  /// inbox watch, the thread watch, and every model binding.
  val mount: dispatcher: IDispatcher * props: Props<'env> -> Mount

  /// The panel a host shows while the account has remotes off.
  /// onEnable runs Remotes.SetEnabled(true) through the store.
  val disabledPanel:
    onEnable: (unit -> unit) * onUseWebsite: (unit -> unit) -> Control
