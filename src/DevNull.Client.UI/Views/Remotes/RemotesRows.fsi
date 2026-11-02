namespace DevNull.Client.Views

open DevNull.Client.Core.ApiTypes
open DevNull.Client.RemotesState

/// The remotes page's pure shaping: the inbox rows, the thread rows,
/// the reaction and typing words, and the pane's own row facts. No
/// function here needs an env.
module RemotesRows =

  /// The tabs the inbox shows: the fetched requests only while the
  /// account takes them.
  val tabsOf: requestsOn: bool -> (InboxTab * string) list

  /// What one logged org change says, from the reader's point of view.
  val eventText: viewerId: string * message: Message -> string

  /// One org's name: its own, or its other members' first names.
  val orgTitle: viewerId: string * row: Conversation -> string

  /// The last message as the row previews it: the event line for an
  /// org event, the body otherwise, with a voice note or an image
  /// standing in for an empty one.
  val excerptOf: viewerId: string * row: Conversation -> State.Excerpt

  /// The rows the column shows: the search's answer while one stands,
  /// the tab's list otherwise.
  val rowsOf: model: State.Model -> State.InboxRow list

  /// One row's stable key.
  val rowKey: row: State.InboxRow -> string

  /// The empty line each tab says when its list holds nothing.
  val emptyLine: tab: InboxTab -> string

  /// The thread's rows: the messages and staged sends folded into day
  /// breaks, the unread divider above the first message the reader
  /// had not seen, quiet event lines, and author runs.
  val threadRows: shape: State.ThreadShape -> State.ThreadRow list

  /// One thread row's stable key.
  val threadRowKey: row: State.ThreadRow -> string

  /// The typing names one thread carries, as handles: the partner, or
  /// the org's members.
  val typingNames: isOrg: bool * thread: ThreadState -> string list

  /// The message the receipts stand under: my newest one in a
  /// conversation, the newest one of anyone in an org.
  val statusOf:
    isOrg: bool * viewerId: string * thread: ThreadState -> Message option

  /// The reactions one message carries, folded to one chip per emoji.
  val summarizeReactions:
    reactions: Reaction list * viewerId: string -> State.ReactionSummary list

  /// The three reactions one click away, following how the user reacts.
  val quickReactions: usage: ReactionUse list -> string list

  /// The emoji the user reacts with, most used first.
  val frequentReactions: usage: ReactionUse list -> string list

  /// The word and shape one message's meta line carries: `pushed`
  /// until the other side pulled, then `pulled` with its own clock
  /// time in a direct thread, or the org's pull count.
  val receiptWord:
    isOrg: bool * thread: ThreadState * message: Message ->
      (bool * string) option

  /// The who-reacted line: the viewer, the partner, a member the
  /// thread carries — each as their handle — or someone the payload
  /// did not name.
  val whoNames:
    thread: ThreadState option * viewerId: string * who: string list -> string

  /// One message's pin state as the thread holds it: the org's pinned
  /// list once it loaded, the message's own mark before that.
  val pinnedNow: current: ThreadState * message: Message -> bool
