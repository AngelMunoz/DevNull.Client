module DevNull.Client.Core.ApiTypes

#nowarn "3535"

open System
open System.Text.Json
open System.Text.RegularExpressions
open JDeck

type CurrentUser = {
  Id: string
  Handle: string
  Name: string
  AvatarUrl: string option
}

type Lane =
  | LaneIndex of index: int
  | LaneColor of color: string

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

type Image = {
  Id: string
  Width: int
  Height: int
  Description: string option
  Url: string
}

type CommitStatus =
  | Approved
  | Pending
  | Rejected
  | UnknownStatus of raw: string

module CommitStatuses =

  let parse(raw: string) =
    match raw with
    | "approved" -> Approved
    | "pending" -> Pending
    | "rejected" -> Rejected
    | raw -> UnknownStatus raw

  let wire(status: CommitStatus) =
    match status with
    | Approved -> "approved"
    | Pending -> "pending"
    | Rejected -> "rejected"
    | UnknownStatus raw -> raw

type Segment =
  | TextSegment of text: string
  | CodeSegment of code: string
  | BlockSegment of language: string option * html: string

type Mention = {
  Handle: string
  Offset: int
  Length: int
}

type Embed = {
  Provider: string
  Kind: string
  Src: string
  AspectRatio: string option
  Height: int option
  Poster: string option
}

type LinkPreview = {
  Url: string
  Title: string option
  Description: string option
  ImageUrl: string option
  SiteName: string option
  IsPending: bool
  Embed: Embed option
}

type PollOption = {
  Id: string
  Label: string
  VotesCount: int option
  Voted: bool
  Percentage: int option
}

type Poll = {
  Id: string
  ClosesAt: DateTimeOffset option
  IsClosed: bool
  HasVoted: bool
  TotalVotes: int option
  Options: PollOption list
}

type Tombstone = {
  Id: string
  ParentId: string option
  DeletedAt: DateTimeOffset
  RepliesCount: int option
}

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

and CommitNode =
  | LiveCommit of Commit
  | Tombstone of Tombstone

and ReplyPreview = {
  Data: CommitNode list
  NextCursor: string option
}

type CommitPage = {
  Commit: Commit
  Ancestors: CommitNode list
}

type PageLinks = {
  First: string option
  Last: string option
  Prev: string option
  Next: string option
}

type Page<'value> = {
  Items: 'value list
  Links: PageLinks
  Path: string option
  NextCursor: string option
  PrevCursor: string option
  PerPage: int
}

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

type ActivityBucket = { Date: string; Count: int }

type Activity = {
  Scale: string
  Buckets: ActivityBucket list
}

type Streaks = { Longest: int; Current: int }

type XpItem = { Count: int; Multiplier: int; Xp: int }

type XpBreakdown = {
  Commits: XpItem
  Streak: XpItem
  StarsReceived: XpItem
  Replies: XpItem
  StarsGiven: XpItem
  Luddite: XpItem
}

type Rank = {
  Rank: string
  Level: int
  MinXp: int
  Description: string
  Achieved: bool
}

type Badge = {
  Id: string
  Name: string
  Description: string
  Icon: string
  Unlocked: bool
  Progress: int
  Target: int
}

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

type LogKind =
  | Following
  | Latest
  | Trending

module LogKinds =

  let tryParse(raw: string) =
    match raw with
    | "following" -> Some Following
    | "latest" -> Some Latest
    | "trending" -> Some Trending
    | _ -> None

  let wire(log: LogKind) =
    match log with
    | Following -> "following"
    | Latest -> "latest"
    | Trending -> "trending"

type LogPage = {
  Page: Page<CommitNode>
  Log: LogKind
  IsRanked: bool
  NewCommitsCount: int option
  Suggestions: CommitNode list
}

type ReportReason =
  | Spam
  | Harassment
  | Unsafe
  | Other

module ReportReasons =

  let wire(reason: ReportReason) =
    match reason with
    | Spam -> "spam"
    | Harassment -> "harassment"
    | Unsafe -> "unsafe"
    | Other -> "other"

type LaneChoice =
  | Automatic
  | Palette of index: int
  | Custom of color: string

type LaneError<'e> =
  static abstract InvalidLaneIndex: index: int -> 'e
  static abstract InvalidLaneColor: color: string -> 'e

module LaneChoices =

  let validate<'e when LaneError<'e>>
    (choice: LaneChoice)
    : Result<LaneChoice, 'e> =
    match choice with
    | Automatic -> Ok choice
    | Palette index when index >= 0 && index <= 5 -> Ok choice
    | Palette index -> Error('e.InvalidLaneIndex index)
    | Custom color when Regex.IsMatch(color, "^#[0-9a-f]{6}$") -> Ok choice
    | Custom color -> Error('e.InvalidLaneColor color)

type PollDuration =
  | OneHour
  | OneDay
  | ThreeDays
  | SevenDays

module PollDurations =

  let wire(duration: PollDuration) =
    match duration with
    | OneHour -> 60
    | OneDay -> 1440
    | ThreeDays -> 4320
    | SevenDays -> 10080

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

type PollSpec = {
  Options: string list
  Duration: PollDuration
}

module PollSpecs =

  let maxOptionLength = 50

  let parse<'e when PollError<'e>>
    (options: string list, duration: PollDuration)
    : Result<PollSpec, 'e> =
    let count = List.length options

    if count < 2 then
      Error('e.TooFewPollOptions count)
    elif count > 4 then
      Error('e.TooManyPollOptions count)
    else
      // The server refuses options that repeat ignoring case; the first
      // of a repeated pair names the duplicate.
      let rec duplicate(seen: Set<string>) =
        function
        | [] -> None
        | (label: string) :: rest ->
          let folded = label.ToLowerInvariant()

          if Set.contains folded seen then
            Some label
          else
            duplicate (Set.add folded seen) rest

      match duplicate Set.empty options with
      | Some label -> Error('e.DuplicatePollOption label)
      | None ->
        match
          options |> List.tryFind(fun label -> label.Length > maxOptionLength)
        with
        | Some label -> Error('e.PollOptionTooLong(label, label.Length))
        | None ->
          Ok {
            Options = options
            Duration = duration
          }

  let options(spec: PollSpec) = spec.Options

  let duration(spec: PollSpec) = spec.Duration

type PastelResult = {
  Commit: Commit
  PasteisRemaining: int
  PasteisResetsAt: DateTimeOffset
  AuthorPasteisReceived: int
}

type MuteResult = { User: User; IsMuting: bool }

type BlockResult = { User: User; IsBlocking: bool }

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

type NotificationPage = {
  Page: Page<Notification>
  FilterType: string
  ReadAt: DateTimeOffset
}

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

  let parse(raw: string) =
    match raw with
    | "invalid_signature" -> InvalidSignature
    | "device_not_approved" -> DeviceNotApproved
    | "invalid_grant" -> InvalidGrant
    | "user_blocked" -> UserBlocked
    | "user_unavailable" -> UserUnavailable
    | "own_commit" -> OwnCommit
    | "commit_unavailable" -> CommitUnavailable
    | "pasteis_exhausted" -> PasteisExhausted
    | "rate_limited" -> RateLimited
    | "github_sync_failed" -> GitHubSyncFailed
    | "timed_out" -> TimedOut
    | "banned" -> Banned
    | "commit_rules_required" -> CommitRulesRequired
    | raw -> Unknown raw

  let wire(code: ErrorCode) =
    match code with
    | InvalidSignature -> "invalid_signature"
    | DeviceNotApproved -> "device_not_approved"
    | InvalidGrant -> "invalid_grant"
    | UserBlocked -> "user_blocked"
    | UserUnavailable -> "user_unavailable"
    | OwnCommit -> "own_commit"
    | CommitUnavailable -> "commit_unavailable"
    | PasteisExhausted -> "pasteis_exhausted"
    | RateLimited -> "rate_limited"
    | GitHubSyncFailed -> "github_sync_failed"
    | TimedOut -> "timed_out"
    | Banned -> "banned"
    | CommitRulesRequired -> "commit_rules_required"
    | Unknown raw -> raw

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

/// How long one mute lasts, as the server takes it.
type MuteDuration =
  | MuteHour
  | MuteTomorrow
  | MuteWeek
  | MuteForever
  | MuteOff

/// Who the server lets push to the user. An unknown value stays raw.
type RemotesFrom =
  | FromFollowing
  | FromEveryone
  | UnknownRemotesFrom of raw: string

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

  let wire(role: OrgRole) =
    match role with
    | Maintainer -> "maintainer"
    | Contributor -> "contributor"
    | UnknownRole raw -> raw

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

module InboxTabs =

  let wire(tab: InboxTab) =
    match tab with
    | TabOpen -> "open"
    | TabFetched -> "fetched"
    | TabStashed -> "stashed"

module MuteDurations =

  let wire(duration: MuteDuration) =
    match duration with
    | MuteHour -> "hour"
    | MuteTomorrow -> "tomorrow"
    | MuteWeek -> "week"
    | MuteForever -> "forever"
    | MuteOff -> "off"

module RemotesFroms =

  let parse(raw: string) =
    match raw with
    | "following" -> FromFollowing
    | "everyone" -> FromEveryone
    | raw -> UnknownRemotesFrom raw

  let wire(from: RemotesFrom) =
    match from with
    | FromFollowing -> "following"
    | FromEveryone -> "everyone"
    | UnknownRemotesFrom raw -> raw

type Reaction = { UserId: string; Emoji: string }

type ReactionUse = { Emoji: string; Count: int }

type ReactionChange = {
  MessageId: int
  Reactions: Reaction list
}

type VoiceClip = {
  Id: string
  DurationMs: int
  Waveform: int list
  Url: string
}

type MessageParent = {
  Id: int
  UserId: string
  Body: string
  ImagesCount: int
  HasVoice: bool
}

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

type OrgMember = { User: User; Role: OrgRole option }

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

type Inbox = {
  Conversations: Conversation list
  HasMore: bool
  UnreadCount: int
  RequestsCount: int
  Pins: Map<string, DateTimeOffset option>
}

type ThreadMember = {
  User: User
  Role: OrgRole option
  ReadThroughId: int
}

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

type OrgRef = { Id: string; Name: string option }

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

type RemoteCandidate = { User: User; Reason: string option }

type PinResult = { PinnedAt: DateTimeOffset option }

type ThreadMuteResult = { MutedUntilAt: DateTimeOffset option }

type CreatedOrg = { Id: string }

type CommitPreview = {
  Body: string
  BodySegments: Segment list
  PreviewSegments: Segment list option
  Mentions: Mention list
}

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

let inline failure path message = { Path = path; Message = message }

let toDecodeFailure(error: JDeck.DecodeError) : DecodeFailure =
  let path =
    match error.property, error.index with
    | Some property, Some index -> $"{property}[{index}]"
    | Some property, None -> property
    | None, Some index -> $"[{index}]"
    | None, None -> "$"

  { Path = path; Message = error.message }

let parseBody
  (body: string)
  (decoder: JDeck.Decoder<'value>)
  : Result<'value, DecodeFailure> =
  try
    JDeck.Decoding.fromString(body, decoder) |> Result.mapError toDecodeFailure
  with :? JsonException as ex ->
    Error(failure "$" ex.Message)

let inline optionalDecoder
  (decoder: JDeck.Decoder<'value>)
  (element: JsonElement)
  : Result<'value option, JDeck.DecodeError> =
  JDeck.Decode.Decode.oneOf
    [
      fun el -> JDeck.Decode.Optional.unit el |> Result.map(fun _ -> None)
      fun el -> decoder el |> Result.map Some
    ]
    element

let inline optionalListDecoder
  (decoder: JDeck.Decoder<'value>)
  (element: JsonElement)
  : Result<'value list option, JDeck.DecodeError> =
  optionalDecoder (JDeck.Decode.Decode.list(fun _ item -> decoder item)) element

let laneDecoder(element: JsonElement) : Result<Lane, JDeck.DecodeError> =
  JDeck.Decode.Decode.oneOf
    [
      fun el -> Required.int el |> Result.map LaneIndex
      fun el -> Required.string el |> Result.map LaneColor
    ]
    element

let statusDecoder
  (element: JsonElement)
  : Result<CommitStatus, JDeck.DecodeError> =
  Required.string element |> Result.map CommitStatuses.parse

let logKindDecoder(element: JsonElement) : Result<LogKind, JDeck.DecodeError> =
  Required.string element
  |> Result.bind(fun raw ->
    match LogKinds.tryParse raw with
    | Some log -> Ok log
    | None ->
      JDeck.DecodeError.ofError<LogKind>(
        element.Clone(),
        $"Unknown log '{raw}'."
      )
      |> Error)

let userDecoder(element: JsonElement) = decode {
  let! id = Required.Property.get ("id", Required.string) element
  let! handle = Required.Property.get ("handle", Required.string) element
  let! name = Required.Property.get ("name", Required.string) element

  let! avatarUrl =
    Optional.Property.get ("avatar_url", Optional.string) element
    |> Result.map Option.flatten

  let! lane =
    Optional.Property.get ("lane", optionalDecoder laneDecoder) element
    |> Result.map Option.flatten

  let! commitsCount =
    Optional.Property.get ("commits_count", Optional.int) element
    |> Result.map Option.flatten

  let! followersCount =
    Optional.Property.get ("followers_count", Optional.int) element
    |> Result.map Option.flatten

  let! followingCount =
    Optional.Property.get ("following_count", Optional.int) element
    |> Result.map Option.flatten

  let! isFollowed =
    Optional.Property.get ("is_followed", Optional.boolean) element
    |> Result.map Option.flatten

  let! followsYou =
    Optional.Property.get ("follows_you", Optional.boolean) element
    |> Result.map Option.flatten

  return {
    Id = id
    Handle = handle
    Name = name
    AvatarUrl = avatarUrl
    Lane = lane
    CommitsCount = commitsCount
    FollowersCount = followersCount
    FollowingCount = followingCount
    IsFollowed = isFollowed
    FollowsYou = followsYou
  }
}

let imageDecoder(element: JsonElement) = decode {
  let! id = Required.Property.get ("id", Required.string) element
  let! width = Required.Property.get ("width", Required.int) element
  let! height = Required.Property.get ("height", Required.int) element

  let! description =
    Optional.Property.get ("description", Optional.string) element
    |> Result.map Option.flatten

  let! url = Required.Property.get ("url", Required.string) element

  return {
    Id = id
    Width = width
    Height = height
    Description = description
    Url = url
  }
}

let segmentDecoder(element: JsonElement) : Result<Segment, JDeck.DecodeError> =
  match Required.Property.get ("type", Required.string) element with
  | Error error -> Error error
  | Ok "text" -> decode {
      let! text = Required.Property.get ("text", Required.string) element
      return TextSegment text
    }
  | Ok "code" -> decode {
      let! code = Required.Property.get ("code", Required.string) element
      return CodeSegment code
    }
  | Ok "block" -> decode {
      let! language =
        Optional.Property.get ("language", Optional.string) element
        |> Result.map Option.flatten

      let! html = Required.Property.get ("html", Required.string) element
      return BlockSegment(language, html)
    }
  | Ok other ->
    JDeck.DecodeError.ofError<Segment>(
      element.Clone(),
      $"Unknown segment type '{other}'."
    )
    |> Error

let mentionDecoder(element: JsonElement) = decode {
  let! handle = Required.Property.get ("handle", Required.string) element
  let! offset = Required.Property.get ("offset", Required.int) element
  let! length = Required.Property.get ("length", Required.int) element

  return {
    Handle = handle
    Offset = offset
    Length = length
  }
}

let embedDecoder(element: JsonElement) = decode {
  let! provider = Required.Property.get ("provider", Required.string) element
  let! kind = Required.Property.get ("kind", Required.string) element
  let! src = Required.Property.get ("src", Required.string) element

  let! aspectRatio =
    Optional.Property.get ("aspect_ratio", Optional.string) element
    |> Result.map Option.flatten

  let! height =
    Optional.Property.get ("height", Optional.int) element
    |> Result.map Option.flatten

  let! poster =
    Optional.Property.get ("poster", Optional.string) element
    |> Result.map Option.flatten

  return {
    Provider = provider
    Kind = kind
    Src = src
    AspectRatio = aspectRatio
    Height = height
    Poster = poster
  }
}

let linkPreviewDecoder(element: JsonElement) = decode {
  let! url = Required.Property.get ("url", Required.string) element

  let! title =
    Optional.Property.get ("title", Optional.string) element
    |> Result.map Option.flatten

  let! description =
    Optional.Property.get ("description", Optional.string) element
    |> Result.map Option.flatten

  let! imageUrl =
    Optional.Property.get ("image_url", Optional.string) element
    |> Result.map Option.flatten

  let! siteName =
    Optional.Property.get ("site_name", Optional.string) element
    |> Result.map Option.flatten

  let! isPending =
    Required.Property.get ("is_pending", Required.boolean) element

  let! embed =
    Optional.Property.get ("embed", optionalDecoder embedDecoder) element
    |> Result.map Option.flatten

  return {
    Url = url
    Title = title
    Description = description
    ImageUrl = imageUrl
    SiteName = siteName
    IsPending = isPending
    Embed = embed
  }
}

let pollOptionDecoder(element: JsonElement) = decode {
  let! id = Required.Property.get ("id", Required.string) element
  let! label = Required.Property.get ("label", Required.string) element

  let! votesCount =
    Optional.Property.get ("votes_count", Optional.int) element
    |> Result.map Option.flatten

  let! voted = Required.Property.get ("voted", Required.boolean) element

  let! percentage =
    Optional.Property.get ("percentage", Optional.int) element
    |> Result.map Option.flatten

  return {
    Id = id
    Label = label
    VotesCount = votesCount
    Voted = voted
    Percentage = percentage
  }
}

let pollDecoder(element: JsonElement) = decode {
  let! id = Required.Property.get ("id", Required.string) element

  let! closesAt =
    Optional.Property.get ("closes_at", Optional.dateTimeOffset) element
    |> Result.map Option.flatten

  let! isClosed = Required.Property.get ("is_closed", Required.boolean) element

  let! hasVoted = Required.Property.get ("has_voted", Required.boolean) element

  let! totalVotes =
    Optional.Property.get ("total_votes", Optional.int) element
    |> Result.map Option.flatten

  let! options = Required.Property.list ("options", pollOptionDecoder) element

  return {
    Id = id
    ClosesAt = closesAt
    IsClosed = isClosed
    HasVoted = hasVoted
    TotalVotes = totalVotes
    Options = options
  }
}

let activityBucketDecoder(element: JsonElement) = decode {
  let! date = Required.Property.get ("date", Required.string) element
  let! count = Required.Property.get ("count", Required.int) element
  return { Date = date; Count = count }
}

let activityDecoder(element: JsonElement) = decode {
  let! scale = Required.Property.get ("scale", Required.string) element

  let! buckets =
    Required.Property.list ("buckets", activityBucketDecoder) element

  return { Scale = scale; Buckets = buckets }
}

let streaksDecoder(element: JsonElement) = decode {
  let! longest = Required.Property.get ("longest", Required.int) element
  let! current = Required.Property.get ("current", Required.int) element
  return { Longest = longest; Current = current }
}

let xpItemDecoder(element: JsonElement) = decode {
  let! count = Required.Property.get ("count", Required.int) element
  let! multiplier = Required.Property.get ("multiplier", Required.int) element
  let! xp = Required.Property.get ("xp", Required.int) element

  return {
    Count = count
    Multiplier = multiplier
    Xp = xp
  }
}

let xpBreakdownDecoder(element: JsonElement) = decode {
  let! commits = Required.Property.get ("commits", xpItemDecoder) element
  let! streak = Required.Property.get ("streak", xpItemDecoder) element

  let! starsReceived =
    Required.Property.get ("stars_received", xpItemDecoder) element

  let! replies = Required.Property.get ("replies", xpItemDecoder) element

  let! starsGiven = Required.Property.get ("stars_given", xpItemDecoder) element

  let! luddite = Required.Property.get ("luddite", xpItemDecoder) element

  return {
    Commits = commits
    Streak = streak
    StarsReceived = starsReceived
    Replies = replies
    StarsGiven = starsGiven
    Luddite = luddite
  }
}

let rankDecoder(element: JsonElement) = decode {
  let! rank = Required.Property.get ("rank", Required.string) element
  let! level = Required.Property.get ("level", Required.int) element
  let! minXp = Required.Property.get ("min_xp", Required.int) element

  let! description =
    Required.Property.get ("description", Required.string) element

  let! achieved = Required.Property.get ("achieved", Required.boolean) element

  return {
    Rank = rank
    Level = level
    MinXp = minXp
    Description = description
    Achieved = achieved
  }
}

let badgeDecoder(element: JsonElement) = decode {
  let! id = Required.Property.get ("id", Required.string) element
  let! name = Required.Property.get ("name", Required.string) element

  let! description =
    Required.Property.get ("description", Required.string) element

  let! icon = Required.Property.get ("icon", Required.string) element
  let! unlocked = Required.Property.get ("unlocked", Required.boolean) element
  let! progress = Required.Property.get ("progress", Required.int) element
  let! target = Required.Property.get ("target", Required.int) element

  return {
    Id = id
    Name = name
    Description = description
    Icon = icon
    Unlocked = unlocked
    Progress = progress
    Target = target
  }
}

let achievementsDecoder(element: JsonElement) = decode {
  let! level = Required.Property.get ("level", Required.int) element
  let! rank = Required.Property.get ("rank", Required.string) element
  let! xp = Required.Property.get ("xp", Required.int) element

  let! nextLevelXp =
    Optional.Property.get ("next_level_xp", Optional.int) element
    |> Result.map Option.flatten

  let! levelProgress =
    Required.Property.get ("level_progress", Required.int) element

  let! xpBreakdown =
    Required.Property.get ("xp_breakdown", xpBreakdownDecoder) element

  let! ranks = Required.Property.list ("ranks", rankDecoder) element
  let! badges = Required.Property.list ("badges", badgeDecoder) element

  return {
    Level = level
    Rank = rank
    Xp = xp
    NextLevelXp = nextLevelXp
    LevelProgress = levelProgress
    XpBreakdown = xpBreakdown
    Ranks = ranks
    Badges = badges
  }
}

let profileDecoder(element: JsonElement) = decode {
  let! user = userDecoder element

  let! bio =
    Optional.Property.get ("bio", Optional.string) element
    |> Result.map Option.flatten

  let! company =
    Optional.Property.get ("company", Optional.string) element
    |> Result.map Option.flatten

  let! location =
    Optional.Property.get ("location", Optional.string) element
    |> Result.map Option.flatten

  let! website =
    Optional.Property.get ("website", Optional.string) element
    |> Result.map Option.flatten

  let! githubUrl = Required.Property.get ("github_url", Required.string) element
  let! htmlUrl = Required.Property.get ("html_url", Required.string) element
  let! feedUrl = Required.Property.get ("feed_url", Required.string) element

  let! createdAt =
    Required.Property.get ("created_at", Required.dateTimeOffset) element

  let! commitsCount =
    Optional.Property.get ("commits_count", Optional.int) element
    |> Result.map Option.flatten

  let! followersCount =
    Optional.Property.get ("followers_count", Optional.int) element
    |> Result.map Option.flatten

  let! followingCount =
    Optional.Property.get ("following_count", Optional.int) element
    |> Result.map Option.flatten

  let! lane =
    Optional.Property.get ("lane", optionalDecoder laneDecoder) element
    |> Result.map Option.flatten

  return {
    User = user
    Bio = bio
    Company = company
    Location = location
    Website = website
    GithubUrl = githubUrl
    HtmlUrl = htmlUrl
    FeedUrl = feedUrl
    CreatedAt = createdAt
    CommitsCount = commitsCount
    FollowersCount = followersCount
    FollowingCount = followingCount
    Lane = lane
  }
}

let tombstoneDecoder(element: JsonElement) = decode {
  let! id = Required.Property.get ("id", Required.string) element

  let! parentId = Required.Property.get ("parent_id", Optional.string) element

  let! deletedAt =
    Required.Property.get ("deleted_at", Required.dateTimeOffset) element

  let! repliesCount =
    Optional.Property.get ("replies_count", Optional.int) element
    |> Result.map Option.flatten

  return {
    Id = id
    ParentId = parentId
    DeletedAt = deletedAt
    RepliesCount = repliesCount
  }
}

let rec commitDecoder
  (element: JsonElement)
  : Result<Commit, JDeck.DecodeError> = decode {
  let! id = Required.Property.get ("id", Required.string) element
  let! shortHash = Required.Property.get ("short_hash", Required.string) element
  let! body = Required.Property.get ("body", Required.string) element

  let! bodySegments =
    Required.Property.list ("body_segments", segmentDecoder) element

  let! previewSegments =
    Optional.Property.get
      ("preview_segments", optionalListDecoder segmentDecoder)
      element
    |> Result.map Option.flatten

  let! mentions = Required.Property.list ("mentions", mentionDecoder) element

  let! parentId = Required.Property.get ("parent_id", Optional.string) element

  let! parent =
    Optional.Property.get ("parent", optionalDecoder commitNodeDecoder) element
    |> Result.map Option.flatten

  let! htmlUrl = Required.Property.get ("html_url", Required.string) element

  let! createdAt =
    Required.Property.get ("created_at", Required.dateTimeOffset) element

  let! starsCount =
    Optional.Property.get ("stars_count", Optional.int) element
    |> Result.map Option.flatten

  let! repliesCount =
    Optional.Property.get ("replies_count", Optional.int) element
    |> Result.map Option.flatten

  let! status = Required.Property.get ("status", statusDecoder) element
  let! author = Required.Property.get ("author", userDecoder) element

  let! images =
    Optional.Property.get ("images", optionalListDecoder imageDecoder) element
    |> Result.map Option.flatten

  let! starred =
    Optional.Property.get ("starred", Optional.boolean) element
    |> Result.map Option.flatten

  let! isPinned = Required.Property.get ("is_pinned", Required.boolean) element

  let! pasteisCount =
    Required.Property.get ("pasteis_count", Required.int) element

  let! quotedCommitId =
    Required.Property.get ("quoted_commit_id", Optional.string) element

  let! quotedCommit =
    Optional.Property.get
      ("quoted_commit", optionalDecoder commitNodeDecoder)
      element
    |> Result.map Option.flatten

  let! poll =
    Optional.Property.get ("poll", optionalDecoder pollDecoder) element
    |> Result.map Option.flatten

  let! linkPreview =
    Optional.Property.get
      ("link_preview", optionalDecoder linkPreviewDecoder)
      element
    |> Result.map Option.flatten

  let! replyPreview =
    Optional.Property.get
      ("reply_preview", optionalDecoder replyPreviewDecoder)
      element
    |> Result.map Option.flatten

  return {
    Id = id
    ShortHash = shortHash
    Body = body
    BodySegments = bodySegments
    PreviewSegments = previewSegments
    Mentions = mentions
    ParentId = parentId
    Parent = parent
    HtmlUrl = htmlUrl
    CreatedAt = createdAt
    StarsCount = starsCount
    RepliesCount = repliesCount
    Status = status
    Author = author
    Images = Option.defaultValue [] images
    Starred = starred
    IsPinned = isPinned
    PasteisCount = pasteisCount
    QuotedCommitId = quotedCommitId
    QuotedCommit = quotedCommit
    Poll = poll
    LinkPreview = linkPreview
    ReplyPreview = replyPreview
  }
}

and replyPreviewDecoder
  (element: JsonElement)
  : Result<ReplyPreview, JDeck.DecodeError> = decode {
  let! data = Required.Property.list ("data", commitNodeDecoder) element

  let! nextCursor =
    Required.Property.get ("next_cursor", Optional.string) element

  return { Data = data; NextCursor = nextCursor }
}

and commitNodeDecoder
  (element: JsonElement)
  : Result<CommitNode, JDeck.DecodeError> =
  JDeck.Decode.Decode.oneOf
    [
      fun el -> commitDecoder el |> Result.map LiveCommit
      fun el -> tombstoneDecoder el |> Result.map Tombstone
    ]
    element

let pageLinksDecoder(element: JsonElement) = decode {
  let! first =
    Optional.Property.get ("first", Optional.string) element
    |> Result.map Option.flatten

  let! last =
    Optional.Property.get ("last", Optional.string) element
    |> Result.map Option.flatten

  let! prev =
    Optional.Property.get ("prev", Optional.string) element
    |> Result.map Option.flatten

  let! next =
    Optional.Property.get ("next", Optional.string) element
    |> Result.map Option.flatten

  return {
    First = first
    Last = last
    Prev = prev
    Next = next
  }
}

type PageMeta = {
  Path: string option
  NextCursor: string option
  PrevCursor: string option
  PerPage: int
}

let pageMetaDecoder(element: JsonElement) = decode {
  let! path =
    Optional.Property.get ("path", Optional.string) element
    |> Result.map Option.flatten

  let! nextCursor =
    Required.Property.get ("next_cursor", Optional.string) element

  let! prevCursor =
    Required.Property.get ("prev_cursor", Optional.string) element

  let! perPage = Required.Property.get ("per_page", Required.int) element

  return {
    Path = path
    NextCursor = nextCursor
    PrevCursor = prevCursor
    PerPage = perPage
  }
}

let pageDecoder (itemDecoder: Decoder<'value>) (element: JsonElement) = decode {
  let! items = Required.Property.list ("data", itemDecoder) element

  let! links =
    Optional.Property.get ("links", optionalDecoder pageLinksDecoder) element
    |> Result.map Option.flatten

  let! meta = Required.Property.get ("meta", pageMetaDecoder) element

  return {
    Items = items
    Links =
      Option.defaultValue
        {
          First = None
          Last = None
          Prev = None
          Next = None
        }
        links
    Path = meta.Path
    NextCursor = meta.NextCursor
    PrevCursor = meta.PrevCursor
    PerPage = meta.PerPage
  }
}

let commitPageDecoder(element: JsonElement) = decode {
  let! commit = Required.Property.get ("data", commitDecoder) element

  let! ancestors =
    Optional.Property.get
      ("ancestors", optionalListDecoder commitNodeDecoder)
      element
    |> Result.map Option.flatten

  return {
    Commit = commit
    Ancestors = Option.defaultValue [] ancestors
  }
}

let profileDetailsDecoder(element: JsonElement) = decode {
  let! profile = Required.Property.get ("data", profileDecoder) element

  let! isFollowing =
    Optional.Property.get ("is_following", Optional.boolean) element
    |> Result.map Option.flatten

  let! followsYou =
    Optional.Property.get ("follows_you", Optional.boolean) element
    |> Result.map Option.flatten

  let! isMuting =
    Optional.Property.get ("is_muting", Optional.boolean) element
    |> Result.map Option.flatten

  let! pasteisReceived =
    Optional.Property.get ("pasteis_received", Optional.int) element
    |> Result.map Option.flatten

  let! activity =
    Optional.Property.get ("activity", optionalDecoder activityDecoder) element
    |> Result.map Option.flatten

  let! streaks =
    Optional.Property.get ("streaks", optionalDecoder streaksDecoder) element
    |> Result.map Option.flatten

  let! achievements =
    Optional.Property.get
      ("achievements", optionalDecoder achievementsDecoder)
      element
    |> Result.map Option.flatten

  let! pinnedCommit =
    Optional.Property.get
      ("pinned_commit", optionalDecoder commitNodeDecoder)
      element
    |> Result.map Option.flatten

  return {
    Profile = profile
    IsFollowing = Option.defaultValue false isFollowing
    FollowsYou = Option.defaultValue false followsYou
    IsMuting = Option.defaultValue false isMuting
    PasteisReceived = Option.defaultValue 0 pasteisReceived
    Activity = activity
    Streaks = streaks
    Achievements = achievements
    PinnedCommit = pinnedCommit
  }
}

let meRemotesDecoder(element: JsonElement) = decode {
  let! from = Required.Property.get ("from", Required.string) element

  let! pullReceipts =
    Required.Property.get ("pull_receipts", Required.boolean) element

  let! requests = Required.Property.get ("requests", Required.boolean) element
  let! everyone = Required.Property.get ("everyone", Required.boolean) element

  let! unreadCount =
    Required.Property.get ("unread_count", Required.int) element

  return {
    From = RemotesFroms.parse from
    PullReceipts = pullReceipts
    Requests = requests
    Everyone = everyone
    UnreadCount = unreadCount
  }
}

type CurrentUserData = {
  Profile: Profile
  PasteisMode: bool
  PasteisRemaining: int
  PasteisResetsAt: DateTimeOffset
  PasteisReceived: int
  PreferredLane: Lane option
  AutomaticLane: int
  SponsorsUrl: string option
  GithubSyncedAt: DateTimeOffset option
  Remotes: MeRemotes option
}

let currentUserDataDecoder(element: JsonElement) = decode {
  let! profile = profileDecoder element

  let! pasteisMode =
    Required.Property.get ("pasteis_mode", Required.boolean) element

  let! pasteisRemaining =
    Required.Property.get ("pasteis_remaining", Required.int) element

  let! pasteisResetsAt =
    Required.Property.get ("pasteis_resets_at", Required.dateTimeOffset) element

  let! pasteisReceived =
    Required.Property.get ("pasteis_received", Required.int) element

  let! preferredLane =
    Optional.Property.get
      ("preferred_lane", optionalDecoder laneDecoder)
      element
    |> Result.map Option.flatten

  let! automaticLane =
    Required.Property.get ("automatic_lane", Required.int) element

  let! sponsorsUrl =
    Optional.Property.get ("sponsors_url", Optional.string) element
    |> Result.map Option.flatten

  let! githubSyncedAt =
    Optional.Property.get ("github_synced_at", Optional.dateTimeOffset) element
    |> Result.map Option.flatten

  let! remotes =
    Required.Property.get ("remotes", optionalDecoder meRemotesDecoder) element

  return {
    Profile = profile
    PasteisMode = pasteisMode
    PasteisRemaining = pasteisRemaining
    PasteisResetsAt = pasteisResetsAt
    PasteisReceived = pasteisReceived
    PreferredLane = preferredLane
    AutomaticLane = automaticLane
    SponsorsUrl = sponsorsUrl
    GithubSyncedAt = githubSyncedAt
    Remotes = remotes
  }
}

let meDecoder(element: JsonElement) = decode {
  let! data = Required.Property.get ("data", currentUserDataDecoder) element

  let! unread =
    Required.Property.get ("unread_notifications_count", Required.int) element

  return {
    Profile = data.Profile
    PasteisMode = data.PasteisMode
    PasteisRemaining = data.PasteisRemaining
    PasteisResetsAt = data.PasteisResetsAt
    PasteisReceived = data.PasteisReceived
    PreferredLane = data.PreferredLane
    AutomaticLane = data.AutomaticLane
    SponsorsUrl = data.SponsorsUrl
    GithubSyncedAt = data.GithubSyncedAt
    UnreadNotificationsCount = unread
    Remotes = data.Remotes
  }
}

let logPageDecoder(element: JsonElement) = decode {
  let! page = pageDecoder commitNodeDecoder element
  let! log = Required.Property.get ("log", logKindDecoder) element

  let! isRanked = Required.Property.get ("is_ranked", Required.boolean) element

  let! newCommits =
    Optional.Property.get ("new_commits_count", Optional.int) element
    |> Result.map Option.flatten

  let! suggestions =
    Optional.Property.get
      ("suggestions", optionalListDecoder commitNodeDecoder)
      element
    |> Result.map Option.flatten

  return {
    Page = page
    Log = log
    IsRanked = isRanked
    NewCommitsCount = newCommits
    Suggestions = Option.defaultValue [] suggestions
  }
}

let pastelResultDecoder(element: JsonElement) = decode {
  let! commit = Required.Property.get ("data", commitDecoder) element

  let! remaining =
    Required.Property.get ("pasteis_remaining", Required.int) element

  let! resetsAt =
    Required.Property.get ("pasteis_resets_at", Required.dateTimeOffset) element

  let! received =
    Required.Property.get ("author_pasteis_received", Required.int) element

  return {
    Commit = commit
    PasteisRemaining = remaining
    PasteisResetsAt = resetsAt
    AuthorPasteisReceived = received
  }
}

let muteResultDecoder(element: JsonElement) = decode {
  let! user = Required.Property.get ("data", userDecoder) element

  let! isMuting = Required.Property.get ("is_muting", Required.boolean) element

  return { User = user; IsMuting = isMuting }
}

let blockResultDecoder(element: JsonElement) = decode {
  let! user = Required.Property.get ("data", userDecoder) element

  let! isBlocking =
    Required.Property.get ("is_blocking", Required.boolean) element

  return { User = user; IsBlocking = isBlocking }
}

let notificationDecoder(element: JsonElement) = decode {
  let! id = Required.Property.get ("id", Required.string) element

  let! notificationType =
    Required.Property.get ("type", Required.string) element

  let! subjectId =
    Optional.Property.get ("subject_id", Optional.string) element
    |> Result.map Option.flatten

  let! reason =
    Optional.Property.get ("reason", Optional.string) element
    |> Result.map Option.flatten

  let! readAt =
    Optional.Property.get ("read_at", Optional.dateTimeOffset) element
    |> Result.map Option.flatten

  let! createdAt =
    Required.Property.get ("created_at", Required.dateTimeOffset) element

  let! actor = Required.Property.get ("actor", userDecoder) element

  let! actors =
    Optional.Property.get ("actors", optionalListDecoder userDecoder) element
    |> Result.map Option.flatten

  let! groupId = Required.Property.get ("group_id", Required.string) element
  let! eventCount = Required.Property.get ("event_count", Required.int) element

  let! actorsCount =
    Required.Property.get ("actors_count", Required.int) element

  let! unreadCount =
    Required.Property.get ("unread_count", Required.int) element

  let! commit =
    Optional.Property.get ("commit", optionalDecoder commitNodeDecoder) element
    |> Result.map Option.flatten

  return {
    Id = id
    Type = notificationType
    SubjectId = subjectId
    Reason = reason
    ReadAt = readAt
    CreatedAt = createdAt
    Actor = actor
    Actors = Option.defaultValue [] actors
    GroupId = groupId
    EventCount = eventCount
    ActorsCount = actorsCount
    UnreadCount = unreadCount
    Commit = commit
  }
}

let notificationPageDecoder(element: JsonElement) = decode {
  let! page = pageDecoder notificationDecoder element

  let! filterType =
    Required.Property.get
      ("filters", Required.Property.get("type", Required.string))
      element

  let! readAt =
    Required.Property.get ("read_at", Required.dateTimeOffset) element

  return {
    Page = page
    FilterType = filterType
    ReadAt = readAt
  }
}

let apiFailureDecoder(element: JsonElement) = decode {
  let! message = Required.Property.get ("message", Required.string) element

  let! code =
    Optional.Property.get ("code", Optional.string) element
    |> Result.map Option.flatten

  return {
    Message = message
    Code = code |> Option.map ErrorCodes.parse
  }
}

let conversationKindDecoder
  (element: JsonElement)
  : Result<ConversationKind, JDeck.DecodeError> =
  Required.string element
  |> Result.map(fun raw ->
    match raw with
    | "direct" -> Direct
    | "org" -> Org
    | raw -> UnknownKind raw)

let pushAccessDecoder
  (element: JsonElement)
  : Result<PushAccess, JDeck.DecodeError> =
  Required.string element
  |> Result.map(fun raw ->
    match raw with
    | "open" -> PushOpen
    | "request" -> PushRequest
    | "waiting" -> PushWaiting
    | "restricted" -> PushRestricted
    | "closed" -> PushClosed
    | raw -> UnknownAccess raw)

let orgRoleOf(raw: string) =
  match raw with
  | "maintainer" -> Maintainer
  | "contributor" -> Contributor
  | raw -> UnknownRole raw

let orgEventOf(raw: string) =
  match raw with
  | "created" -> Created
  | "added" -> Added
  | "removed" -> Removed
  | "left" -> Left
  | "renamed" -> Renamed
  | "promoted" -> Promoted
  | "demoted" -> Demoted
  | "inherited" -> Inherited
  | "ping" -> Ping
  | raw -> UnknownEvent raw

/// Reads a member's role, which an org may leave null.
let orgRoleDecoder
  (element: JsonElement)
  : Result<OrgRole option, JDeck.DecodeError> =
  Optional.string element |> Result.map(Option.map orgRoleOf)

let orgEventDecoder
  (element: JsonElement)
  : Result<OrgEvent, JDeck.DecodeError> =
  Required.string element |> Result.map orgEventOf

let reactionDecoder(element: JsonElement) = decode {
  let! userId = Required.Property.get ("user_id", Required.string) element
  let! emoji = Required.Property.get ("emoji", Required.string) element
  return { UserId = userId; Emoji = emoji }
}

let reactionUseDecoder(element: JsonElement) = decode {
  let! emoji = Required.Property.get ("emoji", Required.string) element
  let! count = Required.Property.get ("count", Required.int) element
  return { Emoji = emoji; Count = count }
}

let reactionChangeDecoder(element: JsonElement) = decode {
  let! messageId = Required.Property.get ("id", Required.int) element
  let! reactions = Required.Property.list ("reactions", reactionDecoder) element

  return {
    MessageId = messageId
    Reactions = reactions
  }
}

let voiceClipDecoder(element: JsonElement) = decode {
  let! id = Required.Property.get ("id", Required.string) element
  let! durationMs = Required.Property.get ("duration_ms", Required.int) element
  let! waveform = Required.Property.list ("waveform", Required.int) element
  let! url = Required.Property.get ("url", Required.string) element

  return {
    Id = id
    DurationMs = durationMs
    Waveform = waveform
    Url = url
  }
}

let messageParentDecoder(element: JsonElement) = decode {
  let! id = Required.Property.get ("id", Required.int) element
  let! userId = Required.Property.get ("user_id", Required.string) element
  let! body = Required.Property.get ("body", Required.string) element

  let! imagesCount =
    Required.Property.get ("images_count", Required.int) element

  let! hasVoice = Required.Property.get ("has_voice", Required.boolean) element

  return {
    Id = id
    UserId = userId
    Body = body
    ImagesCount = imagesCount
    HasVoice = hasVoice
  }
}

let messageDecoder(element: JsonElement) = decode {
  let! id = Required.Property.get ("id", Required.int) element
  let! userId = Required.Property.get ("user_id", Required.string) element
  let! clientId = Required.Property.get ("client_id", Required.string) element
  let! body = Required.Property.get ("body", Required.string) element

  let! bodySegments =
    Required.Property.list ("body_segments", segmentDecoder) element

  let! mentions = Required.Property.list ("mentions", mentionDecoder) element

  // The server leaves `reply_to_id` out entirely wherever the model
  // hides it, as pinned messages do.
  let! replyToId =
    Optional.Property.get ("reply_to_id", Optional.int) element
    |> Result.map Option.flatten

  let! replyTo =
    Optional.Property.get
      ("reply_to", optionalDecoder messageParentDecoder)
      element
    |> Result.map Option.flatten

  let! linkPreview =
    Optional.Property.get
      ("link_preview", optionalDecoder linkPreviewDecoder)
      element
    |> Result.map Option.flatten

  let! images =
    Optional.Property.get ("images", optionalListDecoder imageDecoder) element
    |> Result.map Option.flatten

  let! voice =
    Optional.Property.get ("voice", optionalDecoder voiceClipDecoder) element
    |> Result.map Option.flatten

  let! reactions =
    Optional.Property.get
      ("reactions", optionalListDecoder reactionDecoder)
      element
    |> Result.map Option.flatten

  let! event =
    Required.Property.get ("event", Optional.string) element
    |> Result.map(Option.map orgEventOf)

  let! subjectId = Required.Property.get ("subject_id", Optional.string) element

  let! user =
    Optional.Property.get ("user", optionalDecoder userDecoder) element
    |> Result.map Option.flatten

  let! subject =
    Optional.Property.get ("subject", optionalDecoder userDecoder) element
    |> Result.map Option.flatten

  let! createdAt =
    Required.Property.get ("created_at", Required.dateTimeOffset) element

  return {
    Id = id
    UserId = userId
    ClientId = clientId
    Body = body
    BodySegments = bodySegments
    Mentions = mentions
    ReplyToId = replyToId
    ReplyTo = replyTo
    LinkPreview = linkPreview
    Images = Option.defaultValue [] images
    Voice = voice
    Reactions = Option.defaultValue [] reactions
    Event = event
    SubjectId = subjectId
    User = user
    Subject = subject
    CreatedAt = createdAt
  }
}

let orgMemberDecoder(element: JsonElement) = decode {
  let! user = Required.Property.get ("user", userDecoder) element
  let! role = Required.Property.get ("role", orgRoleDecoder) element
  return { User = user; Role = role }
}

let conversationDecoder(element: JsonElement) = decode {
  let! id = Required.Property.get ("id", Required.string) element
  let! kind = Required.Property.get ("kind", conversationKindDecoder) element
  let! name = Required.Property.get ("name", Optional.string) element

  let! partner =
    Required.Property.get ("partner", optionalDecoder userDecoder) element

  let! members =
    Required.Property.get
      ("members", optionalListDecoder orgMemberDecoder)
      element

  let! lastMessage =
    Required.Property.get
      ("last_message", optionalDecoder messageDecoder)
      element

  let! lastMessageAt =
    Required.Property.get ("last_message_at", Optional.dateTimeOffset) element

  let! ahead = Required.Property.get ("ahead", Required.int) element

  let! lastIncomingMessageId =
    Required.Property.get ("last_incoming_message_id", Optional.int) element

  let! lastIncomingSender =
    Required.Property.get ("last_incoming_sender", Optional.string) element

  let! partnerReadThroughId =
    Required.Property.get ("partner_read_through_id", Required.int) element

  let! partnerReadAt =
    Required.Property.get ("partner_read_at", Optional.dateTimeOffset) element

  let! isArchived =
    Required.Property.get ("is_archived", Required.boolean) element

  let! pinnedAt =
    Required.Property.get ("pinned_at", Optional.dateTimeOffset) element

  let! mutedUntilAt =
    Required.Property.get ("muted_until_at", Optional.dateTimeOffset) element

  return {
    Id = id
    Kind = kind
    Name = name
    Partner = partner
    Members = members
    LastMessage = lastMessage
    LastMessageAt = lastMessageAt
    Ahead = ahead
    LastIncomingMessageId = lastIncomingMessageId
    LastIncomingSender = lastIncomingSender
    PartnerReadThroughId = partnerReadThroughId
    PartnerReadAt = partnerReadAt
    IsArchived = isArchived
    PinnedAt = pinnedAt
    MutedUntilAt = mutedUntilAt
  }
}

let pinsDecoder(element: JsonElement) : Map<string, DateTimeOffset option> =
  match element.TryGetProperty "pins" with
  | false, _ -> Map.empty
  | true, pins when pins.ValueKind = JsonValueKind.Object ->
    [
      for property in pins.EnumerateObject() do
        match property.Value.ValueKind with
        | JsonValueKind.String ->
          property.Name, Some(property.Value.GetDateTimeOffset())
        | _ -> property.Name, None
    ]
    |> Map.ofList
  | true, _ -> Map.empty

let inboxDecoder(element: JsonElement) = decode {
  let! conversations =
    Required.Property.list ("data", conversationDecoder) element

  let! hasMore = Required.Property.get ("has_more", Required.boolean) element

  let! unreadCount =
    Required.Property.get ("unread_count", Required.int) element

  let! requestsCount =
    Required.Property.get ("requests_count", Required.int) element

  return {
    Conversations = conversations
    HasMore = hasMore
    UnreadCount = unreadCount
    RequestsCount = requestsCount
    Pins = pinsDecoder element
  }
}

let threadMemberDecoder(element: JsonElement) = decode {
  let! user = Required.Property.get ("user", userDecoder) element
  let! role = Required.Property.get ("role", orgRoleDecoder) element

  let! readThroughId =
    Required.Property.get ("read_through_id", Required.int) element

  return {
    User = user
    Role = role
    ReadThroughId = readThroughId
  }
}

let threadDataDecoder(element: JsonElement) = decode {
  let! partner = Required.Property.get ("partner", userDecoder) element
  let! messages = Required.Property.list ("messages", messageDecoder) element
  let! hasOlder = Required.Property.get ("has_older", Required.boolean) element

  let! reactionsVersion =
    Required.Property.get ("reactions_version", Required.int) element

  let! reactionChanges =
    Required.Property.list ("reaction_changes", reactionChangeDecoder) element

  let! readThroughId =
    Required.Property.get ("read_through_id", Required.int) element

  let! partnerReadThroughId =
    Required.Property.get ("partner_read_through_id", Required.int) element

  let! partnerReadAt =
    Required.Property.get ("partner_read_at", Optional.dateTimeOffset) element

  let! partnerTyping =
    Required.Property.get ("partner_typing", Required.boolean) element

  let! isArchived =
    Required.Property.get ("is_archived", Required.boolean) element

  let! pinnedAt =
    Required.Property.get ("pinned_at", Optional.dateTimeOffset) element

  let! mutedUntilAt =
    Required.Property.get ("muted_until_at", Optional.dateTimeOffset) element

  let! access = Required.Property.get ("access", pushAccessDecoder) element

  return {
    Partner = partner
    Messages = messages
    HasOlder = hasOlder
    ReactionsVersion = reactionsVersion
    ReactionChanges = reactionChanges
    ReadThroughId = readThroughId
    PartnerReadThroughId = partnerReadThroughId
    PartnerReadAt = partnerReadAt
    PartnerTyping = partnerTyping
    IsArchived = isArchived
    PinnedAt = pinnedAt
    MutedUntilAt = mutedUntilAt
    Access = access
  }
}

let orgRefDecoder(element: JsonElement) = decode {
  let! id = Required.Property.get ("id", Required.string) element
  let! name = Required.Property.get ("name", Optional.string) element
  return { Id = id; Name = name }
}

let orgThreadDataDecoder(element: JsonElement) = decode {
  let! org = Required.Property.get ("org", orgRefDecoder) element
  let! messages = Required.Property.list ("messages", messageDecoder) element
  let! hasOlder = Required.Property.get ("has_older", Required.boolean) element

  let! reactionsVersion =
    Required.Property.get ("reactions_version", Required.int) element

  let! reactionChanges =
    Required.Property.list ("reaction_changes", reactionChangeDecoder) element

  let! readThroughId =
    Required.Property.get ("read_through_id", Required.int) element

  let! members = Required.Property.list ("members", threadMemberDecoder) element
  let! typing = Required.Property.list ("typing", Required.string) element

  let! isArchived =
    Required.Property.get ("is_archived", Required.boolean) element

  let! pinnedAt =
    Required.Property.get ("pinned_at", Optional.dateTimeOffset) element

  let! mutedUntilAt =
    Required.Property.get ("muted_until_at", Optional.dateTimeOffset) element

  let! role =
    Required.Property.get ("role", Optional.string) element
    |> Result.map(Option.map orgRoleOf)

  let! access = Required.Property.get ("access", pushAccessDecoder) element

  return {
    Org = org
    Messages = messages
    HasOlder = hasOlder
    ReactionsVersion = reactionsVersion
    ReactionChanges = reactionChanges
    ReadThroughId = readThroughId
    Members = members
    Typing = typing
    IsArchived = isArchived
    PinnedAt = pinnedAt
    MutedUntilAt = mutedUntilAt
    Role = role
    Access = access
  }
}

let remoteCandidateDecoder(element: JsonElement) = decode {
  let! user = Required.Property.get ("user", userDecoder) element
  let! reason = Required.Property.get ("reason", Optional.string) element
  return { User = user; Reason = reason }
}

let reactionUsesDecoder(element: JsonElement) = decode {
  let! usage = Required.Property.list ("usage", reactionUseDecoder) element
  return usage
}

let reactionsDecoder(element: JsonElement) = decode {
  let! reactions = Required.Property.list ("reactions", reactionDecoder) element
  return reactions
}

let pinResultDecoder(element: JsonElement) = decode {
  let! pinnedAt =
    Required.Property.get ("pinned_at", Optional.dateTimeOffset) element

  return { PinnedAt = pinnedAt }
}

let threadMuteResultDecoder(element: JsonElement) = decode {
  let! mutedUntilAt =
    Required.Property.get ("muted_until_at", Optional.dateTimeOffset) element

  return { MutedUntilAt = mutedUntilAt }
}

let createdOrgDecoder(element: JsonElement) = decode {
  let! id =
    Required.Property.get
      ("org", Required.Property.get("id", Required.string))
      element

  return { Id = id }
}

let messageLinkPreviewDecoder(element: JsonElement) = decode {
  let! linkPreview =
    Required.Property.get
      ("link_preview", optionalDecoder linkPreviewDecoder)
      element

  return linkPreview
}

let commitPreviewDataDecoder(element: JsonElement) = decode {
  let! body = Required.Property.get ("body", Required.string) element

  let! bodySegments =
    Required.Property.list ("body_segments", segmentDecoder) element

  let! previewSegments =
    Optional.Property.get
      ("preview_segments", optionalListDecoder segmentDecoder)
      element
    |> Result.map Option.flatten

  let! mentions = Required.Property.list ("mentions", mentionDecoder) element

  return {
    Body = body
    BodySegments = bodySegments
    PreviewSegments = previewSegments
    Mentions = mentions
  }
}

let inline decodeMe(body: string) : Result<Me, DecodeFailure> =
  parseBody body meDecoder

let inline decodeFailure(body: string) : Result<Failure, DecodeFailure> =
  parseBody body apiFailureDecoder

let inline decodeCreatedCommit(body: string) : Result<Commit, DecodeFailure> =
  parseBody body (Required.Property.get("data", commitDecoder))

let inline decodeCommitPage(body: string) : Result<CommitPage, DecodeFailure> =
  parseBody body commitPageDecoder

let inline decodeCommitList
  (body: string)
  : Result<Page<CommitNode>, DecodeFailure> =
  parseBody body (pageDecoder commitNodeDecoder)

let inline decodeUserList(body: string) : Result<Page<User>, DecodeFailure> =
  parseBody body (pageDecoder userDecoder)

let inline decodeNotificationList
  (body: string)
  : Result<Page<Notification>, DecodeFailure> =
  parseBody body (pageDecoder notificationDecoder)

let inline decodeNotificationPage
  (body: string)
  : Result<NotificationPage, DecodeFailure> =
  parseBody body notificationPageDecoder

let inline decodeProfileDetails
  (body: string)
  : Result<ProfileDetails, DecodeFailure> =
  parseBody body profileDetailsDecoder

let inline decodeLogPage(body: string) : Result<LogPage, DecodeFailure> =
  parseBody body logPageDecoder

let inline decodePastelResult
  (body: string)
  : Result<PastelResult, DecodeFailure> =
  parseBody body pastelResultDecoder

let inline decodeMuteResult(body: string) : Result<MuteResult, DecodeFailure> =
  parseBody body muteResultDecoder

let inline decodeBlockResult
  (body: string)
  : Result<BlockResult, DecodeFailure> =
  parseBody body blockResultDecoder

let inline decodeInbox(body: string) : Result<Inbox, DecodeFailure> =
  parseBody body inboxDecoder

let inline decodeThread(body: string) : Result<Thread, DecodeFailure> =
  parseBody body (Required.Property.get("data", threadDataDecoder))

let inline decodeOrgThread(body: string) : Result<OrgThread, DecodeFailure> =
  parseBody body (Required.Property.get("data", orgThreadDataDecoder))

let inline decodeMessage(body: string) : Result<Message, DecodeFailure> =
  parseBody body (Required.Property.get("data", messageDecoder))

let inline decodePinResult(body: string) : Result<PinResult, DecodeFailure> =
  parseBody body pinResultDecoder

let inline decodeThreadMuteResult
  (body: string)
  : Result<ThreadMuteResult, DecodeFailure> =
  parseBody body threadMuteResultDecoder

let inline decodeCreatedOrg(body: string) : Result<CreatedOrg, DecodeFailure> =
  parseBody body createdOrgDecoder

let inline decodeReactionUses
  (body: string)
  : Result<ReactionUse list, DecodeFailure> =
  parseBody body reactionUsesDecoder

let inline decodeReactions
  (body: string)
  : Result<Reaction list, DecodeFailure> =
  parseBody body reactionsDecoder

let inline decodeRemoteCandidates
  (body: string)
  : Result<RemoteCandidate list, DecodeFailure> =
  parseBody body (Required.Property.list("candidates", remoteCandidateDecoder))

let inline decodeMessageLinkPreview
  (body: string)
  : Result<LinkPreview option, DecodeFailure> =
  parseBody body messageLinkPreviewDecoder

let inline decodeCommitPreview
  (body: string)
  : Result<CommitPreview, DecodeFailure> =
  parseBody body (Required.Property.get("data", commitPreviewDataDecoder))

module Me =
  let decode(body: string) = decodeMe body

module Failure =
  let decode(body: string) = decodeFailure body

module Commit =
  let decodeCreated(body: string) = decodeCreatedCommit body

module CommitPage =
  let decode(body: string) = decodeCommitPage body

module CommitNode =
  let decodePage(body: string) = decodeCommitList body

module User =
  let decodePage(body: string) = decodeUserList body

module ProfileDetails =
  let decode(body: string) = decodeProfileDetails body

module LogPage =
  let decode(body: string) = decodeLogPage body

module PastelResult =
  let decode(body: string) = decodePastelResult body

module MuteResult =
  let decode(body: string) = decodeMuteResult body

module BlockResult =
  let decode(body: string) = decodeBlockResult body

module NotificationPage =
  let decode(body: string) = decodeNotificationPage body

module Notification =
  let decodePage(body: string) = decodeNotificationList body

module Inbox =
  let decode(body: string) = decodeInbox body

module Thread =
  let decode(body: string) = decodeThread body

module OrgThread =
  let decode(body: string) = decodeOrgThread body

module Message =
  let decode(body: string) = decodeMessage body

module PinResult =
  let decode(body: string) = decodePinResult body

module ThreadMuteResult =
  let decode(body: string) = decodeThreadMuteResult body

module CreatedOrg =
  let decode(body: string) = decodeCreatedOrg body

module ReactionUses =
  let decode(body: string) = decodeReactionUses body

module Reactions =
  let decode(body: string) = decodeReactions body

module RemoteCandidates =
  let decode(body: string) = decodeRemoteCandidates body

module MessageLinkPreview =
  let decode(body: string) = decodeMessageLinkPreview body

module CommitPreview =
  let decode(body: string) = decodeCommitPreview body

type TransportError<'e> =
  static abstract TransportFailed: message: string -> 'e

type Rejected<'e> =
  static abstract Refused:
    status: int * code: ErrorCode option * message: string -> 'e

type DecodeError<'e> =
  static abstract DecodeFailed: failure: DecodeFailure -> 'e
