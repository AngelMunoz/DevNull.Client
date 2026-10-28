module DevNull.Client.Views.RemotesView

open Avalonia.Controls
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.UiClock
open Navs

/// The direct messages: the inbox and the open thread side by side.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideNatives
  and 'env :> IProvideRemotes> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
  /// The route the page lives in; owns the page's program lifetime.
  Context: RouteContext
}

/// One remotes page, self-contained: mounts the MVUish program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideNatives
  and 'env :> IProvideRemotes> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
