module DevNull.Client.Components.Composer

open Avalonia.Controls
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Components.ReplyPicker
open DevNull.Client.Stores

/// What the composer answers to: a fresh commit, a reply, or a quote.
[<Struct>]
type Target =
  | Fresh
  | ReplyTo of parentId: string
  | Quoting of quotedId: string

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
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Target: Target
  /// Who a reply may go to, when the host knows them: the parent's
  /// author first, then the thread read's other participants. An
  /// absent value leaves the picker out and the field off the request.
  Participants: ParticipantRow list voption
  /// Runs with the created commit, on the UI thread.
  OnCommitted: Commit -> unit
  /// Runs with the queued commit when one was scheduled, on the UI
  /// thread.
  OnScheduled: (ScheduledCommit -> unit) voption
  /// Opens the scheduled queue, from the schedule panel's link.
  OnOpenQueue: (unit -> unit) voption
  /// The draft contract for hosts whose row recycles: the seed the
  /// composer starts from, and the sink every change reaches.
  Draft: DraftSource voption
  /// Reports the body box once it exists, so a host can focus it
  /// without searching the visual tree for a name.
  OnBodyReady: (TextBox -> unit) voption
}

/// The composer's target line: who the draft answers or what it
/// quotes, with the commit's short hash. A host shows it directly
/// above the composer so a stale draft never hides its target.
val targetLabel: verb: string * commit: Commit -> string

/// The target line with its cancel button: the button stands only
/// while the host passes a clear sink.
val targetStrip: label: string * onClear: (unit -> unit) voption -> Control

/// One composer, self-contained: mounts the MVUish program. It is
/// replace-only: a host builds one and never re-shows it, and the first
/// detach ends the program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLogging> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
