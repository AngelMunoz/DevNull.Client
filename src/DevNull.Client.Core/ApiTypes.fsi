module DevNull.Client.Core.ApiTypes

#nowarn "3535"

open System

/// The signed-in user, as POST /api/v1/devices returns them.
type CurrentUser = {
  Id: string
  Handle: string
  Name: string
  AvatarUrl: string option
}

/// A lane as the API reports it: a palette index or a custom color.
type Lane =
  | LaneIndex of index: int
  | LaneColor of color: string

/// A user as the signed API shows them on a commit or in a list.
type User = {
  Id: string
  Handle: string
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
  Handle: string
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

/// How long a poll stays open, as the server takes it.
type PollDuration =
  | OneHour
  | OneDay
  | ThreeDays
  | SevenDays

module PollDurations =
  /// The wire value: whole minutes.
  val wire: duration: PollDuration -> int

/// What a poll may not ride with, as the server refuses it.
type PollConflict =
  | PollOnReply
  | PollOnQuote
  | PollWithImages

type PollError<'e> =
  static abstract TooFewPollOptions: count: int -> 'e
  static abstract TooManyPollOptions: count: int -> 'e
  static abstract PollOptionTooLong: label: string * length: int -> 'e
  static abstract DuplicatePollOption: label: string -> 'e
  static abstract PollProhibited: conflict: PollConflict -> 'e

/// One poll on a new commit: two to four options and the time it stays
/// open. A poll rides a fresh commit only.
type PollSpec = private {
  Options: string list
  Duration: PollDuration
}

module PollSpecs =
  /// Reads the options a poll offers. Accepts two to four options, each
  /// at most fifty characters, distinct ignoring case.
  val parse<'e when PollError<'e>> :
    options: string list * duration: PollDuration -> Result<PollSpec, 'e>

  /// The options the poll offers.
  val options: spec: PollSpec -> string list

  /// The time the poll stays open.
  val duration: spec: PollSpec -> PollDuration

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
  | TimedOut
  | Banned
  | CommitRulesRequired
  | Unknown of raw: string

module ErrorCodes =
  /// Reads the wire code. An unknown code becomes `Unknown raw`.
  val parse: raw: string -> ErrorCode

  /// The wire code.
  val wire: code: ErrorCode -> string

/// One JSON value the decoder could not read.
type DecodeFailure = { Path: string; Message: string }

/// The message and code of one refused request. A body that carries no
/// code leaves the option empty.
type Failure = {
  Message: string
  Code: ErrorCode option
}

/// Which inbox tab one page reads.
type InboxTab =
  | TabOpen
  | TabFetched
  | TabStashed

module InboxTabs =
  /// The wire name.
  val wire: tab: InboxTab -> string

/// How long one mute lasts, as the server takes it.
type MuteDuration =
  | MuteHour
  | MuteTomorrow
  | MuteWeek
  | MuteForever
  | MuteOff

module MuteDurations =
  /// The wire name.
  val wire: duration: MuteDuration -> string

/// Who the server lets push to the user. An unknown value stays raw.
type RemotesFrom =
  | FromFollowing
  | FromEveryone
  | UnknownRemotesFrom of raw: string

module RemotesFroms =
  /// Reads the wire name. An unknown name stays raw.
  val parse: raw: string -> RemotesFrom

  /// The wire name.
  val wire: from: RemotesFrom -> string

/// One conversation's kind. An unknown kind stays raw.
type ConversationKind =
  | Direct
  | Org
  | UnknownKind of raw: string

/// How far the server lets a push go. An unknown value stays raw.
type PushAccess =
  | PushOpen
  | PushRequest
  | PushWaiting
  | PushRestricted
  | PushClosed
  | UnknownAccess of raw: string

/// One member's role in an org. An unknown role stays raw.
type OrgRole =
  | Maintainer
  | Contributor
  | UnknownRole of raw: string

module OrgRoles =
  /// The wire name.
  val wire: role: OrgRole -> string

/// The event one message records: an org's own changes and a ping. An
/// unknown event stays raw.
type OrgEvent =
  | Created
  | Added
  | Removed
  | Left
  | Renamed
  | Promoted
  | Demoted
  | Inherited
  | Ping
  | UnknownEvent of raw: string

/// One reaction left on a message.
type Reaction = { UserId: string; Emoji: string }

/// One emoji the user reacts with, and how often.
type ReactionUse = { Emoji: string; Count: int }

/// One message whose reactions changed since a poll last looked.
type ReactionChange = {
  MessageId: int
  Reactions: Reaction list
}

/// One voice note on a message, as the signed API returns it.
type VoiceClip = {
  Id: string
  DurationMs: int
  Waveform: int list
  Url: string
}

/// The message one message answers, as much as its ghost shows.
type MessageParent = {
  Id: int
  UserId: string
  Body: string
  ImagesCount: int
  HasVoice: bool
}

/// One message in a thread, as the signed API returns it.
type Message = {
  Id: int
  UserId: string
  ClientId: string
  Body: string
  BodySegments: Segment list
  Mentions: Mention list
  ReplyToId: int option
  ReplyTo: MessageParent option
  LinkPreview: LinkPreview option
  Images: Image list
  Voice: VoiceClip option
  Reactions: Reaction list
  Event: OrgEvent option
  SubjectId: string option
  User: User option
  Subject: User option
  CreatedAt: DateTimeOffset
}

/// One member of an org, as the inbox shows them.
type OrgMember = { User: User; Role: OrgRole option }

/// One row of the remotes inbox: a partner or an org, its last message,
/// and the viewer's relation to it.
type Conversation = {
  Id: string
  Kind: ConversationKind
  Name: string option
  Partner: User option
  Members: OrgMember list option
  LastMessage: Message option
  LastMessageAt: DateTimeOffset option
  Ahead: int
  LastIncomingMessageId: int option
  LastIncomingSender: string option
  PartnerReadThroughId: int
  PartnerReadAt: DateTimeOffset option
  IsArchived: bool
  PinnedAt: DateTimeOffset option
  MutedUntilAt: DateTimeOffset option
}

/// One page of the remotes inbox, with the counts the nav shows and
/// the pin map the pages move under.
type Inbox = {
  Conversations: Conversation list
  HasMore: bool
  UnreadCount: int
  RequestsCount: int
  Pins: Map<string, DateTimeOffset option>
}

/// One member of an org thread, with their read position.
type ThreadMember = {
  User: User
  Role: OrgRole option
  ReadThroughId: int
}

/// A one-to-one thread, as the API loads and polls it.
type Thread = {
  Partner: User
  Messages: Message list
  HasOlder: bool
  ReactionsVersion: int
  ReactionChanges: ReactionChange list
  ReadThroughId: int
  PartnerReadThroughId: int
  PartnerReadAt: DateTimeOffset option
  PartnerTyping: bool
  IsArchived: bool
  PinnedAt: DateTimeOffset option
  MutedUntilAt: DateTimeOffset option
  Access: PushAccess
}

/// An org, as a thread names it.
type OrgRef = { Id: string; Name: string option }

/// An org thread, as the API loads and polls it.
type OrgThread = {
  Org: OrgRef
  Messages: Message list
  HasOlder: bool
  ReactionsVersion: int
  ReactionChanges: ReactionChange list
  ReadThroughId: int
  Members: ThreadMember list
  Typing: string list
  IsArchived: bool
  PinnedAt: DateTimeOffset option
  MutedUntilAt: DateTimeOffset option
  Role: OrgRole option
  Access: PushAccess
}

/// Someone a new remote or org can be started with, and the reason they
/// cannot, when they cannot.
type RemoteCandidate = { User: User; Reason: string option }

/// The result of pinning or unpinning a thread.
type PinResult = { PinnedAt: DateTimeOffset option }

/// The result of muting or unmuting a thread.
type ThreadMuteResult = { MutedUntilAt: DateTimeOffset option }

/// The org POST /api/v1/remotes/orgs started.
type CreatedOrg = { Id: string }

/// The signed-in user's remotes settings, as GET /api/v1/me reports
/// them while remotes are on.
type MeRemotes = {
  From: RemotesFrom
  PullReceipts: bool
  Requests: bool
  Everyone: bool
  UnreadCount: int
}

/// One redeemed device: the user, the unread notifications in the
/// envelope, whether the user shows pastel controls at all, and the
/// direct-message settings while they are on.
type DeviceSession = {
  User: CurrentUser
  UnreadNotificationsCount: int
  PasteisMode: bool
  Remotes: MeRemotes option
}

/// One draft as the commit it would become, as POST
/// /api/v1/commits/preview returns it. Nothing is kept.
type CommitPreview = {
  Body: string
  BodySegments: Segment list
  PreviewSegments: Segment list option
  Mentions: Mention list
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
  Remotes: MeRemotes option
}

module Me =
  /// Reads the signed-in account envelope, from GET /api/v1/me and the sign-in responses.
  val decode: body: string -> Result<Me, DecodeFailure>

module Failure =
  /// Reads the error envelope `{message, code}` of a signed API request.
  val decode: body: string -> Result<Failure, DecodeFailure>

module Commit =
  /// Reads the `{data: commit}` envelope of POST /api/v1/commits.
  val decodeCreated: body: string -> Result<Commit, DecodeFailure>

module CommitPage =
  /// Reads the `{data: commit, ancestors: [...]}` envelope of GET /api/v1/commits/{commit}.
  val decode: body: string -> Result<CommitPage, DecodeFailure>

module CommitNode =
  /// Reads a cursor page of commits and tombstones.
  val decodePage: body: string -> Result<Page<CommitNode>, DecodeFailure>

module User =
  /// Reads a cursor page of users.
  val decodePage: body: string -> Result<Page<User>, DecodeFailure>

module ProfileDetails =
  /// Reads one profile and the signed viewer's relation to it.
  val decode: body: string -> Result<ProfileDetails, DecodeFailure>

module LogPage =
  /// Reads one log page and its extras.
  val decode: body: string -> Result<LogPage, DecodeFailure>

module PastelResult =
  /// Reads the result of giving a pastel.
  val decode: body: string -> Result<PastelResult, DecodeFailure>

module MuteResult =
  /// Reads the result of muting or unmuting.
  val decode: body: string -> Result<MuteResult, DecodeFailure>

module BlockResult =
  /// Reads the result of blocking or unblocking.
  val decode: body: string -> Result<BlockResult, DecodeFailure>

module NotificationPage =
  /// Reads a cursor page of notifications and its extras.
  val decode: body: string -> Result<NotificationPage, DecodeFailure>

module Notification =
  /// Reads a cursor page of notifications without the inbox extras.
  val decodePage: body: string -> Result<Page<Notification>, DecodeFailure>

module Inbox =
  /// Reads one page of the remotes inbox and its counts.
  val decode: body: string -> Result<Inbox, DecodeFailure>

module Thread =
  /// Reads one one-to-one thread, from its `{data: thread}` envelope.
  val decode: body: string -> Result<Thread, DecodeFailure>

module OrgThread =
  /// Reads one org thread, from its `{data: thread}` envelope.
  val decode: body: string -> Result<OrgThread, DecodeFailure>

module Message =
  /// Reads one pushed message, from its `{data: message}` envelope.
  val decode: body: string -> Result<Message, DecodeFailure>

module PinResult =
  /// Reads the result of pinning or unpinning.
  val decode: body: string -> Result<PinResult, DecodeFailure>

module ThreadMuteResult =
  /// Reads the result of muting or unmuting.
  val decode: body: string -> Result<ThreadMuteResult, DecodeFailure>

module CreatedOrg =
  /// Reads the id of the org the server started.
  val decode: body: string -> Result<CreatedOrg, DecodeFailure>

module ReactionUses =
  /// Reads the emoji the user reacts with, most used first.
  val decode: body: string -> Result<ReactionUse list, DecodeFailure>

module Reactions =
  /// Reads a message's reactions, in the order they were left.
  val decode: body: string -> Result<Reaction list, DecodeFailure>

module RemoteCandidates =
  /// Reads the people a new remote or org can be started with.
  val decode: body: string -> Result<RemoteCandidate list, DecodeFailure>

module MessageLinkPreview =
  /// Reads a message's link card, which stays empty while it is pending.
  val decode: body: string -> Result<LinkPreview option, DecodeFailure>

module CommitPreview =
  /// Reads one draft as the commit it would become.
  val decode: body: string -> Result<CommitPreview, DecodeFailure>

/// The transport could not reach the server.
type TransportError<'e> =
  static abstract TransportFailed: message: string -> 'e

/// The server refused the request with a status, a code and a message.
type Rejected<'e> =
  static abstract Refused:
    status: int * code: ErrorCode option * message: string -> 'e

/// The server answered with JSON this client could not read.
type DecodeError<'e> =
  static abstract DecodeFailed: failure: DecodeFailure -> 'e
