namespace DevNull.Client.Views

open System
open FSharp.UMX
open DevNull.Client.AttachmentSources
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.UiClock
open Navs

/// Everything the remotes page reads from its host.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideNatives
  and 'env :> IProvideRemotes
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
  /// The route the page lives in; owns the page's program lifetime.
  Context: RouteContext
}

/// The remotes page's state types: the model, its messages, and the
/// row shapes the inbox and the thread render.
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
    | Message of id: int * beat: int
    | Present

  /// One action whose call is in flight: the guarded surfaces stay
  /// disabled until it settles.
  type Flight =
    | PinFlight
    | UnpinFlight
    | MuteFlight
    | ArchiveFlight
    | BlockFlight
    | RemoveFlight

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
    /// The beat of the latest message landing: a repeated landing on
    /// the same message takes a new beat, so its row's timer can never
    /// end an older one.
    LandingBeat: int
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
    /// The message the first-pin dialog stands open for, when one
    /// does.
    PinAsk: Message option
    /// The action in flight, when one is: the pin and confirm
    /// surfaces stay disabled, and the guarded menu actions wait,
    /// until its call settles.
    Busy: Flight option
    /// Whether the pinned-messages panel stands open over the reading.
    PinnedPanel: bool
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
    /// The landing's row stands realized, so its flash beat starts:
    /// the beat runs from the row being seen, never from the ask, so
    /// a slow layout cannot spend it before the row exists.
    | FlashSeen of landing: Landing
    | Landed of landing: Landing
    | PresentWanted
    | PreviewsChecked
    | ReadSettled of went: bool
    | StoreWorkDone
    | DraftChanged of text: string * caret: int
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
    | ArchiveFailed of message: string
    | ActionSettled of Result<unit, AppError>
    | PinAskWanted of message: Message
    | PinConfirmed of keep: bool
    | PinAskCancelled
    | MessageUnpinWanted of message: Message
    | PinnedPanelWanted of shown: bool
    | MessagePinSettled of Result<unit, AppError>
    | MessageUnpinSettled of Result<unit, AppError>
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
    | VoiceStaged of Result<(ImageAttachment * Waveform), AppError>
    | VoiceDiscarded

  /// One row's excerpt: what the last message says, and who said it.
  type Excerpt = {
    Prefix: string
    Text: string
    IsEvent: bool
  }

  /// One row of the inbox column: a conversation of one tab, or a
  /// person a new remote can start with.
  type InboxRow =
    | Conversation of tab: InboxTab * row: Conversation
    | Candidate of person: RemoteCandidate

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

  /// One thread's shaping ask: who the viewer is, whether the thread
  /// is an org's, the clock the day labels read, and the thread state
  /// itself.
  [<Struct>]
  type ThreadShape = {
    ViewerId: string
    IsOrg: bool
    Now: DateTimeOffset
    Thread: ThreadState
  }

  /// One chip per emoji: how many left it, whether the viewer's is
  /// among them, and who they are.
  type ReactionSummary = {
    Emoji: string
    Count: int
    IsMine: bool
    Who: string list
  }
