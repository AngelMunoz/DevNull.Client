module DevNull.Client.Views.Routes

open Avalonia.Controls
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Remotes
open DevNull.Client.SettingsStore
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Timeline
open DevNull.Client.UiClock
open Navs

/// The router and the entry the app boots into.
[<Struct>]
type RouterShell = {
  Router: IRouter<Control>
  BootUrl: string
}

/// Fires one navigation now and reports a failed one through the env's
/// logger.
val go:
  env: 'env * nav: INavigable * url: string -> unit when 'env :> IProvideLogging

/// Builds the app route table and the router. The first navigation
/// (`/log/latest` signed in, `/signin` signed out) is already in flight
/// when this returns.
val build:
  env: 'env -> RouterShell
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvidePkceSource
    and 'env :> IProvideHttp
    and 'env :> IProvideSession
    and 'env :> IProvideUiClock
    and 'env :> IProvideImageCache
    and 'env :> IProvideAttachmentSources
    and 'env :> IProvideLastVisit
    and 'env :> IProvideRecentSearches
    and 'env :> IProvideProfiles
    and 'env :> IProvideTimeline
    and 'env :> IProvideSshKeys
    and 'env :> IProvideSettings
    and 'env :> IProvideLogging
    and 'env :> IProvideRemotes
    and 'env :> IProvideNatives
