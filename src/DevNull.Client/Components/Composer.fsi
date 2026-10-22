module DevNull.Client.Components.Composer

open System
open Avalonia.Controls
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.SshKeys
open DevNull.Client.Stores

/// What the composer answers to: a fresh commit, a reply, or a quote.
[<Struct>]
type Target =
  | Fresh
  | ReplyTo of parentId: string
  | Quoting of quotedId: string

/// What one landed post answers with: the created commit, when the v1
/// route took it, or the posted URL, when the CLI route took the
/// images.
[<Struct>]
type Posted =
  | Created of Commit
  | PostedUrl of url: string

/// The quick composer: one body, optional staged images, one commit
/// button, one error line. A post without images goes through the
/// signed v1 route; a post with images goes through the CLI composite,
/// the only route the server gives images.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideSshKeys> = {
  Env: 'env
  Session: SignedInSession
  Target: Target
  /// Runs with the post's outcome, on the UI thread.
  OnCommitted: Posted -> unit
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
  and 'env :> IProvideHttp
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideSshKeys> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
