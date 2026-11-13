module DevNull.Client.Workbench.Staging

open System
open Avalonia.Controls
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.Stores

/// One scheduled queue read as staging shows it: the first page, the
/// total the server counts, and the read's state.
[<Struct>]
type Queue = {
  Items: ScheduledCommit list
  Total: int
  Loading: bool
  Error: AppError option
}

/// The staging state the host owns: the draft text, the armed quote,
/// and the scheduled queue.
[<Struct>]
type Model = {
  Draft: string
  Quote: Commit option
  Queue: Queue
}

/// The staging view: the host's open flag with the staging state.
[<Struct>]
type View = {
  Open: bool
  Draft: string
  Quote: Commit option
  Queue: Queue
}

/// What staging needs from the host: the composer's environment and
/// session, and the host's toggle, draft, post, schedule, and
/// quote-clear callbacks.
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
  Session: SignedInSession option
  OnToggle: unit -> unit
  OnDraftChanged: string -> unit
  OnPosted: Commit -> unit
  OnScheduled: ScheduledCommit -> unit
  OnClearQuote: unit -> unit
}

/// The empty staging state: no draft, no quote, and an unread queue.
val initial: Model

/// The staging strip and, when open, the composer and scheduled queue.
val view: props: Props<'env> -> model: IObservable<View> -> Control
