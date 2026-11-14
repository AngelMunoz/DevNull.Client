namespace DevNull.Client.Views

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

/// The shared thread update: one body of logic for the Website page
/// and the per-tab host. Comments live here; the implementation hides
/// its helpers by omission.
module RemotesThread =

  /// The fixed target plus the thread fields one update runs against.
  /// The Website passes its open target; a per-tab host passes its fixed
  /// target. The store supplies the live thread slice.
  type UpdateArgs<'env
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
    Msg: State.Msg
    Model: State.ThreadModel
  }

  /// One thread message against the thread fields: the new fields and
  /// the effects it asks for. Inbox messages return the same model
  /// reference with no effects. Global open-target moves stay with the
  /// Website caller; this update never calls SetOpenTarget.
  val update: args: UpdateArgs<'env> -> State.ThreadModel * Cmd<State.Msg>
