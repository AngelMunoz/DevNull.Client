module DevNull.Client.Components.ProfileCard

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Stores

/// The handle line: a lane dot beside the @handle.
val laneHandle: lane: Lane option * username: string -> Control

/// One count-and-label pair of a stats row.
val statRow: count: int * label: string -> Control

/// One signed-in user's card for the shell aside: avatar, handle, bio,
/// and counts, from the profile route. The env names every capability
/// the fetch needs.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> = { Env: 'env; Session: SignedInSession }

/// One profile card, self-contained: mounts the MVUish program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
