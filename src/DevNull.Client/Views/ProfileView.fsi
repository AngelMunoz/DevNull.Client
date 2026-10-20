module DevNull.Client.Views.ProfileView

open Avalonia.Controls
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Stores
open Navs

/// One profile by username, with the viewer's relation to it.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> = {
  Env: 'env
  Username: string
  Session: SignedInSession
  Nav: INavigable
}

/// One profile page, self-contained: mounts the MVUish program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
