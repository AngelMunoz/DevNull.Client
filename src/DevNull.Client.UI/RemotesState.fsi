module DevNull.Client.RemotesState

open System
open FSharp.UMX
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Types

/// One staged image, as the push API takes it.
type ImageAttachment = DevNull.Client.Core.Commit.ImageAttachment

/// One voice note's loudness bars: 48 values between 0 and 100.
type Waveform = DevNull.Client.Core.Commit.Waveform

/// Which link gets the card on one pushed message.
type LinkCard = DevNull.Client.Core.Api.Remotes.LinkCard

/// Who a conversation is with: one partner, or one org.
type ChatTarget =
  | Partner of user: User
  | Org of org: OrgRef

/// One message to push: the body, the reply it answers, the staged
/// images, the link chosen for the card, and a voice note, which
/// rides alone or with a reply.
type OutgoingMessage = {
  Body: string option
  ReplyTo: int option
  Card: LinkCard
  Images: ImageAttachment list
  ImageDescriptions: string option list
  Voice: (ImageAttachment * Waveform) option
}

/// One message on its way in: the staged payload with its client id,
/// when it was staged, and whether the server refused it.
type PendingSend = {
  Message: OutgoingMessage
  ClientId: string<ClientId>
  CreatedAt: DateTimeOffset
  Rejected: bool
}

/// One tab of the inbox: its loaded rows, whether more sit behind the
/// last one, and whether the next page is loading.
type InboxList = {
  Items: Conversation list option
  HasMore: bool
  LoadingMore: bool
}

/// One conversation's whole state: the loaded messages, what each
/// side has read, what is pending, and where the reading stands.
type ThreadState = {
  Target: ChatTarget
  Messages: Message list
  HasOlder: bool
  ReadThroughId: int
  PartnerReadThroughId: int
  PartnerReadAt: DateTimeOffset option
  PartnerTyping: bool
  Members: ThreadMember list
  Typing: string list
  Role: OrgRole option
  IsArchived: bool
  PinnedAt: DateTimeOffset option
  MutedUntilAt: DateTimeOffset option
  Access: PushAccess
  Pending: PendingSend list
  ReactionsVersion: int option
  ReplyingTo: MessageParent option
  Head: int
  HeadUntil: int
  IsLoaded: bool
  IsLoadingOlder: bool
}

/// One saved value stamped with the version of the request that wrote
/// it, so a slower, older answer never overwrites a newer one.
type Versioned<'value> = { Value: 'value; Version: int }

/// The chat's whole state: the three inbox tabs, the open threads,
/// the versioned pin and mute preferences, the emoji the user reacts
/// with, and the bookkeeping the polls and guards keep.
type RemotesState = {
  Open: InboxList
  Fetched: InboxList
  Stashed: InboxList
  Unread: int option
  Requests: int option
  Threads: Map<string, ThreadState>
  MutePreferences: Map<string, Versioned<DateTimeOffset option>>
  PinPreferences: Map<string, Versioned<DateTimeOffset option>>
  ReactionUsage: ReactionUse list option
  InboxRevision: int
  InvalidatedTabs: InboxTab list
  PollingInbox: bool
  MuteVersion: int
  PinVersion: int
  /// The live reaction toggles, counted per message, so a poll cannot
  /// clobber a change the viewer is making.
  ReactionsInFlight: Map<int, int>
  /// When each message's last reaction toggle settled.
  ReactionsSettledAt: Map<int, DateTimeOffset>
  /// When each thread last carried fresh messages or typing.
  Activity: Map<string, DateTimeOffset>
  /// When each thread last sent a typing ping.
  TypingPings: Map<string, DateTimeOffset>
  /// How many readers hold each thread open.
  WatchCounts: Map<string, int>
  /// Whether the window is focused and visible; reading state keys on
  /// it.
  WindowActive: bool
}

/// One inbox thread page, whichever kind of thread it came from.
type FreshThread =
  | DirectFresh of thread: Thread
  | OrgFresh of thread: OrgThread

/// One page's messages.
val messagesOf: page: FreshThread -> Message list

/// Whether one page says older messages sit above it.
val hasOlderOf: page: FreshThread -> bool

/// The read position one page reports.
val readThroughOf: page: FreshThread -> int

/// The preference versions one request reserved when it left.
type VersionPair = { Mute: int; Pin: int }

/// One thread page's merge into the loaded state.
type ThreadMerge = {
  Key: string
  Target: ChatTarget
  Page: FreshThread
  PolledAt: DateTimeOffset
  Versions: VersionPair
}

/// One inbox page's merge into the loaded state.
type InboxMerge = {
  Tab: InboxTab
  Page: Inbox
  Known: Conversation list
  Versions: VersionPair
}

/// One preference write: the key, the value, and the version of the
/// request that carried it.
type PreferenceWrite<'value> = {
  Key: string
  Value: 'value
  Version: int
}

/// One reaction toggle: the thread key, the message, the emoji, and
/// the viewer's id.
type ReactionToggle = {
  Key: string
  MessageId: int
  Emoji: string
  UserId: string
}

/// One settled toggle: the message and the reactions the server
/// answered.
type ReactionSettle = {
  MessageId: int
  Reactions: Reaction list
}

/// One stash move: the row, its key, and the direction.
type StashMove = {
  Conversation: Conversation
  Key: string
  Archived: bool
}

/// One settled request: the row's key, its fetched row on a merge,
/// and whether it merged.
type RequestSettle = {
  Key: string
  Row: Conversation option
  Merging: bool
}

/// One landed push: the thread's key and the message.
type MessageLanding = { Key: string; Message: Message }

/// One push's whole landing: the message merges, the inbox row moves
/// to the top, and the thread unarchives.
type PushLanding = {
  Key: string
  Target: ChatTarget
  Message: Message
}

/// One page appended to a tab it already holds rows in.
type PageAppend = {
  Tab: InboxTab
  Page: Inbox
  Known: Conversation list
}

/// One inbox poll's landing: the page read, the rows with the watched
/// aheads already zeroed, and whether a rebuild owns the open list.
type InboxLanding = {
  Page: Inbox
  Rows: Conversation list
  Watched: int
  ReconcileOpen: bool
}

/// The rows one poll shows, with the watched aheads zeroed, and the
/// ahead total the watcher held.
type WatchedRows = {
  Rows: Conversation list
  Watched: int
}

/// The keys and targets of the store's threads.
module ChatTargets =

  /// The store's key of one target: the partner's username, or the
  /// org id under the `org:` prefix.
  val key: target: ChatTarget -> string

  /// The key of one inbox row: the same key its thread carries.
  val summaryKey: summary: Conversation -> string

  /// The thread target of one inbox row.
  val targetOf: summary: Conversation -> ChatTarget

  /// One row against another: pinned first, then newest message, then
  /// the newer id. The inbox's sort order.
  val order: left: Conversation * right: Conversation -> int

/// The state at boot, before anything loaded.
val emptyState: RemotesState

/// One thread's slot before anything loaded.
val emptyThread: target: ChatTarget -> ThreadState

/// One tab's list.
val listTab: state: RemotesState * tab: InboxTab -> InboxList

/// One thread under a change, when the thread exists. Curried on the
/// state, so one change pipes into `apply`.
val updateThread:
  key: string ->
  change: (ThreadState -> ThreadState) ->
  state: RemotesState ->
    RemotesState

/// One landed message's row seed: who it went to, what the lists
/// already knew, and the message itself.
type LandedRow = {
  Target: ChatTarget
  Existing: Conversation option
  Message: Message
  PinnedAt: DateTimeOffset option
}

/// The inbox row a landed message puts at the top of the open list:
/// the existing row kept what it knew, the message fills the rest.
val landedRow: landing: LandedRow -> Conversation

/// One tab under a change to its list. Curried on the state, so one
/// change pipes into the next.
val withTab:
  tab: InboxTab ->
  change: (InboxList -> InboxList) ->
  state: RemotesState ->
    RemotesState

/// One page's messages fold into the loaded ones, the newer id of
/// each message winning, in id order.
val mergeMessages:
  current: Message list * incoming: Message list -> Message list

/// The unread total of one list of rows.
val unreadOf: items: Conversation list -> int

/// One row list without one key.
val withoutKey:
  items: Conversation list option * key: string -> Conversation list option

/// One row list under the saved pins, sorted back into the inbox's
/// order.
val withConversationPins:
  preferences: Map<string, Versioned<DateTimeOffset option>> *
  items: Conversation list ->
    Conversation list

/// The first page fresh from the server, followed by the older rows
/// already scrolled into, without duplicates.
val withFirstPage:
  preferences: Map<string, Versioned<DateTimeOffset option>> *
  current: Conversation list option *
  firstPage: Conversation list ->
    Conversation list

/// One preference write, kept only when its version is the newest
/// that ever wrote the key.
val writePreference:
  preferences: Map<string, Versioned<'value>> * write: PreferenceWrite<'value> ->
    Map<string, Versioned<'value>>

/// The emoji usage after one toggle lands or reverts.
val withReactionUse:
  usage: ReactionUse list * emoji: string * change: int -> ReactionUse list

/// One thread page merged into the loaded state: the messages, the
/// pending sends the page settles, the read baseline, and the pin and
/// mute preferences, under the versions the poll reserved.
val mergeThread: state: RemotesState * merge: ThreadMerge -> RemotesState

/// One inbox page's preference writes merged into the loaded state.
val mergeInbox: state: RemotesState * merge: InboxMerge -> RemotesState

/// Whether the pin state moved under the loaded depth: a saved pin a
/// known row lost, or a pinned row no known list holds.
val pinsMoved:
  state: RemotesState * page: Inbox * known: Conversation list -> bool

/// A moved pin makes the last row of every loaded tab unusable as a
/// cursor; the next scroll rebuilds instead of paging.
val invalidatePins: state: RemotesState -> RemotesState

/// The optimistic half of one reaction toggle: the viewer's reaction
/// lands, the usage count moves, and the in-flight guard counts up.
val reactOptimistically:
  state: RemotesState * toggle: ReactionToggle -> RemotesState

/// One toggle settles: the guard counts down; the last one out sets
/// the reactions the server answered.
val settleReaction: state: RemotesState * settle: ReactionSettle -> RemotesState

/// One toggle failed: the optimistic change reverts.
val revertReaction: state: RemotesState * toggle: ReactionToggle -> RemotesState

/// One thread pulled through its newest message: the read advances,
/// the row's ahead count clears, and the unread total recounts.
val markThreadRead: state: RemotesState * key: string -> RemotesState

/// One conversation moved between the inbox and the stash, and the
/// unread total adjusted.
val stashMove: state: RemotesState * move: StashMove -> RemotesState

/// One fetched request settled: the row leaves the fetched tab, the
/// counts fall, and a merge moves it into the open list.
val settleRequestState:
  state: RemotesState * settle: RequestSettle -> RemotesState

/// One landed message's touch on the inbox: the conversation moves to
/// the top of the open list.
val touchInboxState:
  state: RemotesState * key: string * summary: Conversation -> RemotesState

/// The thread a pushed message lands in: the message merges, the
/// staged copy drops, and the read and head advance.
val landMessage: state: RemotesState * landing: MessageLanding -> RemotesState

/// One push's whole landing: the thread takes the message, the open
/// list takes the row at its top, and the thread unarchives.
val landPush: state: RemotesState * landing: PushLanding -> RemotesState

/// One page appended to one tab, without the rows the tab already
/// holds.
val appendInboxPage: state: RemotesState * append: PageAppend -> RemotesState

/// One inbox poll's landing: the open list takes the first page, the
/// other tabs re-pin, and the counts settle.
val refreshInbox: state: RemotesState * landing: InboxLanding -> RemotesState

/// The rows one poll shows, with the watched aheads zeroed, and how
/// many the watcher held.
val zeroWatched: state: RemotesState * rows: Conversation list -> WatchedRows

/// One older thread page merged above the loaded messages, under the
/// loader's guards.
val mergeOlderPage:
  state: RemotesState * key: string * page: FreshThread -> RemotesState

/// One staged send marks refused.
val refuseSend:
  state: RemotesState * key: string * clientId: string<ClientId> -> RemotesState

/// One refused send stages again.
val restageSend:
  state: RemotesState * key: string * clientId: string<ClientId> -> RemotesState
