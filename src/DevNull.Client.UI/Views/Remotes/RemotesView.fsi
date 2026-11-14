module DevNull.Client.Views.RemotesView

open Avalonia.Controls
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.UiClock
open Navs

/// One remotes page, self-contained: the shared mount composed through
/// the two-pane frame; the route context owns the mount lifetime.
type Page<'env
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
  and 'env :> IProvideLogging> =
  new: props: Props<'env> * context: RouteContext -> Page<'env>
  inherit UserControl
