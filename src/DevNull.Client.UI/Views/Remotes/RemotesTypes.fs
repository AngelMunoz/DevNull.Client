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
  and 'env :> IProvideRemotes
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
}

module State =

  type SearchFound = {
    Term: string
    Conversations: Conversation list
    Candidates: RemoteCandidate list
  }

  type Landing =
    | Message of id: int * beat: int
    | Present

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
    LandingBeat: int
    MarkedThrough: int
    DraftKey: string
    Draft: string
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
    PinAsk: Message option
    Busy: Flight option
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

  type Excerpt = {
    Prefix: string
    Text: string
    IsEvent: bool
  }

  type InboxRow =
    | Conversation of tab: InboxTab * row: Conversation
    | Candidate of person: RemoteCandidate

  type Bubble =
    | Sent of Message
    | Staged of PendingSend

  type Run = {
    Key: string
    IsMine: bool
    AuthorId: string
    Author: User option
    Bubbles: Bubble list
  }

  type ThreadRow =
    | Day of key: string * label: string
    | Head of count: int
    | Event of message: Message
    | Run of run: Run

  [<Struct>]
  type ThreadShape = {
    ViewerId: string
    IsOrg: bool
    Now: DateTimeOffset
    Thread: ThreadState
  }

  type ReactionSummary = {
    Emoji: string
    Count: int
    IsMine: bool
    Who: string list
  }
