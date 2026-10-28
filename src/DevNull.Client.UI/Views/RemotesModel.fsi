namespace DevNull.Client.Views

open System
open FSharp.UMX
open DevNull.Client.AttachmentSources
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.UiClock
open MVUish.Avalonia
open Navs

/// Everything the remotes page reads from its host.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideNatives
  and 'env :> IProvideRemotes> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
  /// The route the page lives in; owns the page's program lifetime.
  Context: RouteContext
}

/// The page's state logic: model, messages, update, the row shaping,
/// and the subscriptions that feed it from outside.
module State =

  /// One search's answer: the matching conversations, and the people a
  /// new remote can start with.
  type SearchFound = {
    Term: string
    Conversations: Conversation list
    Candidates: RemoteCandidate list
  }

  /// Where the thread reading goes next.
  type Landing =
    | Message of id: int
    | Present

  type Model = {
    State: RemotesState
    Tab: InboxTab
    Query: string
    Found: SearchFound option
    AskedMore: bool
    RequestsOn: bool
    Now: DateTimeOffset
    Riding: bool
    Landing: Landing option
    MarkedThrough: int
    DraftKey: string
    Draft: string
    /// The `@token` the caret stands in while one is being typed.
    Mention: string option
    Images: ImageAttachment list
    LinkChoice: LinkCard
    Voice: (ImageAttachment * Waveform) option
    Recording: bool
    OrgName: string
    OrgMembers: string list
    OrgShare: bool
    OrgRename: string
    Notice: string option
    SeenThrough: int
    Unseen: int
    Behind: bool
    PingHeard: int
  }

  type Msg =
    | StoreChanged of RemotesState
    | ClockTicked of moment: DateTimeOffset
    | SessionChanged of state: SessionState
    | TabChosen of InboxTab
    | Searched of text: string
    | SearchLanded of term: string * Result<SearchFound, AppError>
    | MoreWanted
    | Opened of ChatTarget
    | Closed
    | SettleWanted of partner: User * merging: bool
    | Settled of taken: bool
    | OlderWanted
    | RideMoved of riding: bool * behind: bool
    | ThreadScrolled of offset: float
    | GhostWanted of messageId: int
    | Walked of messageId: int * found: bool
    | Landed
    | PresentWanted
    | PreviewsChecked
    | ReadSettled of went: bool
    | StoreWorkDone
    | DraftChanged of text: string * caret: int
    | DraftSettled of text: string
    | ImageRemoved of index: int
    | LinkChosen of LinkCard
    | ReplyWanted of Message
    | ReplyCancelled
    | SendWanted
    | RetryWanted of clientId: string<ClientId>
    | PickWanted
    | Picked of ImageAttachment list
    | MuteWanted of MuteDuration
    | PinWanted of pinned: bool
    | ArchiveWanted of archived: bool
    | ActionSettled of Result<unit, AppError>
    | BlockWanted of partner: User
    | Blocked of Result<unit, AppError>
    | OrgCreateWanted
    | OrgNameChanged of name: string
    | OrgCreated of Result<OrgRef, AppError>
    | OrgRenameWanted
    | OrgRenameChanged of name: string
    | OrgRenamed of Result<unit, AppError>
    | OrgAddWanted
    | OrgMemberChanged of username: string
    | OrgShareChanged of shared: bool
    | OrgMembersAdded of Result<unit, AppError>
    | OrgRoleWanted of username: string * role: OrgRole
    | OrgRoleSet of Result<unit, AppError>
    | OrgRemoveWanted of username: string * leaving: bool
    | OrgRemoved of left: bool * Result<unit, AppError>
    | MenuNotice of message: string
    | ReactWanted of messageId: int * emoji: string
    | ReactSettled of took: bool
    | RecordWanted
    | RecordCancelled
    | PingWanted
    | Pinged of Result<unit, AppError>
    | VoiceStaged of Result<(ImageAttachment * Waveform), string>
    | VoiceDiscarded

  /// The search term the raw box text stands for.
  val inline termOf: query: string -> string

  /// The tabs the inbox shows, with their labels.
  val tabsOf: requestsOn: bool -> (InboxTab * string) list

  /// What one logged org change says, from the reader's point of view.
  val eventText: viewerId: string * message: Message -> string

  /// One org's name: its own, or its other members' first names.
  val orgTitle: viewerId: string * row: Conversation -> string

  /// One row's excerpt: what the last message says, and who said it.
  type Excerpt = {
    Prefix: string
    Text: string
    IsEvent: bool
  }

  val excerptOf: viewerId: string * row: Conversation -> Excerpt

  /// One row of the inbox column: a conversation of one tab, or a
  /// person a new remote can start with.
  type InboxRow =
    | Conversation of tab: InboxTab * row: Conversation
    | Candidate of person: RemoteCandidate

  val rowsOf: model: Model -> InboxRow list

  /// One row's stable key.
  val rowKey: row: InboxRow -> string

  /// The empty line each tab says when its list holds nothing.
  val emptyLine: tab: InboxTab -> string

  /// One staged message as the thread shows it.
  type Bubble =
    | Sent of Message
    | Staged of PendingSend

  /// One author's run of bubbles.
  type Run = {
    Key: string
    IsMine: bool
    AuthorId: string
    Author: User option
    Bubbles: Bubble list
  }

  /// One row of the thread.
  type ThreadRow =
    | Day of key: string * label: string
    | Head of count: int
    | Event of message: Message
    | Run of run: Run

  /// The thread's rows: messages and staged sends folded into day
  /// breaks, the unread divider, quiet event lines, and author runs.
  val threadRows:
    viewerId: string * isOrg: bool * now: DateTimeOffset * thread: ThreadState ->
      ThreadRow list

  /// One thread row's stable key.
  val threadRowKey: row: ThreadRow -> string

  /// What the typing line says: one name, two names, or the crowd. The
  /// dots are the view's own beat, never part of the word.
  val typingText: who: string list -> string

  /// The typing names one thread carries, as handles.
  val typingNames: isOrg: bool * thread: ThreadState -> string list

  /// The message the receipts stand under.
  val statusOf:
    isOrg: bool * viewerId: string * thread: ThreadState -> Message option

  /// One chip per emoji: how many left it, whether the viewer's is
  /// among them, and who they are.
  type ReactionSummary = {
    Emoji: string
    Count: int
    IsMine: bool
    Who: string list
  }

  /// The reactions one message carries, folded to one chip per emoji.
  val summarizeReactions:
    reactions: Reaction list * viewerId: string -> ReactionSummary list

  /// The three reactions one click away, following how the user reacts.
  val quickReactions: usage: ReactionUse list -> string list

  /// The emoji the user reacts with, most used first.
  val frequentReactions: usage: ReactionUse list -> string list

  /// The links one draft carries, first-wins order.
  val linksOf: text: string -> string list

  /// The `@token` the caret stands in, when one does: the letters after
  /// an `@` that opens a word, up to the caret. None once the token
  /// closed or the caret sits elsewhere.
  val mentionOf: text: string * caret: int -> string option

  /// One handle's completion of the mention at the caret: the rewritten
  /// text and where the caret lands after it.
  val mentionCompleted:
    text: string * caret: int * handle: string -> string * int

  /// One link as the picker names it: the address without its scheme.
  val plainLink: url: string -> string

  /// One recording's bytes staged as the push API takes them.
  val stageVoice:
    bytes: byte array -> Result<(ImageAttachment * Waveform), string>

  val init: props: Props<'env> -> unit -> Model * Cmd<Msg>

  val update: props: Props<'env> -> msg: Msg -> model: Model -> Model * Cmd<Msg>

  /// The page's inlets from outside: the shared clock, the session,
  /// the store, and the watcher's readership handles.
  val subscribe: props: Props<'env> -> model: Model -> Sub<Msg>
