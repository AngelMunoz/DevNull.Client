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
  ReactionsInFlight: Map<int, int>
  ReactionsSettledAt: Map<int, DateTimeOffset>
  Activity: Map<string, DateTimeOffset>
  TypingPings: Map<string, DateTimeOffset>
  WatchCounts: Map<string, int>
  WindowActive: bool
  OpenTarget: ChatTarget option
  ScrollPlaces: Map<string, float>
}

/// One inbox thread page, whichever kind of thread it came from.
type FreshThread =
  | DirectFresh of thread: Thread
  | OrgFresh of thread: OrgThread

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

/// One landed message's row seed: who it went to, what the lists
/// already knew, and the message itself.
type LandedRow = {
  Target: ChatTarget
  Existing: Conversation option
  Message: Message
  PinnedAt: DateTimeOffset option
}

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

/// The rows one poll shows, with the watched aheads zeroed, and how
/// many the watcher held.
type WatchedRows = {
  Rows: Conversation list
  Watched: int
}

module ChatTargets =

  /// The store's key of one target: the partner's handle, or the
  /// org id under the `org:` prefix.
  let key(target: ChatTarget) : string =
    match target with
    | Partner user -> user.Handle
    | Org org -> "org:" + org.Id

  /// The key of one inbox row: the same key its thread carries.
  let summaryKey(summary: Conversation) : string =
    match summary.Kind, summary.Partner with
    | ConversationKind.Org, _ -> "org:" + summary.Id
    | _, Some partner -> partner.Handle
    | _, None -> "org:" + summary.Id

  /// The thread target of one inbox row.
  let targetOf(summary: Conversation) : ChatTarget =
    match summary.Kind, summary.Partner with
    | ConversationKind.Org, _ -> Org { Id = summary.Id; Name = summary.Name }
    | _, Some partner -> Partner partner
    | _, None -> Org { Id = summary.Id; Name = summary.Name }

  /// One row against another: pinned first, then newest message, then
  /// the newer id. The inbox's sort order.
  let order(left: Conversation, right: Conversation) : int =
    let ticks(at: DateTimeOffset option) =
      at |> Option.map(fun moment -> moment.UtcTicks) |> Option.defaultValue 0L

    match compare right.PinnedAt.IsSome left.PinnedAt.IsSome with
    | 0 ->
      match compare (ticks right.LastMessageAt) (ticks left.LastMessageAt) with
      | 0 -> compare right.Id left.Id
      | byFreshness -> byFreshness
    | byPins -> byPins

let emptyList: InboxList = {
  Items = None
  HasMore = false
  LoadingMore = false
}

let emptyThread(target: ChatTarget) : ThreadState = {
  Target = target
  Messages = []
  HasOlder = false
  ReadThroughId = 0
  PartnerReadThroughId = 0
  PartnerReadAt = None
  PartnerTyping = false
  Members = []
  Typing = []
  Role = None
  IsArchived = false
  PinnedAt = None
  MutedUntilAt = None
  Access = PushOpen
  Pending = []
  ReactionsVersion = None
  ReplyingTo = None
  Head = 0
  HeadUntil = 0
  IsLoaded = false
  IsLoadingOlder = false
}

let emptyState: RemotesState = {
  Open = emptyList
  Fetched = emptyList
  Stashed = emptyList
  Unread = None
  Requests = None
  Threads = Map.empty
  MutePreferences = Map.empty
  PinPreferences = Map.empty
  ReactionUsage = None
  InboxRevision = 0
  InvalidatedTabs = []
  PollingInbox = false
  MuteVersion = 0
  PinVersion = 0
  ReactionsInFlight = Map.empty
  ReactionsSettledAt = Map.empty
  Activity = Map.empty
  TypingPings = Map.empty
  WatchCounts = Map.empty
  WindowActive = true
  OpenTarget = None
  ScrollPlaces = Map.empty
}

let listTab(state: RemotesState, tab: InboxTab) : InboxList =
  match tab with
  | TabOpen -> state.Open
  | TabFetched -> state.Fetched
  | TabStashed -> state.Stashed

/// One thread under a change, when the thread exists. Curried on the
/// state, so one change pipes into `apply`.
let updateThread
  (key: string)
  (change: ThreadState -> ThreadState)
  (state: RemotesState)
  : RemotesState =
  state.Threads
  |> Map.tryFind key
  |> Option.map(fun thread -> {
    state with
        Threads = Map.add key (change thread) state.Threads
  })
  |> Option.defaultValue state

/// The inbox row a landed message puts at the top of the open list:
/// the existing row kept what it knew, the message fills the rest.
let landedRow(landing: LandedRow) : Conversation = {
  Id = landing.Existing |> Option.map _.Id |> Option.defaultValue ""
  Kind =
    (match landing.Target with
     | Partner _ -> Direct
     | Org _ -> ConversationKind.Org)
  Name = landing.Existing |> Option.bind _.Name
  Partner =
    (match landing.Target with
     | Partner user -> Some user
     | Org _ -> None)
  Members = landing.Existing |> Option.bind _.Members
  LastMessage = Some landing.Message
  LastMessageAt = Some landing.Message.CreatedAt
  Ahead = landing.Existing |> Option.map _.Ahead |> Option.defaultValue 0
  LastIncomingMessageId =
    landing.Existing |> Option.bind _.LastIncomingMessageId
  LastIncomingSender = landing.Existing |> Option.bind _.LastIncomingSender
  PartnerReadThroughId =
    landing.Existing
    |> Option.map _.PartnerReadThroughId
    |> Option.defaultValue 0
  PartnerReadAt = landing.Existing |> Option.bind _.PartnerReadAt
  IsArchived = false
  PinnedAt =
    landing.PinnedAt
    |> Option.orElse(landing.Existing |> Option.bind _.PinnedAt)
  MutedUntilAt = landing.Existing |> Option.bind _.MutedUntilAt
}

/// One tab under a change to its list. Curried on the state, so one
/// change pipes into the next.
let withTab
  (tab: InboxTab)
  (change: InboxList -> InboxList)
  (state: RemotesState)
  : RemotesState =
  match tab with
  | TabOpen -> { state with Open = change state.Open }
  | TabFetched -> {
      state with
          Fetched = change state.Fetched
    }
  | TabStashed -> {
      state with
          Stashed = change state.Stashed
    }

let messagesOf(page: FreshThread) : Message list =
  match page with
  | DirectFresh thread -> thread.Messages
  | OrgFresh thread -> thread.Messages

let reactionChangesOf(page: FreshThread) : ReactionChange list =
  match page with
  | DirectFresh thread -> thread.ReactionChanges
  | OrgFresh thread -> thread.ReactionChanges

let reactionVersionOf(page: FreshThread) : int =
  match page with
  | DirectFresh thread -> thread.ReactionsVersion
  | OrgFresh thread -> thread.ReactionsVersion

let hasOlderOf(page: FreshThread) : bool =
  match page with
  | DirectFresh thread -> thread.HasOlder
  | OrgFresh thread -> thread.HasOlder

let readThroughOf(page: FreshThread) : int =
  match page with
  | DirectFresh thread -> thread.ReadThroughId
  | OrgFresh thread -> thread.ReadThroughId

/// One page's messages fold into the loaded ones, the newer id of
/// each message winning, in id order.
let mergeMessages
  (current: Message list, incoming: Message list)
  : Message list =
  if incoming.IsEmpty then
    current
  else
    (Map.empty, current @ incoming)
    ||> List.fold(fun byId (message: Message) ->
      Map.add message.Id message byId)
    |> Map.toList
    |> List.map snd

/// The unread total of one list of rows.
let unreadOf(items: Conversation list) : int = items |> List.sumBy _.Ahead

/// One row list without one key.
let withoutKey
  (items: Conversation list option, key: string)
  : Conversation list option =
  items
  |> Option.map(List.filter(fun item -> ChatTargets.summaryKey item <> key))

/// One row list under the saved pins, sorted back into the inbox's
/// order.
let withConversationPins
  (
    preferences: Map<string, Versioned<DateTimeOffset option>>,
    items: Conversation list
  ) : Conversation list =
  items
  |> List.map(fun item ->
    match Map.tryFind (ChatTargets.summaryKey item) preferences with
    | Some saved -> { item with PinnedAt = saved.Value }
    | None -> item)
  |> List.sortWith(fun left right -> ChatTargets.order(left, right))

/// The first page fresh from the server, followed by the older rows
/// already scrolled into, without duplicates.
let withFirstPage
  (
    preferences: Map<string, Versioned<DateTimeOffset option>>,
    current: Conversation list option,
    firstPage: Conversation list
  ) : Conversation list =
  let fresh = firstPage |> List.map ChatTargets.summaryKey |> Set.ofList

  match firstPage |> List.tryLast with
  | None -> []
  | Some oldest ->
    let kept =
      current
      |> Option.defaultValue []
      |> List.filter(fun item ->
        not(fresh.Contains(ChatTargets.summaryKey item))
        && ChatTargets.order(item, oldest) >= 0)

    withConversationPins(preferences, firstPage @ kept)

/// One preference write, kept only when its version is the newest
/// that ever wrote the key.
let writePreference
  (preferences: Map<string, Versioned<'value>>, write: PreferenceWrite<'value>)
  : Map<string, Versioned<'value>> =
  match Map.tryFind write.Key preferences with
  | Some saved when saved.Version > write.Version -> preferences
  | _ ->
    Map.add
      write.Key
      {
        Value = write.Value
        Version = write.Version
      }
      preferences

/// The emoji usage after one toggle lands or reverts.
let withReactionUse
  (usage: ReactionUse list, emoji: string, change: int)
  : ReactionUse list =
  match usage |> List.tryFindIndex(fun entry -> entry.Emoji = emoji) with
  | Some index ->
    let entry = usage[index]
    let count = entry.Count + change

    if count <= 0 then
      List.removeAt index usage
    else
      List.updateAt index { entry with Count = count } usage
  | None when change > 0 -> usage @ [ { Emoji = emoji; Count = change } ]
  | None -> usage

/// The reactions that changed, except on messages the viewer is
/// reacting to right now, or reacted to after the poll was sent.
let withReactionChanges
  (
    state: RemotesState,
    messages: Message list,
    changes: ReactionChange list,
    polledAt: DateTimeOffset
  ) : Message list =
  let changed =
    changes
    |> List.choose(fun change ->
      let settled =
        state.ReactionsSettledAt
        |> Map.tryFind change.MessageId
        |> Option.defaultValue DateTimeOffset.MinValue

      let inFlight =
        state.ReactionsInFlight |> Map.containsKey change.MessageId

      if not inFlight && settled < polledAt then
        Some(change.MessageId, change.Reactions)
      else
        None)
    |> Map.ofList

  if changed.IsEmpty then
    messages
  else
    messages
    |> List.map(fun message ->
      match Map.tryFind message.Id changed with
      | Some reactions -> { message with Reactions = reactions }
      | None -> message)

/// One thread page merged into the loaded state: the messages, the
/// pending sends the page settles, the read baseline, and the pin and
/// mute preferences, under the versions the poll reserved.
let mergeThread(state: RemotesState, merge: ThreadMerge) : RemotesState =
  let page = merge.Page
  let freshMessages = messagesOf page

  let isTyping =
    match page with
    | DirectFresh thread -> thread.PartnerTyping
    | OrgFresh thread -> not thread.Typing.IsEmpty

  let current =
    state.Threads
    |> Map.tryFind merge.Key
    |> Option.defaultWith(fun () -> emptyThread merge.Target)

  let carried = {
    current with
        Messages =
          mergeMessages(
            withReactionChanges(
              state,
              current.Messages,
              reactionChangesOf page,
              merge.PolledAt
            ),
            freshMessages
          )
        ReactionsVersion =
          Some(
            max
              (current.ReactionsVersion |> Option.defaultValue 0)
              (reactionVersionOf page)
          )
        Pending =
          match freshMessages with
          | [] -> current.Pending
          | _ ->
            current.Pending
            |> List.filter(fun pending ->
              not(
                freshMessages
                |> List.exists(fun message ->
                  message.ClientId = ClientIds.value pending.ClientId)
              ))
        HasOlder =
          (if current.IsLoaded then
             current.HasOlder
           else
             hasOlderOf page)
        ReadThroughId = max current.ReadThroughId (readThroughOf page)
        Head =
          (if current.IsLoaded then
             current.Head
           else
             readThroughOf page)
        HeadUntil =
          (if current.IsLoaded then
             current.HeadUntil
           else
             freshMessages
             |> List.tryLast
             |> Option.map _.Id
             |> Option.defaultValue 0)
        IsLoaded = true
  }

  let merged =
    match page with
    | DirectFresh thread -> {
        carried with
            Target =
              (if current.IsLoaded then
                 current.Target
               else
                 Partner thread.Partner)
            PartnerReadThroughId = thread.PartnerReadThroughId
            PartnerReadAt = thread.PartnerReadAt
            PartnerTyping = thread.PartnerTyping
            IsArchived = thread.IsArchived
            PinnedAt = thread.PinnedAt
            MutedUntilAt = thread.MutedUntilAt
            Access = thread.Access
      }
    | OrgFresh thread -> {
        carried with
            Target = Org thread.Org
            Members = thread.Members
            Typing = thread.Typing
            Role = thread.Role
            IsArchived = thread.IsArchived
            PinnedAt = thread.PinnedAt
            MutedUntilAt = thread.MutedUntilAt
            Access = thread.Access
      }

  let write key value version = {
    Key = key
    Value = value
    Version = version
  }

  {
    state with
        MuteVersion = max state.MuteVersion merge.Versions.Mute
        PinVersion = max state.PinVersion merge.Versions.Pin
        MutePreferences =
          writePreference(
            state.MutePreferences,
            write merge.Key merged.MutedUntilAt merge.Versions.Mute
          )
        PinPreferences =
          writePreference(
            state.PinPreferences,
            write merge.Key merged.PinnedAt merge.Versions.Pin
          )
        Threads = Map.add merge.Key merged state.Threads
        Activity =
          if not freshMessages.IsEmpty || isTyping then
            Map.add merge.Key DateTimeOffset.UtcNow state.Activity
          else
            state.Activity
  }

/// One inbox page's preference writes merged into the loaded state.
let mergeInbox(state: RemotesState, merge: InboxMerge) : RemotesState =
  let write key value version = {
    Key = key
    Value = value
    Version = version
  }

  let mutes =
    (state.MutePreferences, merge.Page.Conversations)
    ||> List.fold(fun preferences item ->
      writePreference(
        preferences,
        write
          (ChatTargets.summaryKey item)
          item.MutedUntilAt
          merge.Versions.Mute
      ))

  let pins =
    (state.PinPreferences, merge.Known @ merge.Page.Conversations)
    ||> List.fold(fun preferences item ->
      writePreference(
        preferences,
        write
          (ChatTargets.summaryKey item)
          (if merge.Page.Pins.IsEmpty then
             item.PinnedAt
           else
             merge.Page.Pins |> Map.tryFind item.Id |> Option.flatten)
          merge.Versions.Pin
      ))

  {
    state with
        MuteVersion = max state.MuteVersion merge.Versions.Mute
        PinVersion = max state.PinVersion merge.Versions.Pin
        MutePreferences = mutes
        PinPreferences = pins
  }

/// Whether the pin state moved under the loaded depth: a saved pin a
/// known row lost, or a pinned row no known list holds.
let pinsMoved
  (state: RemotesState, page: Inbox, known: Conversation list)
  : bool =
  known
  |> List.exists(fun item ->
    state.PinPreferences
    |> Map.tryFind(ChatTargets.summaryKey item)
    |> Option.map _.Value
    <> Some item.PinnedAt)
  || page.Conversations
     |> List.exists(fun item ->
       item.PinnedAt.IsSome
       && not(known |> List.exists(fun current -> current.Id = item.Id)))

/// A moved pin makes the last row of every loaded tab unusable as a
/// cursor; the next scroll rebuilds instead of paging.
let invalidatePins(state: RemotesState) : RemotesState =
  let tabs =
    [ TabOpen; TabFetched; TabStashed ]
    |> List.filter(fun tab ->
      (listTab(state, tab)).Items.IsSome && (listTab(state, tab)).HasMore)

  {
    state with
        InboxRevision = state.InboxRevision + 1
        InvalidatedTabs = List.distinct(state.InvalidatedTabs @ tabs)
  }

/// One message's reactions under a change.
let withMessageReactions
  (
    messages: Message list,
    messageId: int,
    change: Reaction list -> Reaction list
  ) : Message list =
  messages
  |> List.map(fun message ->
    if message.Id = messageId then
      {
        message with
            Reactions = change message.Reactions
      }
    else
      message)

/// The optimistic half of one reaction toggle: the viewer's reaction
/// lands, the usage count moves, and the in-flight guard counts up.
let reactOptimistically
  (state: RemotesState, toggle: ReactionToggle)
  : RemotesState =
  let isMine(reaction: Reaction) =
    reaction.UserId = toggle.UserId && reaction.Emoji = toggle.Emoji

  let message =
    state.Threads
    |> Map.tryFind toggle.Key
    |> Option.bind(fun thread ->
      thread.Messages
      |> List.tryFind(fun message -> message.Id = toggle.MessageId))

  match message with
  | None -> state
  | Some message ->
    let isRemoving = message.Reactions |> List.exists isMine

    let add(reactions: Reaction list) =
      if reactions |> List.exists isMine then
        reactions
      else
        [
          yield! reactions
          {
            UserId = toggle.UserId
            Emoji = toggle.Emoji
          }
        ]


    let remove(reactions: Reaction list) =
      reactions |> List.filter(isMine >> not)

    let change = if isRemoving then remove else add

    let counted =
      state.ReactionsInFlight
      |> Map.tryFind toggle.MessageId
      |> Option.defaultValue 0

    {
      state with
          ReactionUsage =
            state.ReactionUsage
            |> Option.map(fun usage ->
              withReactionUse(
                usage,
                toggle.Emoji,
                if isRemoving then -1 else 1
              ))
          ReactionsInFlight =
            Map.add toggle.MessageId (counted + 1) state.ReactionsInFlight
          Threads =
            state.Threads
            |> Map.change
              toggle.Key
              (Option.map(fun thread -> {
                thread with
                    Messages =
                      withMessageReactions(
                        thread.Messages,
                        toggle.MessageId,
                        change
                      )
              }))
    }

/// One toggle settles: the guard counts down; the last one out sets
/// the reactions the server answered.
let settleReaction(state: RemotesState, settle: ReactionSettle) : RemotesState =
  let inFlight =
    state.ReactionsInFlight
    |> Map.tryFind settle.MessageId
    |> Option.defaultValue 1

  let left = inFlight - 1

  let counted = {
    state with
        ReactionsSettledAt =
          Map.add
            settle.MessageId
            DateTimeOffset.UtcNow
            state.ReactionsSettledAt
        ReactionsInFlight =
          (if left = 0 then
             Map.remove settle.MessageId state.ReactionsInFlight
           else
             Map.add settle.MessageId left state.ReactionsInFlight)
  }

  if left = 0 then
    {
      counted with
          Threads =
            state.Threads
            |> Map.map(fun _ thread -> {
              thread with
                  Messages =
                    withMessageReactions(
                      thread.Messages,
                      settle.MessageId,
                      fun _ -> settle.Reactions
                    )
            })
    }
  else
    counted

/// One toggle failed: the optimistic change reverts.
let revertReaction(state: RemotesState, toggle: ReactionToggle) : RemotesState =
  let isMine(reaction: Reaction) =
    reaction.UserId = toggle.UserId && reaction.Emoji = toggle.Emoji

  let message =
    state.Threads
    |> Map.tryFind toggle.Key
    |> Option.bind(fun thread ->
      thread.Messages
      |> List.tryFind(fun message -> message.Id = toggle.MessageId))

  match message with
  | None -> state
  | Some message ->
    let isRemoving = message.Reactions |> List.exists isMine

    let add(reactions: Reaction list) =
      if reactions |> List.exists isMine then
        reactions
      else
        [
          yield! reactions
          {
            UserId = toggle.UserId
            Emoji = toggle.Emoji
          }
        ]


    let remove(reactions: Reaction list) =
      reactions |> List.filter(isMine >> not)

    let change = if isRemoving then add else remove

    {
      state with
          ReactionUsage =
            state.ReactionUsage
            |> Option.map(fun usage ->
              withReactionUse(
                usage,
                toggle.Emoji,
                if isRemoving then 1 else -1
              ))
          Threads =
            state.Threads
            |> Map.change
              toggle.Key
              (Option.map(fun thread -> {
                thread with
                    Messages =
                      withMessageReactions(
                        thread.Messages,
                        toggle.MessageId,
                        change
                      )
              }))
    }

/// One thread pulled through its newest message: the read advances,
/// the row's ahead count clears, and the unread total recounts.
let markThreadRead(state: RemotesState, key: string) : RemotesState =
  match state.Threads |> Map.tryFind key with
  | None -> state
  | Some thread ->
    let latest =
      thread.Messages
      |> List.tryLast
      |> Option.map _.Id
      |> Option.defaultValue 0

    if latest <= thread.ReadThroughId then
      state
    else
      let read = { thread with ReadThroughId = latest }

      let items =
        state.Open.Items
        |> Option.map(fun items ->
          items
          |> List.map(fun item ->
            if ChatTargets.summaryKey item = key then
              { item with Ahead = 0 }
            else
              item))

      {
        state with
            Threads = Map.add key read state.Threads
            Open = { state.Open with Items = items }
            Unread = items |> Option.map unreadOf
      }

/// One conversation moved between the inbox and the stash, and the
/// unread total adjusted.
let stashMove(state: RemotesState, move: StashMove) : RemotesState =
  let moved = {
    move.Conversation with
        IsArchived = move.Archived
  }

  let openItems =
    if move.Archived then
      withoutKey(state.Open.Items, move.Key)
    else
      Some(
        withConversationPins(
          state.PinPreferences,
          [
            yield!
              withoutKey(state.Open.Items, move.Key) |> Option.defaultValue []
            moved
          ]
        )
      )

  let stashedItems =
    if move.Archived then
      state.Stashed.Items
      |> Option.map(fun items ->
        withConversationPins(
          state.PinPreferences,
          [
            yield! withoutKey(Some items, move.Key) |> Option.defaultValue []
            moved
          ]
        ))
    else
      withoutKey(state.Stashed.Items, move.Key)

  {
    state with
        Open = { state.Open with Items = openItems }
        Stashed = {
          state.Stashed with
              Items = stashedItems
        }
        Unread =
          match state.Unread with
          | Some unread ->
            Some(unread + (if move.Archived then -moved.Ahead else moved.Ahead))
          | None -> None
  }

/// One fetched request settled: the row leaves the fetched tab, the
/// counts fall, and a merge moves it into the open list.
let settleRequestState
  (state: RemotesState, settle: RequestSettle)
  : RemotesState =
  let openItems =
    match settle.Merging, settle.Row with
    | true, Some row ->
      Some(
        withConversationPins(
          state.PinPreferences,
          (withoutKey(state.Open.Items, settle.Key) |> Option.defaultValue [])
          @ [ { row with Ahead = 0 } ]
        )
      )
    | _ -> state.Open.Items

  {
    state with
        Open = { state.Open with Items = openItems }
        Fetched = {
          state.Fetched with
              Items = withoutKey(state.Fetched.Items, settle.Key)
        }
        Requests = Some(max 0 ((state.Requests |> Option.defaultValue 1) - 1))
        Unread = Some(max 0 ((state.Unread |> Option.defaultValue 1) - 1))
  }

/// One landed message's touch on the inbox: the conversation moves to
/// the top of the open list.
let touchInboxState
  (state: RemotesState, key: string, summary: Conversation)
  : RemotesState = {
  state with
      Open = {
        state.Open with
            Items =
              Some(
                summary
                :: (withoutKey(state.Open.Items, key) |> Option.defaultValue [])
              )
      }
      Stashed = {
        state.Stashed with
            Items = withoutKey(state.Stashed.Items, key)
      }
}

/// The thread a pushed message lands in: the message merges, the
/// staged copy drops, and the read and head advance.
let landMessage(state: RemotesState, landing: MessageLanding) : RemotesState =
  match state.Threads |> Map.tryFind landing.Key with
  | None -> state
  | Some thread ->
    let landed = {
      thread with
          Messages = mergeMessages(thread.Messages, [ landing.Message ])
          Pending =
            thread.Pending
            |> List.filter(fun item ->
              landing.Message.ClientId <> ClientIds.value item.ClientId)
          ReadThroughId = max thread.ReadThroughId landing.Message.Id
          Head = max thread.Head landing.Message.Id
    }

    {
      state with
          Threads = Map.add landing.Key landed state.Threads
          Activity = Map.add landing.Key DateTimeOffset.UtcNow state.Activity
    }

/// One push's whole landing: the thread takes the message, the open
/// list takes the row at its top, and the thread unarchives.
let landPush(state: RemotesState, landing: PushLanding) : RemotesState =
  let landed =
    landMessage(
      state,
      {
        Key = landing.Key
        Message = landing.Message
      }
    )

  let existing =
    [
      landed.Open.Items |> Option.defaultValue []
      landed.Stashed.Items |> Option.defaultValue []
    ]
    |> List.concat
    |> List.tryFind(fun item -> ChatTargets.summaryKey item = landing.Key)

  let row =
    landedRow {
      Target = landing.Target
      Existing = existing
      Message = landing.Message
      PinnedAt =
        landed.PinPreferences |> Map.tryFind landing.Key |> Option.bind _.Value
    }

  landed
  |> fun state -> touchInboxState(state, landing.Key, row)
  |> updateThread landing.Key (fun thread -> { thread with IsArchived = false })

/// One page appended to one tab, without the rows the tab already
/// holds.
let appendInboxPage(state: RemotesState, append: PageAppend) : RemotesState =
  let known = append.Known |> List.map ChatTargets.summaryKey |> Set.ofList

  withTab
    append.Tab
    (fun list -> {
      list with
          Items =
            Some(
              withConversationPins(
                state.PinPreferences,
                (list.Items |> Option.defaultValue [])
                @ (append.Page.Conversations
                   |> List.filter(fun item ->
                     not(known.Contains(ChatTargets.summaryKey item))))
              )
            )
    })
    state
  |> withTab append.Tab (fun list -> {
    list with
        HasMore = append.Page.HasMore
  })

/// Whether one row's thread sits open under the window.
let isWatched(state: RemotesState, row: Conversation) : bool =
  state.WindowActive
  && state.WatchCounts |> Map.containsKey(ChatTargets.summaryKey row)

/// The rows one poll shows, with the watched aheads zeroed, and the
/// ahead total the watcher held.
let zeroWatched(state: RemotesState, rows: Conversation list) : WatchedRows = {
  Rows =
    rows
    |> List.map(fun row ->
      if isWatched(state, row) then
        { row with Ahead = 0 }
      else
        row)
  Watched = rows |> List.filter(fun row -> isWatched(state, row)) |> unreadOf
}

/// One inbox poll's landing: the open list takes the first page, the
/// other tabs re-pin, and the counts settle.
let refreshInbox(state: RemotesState, landing: InboxLanding) : RemotesState = {
  state with
      Open = {
        state.Open with
            Items =
              (if landing.ReconcileOpen then
                 state.Open.Items
               else
                 Some(
                   withFirstPage(
                     state.PinPreferences,
                     state.Open.Items,
                     landing.Rows
                   )
                 ))
            HasMore = landing.Page.HasMore
      }
      Fetched = {
        state.Fetched with
            Items =
              state.Fetched.Items
              |> Option.map(fun items ->
                withConversationPins(state.PinPreferences, items))
      }
      Stashed = {
        state.Stashed with
            Items =
              state.Stashed.Items
              |> Option.map(fun items ->
                withConversationPins(state.PinPreferences, items))
      }
      Requests = Some landing.Page.RequestsCount
      Unread = Some(landing.Page.UnreadCount - landing.Watched)
}

/// One older thread page merged above the loaded messages, under the
/// loader's guards.
let mergeOlderPage
  (state: RemotesState, key: string, page: FreshThread)
  : RemotesState =
  updateThread
    key
    (fun thread -> {
      thread with
          Messages = mergeMessages(thread.Messages, messagesOf page)
          HasOlder = hasOlderOf page
          IsLoadingOlder = false
    })
    state

/// One staged send marks refused.
let refuseSend
  (state: RemotesState, key: string, clientId: string<ClientId>)
  : RemotesState =
  state.Threads
  |> Map.tryFind key
  |> Option.map(fun thread ->
    let pending =
      thread.Pending
      |> List.map(fun item ->
        if item.ClientId = clientId then
          { item with Rejected = true }
        else
          item)

    {
      state with
          Threads = Map.add key { thread with Pending = pending } state.Threads
    })
  |> Option.defaultValue state

/// One refused send stages again.
let restageSend
  (state: RemotesState, key: string, clientId: string<ClientId>)
  : RemotesState =
  state.Threads
  |> Map.tryFind key
  |> Option.map(fun thread ->
    let pending =
      thread.Pending
      |> List.map(fun item ->
        if item.ClientId = clientId then
          { item with Rejected = false }
        else
          item)

    {
      state with
          Threads = Map.add key { thread with Pending = pending } state.Threads
    })
  |> Option.defaultValue state

/// One message's link card landed: the preview the server answered
/// takes the message's slot.
let landLinkPreview
  (state: RemotesState, key: string, messageId: int, preview: LinkPreview)
  : RemotesState =
  updateThread
    key
    (fun thread -> {
      thread with
          Messages =
            thread.Messages
            |> List.map(fun message ->
              if message.Id = messageId then
                {
                  message with
                      LinkPreview = Some preview
                }
              else
                message)
    })
    state
