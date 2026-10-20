module DevNull.Client.Views.SettingsView

open Avalonia.Controls
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open Navs

/// The account settings: lane, pastéis mode, the GitHub sync, the
/// application preferences, and the sign-out. `OnSignOut` runs when the
/// user asks to sign out.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideSettings> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
  OnSignOut: unit -> unit
}

/// One settings page, self-contained: mounts the MVUish program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideSettings> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
