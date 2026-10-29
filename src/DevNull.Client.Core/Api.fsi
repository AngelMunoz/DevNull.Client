module DevNull.Client.Core.Api

open System
open FSharp.UMX
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types

/// One staged image as the CLI composite defines it: a name and raw bytes.
type ImageAttachment = DevNull.Client.Core.Commit.ImageAttachment

/// One parsed image: its bytes, its content type, and its SHA-256.
type PostableImage = DevNull.Client.Core.Commit.Image

/// One staged voice note: its bytes, its content type, and its SHA-256.
type VoiceAttachment = DevNull.Client.Core.Commit.Voice

/// One voice note's loudness bars: 48 values between 0 and 100.
type Waveform = DevNull.Client.Core.Commit.Waveform

module Signing =

  /// The values one signed v1 request needs.
  type Request = {
    KeyFile: string
    Method: string
    Target: string
    Body: string
  }

  /// The values one signed v1 request with images needs.
  type ImageRequest = {
    KeyFile: string
    Method: string
    Target: string
    Body: string
    Images: PostableImage list
  }

  /// The values one signed v1 request with named files needs. Each file
  /// rides under its own part name, and the signed message carries the
  /// comma-separated file hashes on one line before the body, under the
  /// images namespace.
  type FilesRequest = {
    KeyFile: string
    Method: string
    Target: string
    Body: string
    PayloadName: string
    Files: (string * PostableImage) list
  }

  /// Signs one v1 request and returns it ready to send.
  val signedRequest:
    env: 'env * request: Request -> Async<Result<HttpRequest, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and NonceError<'e>
      and SigningError<'e>

  /// Signs one v1 request with images and returns it ready to send: the
  /// JSON body rides a payload part, the images ride `images[]` files
  /// beside it, and the signed message carries the comma-separated
  /// image hashes on one line before the body, under the images
  /// namespace.
  val signedImageRequest:
    env: 'env * request: ImageRequest -> Async<Result<HttpRequest, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and NonceError<'e>
      and SigningError<'e>

  /// Signs one v1 request with named files and returns it ready to send.
  val signedFilesRequest:
    env: 'env * request: FilesRequest -> Async<Result<HttpRequest, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and NonceError<'e>
      and SigningError<'e>

module Device =

  /// The values the browser authorization page needs.
  type AuthorizationRequest = {
    BaseAddress: string
    Fingerprint: string
    DeviceName: string
    RedirectUri: string
  }

  /// One pending device authorization: the verifier the app keeps, and the URL it opens.
  type Authorization = {
    Verifier: string
    State: string
    Challenge: string
    Url: string
  }

  /// Builds the browser URL for the device authorization page.
  val authorizationUrl:
    request: AuthorizationRequest * challenge: string * state: string -> string

  /// Generates the verifier, state and URL for one device authorization.
  val beginAuthorization:
    env: #IProvidePkceSource & #IProvideNonceSource *
    request: AuthorizationRequest ->
      Authorization

  /// The code the authorization page returned, and the verifier the app kept.
  type RedeemRequest = {
    KeyFile: string
    Code: string
    Verifier: string
  }

  /// Redeems the code and returns the signed-in user.
  val redeem:
    env: 'env * request: RedeemRequest -> Async<Result<Me, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

module Me =

  /// One signed request that needs only the key file.
  type Request = { KeyFile: string }

  /// Turns pastéis mode on or off.
  type PasteisModeRequest = { KeyFile: string; Enabled: bool }

  /// Sets the lane, automatic, palette, or custom color.
  type LaneRequest = { KeyFile: string; Lane: LaneChoice }

  /// The terms version the app showed the person, as the server takes
  /// it back.
  type TermsRequest = { KeyFile: string; Version: string }

  /// Reads the current user and unread notification count.
  val get:
    env: 'env * request: Request -> Async<Result<Me, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Deletes the signed-in account.
  val delete:
    env: 'env * request: Request -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Turns pastéis mode on or off and returns the current user.
  val setPasteisMode:
    env: 'env * request: PasteisModeRequest -> Async<Result<Me, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Mirrors the GitHub profile again and returns the current user.
  val syncGithub:
    env: 'env * request: Request -> Async<Result<Me, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Validates the lane, sets it, and returns the current user.
  val setLane:
    env: 'env * request: LaneRequest -> Async<Result<Me, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>
      and LaneError<'e>

  /// Records that the person accepted the terms version the app showed
  /// them, and returns the current user as `Me.get` does.
  val acceptTerms:
    env: 'env * request: TermsRequest -> Async<Result<Me, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

module Devices =

  /// One device list read.
  type Request = { KeyFile: string }

  /// One device revoked by id.
  type RevokeRequest = { KeyFile: string; DeviceId: string }

  /// Reads the keys approved to sign as the user, newest approval
  /// first.
  val list:
    env: 'env * request: Request -> Async<Result<Device list, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Revokes one device. Revoking the one that signed the request
  /// signs the app out on it — the caller knows which row is its own.
  val revoke:
    env: 'env * request: RevokeRequest -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

module Users =

  /// One user by username.
  type Request = { KeyFile: string; Username: string }

  /// One user list by username.
  type ListRequest = {
    KeyFile: string
    Username: string
    Cursor: string option
  }

  /// One user's commits.
  type CommitsRequest = {
    KeyFile: string
    Username: string
    Replies: bool
    Cursor: string option
  }

  /// One list that takes only a cursor.
  type CursorRequest = {
    KeyFile: string
    Cursor: string option
  }

  /// Reads one profile and the viewer's relation to it.
  val get:
    env: 'env * request: Request -> Async<Result<ProfileDetails, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reads one user's commits.
  val commits:
    env: 'env * request: CommitsRequest -> Async<Result<Page<CommitNode>, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reads one user's followers.
  val followers:
    env: 'env * request: ListRequest -> Async<Result<Page<User>, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reads the users one user follows.
  val following:
    env: 'env * request: ListRequest -> Async<Result<Page<User>, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Follows one user.
  val follow:
    env: 'env * request: Request -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Stops following one user.
  val unfollow:
    env: 'env * request: Request -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reads the signed user's muted users.
  val muted:
    env: 'env * request: CursorRequest -> Async<Result<Page<User>, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Mutes one user.
  val mute:
    env: 'env * request: Request -> Async<Result<MuteResult, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Unmutes one user.
  val unmute:
    env: 'env * request: Request -> Async<Result<MuteResult, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reads the signed user's blocked users.
  val blocked:
    env: 'env * request: CursorRequest -> Async<Result<Page<User>, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Blocks one user.
  val block:
    env: 'env * request: Request -> Async<Result<BlockResult, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Unblocks one user.
  val unblock:
    env: 'env * request: Request -> Async<Result<BlockResult, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

module Commits =

  /// What a new commit points at: nothing, a parent, or a quoted commit.
  type Target =
    | New
    | ReplyTo of parentId: string
    | Quote of quotedId: string

  /// One commit to create through the signed API. A poll rides a fresh
  /// commit with no images only, as the server takes it; images travel
  /// as a multipart post whose payload part carries the JSON body,
  /// with one optional description per image.
  type CreateRequest = {
    KeyFile: string
    Body: string
    Target: Target
    Images: ImageAttachment list
    ImageDescriptions: string option list
    Poll: PollSpec option
  }

  /// One commit to read through the signed API.
  type ShowRequest = { KeyFile: string; CommitId: string }

  /// One commit's replies.
  type RepliesRequest = {
    KeyFile: string
    CommitId: string
    Newest: bool
    Cursor: string option
  }

  /// One commit action with no body.
  type ActionRequest = { KeyFile: string; CommitId: string }

  /// One report against a commit.
  type ReportRequest = {
    KeyFile: string
    CommitId: string
    Reason: ReportReason
    Details: string option
  }

  /// One draft to see as the commit it would become. Nothing is kept.
  type PreviewRequest = { KeyFile: string; Body: string }

  /// One poll vote: the commit whose poll it is, and the option taken.
  type VoteRequest = {
    KeyFile: string
    CommitId: string
    OptionId: string
  }

  /// Creates one commit.
  val create:
    env: 'env * request: CreateRequest -> Async<Result<Commit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and CommitBodyError<'e>
      and CommitIdError<'e>
      and ImageError<'e>
      and PollError<'e>
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Votes one poll option and returns the commit with the poll results.
  val vote:
    env: 'env * request: VoteRequest -> Async<Result<Commit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and CommitIdError<'e>
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reads one commit and its ancestors.
  val show:
    env: 'env * request: ShowRequest -> Async<Result<CommitPage, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and CommitIdError<'e>
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reads one commit's replies.
  val replies:
    env: 'env * request: RepliesRequest -> Async<Result<Page<CommitNode>, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and CommitIdError<'e>
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Deletes one commit.
  val delete:
    env: 'env * request: ActionRequest -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and CommitIdError<'e>
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Undoes deleting one of the user's own commits within the
  /// server's undo window, and answers with the commit as its page
  /// shows it.
  val restore:
    env: 'env * request: ActionRequest -> Async<Result<Commit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and CommitIdError<'e>
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reports one commit.
  val report:
    env: 'env * request: ReportRequest -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and CommitIdError<'e>
      and ReportDetailsError<'e>
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Shows one draft as the commit it would become: the body as it
  /// would be kept, the segments a list folds it to, and where it
  /// mentions someone. Nothing is kept.
  val preview:
    env: 'env * request: PreviewRequest -> Async<Result<CommitPreview, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and CommitBodyError<'e>
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

module Stars =

  /// The starred commits, one cursor page at a time. Starring is also
  /// how the app saves commits: one action, one list.
  type ListRequest = {
    KeyFile: string
    Cursor: string option
  }

  /// One commit to star or unstar.
  type ActionRequest = { KeyFile: string; CommitId: string }

  /// Reads the signed user's starred commits.
  val index:
    env: 'env * request: ListRequest -> Async<Result<Page<CommitNode>, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Stars one commit.
  val star:
    env: 'env * request: ActionRequest -> Async<Result<Commit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and CommitIdError<'e>
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Removes the star from one commit.
  val unstar:
    env: 'env * request: ActionRequest -> Async<Result<Commit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and CommitIdError<'e>
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

module Pasteis =

  /// One commit to give a pastel to.
  type ActionRequest = { KeyFile: string; CommitId: string }

  /// Gives one pastel to a commit.
  val give:
    env: 'env * request: ActionRequest -> Async<Result<PastelResult, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and CommitIdError<'e>
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

module Logs =

  /// One log page.
  type Request = {
    KeyFile: string
    Log: LogKind
    Since: string option
    Cursor: string option
  }

  /// Reads one log page.
  val get:
    env: 'env * request: Request -> Async<Result<LogPage, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and CommitIdError<'e>
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

module Notifications =

  /// The notification inbox.
  type ListRequest = {
    KeyFile: string
    Filter: string option
    Through: DateTimeOffset option
    Cursor: string option
  }

  /// One notification group.
  type GroupRequest = {
    KeyFile: string
    NotificationId: string
    Filter: string option
    Cursor: string option
  }

  /// Reads the signed user's notification inbox.
  val list:
    env: 'env * request: ListRequest -> Async<Result<NotificationPage, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reads one notification group.
  val group:
    env: 'env * request: GroupRequest -> Async<Result<Page<Notification>, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

module Remotes =

  /// Which thread an endpoint targets: a partner by username, or an org
  /// by id.
  type ThreadTarget =
    | Partner of username: string
    | Org of orgId: string

  /// The request shape every thread-state endpoint takes.
  type TargetRequest = {
    KeyFile: string
    Target: ThreadTarget
  }

  /// One thread pulled through a message.
  type ReadRequest = {
    KeyFile: string
    Target: ThreadTarget
    Through: int
  }

  /// One thread muted for a duration, or unmuted with `MuteOff`.
  type MuteRequest = {
    KeyFile: string
    Target: ThreadTarget
    Duration: MuteDuration
  }

  /// One thread pinned or unpinned.
  type PinRequest = {
    KeyFile: string
    Target: ThreadTarget
    Pinned: bool
  }

  /// One inbox page: a tab, a search term, and the conversation a
  /// previous page ended at.
  type InboxRequest = {
    KeyFile: string
    Tab: InboxTab
    Search: string option
    Before: string option
  }

  /// One one-to-one thread: the latest page, the page before a message,
  /// or everything after one with the reactions changed since a version.
  type ThreadRequest = {
    KeyFile: string
    Partner: string
    After: int option
    Before: int option
    ReactionsAfter: int option
  }

  /// One org thread, polled the same way.
  type OrgThreadRequest = {
    KeyFile: string
    OrgId: string
    After: int option
    Before: int option
    ReactionsAfter: int option
  }

  /// Which link gets the card on a pushed message: the first one, a
  /// chosen one, or none.
  type LinkCard =
    | FirstLink
    | NoCard
    | CardOf of url: string

  /// One message to push to a partner or an org. The body, the images,
  /// or the voice note may carry it alone; a voice note rides alone, or
  /// with a reply.
  type PushRequest = {
    KeyFile: string
    Target: ThreadTarget
    Body: string option
    ClientId: string<ClientId>
    ReplyTo: int option
    Card: LinkCard
    Images: ImageAttachment list
    ImageDescriptions: string option list
    Voice: (ImageAttachment * Waveform) option
  }

  /// One fetched request accepted into the open conversations.
  type MergeRequest = { KeyFile: string; Partner: string }

  /// One emoji left on a message, or taken back.
  type ReactionRequest = {
    KeyFile: string
    MessageId: int
    Emoji: string
  }

  /// One message's link card, asked for again while pending.
  type LinkPreviewRequest = { KeyFile: string; MessageId: int }

  /// The emoji the user reacts with.
  type UsageRequest = { KeyFile: string }

  /// The remotes master switch.
  type SettingsRequest = { KeyFile: string; Enabled: bool }

  /// Who can push to the user, and whether they share pulls.
  type PreferencesRequest = {
    KeyFile: string
    From: RemotesFrom
    PullReceipts: bool
  }

  /// What the candidate list is picking for.
  type CandidateFor =
    | ForRemote
    | ForOrg

  /// One candidate search: a term, the org picked for, and whether the
  /// org does not exist yet.
  type CandidatesRequest = {
    KeyFile: string
    Search: string option
    Org: string option
    For: CandidateFor option
  }

  /// One org to start: an optional name and the first members.
  type CreateOrgRequest = {
    KeyFile: string
    Name: string option
    Members: string list
  }

  /// One org renamed, or its name taken away.
  type RenameOrgRequest = {
    KeyFile: string
    OrgId: string
    Name: string option
  }

  /// Members added to an org, sharing its history or starting fresh.
  type AddMembersRequest = {
    KeyFile: string
    OrgId: string
    Members: string list
    ShareHistory: bool
  }

  /// One member's role changed.
  type MemberRoleRequest = {
    KeyFile: string
    OrgId: string
    Member: string
    Role: OrgRole
  }

  /// One member taken out of an org.
  type RemoveMemberRequest = {
    KeyFile: string
    OrgId: string
    Member: string
  }

  /// Reads one page of the inbox, pinned first then newest.
  val inbox:
    env: 'env * request: InboxRequest -> Async<Result<Inbox, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reads one one-to-one thread. A partner never written to yet gives
  /// an empty thread, which says whether a first message can go.
  val thread:
    env: 'env * request: ThreadRequest -> Async<Result<Thread, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reads one org thread, from where the user's history starts.
  val orgThread:
    env: 'env * request: OrgThreadRequest -> Async<Result<OrgThread, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Pushes one message, starting the conversation when it is the
  /// first. A client id sent again answers with the message it already
  /// created.
  val push:
    env: 'env * request: PushRequest -> Async<Result<Message, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and MessageBodyError<'e>
      and ImageError<'e>
      and VoiceError<'e>
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Pulls one thread through a message, as a scrolled thread does.
  val markRead:
    env: 'env * request: ReadRequest -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Says the user is typing, for a few seconds.
  val typing:
    env: 'env * request: TargetRequest -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Nudges the partner or the org: a small attention ping.
  val ping:
    env: 'env * request: TargetRequest -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Stashes one thread for the user only. It pops back when either
  /// side writes.
  val stash:
    env: 'env * request: TargetRequest -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Pops one thread back into the open list.
  val unstash:
    env: 'env * request: TargetRequest -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Mutes one thread until the duration ends, or unmutes it.
  val mute:
    env: 'env * request: MuteRequest -> Async<Result<ThreadMuteResult, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Pins one thread to the top of the inbox, or unpins it.
  val pin:
    env: 'env * request: PinRequest -> Async<Result<PinResult, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Merges one fetched request into the open conversations, so it can
  /// be answered.
  val merge:
    env: 'env * request: MergeRequest -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Drops one fetched request for good, without telling the sender.
  val drop:
    env: 'env * request: MergeRequest -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reads the emoji the user reacts with, most used first.
  val reactionUsage:
    env: 'env * request: UsageRequest -> Async<Result<ReactionUse list, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reacts with one emoji and answers with the message's reactions.
  val react:
    env: 'env * request: ReactionRequest -> Async<Result<Reaction list, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Takes one reaction back.
  val unreact:
    env: 'env * request: ReactionRequest -> Async<Result<Reaction list, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reads one message's link card, which stays empty while pending.
  val linkPreview:
    env: 'env * request: LinkPreviewRequest ->
      Async<Result<LinkPreview option, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Reads the people a new remote or org can be started with, each
  /// with the reason they cannot, when they cannot.
  val candidates:
    env: 'env * request: CandidatesRequest ->
      Async<Result<RemoteCandidate list, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Starts one org and answers with its id.
  val createOrg:
    env: 'env * request: CreateOrgRequest -> Async<Result<CreatedOrg, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Renames one org, or takes its name away.
  val renameOrg:
    env: 'env * request: RenameOrgRequest -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Adds members to one org.
  val addMembers:
    env: 'env * request: AddMembersRequest -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Changes one member's role. The server keeps the last maintainer.
  val setMemberRole:
    env: 'env * request: MemberRoleRequest -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Takes one member out of an org.
  val removeMember:
    env: 'env * request: RemoveMemberRequest -> Async<Result<unit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Turns remotes on or off and answers as `Me.get` does.
  val setEnabled:
    env: 'env * request: SettingsRequest -> Async<Result<Me, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>

  /// Saves who can push to the user and whether they share pulls, and
  /// answers as `Me.get` does.
  val setPreferences:
    env: 'env * request: PreferencesRequest -> Async<Result<Me, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and NonceError<'e>
      and SigningError<'e>
      and TransportError<'e>
      and Rejected<'e>
      and DecodeError<'e>
