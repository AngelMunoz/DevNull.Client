module DevNull.Client.Core.ApiTypes

#nowarn "3535"

open System

/// The signed-in user, as POST /api/v1/devices returns them.
type CurrentUser = {
  Id: string
  Username: string
  Name: string
  AvatarUrl: string option
}

/// One redeemed device: the user, plus the unread notifications in the envelope.
type DeviceSession = {
  User: CurrentUser
  UnreadNotificationsCount: int
}

/// A lane as the API reports it: a palette index or a custom color.
type Lane =
  | LaneIndex of index: int
  | LaneColor of color: string

/// A user as the signed API shows them on a commit or in a list.
type User = {
  Id: string
  Username: string
  Name: string
  AvatarUrl: string option
  Lane: Lane option
  CommitsCount: int option
  FollowersCount: int option
  FollowingCount: int option
  IsFollowed: bool option
  FollowsYou: bool option
}

/// One image on a commit, as the signed API returns it.
type Image = {
  Id: string
  Width: int
  Height: int
  Description: string option
  Url: string
}

/// The status the server gives a commit.
type CommitStatus =
  | Approved
  | Pending
  | Rejected
  | UnknownStatus of raw: string

module CommitStatuses =
  /// Reads the wire status. An unknown status stays raw.
  val parse: raw: string -> CommitStatus

  /// The wire status.
  val wire: status: CommitStatus -> string

/// One piece of a commit body.
type Segment =
  | TextSegment of text: string
  | CodeSegment of code: string
  | BlockSegment of language: string option * html: string

/// One mention position in a commit body.
type Mention = {
  Username: string
  Offset: int
  Length: int
}

/// One embed inside a link preview.
type Embed = {
  Provider: string
  Kind: string
  Src: string
  AspectRatio: string option
  Height: int option
  Poster: string option
}

/// One link preview.
type LinkPreview = {
  Url: string
  Title: string option
  Description: string option
  ImageUrl: string option
  SiteName: string option
  IsPending: bool
  Embed: Embed option
}

/// One poll option.
type PollOption = {
  Id: string
  Label: string
  VotesCount: int option
  Voted: bool
  Percentage: int option
}

/// One poll.
type Poll = {
  Id: string
  ClosesAt: DateTimeOffset option
  IsClosed: bool
  HasVoted: bool
  TotalVotes: int option
  Options: PollOption list
}

/// A deleted commit that still has replies.
type Tombstone = {
  Id: string
  ParentId: string option
  DeletedAt: DateTimeOffset
  RepliesCount: int option
}

/// One commit as the signed API returns it.
type Commit = {
  Id: string
  ShortHash: string
  Body: string
  BodySegments: Segment list
  PreviewSegments: Segment list option
  Mentions: Mention list
  ParentId: string option
  Parent: CommitNode option
  HtmlUrl: string
  CreatedAt: DateTimeOffset
  StarsCount: int option
  RepliesCount: int option
  Status: CommitStatus
  Author: User
  Images: Image list
  Starred: bool option
  IsPinned: bool
  PasteisCount: int
  QuotedCommitId: string option
  QuotedCommit: CommitNode option
  Poll: Poll option
  LinkPreview: LinkPreview option
  ReplyPreview: ReplyPreview option
}

/// A commit or a tombstone, as lists and ancestors return them.
and CommitNode =
  | LiveCommit of Commit
  | Tombstone of Tombstone

/// The newest replies shown under a commit.
and ReplyPreview = {
  Data: CommitNode list
  NextCursor: string option
}

/// The commit and its ancestors, as GET /api/v1/commits/{commit} returns them.
type CommitPage = {
  Commit: Commit
  Ancestors: CommitNode list
}

/// The links of one page.
type PageLinks = {
  First: string option
  Last: string option
  Prev: string option
  Next: string option
}

/// One page of a cursor-paginated API list.
type Page<'value> = {
  Items: 'value list
  Links: PageLinks
  Path: string option
  NextCursor: string option
  PrevCursor: string option
  PerPage: int
}

/// One public profile.
type Profile = {
  User: User
  Bio: string option
  Company: string option
  Location: string option
  Website: string option
  GithubUrl: string
  HtmlUrl: string
  FeedUrl: string
  CreatedAt: DateTimeOffset
  CommitsCount: int option
  FollowersCount: int option
  FollowingCount: int option
  Lane: Lane option
}

/// One activity bucket.
type ActivityBucket = { Date: string; Count: int }

/// A profile's activity.
type Activity = {
  Scale: string
  Buckets: ActivityBucket list
}

/// A profile's streaks.
type Streaks = { Longest: int; Current: int }

/// One XP source.
type XpItem = { Count: int; Multiplier: int; Xp: int }

/// The XP breakdown.
type XpBreakdown = {
  Commits: XpItem
  Streak: XpItem
  StarsReceived: XpItem
  Replies: XpItem
  StarsGiven: XpItem
  Luddite: XpItem
}

/// One rank.
type Rank = {
  Rank: string
  Level: int
  MinXp: int
  Description: string
  Achieved: bool
}

/// One badge.
type Badge = {
  Id: string
  Name: string
  Description: string
  Icon: string
  Unlocked: bool
  Progress: int
  Target: int
}

/// A profile's achievements.
type Achievements = {
  Level: int
  Rank: string
  Xp: int
  NextLevelXp: int option
  LevelProgress: int
  XpBreakdown: XpBreakdown
  Ranks: Rank list
  Badges: Badge list
}

/// A profile and the signed viewer's relation to it.
type ProfileDetails = {
  Profile: Profile
  IsFollowing: bool
  FollowsYou: bool
  IsMuting: bool
  PasteisReceived: int
  Activity: Activity option
  Streaks: Streaks option
  Achievements: Achievements option
  PinnedCommit: CommitNode option
}

/// The signed-in account, as GET /api/v1/me returns it.
type Me = {
  Profile: Profile
  PasteisMode: bool
  PasteisRemaining: int
  PasteisResetsAt: DateTimeOffset
  PasteisReceived: int
  PreferredLane: Lane option
  AutomaticLane: int
  SponsorsUrl: string option
  GithubSyncedAt: DateTimeOffset option
  UnreadNotificationsCount: int
}

/// The three logs.
type LogKind =
  | Following
  | Latest
  | Trending

module LogKinds =
  /// Reads a log name. Unknown names stay unparsed.
  val tryParse: raw: string -> LogKind option

  /// The wire name.
  val wire: log: LogKind -> string

/// One log page, with its extras.
type LogPage = {
  Page: Page<CommitNode>
  Log: LogKind
  IsRanked: bool
  NewCommitsCount: int option
  Suggestions: CommitNode list
}

/// A reason for reporting a commit.
type ReportReason =
  | Spam
  | Harassment
  | Unsafe
  | Other

module ReportReasons =
  /// The wire value.
  val wire: reason: ReportReason -> string

/// A lane choice for PUT /api/v1/me/lane.
type LaneChoice =
  | Automatic
  | Palette of index: int
  | Custom of color: string

type LaneError<'e> =
  static abstract InvalidLaneIndex: index: int -> 'e
  static abstract InvalidLaneColor: color: string -> 'e

module LaneChoices =
  /// Accepts palette indexes 0 to 5 and lowercase six-digit hex colors.
  val validate: choice: LaneChoice -> Result<LaneChoice, 'e> when LaneError<'e>

/// The result of giving a pastel.
type PastelResult = {
  Commit: Commit
  PasteisRemaining: int
  PasteisResetsAt: DateTimeOffset
  AuthorPasteisReceived: int
}

/// The result of muting or unmuting.
type MuteResult = { User: User; IsMuting: bool }

/// The result of blocking or unblocking.
type BlockResult = { User: User; IsBlocking: bool }

/// One notification.
type Notification = {
  Id: string
  Type: string
  SubjectId: string option
  Reason: string option
  ReadAt: DateTimeOffset option
  CreatedAt: DateTimeOffset
  Actor: User
  Actors: User list
  GroupId: string
  EventCount: int
  ActorsCount: int
  UnreadCount: int
  Commit: CommitNode option
}

/// One notification page, with the filter and read timestamp.
type NotificationPage = {
  Page: Page<Notification>
  FilterType: string
  ReadAt: DateTimeOffset
}

/// One error of the signed API, as the server names it. A code this client does not know stays raw.
type ErrorCode =
  | InvalidSignature
  | DeviceNotApproved
  | InvalidGrant
  | UserBlocked
  | UserUnavailable
  | OwnCommit
  | CommitUnavailable
  | PasteisExhausted
  | RateLimited
  | GitHubSyncFailed
  | Unknown of raw: string

module ErrorCodes =
  /// Reads the wire code. An unknown code becomes `Unknown raw`.
  val parse: raw: string -> ErrorCode

  /// The wire code.
  val wire: code: ErrorCode -> string

/// One JSON value the decoder could not read.
type DecodeFailure = { Path: string; Message: string }

/// The message and code of one refused request.
type Failure = { Message: string; Code: ErrorCode }

module Me =
  /// Reads the signed-in account envelope, from GET /api/v1/me and the sign-in responses.
  val inline decode: body: string -> Result<Me, DecodeFailure>

module Failure =
  /// Reads the error envelope `{message, code}` of a signed API request.
  val inline decode: body: string -> Result<Failure, DecodeFailure>

module Commit =
  /// Reads the `{data: commit}` envelope of POST /api/v1/commits.
  val decodeCreated: body: string -> Result<Commit, DecodeFailure>

module CommitPage =
  /// Reads the `{data: commit, ancestors: [...]}` envelope of GET /api/v1/commits/{commit}.
  val inline decode: body: string -> Result<CommitPage, DecodeFailure>

module CommitNode =
  /// Reads a cursor page of commits and tombstones.
  val inline decodePage: body: string -> Result<Page<CommitNode>, DecodeFailure>

module User =
  /// Reads a cursor page of users.
  val inline decodePage: body: string -> Result<Page<User>, DecodeFailure>

module ProfileDetails =
  /// Reads one profile and the signed viewer's relation to it.
  val inline decode: body: string -> Result<ProfileDetails, DecodeFailure>

module LogPage =
  /// Reads one log page and its extras.
  val inline decode: body: string -> Result<LogPage, DecodeFailure>

module PastelResult =
  /// Reads the result of giving a pastel.
  val inline decode: body: string -> Result<PastelResult, DecodeFailure>

module MuteResult =
  /// Reads the result of muting or unmuting.
  val inline decode: body: string -> Result<MuteResult, DecodeFailure>

module BlockResult =
  /// Reads the result of blocking or unblocking.
  val inline decode: body: string -> Result<BlockResult, DecodeFailure>

module NotificationPage =
  /// Reads a cursor page of notifications and its extras.
  val inline decode: body: string -> Result<NotificationPage, DecodeFailure>

module Notification =
  /// Reads a cursor page of notifications without the inbox extras.
  val inline decodePage:
    body: string -> Result<Page<Notification>, DecodeFailure>

/// The transport could not reach the server.
type TransportError<'e> =
  static abstract TransportFailed: message: string -> 'e

/// The server refused the request with a status, a code and a message.
type Rejected<'e> =
  static abstract Refused: status: int * code: ErrorCode * message: string -> 'e

/// The server answered with JSON this client could not read.
type DecodeError<'e> =
  static abstract DecodeFailed: failure: DecodeFailure -> 'e
