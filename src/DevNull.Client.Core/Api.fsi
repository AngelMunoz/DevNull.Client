module DevNull.Client.Core.Api

open System
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types

module Signing =

  /// The values one signed v1 request needs.
  type Request = {
    KeyFile: string
    Method: string
    Target: string
    Body: string
  }

  /// Signs one v1 request and returns it ready to send.
  val signedRequest:
    env: 'env * request: Request -> Async<Result<HttpRequest, 'e>>
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

  /// One commit to create through the signed API. The server accepts no images or poll here.
  type CreateRequest = {
    KeyFile: string
    Body: string
    Target: Target
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

  /// Creates one commit.
  val create:
    env: 'env * request: CreateRequest -> Async<Result<Commit, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and CommitBodyError<'e>
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

module Bookmarks =

  /// The saved commits, one cursor page at a time.
  type ListRequest = {
    KeyFile: string
    Cursor: string option
  }

  /// One commit to save or remove from the saved list.
  type ActionRequest = { KeyFile: string; CommitId: string }

  /// Reads the signed user's saved commits.
  val list:
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

  /// Saves one commit.
  val save:
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

  /// Removes one commit from the saved list.
  val unsave:
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

module Stars =

  /// One commit to star or unstar.
  type ActionRequest = { KeyFile: string; CommitId: string }

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
