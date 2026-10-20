module DevNull.Client.Views.Routes

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open Navs

/// The router and the entry the app boots into.
[<Struct>]
type RouterShell = {
  Router: IRouter<Control>
  BootUrl: string
}

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
    and 'env :> IProvideSettings
    and 'env :> IProvideLogging
