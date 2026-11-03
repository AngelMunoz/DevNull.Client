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

/// The workers every signed remotes call needs.
type RemotesWorkers = {
  Clock: IClock
  NonceSource: INonceSource
  Signer: ISigner
  Http: IHttp
}

/// The chat's state machine: one mailbox owns the whole state, every
/// operation awaits its call and posts one pure merge, and the merges
/// live as plain functions in `RemotesState`. The timers that drive
/// the polls live in `RemotesWatcher`; the store never schedules.
type RemotesStore =
  /// The state the mailbox last published, without a subscription.
  abstract Current: RemotesState

  /// Every state change, starting with the current one. Emits on the
  /// UI thread.
  abstract Observe: IObservable<RemotesState>

  /// The watcher that drives this store's polls.
  abstract Watcher: RemotesWatcher

  /// The number of threads a reader holds open.
  abstract WatchedCount: int

  /// When one thread last carried fresh messages or typing.
  abstract LastActivityAt: target: ChatTarget -> DateTimeOffset voption

  /// Records whether the window stands on screen; reading state keys
  /// on it.
  abstract SetWindowActive: active: bool -> unit

  /// Checks one conversation out: the store remembers it, so a page
  /// that returns reopens it. `None` checks nothing out.
  abstract SetOpenTarget: target: ChatTarget option -> unit

  /// Parks one thread's reading place, so a page that returns restores
  /// its scroll.
  abstract RememberScroll: key: string * offset: float -> unit

  /// Where one thread's reading last stood, or none for a fresh read.
  abstract ScrollPlaceOf: key: string -> float voption

  /// Opens one target's slot before anything was written in it, so a
  /// composer can park a draft against a thread that never loaded.
  abstract RememberTarget: target: ChatTarget -> unit

  /// Registers one reader of a thread: the reading baseline moves to
  /// now, and the store counts the thread as read while the handle
  /// lives.
  abstract WatchThread: target: ChatTarget -> IDisposable

  /// Reads one thread: the messages after the loaded head, plus the
  /// reactions changed since the last version.
  abstract PollThread: target: ChatTarget -> Async<unit>

  /// Reads the inbox and reconciles every loaded tab: fresh first
  /// page, requests count, and unread minus what the open threads
  /// already show.
  abstract PollInbox: unit -> Async<unit>

  /// Loads one tab's first page. A failure keeps the last list, or
  /// parks an empty one on a tab that never loaded.
  abstract LoadTab: tab: InboxTab -> Async<unit>

  /// Loads one tab's next page. A moved pin rebuilds the tab instead.
  abstract LoadMore: tab: InboxTab -> Async<unit>

  /// Rebuilds one tab's loaded depth as one continuous prefix: a
  /// moved pin makes the last row unusable as a cursor.
  abstract RefreshPages: tab: InboxTab * more: bool -> Async<unit>

  /// Loads the page above the loaded messages.
  abstract LoadOlder: target: ChatTarget -> Async<unit>

  /// Loads older pages until one message sits in the thread; answers
  /// whether it was found.
  abstract LoadUntil: target: ChatTarget * messageId: int -> Async<bool>

  /// Shows one message staged at once, then swaps in the stored
  /// message once the server has it. A refusal keeps the staged
  /// message marked refused.
  abstract Push: target: ChatTarget * message: OutgoingMessage -> Async<unit>

  /// Sends one refused message again under its own client id, so the
  /// server stores it once at most.
  abstract Retry: target: ChatTarget * clientId: string<ClientId> -> Async<unit>

  /// Toggles the viewer's reaction on one message: the optimistic
  /// change lands at once, and a live poll cannot clobber it while
  /// the request is out. Answers whether the server took it.
  abstract ToggleReaction:
    target: ChatTarget * messageId: int * emoji: string -> Async<bool>

  /// The reply slot of one thread: the message the composer answers.
  abstract SetReplyingTo:
    target: ChatTarget * reply: MessageParent option -> unit

  /// Pulls one thread through its newest message. Nothing is sent
  /// while `focused` is false; the ahead count and the unread total
  /// fall at once. Answers whether a read went out.
  abstract MarkRead: target: ChatTarget * focused: bool -> Async<bool>

  /// Says the user is typing, at most once every three seconds.
  abstract PingTyping: target: ChatTarget -> Async<unit>

  /// Moves one conversation between the inbox and the stash at once,
  /// and back if the server refuses. Answers whether the server took
  /// it.
  abstract SetArchived: target: ChatTarget * archived: bool -> Async<bool>

  /// Pins one thread to the top of the inbox, or unpins it, then
  /// rebuilds every tab the move invalidates.
  abstract SetPinned:
    target: ChatTarget * pinned: bool -> Async<Result<unit, AppError>>

  /// Pins one message of an org thread. The answer's whole pinned
  /// list and its version land in one step, so a poll sent before the
  /// pin cannot roll it back.
  abstract PinMessage:
    target: ChatTarget * message: Message -> Async<Result<unit, AppError>>

  /// Takes one pin off a message of an org thread, the same one-step
  /// landing the pin takes.
  abstract UnpinMessage:
    target: ChatTarget * message: Message -> Async<Result<unit, AppError>>

  /// Mutes one thread for a duration, or unmutes it.
  abstract SetMuted:
    target: ChatTarget * duration: MuteDuration -> Async<Result<unit, AppError>>

  /// Merges one fetched request into the open conversations, or drops
  /// it for good. Answers whether the server took it.
  abstract SettleRequest: partner: User * merging: bool -> Async<bool>

  /// Starts one org with the members picked, and answers it.
  abstract CreateOrg:
    name: string option * members: string list ->
      Async<Result<OrgRef, AppError>>

  /// Renames one org, or takes its name away, and refreshes its
  /// thread.
  abstract RenameOrg:
    org: OrgRef * name: string option -> Async<Result<unit, AppError>>

  /// Adds members to one org, sharing its history or starting fresh.
  abstract AddOrgMembers:
    org: OrgRef * members: string list * shareHistory: bool ->
      Async<Result<unit, AppError>>

  /// Changes one member's role.
  abstract SetOrgMemberRole:
    org: OrgRef * username: string * role: OrgRole ->
      Async<Result<unit, AppError>>

  /// Takes one member out of an org, or leaves it: leaving takes the
  /// org out of the inbox.
  abstract RemoveOrgMember:
    org: OrgRef * username: string * isLeaving: bool ->
      Async<Result<unit, AppError>>

  /// Reads the emoji the user reacts with, most used first.
  abstract LoadReactionUsage: unit -> Async<unit>

  /// Reads one message's link card again, and lands it when the server
  /// has it. A still-pending answer lands nothing, so the reader can
  /// keep asking.
  abstract RefreshLinkPreview:
    target: ChatTarget * messageId: int -> Async<unit>

  /// Turns the direct messages on or off; the account takes the new
  /// settings.
  abstract SetEnabled: enabled: bool -> Async<Result<unit, AppError>>

  /// Saves who can push to the user and whether they share pulls; the
  /// account takes the new settings.
  abstract SetPreferences:
    from: RemotesFrom * pullReceipts: bool -> Async<Result<unit, AppError>>

/// Accessor for the remotes store capability.
type IProvideRemotes =
  abstract Remotes: RemotesStore

/// The chat's state machine over one mailbox.
module RemotesStore =

  /// The store over the given workers, session, and logger.
  val create:
    workers: RemotesWorkers * session: SessionStore * log: ILogger ->
      RemotesStore
