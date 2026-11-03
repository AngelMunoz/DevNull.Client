namespace DevNull.Client.Remotes

open System
open FSharp.UMX
open Microsoft.Extensions.Logging
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.RemotesState
open DevNull.Client.Stores

module internal Wire =

  /// The capabilities one signed remotes call needs, as one interface
  /// the calls' constraints check.
  [<Interface>]
  type SignEnv =
    inherit IProvideClock
    inherit IProvideNonceSource
    inherit IProvideSigner
    inherit IProvideHttp

  /// The account takes one saved settings answer.
  val takeAccount: me: Me * session: SessionStore -> unit

  /// One thread page read: which thread, with which credentials, and
  /// the window of the poll.
  type ThreadRead = {
    Target: ChatTarget
    KeyFile: string
    After: int option
    Before: int option
    ReactionsAfter: int option
  }

  /// One inbox page request: the credentials and key it signs with,
  /// the tab, and the cursor before which the page sits.
  [<Struct>]
  type InboxPageRead = {
    SignEnv: SignEnv
    KeyFile: string voption
    Tab: InboxTab
    Before: string option
  }

  /// One optional name as the org keeps it: trimmed, and absent when
  /// the trim leaves nothing.
  val trimmed: name: string option -> string option

  /// One list with the mute of one key's rows replaced.
  val mutedOnly:
    items: Conversation list option *
    key: string *
    mutedUntil: DateTimeOffset option ->
      Conversation list option

  /// The three tab lists under the saved pins, sorted back into the
  /// inbox's order.
  val repinLists: state: RemotesState -> RemotesState

  /// One chat target as the push API addresses it.
  module ThreadTarget =

    /// The wire form of one target.
    val ofChat:
      target: ChatTarget -> DevNull.Client.Core.Api.Remotes.ThreadTarget

  /// One thread page read: the call, mapped onto whichever kind of
  /// thread it came from.
  val readThread:
    signEnv: SignEnv * read: ThreadRead -> Async<Result<FreshThread, AppError>>

  /// One inbox page read, refused when the session holds no key.
  val readInbox: read: InboxPageRead -> Async<Result<Inbox, AppError>>

  /// How one driver reaches its store: post one pure change, read the
  /// state, the session's key file, whether polling may run, the three
  /// page callers it drives, and the logger of every failed call.
  type Driver = {
    Post: (RemotesState -> RemotesState) -> unit
    Read: unit -> Async<RemotesState>
    KeyFile: unit -> string voption
    CanRun: unit -> bool
    CallInbox: InboxTab * string option -> Async<Result<Inbox, AppError>>
    CallThread: ThreadRead -> Async<Result<FreshThread, AppError>>
    Push:
      PendingSend * DevNull.Client.Core.Api.Remotes.ThreadTarget
        -> Async<Result<Message, AppError>>
    Log: ILogger
  }

  /// The inbox drivers: one tab's pages, the poll, and the rebuilds a
  /// moved pin forces.
  module Inboxes =

    /// Reads the inbox and reconciles every loaded tab.
    val poll: driver: Driver -> Async<unit>

    /// Loads one tab's first page. A slower answer that races a pin
    /// move is dropped and the load runs again.
    val loadTab: driver: Driver * tab: InboxTab -> Async<unit>

    /// Loads one tab's next page, once the reader reaches the end of
    /// the loaded rows.
    val loadMore: driver: Driver * tab: InboxTab -> Async<unit>

    /// Rebuilds one tab's loaded depth as one continuous prefix. A pin
    /// that moves mid-rebuild restarts the walk from the first page.
    val refreshPages: driver: Driver * tab: InboxTab * more: bool -> Async<unit>

  /// The thread drivers: one thread's polls, older pages, and pushes.
  module Threads =

    /// Reads one thread: the messages after the loaded head, plus the
    /// reactions changed since the last version.
    val poll: driver: Driver * target: ChatTarget -> Async<unit>

    /// Loads the page above the loaded messages, under the loader's
    /// guards.
    val loadOlder: driver: Driver * target: ChatTarget -> Async<unit>

    /// One message the server holds: swapped into the thread, the
    /// staged copy dropped, and the inbox touched.
    val deliver:
      driver: Driver * target: ChatTarget * pending: PendingSend -> Async<unit>

  /// One org left: its rows and its thread leave the state.
  val leaveOrg: state: RemotesState * org: OrgRef -> RemotesState

  /// The one-step landing of a pin or an unpin answer: the whole pinned
  /// list and its version take the thread in one posted change, so a
  /// poll that left before the answer carries a version the merge
  /// already treats as stale.
  val landPins:
    post: ((RemotesState -> RemotesState) -> unit) *
    target: ChatTarget *
    result: MessagePinResult ->
      unit
