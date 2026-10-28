module DevNull.Client.Remotes

open System
open FSharp.UMX
open Microsoft.Extensions.Logging
open System.Reactive.Subjects
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types
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
  new:
    workers: RemotesWorkers * session: SessionStore * log: ILogger ->
      RemotesStore

  /// The state the mailbox last published, without a subscription.
  member Current: RemotesState

  /// Every state change, starting with the current one. Emits on the
  /// UI thread.
  member Observe: IObservable<RemotesState>

  /// The watcher that drives this store's polls.
  member Watcher: RemotesWatcher

  /// The number of threads a reader holds open.
  member WatchedCount: int

  /// When one thread last carried fresh messages or typing.
  member LastActivityAt: target: ChatTarget -> DateTimeOffset voption

  /// Records whether the window stands on screen; reading state keys
  /// on it.
  member SetWindowActive: active: bool -> unit

  /// Checks one conversation out: the store remembers it, so a page
  /// that returns reopens it. `None` checks nothing out.
  member SetOpenTarget: target: ChatTarget option -> unit

  /// Parks one thread's reading place, so a page that returns restores
  /// its scroll.
  member RememberScroll: key: string * offset: float -> unit

  /// Where one thread's reading last stood, or none for a fresh read.
  member ScrollPlaceOf: key: string -> float voption

  /// Opens one target's slot before anything was written in it, so a
  /// composer can park a draft against a thread that never loaded.
  member RememberTarget: target: ChatTarget -> unit

  /// Registers one reader of a thread: the reading baseline moves to
  /// now, and the store counts the thread as read while the handle
  /// lives.
  member WatchThread: target: ChatTarget -> IDisposable

  /// Reads one thread: the messages after the loaded head, plus the
  /// reactions changed since the last version.
  member PollThread: target: ChatTarget -> Async<unit>

  /// Reads the inbox and reconciles every loaded tab: fresh first
  /// page, requests count, and unread minus what the open threads
  /// already show.
  member PollInbox: unit -> Async<unit>

  /// Loads one tab's first page. A failure keeps the last list, or
  /// parks an empty one on a tab that never loaded.
  member LoadTab: tab: InboxTab -> Async<unit>

  /// Loads one tab's next page. A moved pin rebuilds the tab instead.
  member LoadMore: tab: InboxTab -> Async<unit>

  /// Rebuilds one tab's loaded depth as one continuous prefix: a
  /// moved pin makes the last row unusable as a cursor.
  member RefreshPages: tab: InboxTab * more: bool -> Async<unit>

  /// Loads the page above the loaded messages.
  member LoadOlder: target: ChatTarget -> Async<unit>

  /// Loads older pages until one message sits in the thread; answers
  /// whether it was found.
  member LoadUntil: target: ChatTarget * messageId: int -> Async<bool>

  /// Shows one message staged at once, then swaps in the stored
  /// message once the server has it. A refusal keeps the staged
  /// message marked refused.
  member Push: target: ChatTarget * message: OutgoingMessage -> Async<unit>

  /// Sends one refused message again under its own client id, so the
  /// server stores it once at most.
  member Retry: target: ChatTarget * clientId: string<ClientId> -> Async<unit>

  /// Toggles the viewer's reaction on one message: the optimistic
  /// change lands at once, and a live poll cannot clobber it while
  /// the request is out. Answers whether the server took it.
  member ToggleReaction:
    target: ChatTarget * messageId: int * emoji: string -> Async<bool>

  /// The reply slot of one thread: the message the composer answers.
  member SetReplyingTo: target: ChatTarget * reply: MessageParent option -> unit

  /// Pulls one thread through its newest message. Nothing is sent
  /// while `focused` is false; the ahead count and the unread total
  /// fall at once. Answers whether a read went out.
  member MarkRead: target: ChatTarget * focused: bool -> Async<bool>

  /// Says the user is typing, at most once every three seconds.
  member PingTyping: target: ChatTarget -> Async<unit>

  /// Moves one conversation between the inbox and the stash at once,
  /// and back if the server refuses. Answers whether the server took
  /// it.
  member SetArchived: target: ChatTarget * archived: bool -> Async<bool>

  /// Pins one thread to the top of the inbox, or unpins it, then
  /// rebuilds every tab the move invalidates.
  member SetPinned:
    target: ChatTarget * pinned: bool -> Async<Result<unit, AppError>>

  /// Mutes one thread for a duration, or unmutes it.
  member SetMuted:
    target: ChatTarget * duration: MuteDuration -> Async<Result<unit, AppError>>

  /// Merges one fetched request into the open conversations, or drops
  /// it for good. Answers whether the server took it.
  member SettleRequest: partner: User * merging: bool -> Async<bool>

  /// Starts one org with the members picked, and answers it.
  member CreateOrg:
    name: string option * members: string list ->
      Async<Result<OrgRef, AppError>>

  /// Renames one org, or takes its name away, and refreshes its
  /// thread.
  member RenameOrg:
    org: OrgRef * name: string option -> Async<Result<unit, AppError>>

  /// Adds members to one org, sharing its history or starting fresh.
  member AddOrgMembers:
    org: OrgRef * members: string list * shareHistory: bool ->
      Async<Result<unit, AppError>>

  /// Changes one member's role.
  member SetOrgMemberRole:
    org: OrgRef * username: string * role: OrgRole ->
      Async<Result<unit, AppError>>

  /// Takes one member out of an org, or leaves it: leaving takes the
  /// org out of the inbox.
  member RemoveOrgMember:
    org: OrgRef * username: string * isLeaving: bool ->
      Async<Result<unit, AppError>>

  /// Reads the emoji the user reacts with, most used first.
  member LoadReactionUsage: unit -> Async<unit>

  /// Reads one message's link card again, and lands it when the server
  /// has it. A still-pending answer lands nothing, so the reader can
  /// keep asking.
  member RefreshLinkPreview: target: ChatTarget * messageId: int -> Async<unit>

  /// Turns the direct messages on or off; the account takes the new
  /// settings.
  member SetEnabled: enabled: bool -> Async<Result<unit, AppError>>

  /// Saves who can push to the user and whether they share pulls; the
  /// account takes the new settings.
  member SetPreferences:
    from: RemotesFrom * pullReceipts: bool -> Async<Result<unit, AppError>>

/// The chat's scheduling: one reference-counted tick loop per thread,
/// one for the inbox, paused while the window is inactive or the
/// session is signed out.
and RemotesWatcher =
  new: store: RemotesStore * session: SessionStore -> RemotesWatcher

  /// Starts the loops under the activity gate: true while the window
  /// stands on screen. The disposable stops every loop.
  member Start: gate: IObservable<bool> -> IDisposable

  /// Registers one reader of a thread; the last release stops its
  /// loop.
  member WatchThread: target: ChatTarget -> IDisposable

  /// Registers one reader of the inbox; the last release stops its
  /// loop.
  member WatchInbox: unit -> IDisposable

/// Accessor for the remotes store capability.
type IProvideRemotes =
  abstract Remotes: RemotesStore
