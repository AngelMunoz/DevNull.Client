module DevNull.Client.Components.Composer

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Stores

/// What the composer answers to: a fresh commit, a reply, or a quote.
[<Struct>]
type Target =
  | Fresh
  | ReplyTo of parentId: string
  | Quoting of quotedId: string

/// The quick composer: one body, one commit button, one error line.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
  Session: SignedInSession
  Target: Target
  /// Runs with the created commit, on the UI thread.
  OnCommitted: Commit -> unit
  /// Where the composer's program lifetime lands. A host whose page
  /// is cached between visits (the log) registers the lifetime with
  /// the route; every other host leaves it empty and the composer
  /// stops itself when it leaves the visual tree.
  Register: (IDisposable -> unit) voption
}

/// One composer, self-contained: mounts the MVUish program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
