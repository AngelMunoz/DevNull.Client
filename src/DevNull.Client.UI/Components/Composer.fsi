module DevNull.Client.Components.Composer

open System
open Avalonia.Controls
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.SshKeys
open DevNull.Client.Stores

/// What the composer answers to: a fresh commit, a reply, or a quote.
[<Struct>]
type Target =
  | Fresh
  | ReplyTo of parentId: string
  | Quoting of quotedId: string

/// One poll being built: two to four options and the time it stays
/// open. A poll rides a fresh commit only, never beside images.
type PollDraft = {
  Options: string list
  Duration: PollDuration
}

/// Where a host whose row recycles keeps the draft alive: the text the
/// composer starts from, and the sink every change reaches. A recycled
/// row rebuilds the composer seeded with the last text.
type DraftSource = {
  Seed: string
  Changed: string -> unit
}

/// The quick composer: one body, optional staged images or a poll, one
/// commit button, one error line. Every post goes through the signed
/// v1 route; images travel as a multipart post whose payload carries
/// the JSON.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Target: Target
  /// Runs with the created commit, on the UI thread.
  OnCommitted: Commit -> unit
  /// Runs with the queued commit when one was scheduled, on the UI
  /// thread.
  OnScheduled: (ScheduledCommit -> unit) voption
  /// Opens the scheduled queue, from the schedule panel's link.
  OnOpenQueue: (unit -> unit) voption
  /// Where the composer's program lifetime lands. A host whose page
  /// is cached between visits (the log) registers the lifetime with
  /// the route; every other host leaves it empty and the composer
  /// stops itself when it leaves the visual tree.
  Register: (IDisposable -> unit) voption
  /// The draft contract for hosts whose row recycles: the seed the
  /// composer starts from, and the sink every change reaches.
  Draft: DraftSource voption
}

/// One composer, self-contained: mounts the MVUish program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLogging> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
