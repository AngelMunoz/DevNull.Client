module DevNull.Client.Core.ApiTypes

#nowarn "3535"

open System
open System.Text.Json
open System.Text.RegularExpressions
open JDeck

type CurrentUser = {
  Id: string
  Username: string
  Name: string
  AvatarUrl: string option
}

type DeviceSession = {
  User: CurrentUser
  UnreadNotificationsCount: int
  PasteisMode: bool
}

type Lane =
  | LaneIndex of index: int
  | LaneColor of color: string

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
  Username: string
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
    | Unknown raw -> raw

/// One JSON value the decoder could not read.
type DecodeFailure = { Path: string; Message: string }

/// The message and code of one refused request. A body that carries no
/// code leaves the option empty.
type Failure = {
  Message: string
  Code: ErrorCode option
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
  let! username = Required.Property.get ("username", Required.string) element
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
    Username = username
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
  let! username = Required.Property.get ("username", Required.string) element
  let! offset = Required.Property.get ("offset", Required.int) element
  let! length = Required.Property.get ("length", Required.int) element

  return {
    Username = username
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

type TransportError<'e> =
  static abstract TransportFailed: message: string -> 'e

type Rejected<'e> =
  static abstract Refused:
    status: int * code: ErrorCode option * message: string -> 'e

type DecodeError<'e> =
  static abstract DecodeFailed: failure: DecodeFailure -> 'e
