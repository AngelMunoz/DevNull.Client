module DevNull.Client.Editor.RemoteList

open System
open Avalonia.Controls
open Avalonia.Threading
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
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

/// What the sidebar list needs from its host: the signed workers, the
/// signed-in session, the zone the times show in, the frame's open sink,
/// and the host's profile opener.
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
  /// The zone the times show in. The inbox build keeps this shape.
  Zone: Zone
  /// The frame opens this chat as a tab. Runs on the UI thread.
  OnOpen: ChatTarget -> unit
  /// The host opens this person's repository tab.
  OnOpenProfile: string -> unit
}

/// The list's state logic: the full Website inbox program with the
/// open-target sink wrapped, so the sidebar never checks a thread out.
module State =

  /// The list's first model: the Website inbox program's first model.
  val init:
    props: Props<'env> ->
      DevNull.Client.Views.State.Model * Cmd<DevNull.Client.Views.State.Msg>

  /// Applies one message. Opened leaves the model unchanged and runs
  /// the frame's sink; every other message runs the Website inbox
  /// update direct.
  val update:
    props: Props<'env> *
    msg: DevNull.Client.Views.State.Msg *
    model: DevNull.Client.Views.State.Model ->
      DevNull.Client.Views.State.Model * Cmd<DevNull.Client.Views.State.Msg>

  /// The list's inlets from outside: the Website inbox program's own
  /// subscriptions. The open target stays None, so only the inbox poll
  /// runs.
  val subscribe:
    props: Props<'env> * model: DevNull.Client.Views.State.Model ->
      Sub<DevNull.Client.Views.State.Msg>

/// The list's view: the Website inbox column over the list model
/// stream.
val view:
  props: Props<'env> *
  model: IObservable<DevNull.Client.Views.State.Model> *
  dispatch: Dispatch<DevNull.Client.Views.State.Msg> ->
    Control

/// The list's program, with its update loop bound to the caller's
/// dispatcher.
val program:
  props: Props<'env> ->
    Program<
      unit,
      DevNull.Client.Views.State.Model,
      DevNull.Client.Views.State.Msg,
      Control
     >

/// Starts the list's program. The caller owns the returned handle and
/// disposes it when the layout closes.
val mount: dispatcher: IDispatcher * props: Props<'env> -> Control * IDisposable
