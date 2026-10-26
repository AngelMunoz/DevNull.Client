module DevNull.Client.UI.Tests.Fixtures

open System
open System.IO
open Microsoft.Extensions.Logging.Abstractions
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Commit
open DevNull.Client.Core.Http
open DevNull.Client.DeviceAuth
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.RouteStore
open DevNull.Client.SettingsStore
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Timeline
open DevNull.Client.UiClock
open DevNull.Client.Unread

/// The canned clock: one fixed unix second for every request.
type FixedClock(epoch: int64) =

  interface IClock with
    member _.UnixNow() = epoch

type FixedNonceSource(hex: string) =

  interface INonceSource with
    member _.NewHex() = hex

type FixedPkceSource(verifier: string) =

  interface IPkceSource with
    member _.NewVerifier() = verifier

/// Bytes served for every commit image. The headless decode stub never
/// parses them, so any payload exercises the full load path.
let imageBytes: byte array = [| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy |]

/// One staged image with the true PNG signature, so the sniffing
/// parser accepts it.
let stagedImage(name: string) : ImageAttachment = {
  FileName = name
  Bytes = [| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy; 26uy; 10uy |]
}

/// The fake attachment sources: every answer is a property the test
/// sets before the composer asks.
type FakeAttachments() =
  member val PickResponse: ImageAttachment list = [] with get, set

  member val ClipboardImage: ImageAttachment option = None with get, set

  member val ClipboardText: string option = None with get, set

  interface IAttachmentSources with
    member this.PickFiles() = async { return this.PickResponse }

    member this.ReadClipboardImage() = async { return this.ClipboardImage }

    member this.ReadClipboardText() = async { return this.ClipboardText }

/// The fake profile worker: one canned answer, every username
/// recorded.
type FakeProfiles() =
  let asked = ResizeArray<string>()

  member val Details: ProfileDetails option = None with get, set

  member _.Asked = asked |> Seq.toList

  interface IProfiles with
    member this.Fetch(username: string) = async {
      asked.Add username
      return this.Details
    }

/// The one "type base64" line the fake key source offers and the fake
/// key lookup answers with.
let lookupKey = "ssh-ed25519 AAAAFakeKeyOfferedToTheLookup"

/// The fake ssh key source: one canned key the lookup claims, one
/// private path the tests set, every ask recorded.
type FakeSshKeys() =
  let asked = ResizeArray<string>()

  member val PrivatePath: string = "/tmp/devnull-test-id_ed25519" with get, set

  member _.Asked = asked |> Seq.toList

  interface ISshKeys with
    member _.Candidates() = [| lookupKey |]

    member this.PrivateKeyOf(keyId: string) =
      asked.Add keyId

      if keyId = lookupKey then Some this.PrivatePath else None

/// One http worker that records every request and answers through the
/// given handler; no test reaches the network. Byte downloads answer
/// through the given bytes handler; the default serves every commit
/// image and refuses anything else.
type FakeApi
  (
    handler: HttpRequest -> HttpResponse,
    ?bytes: string -> Result<byte array, TransportFailure>
  ) =
  let requests = ResizeArray<HttpRequest>()
  let byteRequests = ResizeArray<string>()

  let bytesHandler =
    bytes
    |> Option.defaultWith(fun () ->
      fun target ->
        if target.Contains "/commit-images/" then
          Ok imageBytes
        else
          Error(
            {
              Message = $"no canned bytes for {target}"
            }
            : TransportFailure
          ))

  new(body: string) =
    FakeApi(fun _ -> {
      Status = 200
      Headers = []
      Body = body
    })

  member _.Requests = requests |> Seq.toList

  member _.ByteRequests = byteRequests |> Seq.toList

  interface IHttp with
    member _.Send(request) = async {
      requests.Add request
      return Ok(handler request)
    }

    member _.GetBytes(target: string) = async {
      byteRequests.Add target
      return bytesHandler target
    }

/// The app environment over fakes: fixed workers and the given store.
type TestEnv
  (
    store: SessionStore,
    http: IHttp,
    ?settings: AppSettingsStore,
    ?attachments: FakeAttachments,
    ?lastVisit: LastVisitStore,
    ?profiles: FakeProfiles,
    ?sshKeys: FakeSshKeys
  ) as this =

  let settingsStore =
    settings |> Option.defaultWith(fun () -> AppSettingsStore(None, ignore))

  let attachmentSources =
    attachments |> Option.defaultWith(fun () -> FakeAttachments())

  // The in-memory marker store: no test touches the marker file.
  let lastVisitStore =
    lastVisit |> Option.defaultWith(fun () -> LastVisitStore(Map.empty, ignore))

  let profileWorker = profiles |> Option.defaultWith(fun () -> FakeProfiles())

  let keySource = sshKeys |> Option.defaultWith(fun () -> FakeSshKeys())

  // The signature answers are fixed; every signed input is recorded,
  // so a test can assert which key file a post signed with.
  let signed = ResizeArray<SignRequest>()

  let signer = {
    new ISigner with
      member _.Sign(request: SignRequest) = async {
        signed.Add request
        return Ok "dGVzdC1zaWduYXR1cmU"
      }
  }

  let clock = FixedClock(1760000000L)
  let nonceSource = FixedNonceSource("00112233445566778899aabbccddeeff")

  let pkceSource =
    FixedPkceSource("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")

  /// A fast clock, so time-driven assertions tick in milliseconds.
  let uiClock = UiClockService(TimeSpan.FromSeconds 1.0)

  // A throw-away folder per suite run, so the disk tier never shares
  // state between tests.
  let imageCache =
    ImageCacheService(
      Path.Combine(
        Path.GetTempPath(),
        "devnull-imagecache-" + Guid.NewGuid().ToString("N")
      ),
      http.GetBytes
    )

  let unread =
    UnreadService(
      store,
      (fun () ->
        match store.SignedIn with
        | ValueSome current -> Me.get(this, { KeyFile = current.KeyFile })
        | ValueNone -> async { return Error(SignInFailed "signed out") }),
      NullLogger.Instance
    )

  let timeline = TimelineService()

  let routes = RouteStore()

  let remotes =
    RemotesStore(
      {
        Clock = clock
        NonceSource = nonceSource
        Signer = signer
        Http = http
      },
      store,
      NullLogger.Instance
    )

  interface IProvideClock with
    member _.Clock = clock

  interface IProvideNonceSource with
    member _.NonceSource = nonceSource

  interface IProvideSigner with
    member _.Signer = signer

  interface IProvidePkceSource with
    member _.PkceSource = pkceSource

  interface IProvideHttp with
    member _.Http = http

  /// Every input the fake signer signed, in order.
  member _.SignedRequests = signed |> Seq.toList

  interface IProvideSession with
    member _.Session = store

  interface IProvideSettings with
    member _.Settings = settingsStore

  interface IProvideLogging with
    member _.LoggerFactory = NullLoggerFactory.Instance

  interface IProvideUnread with
    member _.Unread = unread

  interface IProvideRemotes with
    member _.Remotes = remotes

  interface IProvideTimeline with
    member _.Timeline = timeline

  interface IProvideRoutes with
    member _.Routes = routes

  interface IProvideUiClock with
    member _.UiClock = uiClock

  interface IProvideImageCache with
    member _.ImageCache = imageCache

  interface IProvideAttachmentSources with
    member _.Attachments = attachmentSources

  interface IProvideLastVisit with
    member _.LastVisit = lastVisitStore

  interface IProvideProfiles with
    member _.Profiles = profileWorker

  interface IProvideSshKeys with
    member _.SshKeys = keySource

let sessionOf(username: string) : DeviceSession = {
  User = {
    Id = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
    Username = username
    Name = "The Octocat"
    AvatarUrl = None
  }
  UnreadNotificationsCount = 3
  PasteisMode = true
  Remotes = None
}

let signedInStore() =
  SessionStore(Some(sessionOf "octocat"), devicePrivateKeyPath, ignore)

/// A signed-in store whose viewer has pastéis mode off, so the pastel
/// controls must stay hidden everywhere.
let signedInStoreWithoutPasteis() =
  SessionStore(
    Some(
      {
        sessionOf "octocat" with
            PasteisMode = false
      }
    ),
    devicePrivateKeyPath,
    ignore
  )

/// A signed-in store with a custom unread notification count, so the
/// badge assertions can use a count no canned commit carries.
let signedInStoreWithUnread(unread: int) =
  SessionStore(
    Some(
      {
        sessionOf "octocat" with
            UnreadNotificationsCount = unread
      }
    ),
    devicePrivateKeyPath,
    ignore
  )

let signedOutStore() =
  SessionStore(None, devicePrivateKeyPath, ignore)

/// One commit as the server returns it, with one author, one body, and
/// one image.
let commitJson =
  """{"id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","short_hash":"b8b9c0d","body":"It was DNS.","body_segments":[],"preview_segments":null,"mentions":[],"parent_id":null,"quoted_commit_id":null,"html_url":"https://dev-null.sh/@octocat/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","created_at":"2026-10-20T04:17:19Z","stars_count":7,"replies_count":3,"status":"approved","is_pinned":false,"pasteis_count":0,"starred":false,"author":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},"images":[{"id":"018f4c1e-img0-7c3a-9b2d-5e6f7a8b9c0d","width":1600,"height":900,"description":"a diagram","url":"https://dev-null.sh/commit-images/018f4c1e-img0-7c3a-9b2d-5e6f7a8b9c0d"}]}"""

/// A second commit for the next page of the log.
let secondCommitJson =
  """{"id":"018f4c1e-9999-7c3a-9b2d-5e6f7a8b9c0d","short_hash":"c0d1e2f","body":"Second commit.","body_segments":[],"preview_segments":null,"mentions":[],"parent_id":null,"quoted_commit_id":null,"html_url":"https://dev-null.sh/@octocat/018f4c1e-9999-7c3a-9b2d-5e6f7a8b9c0d","created_at":"2026-10-20T04:18:19Z","stars_count":0,"replies_count":0,"status":"approved","is_pinned":false,"pasteis_count":0,"starred":false,"author":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},"images":[]}"""

/// One commit whose highlighted shell block carries the escaped quotes
/// the server writes into the segment html.
let codeBlockCommitJson =
  commitJson.Replace(
    "\"body_segments\":[]",
    "\"body_segments\":[{\"type\":\"block\",\"language\":\"shell\",\"html\":\"<span>figlet &quot;HAVE A GREAT DAY GUYS&quot; | lolcat</span>\"}]"
  )

/// One log page with the commit above and a cursor for the next page.
let logPageJson =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":\"next\",\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
    commitJson

/// The second log page, answered to a cursor request.
let secondLogPageJson =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
    secondCommitJson

/// One empty log page: no items and no cursor.
let emptyLogPageJson =
  """{"data":[],"links":{},"meta":{"per_page":20,"next_cursor":null,"prev_cursor":null},"log":"latest","is_ranked":false,"suggestions":[]}"""

/// One successful response with the body.
let ok(body: string) : HttpResponse = {
  Status = 200
  Headers = []
  Body = body
}

/// The signed-in account, as GET /api/v1/me returns it.
let meJson =
  """{"data":{"id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","username":"octocat","name":"The Octocat","avatar_url":null,"bio":"Builds things.","company":"GitHub","location":"San Francisco","website":"https://example.test","github_url":"https://github.com/octocat","html_url":"https://dev-null.sh/@octocat","feed_url":"https://dev-null.sh/@octocat/feed.atom","created_at":"2026-01-01T00:00:00Z","commits_count":7,"followers_count":11,"following_count":13,"lane":3,"pasteis_mode":true,"pasteis_remaining":7,"pasteis_resets_at":"2026-10-27T00:00:00Z","pasteis_received":12,"preferred_lane":null,"automatic_lane":4,"sponsors_url":null,"github_synced_at":null,"remotes":null},"unread_notifications_count":3}"""

/// The author of the canned commits, as a bare user.
let apiUserJson =
  """{"id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","username":"octocat","name":"The Octocat","avatar_url":null,"bio":"Builds things.","company":"GitHub","location":"San Francisco","website":"https://example.test","github_url":"https://github.com/octocat","html_url":"https://dev-null.sh/@octocat","feed_url":"https://dev-null.sh/@octocat/feed.atom","created_at":"2026-01-01T00:00:00Z","commits_count":7,"followers_count":11,"following_count":13,"lane":3}"""

/// One profile with the viewer's relation, as GET /api/v1/users/{username} returns it.
let profileJson =
  sprintf
    """{"data":%s,"is_following":false,"follows_you":true,"is_muting":false,"pasteis_received":7,"activity":null,"streaks":null,"achievements":null,"pinned_commit":null}"""
    apiUserJson

/// The same profile carrying its activity, streaks, and achievements.
let profileWithStatsJson =
  profileJson.Replace(
    "\"activity\":null,\"streaks\":null,\"achievements\":null",
    "\"activity\":{\"scale\":\"week\",\"buckets\":[{\"date\":\"2026-10-13\",\"count\":3},{\"date\":\"2026-10-14\",\"count\":5},{\"date\":\"2026-10-15\",\"count\":1}]},\"streaks\":{\"longest\":12,\"current\":3},\"achievements\":{\"level\":3,\"rank\":\"Wizard\",\"xp\":1234,\"next_level_xp\":2000,\"level_progress\":61,\"xp_breakdown\":{\"commits\":{\"count\":10,\"multiplier\":2,\"xp\":200},\"streak\":{\"count\":5,\"multiplier\":3,\"xp\":150},\"stars_received\":{\"count\":4,\"multiplier\":5,\"xp\":200},\"replies\":{\"count\":2,\"multiplier\":4,\"xp\":80},\"stars_given\":{\"count\":6,\"multiplier\":1,\"xp\":60},\"luddite\":{\"count\":1,\"multiplier\":2,\"xp\":20}},\"ranks\":[],\"badges\":[{\"id\":\"b1\",\"name\":\"First commit\",\"description\":\"You committed\",\"icon\":\"commit\",\"unlocked\":true,\"progress\":1,\"target\":1}]}"
  )

/// The account answers every fake serves: the shell's aside card asks
/// for the profile on every page, so every fake must answer with the
/// shape the real server sends — a wrong-shaped body here turns into
/// decode noise the tests never asserted on. /me stays unanswered: the
/// unread poll's failures are silent under the test logger, and a
/// canned answer would overwrite each test's own session values.
let accountAnswer(request: HttpRequest) : HttpResponse voption =
  if
    request.Target.StartsWith "/api/v1/users/"
    && request.Target.Contains "/commits"
  then
    ValueSome(
      ok(
        sprintf
          "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
          commitJson
      )
    )
  elif request.Target.StartsWith "/api/v1/users/" then
    ValueSome(ok profileJson)
  else
    ValueNone

/// The api of the tests: log reads answer page one, then page two for
/// the cursor; a commit post answers the created commit. The bytes
/// handler of the worker is the caller's.
let logApiWithBytes(bytes: string -> Result<byte array, TransportFailure>) =
  FakeApi(
    (fun request ->
      if
        request.Method = "POST" && request.Target = "/api/cli/key-lookups"
      then
        ok(lookupKey + "\n")
      elif request.Method = "POST" && request.Target = "/api/cli/commits" then
        ok
          "Committed: https://dev-null.sh/@octocat/018f4c1e-9999-7c3a-9b2d-5e6f7a8b9c0d\n"
      elif request.Method = "POST" && request.Target = "/api/v1/commits" then
        ok(sprintf "{\"data\":%s}" commitJson)
      elif request.Target.Contains "cursor=next" then
        ok secondLogPageJson
      elif request.Target.StartsWith "/api/v1/log/" then
        ok logPageJson
      else
        (match accountAnswer request with
         | ValueSome answer -> answer
         | ValueNone -> ok "{}")),
    bytes
  )

/// The api of the tests: log reads answer page one, then page two for
/// the cursor; a commit post answers the created commit.
let logApi() = logApiWithBytes(fun _ -> Ok imageBytes)

/// The api of the code-block test: the boot log carries the commit whose
/// segment html holds the escaped quotes.
let codeBlockLogApi() =
  let page =
    sprintf
      "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
      codeBlockCommitJson

  FakeApi(
    (fun request ->
      if request.Target.StartsWith "/api/v1/log/" then
        ok page
      else
        (match accountAnswer request with
         | ValueSome answer -> answer
         | ValueNone -> ok "{}")),
    (fun _ -> Ok imageBytes)
  )

/// A commit authored by the signed-in viewer: the star can only ever
/// be the count, never the toggle, because the server refuses a star
/// on one's own commit.
let ownCommitJson =
  commitJson
    .Replace(
      "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d",
      "018f4c1e-0aa0-7c3a-9b2d-5e6f7a8b9c0d"
    )
    .Replace("\"short_hash\":\"b8b9c0d\"", "\"short_hash\":\"0aa0c0d\"")
    .Replace("\"body\":\"It was DNS.\"", "\"body\":\"My own commit.\"")
    .Replace(
      "\"author\":{\"id\":\"01a151db-7fee-727b-a7ce-c911a7556242\"",
      "\"author\":{\"id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\""
    )

/// The log of both directions: the viewer's own commit beside someone
/// else's, so the star affordances can differ on one screen.
let ownLogApi() =
  let page =
    sprintf
      "{\"data\":[%s,%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
      ownCommitJson
      commitJson

  FakeApi(
    (fun request ->
      if
        request.Method = "POST" && request.Target = "/api/cli/key-lookups"
      then
        ok(lookupKey + "\n")
      elif request.Target.StartsWith "/api/v1/log/" then
        ok page
      else
        (match accountAnswer request with
         | ValueSome answer -> answer
         | ValueNone -> ok "{}")),
    (fun _ -> Ok imageBytes)
  )

/// A reply to the first commit, for the thread page.
let replyJson =
  """{"id":"018f4c1e-7777-7c3a-9b2d-5e6f7a8b9c0d","short_hash":"d1e2f3a","body":"A reply.","body_segments":[],"preview_segments":null,"mentions":[],"parent_id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","quoted_commit_id":null,"html_url":"https://dev-null.sh/@octocat/018f4c1e-7777-7c3a-9b2d-5e6f7a8b9c0d","created_at":"2026-10-20T04:19:19Z","stars_count":0,"replies_count":0,"status":"approved","is_pinned":false,"pasteis_count":0,"starred":false,"author":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},"images":[]}"""

/// The thread envelope of GET /api/v1/commits/{commit}.
let threadPageJson = sprintf "{\"data\":%s,\"ancestors\":[]}" commitJson

/// The replies page of the thread.
let repliesPageJson =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
    replyJson

/// The pastel result of POST /api/v1/commits/{commit}/pasteis.
let pastelJson =
  sprintf
    "{\"data\":%s,\"pasteis_remaining\":6,\"pasteis_resets_at\":\"2026-10-27T00:00:00Z\",\"author_pasteis_received\":1}"
    commitJson

/// The api of the thread tests: the log feeds the boot, the commit,
/// its replies, the pastel, and the reply post all answer from cans.
let threadApi() =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif request.Method = "POST" && request.Target = "/api/v1/commits" then
      ok(sprintf "{\"data\":%s}" replyJson)
    elif
      request.Method = "POST"
      && request.Target =
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/pasteis"
    then
      ok pastelJson
    elif
      request.Method = "GET"
      && request.Target.StartsWith(
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/replies"
      )
    then
      ok repliesPageJson
    elif
      request.Target.StartsWith
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
    then
      ok threadPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The api of the empty-log test.
let emptyLogApi() =
  FakeApi(fun request ->
    match accountAnswer request with
    | ValueSome answer -> answer
    | ValueNone -> ok emptyLogPageJson)

/// One commit pinned to a fixed 2020 date, so its day label is stable
/// for decades: "Fri, Apr 3" and "Wed, Apr 1".
let datedCommitJson(id: string, hash: string, body: string, date: string) =
  commitJson
    .Replace(
      "\"id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\"",
      $"\"id\":\"{id}\""
    )
    .Replace("\"short_hash\":\"b8b9c0d\"", $"\"short_hash\":\"{hash}\"")
    .Replace("\"body\":\"It was DNS.\"", $"\"body\":\"{body}\"")
    .Replace(
      "\"created_at\":\"2026-10-20T04:17:19Z\"",
      $"\"created_at\":\"{date}\""
    )

/// The api of the day-separator test: two commits on two fixed days.
let twoDaysLogApi() =
  let twoDays =
    sprintf
      "{\"data\":[%s,%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
      (datedCommitJson(
        "0a0a0a0a-0000-0000-0000-000000000001",
        "aaa1111",
        "First landing.",
        "2020-04-03T12:00:00Z"
      ))
      (datedCommitJson(
        "0a0a0a0a-0000-0000-0000-000000000002",
        "bbb2222",
        "The week before.",
        "2020-04-01T12:00:00Z"
      ))

  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok twoDays
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The signed-in session of the canned user.
let signedInSession() = {
  Session = sessionOf "octocat"
  KeyFile = devicePrivateKeyPath
}

/// One log page carrying a single commit newer than the head.
let newerPageJson =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":1,\"suggestions\":[]}"
    (datedCommitJson(
      "0a0a0a0a-0000-0000-0000-000000000003",
      "ccc3333",
      "A newer commit.",
      "2020-04-04T12:00:00Z"
    ))

/// One ranked page: is_ranked true and the days interleaved by rank,
/// the way Trending orders commits.
let rankedLogPageJson =
  sprintf
    "{\"data\":[%s,%s,%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"trending\",\"is_ranked\":true,\"new_commits_count\":null,\"suggestions\":[]}"
    (datedCommitJson(
      "0a0a0a0a-0000-0000-0000-000000000011",
      "ttt1111",
      "Ranked one.",
      "2020-04-03T12:00:00Z"
    ))
    (datedCommitJson(
      "0a0a0a0a-0000-0000-0000-000000000012",
      "yyy2222",
      "Ranked two.",
      "2020-04-01T12:00:00Z"
    ))
    (datedCommitJson(
      "0a0a0a0a-0000-0000-0000-000000000013",
      "ttt3333",
      "Ranked three.",
      "2020-04-03T13:00:00Z"
    ))

/// A log page of N distinct commits, so the virtualized feed overflows
/// the viewport plus its cache band and recycles rows while scrolling.
let manyCardsPageJson(cardCount: int) : string =
  let card index =
    commitJson
      .Replace(
        "\"id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\"",
        $"\"id\":\"018f4c1e-6f8a-7c3a-9b2d-{index:D4}\""
      )
      .Replace(
        "\"short_hash\":\"b8b9c0d\"",
        $"\"short_hash\":\"card{index:D2}\""
      )
      .Replace("\"body\":\"It was DNS.\"", $"\"body\":\"Card {index}.\"")

  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"suggestions\":[]}"
    (String.concat "," [ for index in 1..cardCount -> card index ])

/// The api of the recycle test: every log read answers the many-cards
/// page.
let manyCardsApi() =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok(manyCardsPageJson 6)
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// One commit by another user, so a stranger's newer commit can sit
/// above the viewer's own.
let strangerCommitJson(id: string, hash: string, body: string, date: string) =
  datedCommitJson(id, hash, body, date)
    .Replace(
      "\"id\":\"01a151db-7fee-727b-a7ce-c911a7556242\"",
      "\"id\":\"01a151db-7fee-727b-a7ce-c911a7556243\""
    )
    .Replace("\"username\":\"octocat\"", "\"username\":\"hubot\"")
    .Replace("\"name\":\"The Octocat\"", "\"name\":\"The Hubot\"")

/// The api of the marker test: a stranger's newer commit above the
/// viewer's older one.
let strangerNewerLogApi() =
  let page =
    sprintf
      "{\"data\":[%s,%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
      (strangerCommitJson(
        "0a0a0a0a-0000-0000-0000-000000000021",
        "new1111",
        "A stranger's landing.",
        "2020-04-03T12:00:00Z"
      ))
      (datedCommitJson(
        "0a0a0a0a-0000-0000-0000-000000000022",
        "old2222",
        "The viewer's older commit.",
        "2020-04-01T12:00:00Z"
      ))

  FakeApi(
    (fun request ->
      if request.Target.StartsWith "/api/v1/log/" then
        ok page
      else
        (match accountAnswer request with
         | ValueSome answer -> answer
         | ValueNone -> ok "{}")),
    (fun _ -> Ok imageBytes)
  )

/// One commit whose author carries an avatar URL, so the rail loads an
/// image over the initial disc.
let avatarCommitJson =
  commitJson.Replace(
    "\"name\":\"The Octocat\",\"avatar_url\":null",
    "\"name\":\"The Octocat\",\"avatar_url\":\"https://dev-null.sh/commit-images/018f4c1e-face-7c3a-9b2d-5e6f7a8b9c0d\""
  )

/// One log page whose single commit's author carries an avatar.
let avatarLogPageJson =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
    avatarCommitJson

/// The api of the avatar test: the boot log answers the avatar page.
let avatarLogApi() =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok avatarLogPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// One poll option as the API returns it. The server sends null counts
/// and shares until the viewer votes, owns the poll, or the poll closes.
[<Struct>]
type PollOptionShape = {
  Id: string
  Label: string
  Votes: int option
  Voted: bool
  Percentage: int option
}

/// One poll as the API returns it.
[<Struct>]
type PollShape = {
  ClosesAt: DateTimeOffset option
  IsClosed: bool
  HasVoted: bool
  TotalVotes: int option
  Options: PollOptionShape list
}

let pollOptionJson(option: PollOptionShape) : string =
  let count = option.Votes |> Option.map string |> Option.defaultValue "null"

  let share =
    option.Percentage |> Option.map string |> Option.defaultValue "null"

  sprintf
    "{\"id\":\"%s\",\"label\":\"%s\",\"votes_count\":%s,\"voted\":%b,\"percentage\":%s}"
    option.Id
    option.Label
    count
    option.Voted
    share

let pollJson(poll: PollShape) : string =
  let closes =
    match poll.ClosesAt with
    | Some at -> "\"" + at.ToString("o") + "\""
    | None -> "null"

  let total = poll.TotalVotes |> Option.map string |> Option.defaultValue "null"

  let options = poll.Options |> List.map pollOptionJson |> String.concat ","

  sprintf
    "{\"id\":\"018f4c1e-poll-7c3a-9b2d-5e6f7a8b9c0d\",\"closes_at\":%s,\"is_closed\":%b,\"has_voted\":%b,\"total_votes\":%s,\"options\":[%s]}"
    closes
    poll.IsClosed
    poll.HasVoted
    total
    options

/// The moment a canned poll closes: two days and an hour from the test
/// start, so the card shows "2d left" with headroom.
let pollClosesAt = DateTimeOffset.UtcNow.AddDays 2.0 + TimeSpan.FromHours 1.0

/// An open poll the viewer did not vote in: the server hides the counts.
let openPoll: PollShape = {
  ClosesAt = Some pollClosesAt
  IsClosed = false
  HasVoted = false
  TotalVotes = None
  Options = [
    {
      Id = "o1"
      Label = "Yes"
      Votes = None
      Voted = false
      Percentage = None
    }
    {
      Id = "o2"
      Label = "No"
      Votes = None
      Voted = false
      Percentage = None
    }
  ]
}

/// The same poll after the viewer took the first option.
let votedPoll: PollShape = {
  openPoll with
      HasVoted = true
      TotalVotes = Some 4
      Options = [
        {
          Id = "o1"
          Label = "Yes"
          Votes = Some 3
          Voted = true
          Percentage = Some 75
        }
        {
          Id = "o2"
          Label = "No"
          Votes = Some 1
          Voted = false
          Percentage = Some 25
        }
      ]
}

/// One poll that closed with its final counts.
let closedPoll: PollShape = {
  ClosesAt = Some(DateTimeOffset.UtcNow.AddDays -1.0)
  IsClosed = true
  HasVoted = false
  TotalVotes = Some 12
  Options = [
    {
      Id = "o1"
      Label = "Yes"
      Votes = Some 9
      Voted = false
      Percentage = Some 75
    }
    {
      Id = "o2"
      Label = "No"
      Votes = Some 3
      Voted = false
      Percentage = Some 25
    }
  ]
}

/// The viewer's own poll: the server reveals the counts, the card shows
/// the results without a vote.
let ownPoll: PollShape = {
  ClosesAt = Some pollClosesAt
  IsClosed = false
  HasVoted = false
  TotalVotes = Some 0
  Options = [
    {
      Id = "o1"
      Label = "Yes"
      Votes = Some 0
      Voted = false
      Percentage = Some 0
    }
    {
      Id = "o2"
      Label = "No"
      Votes = Some 0
      Voted = false
      Percentage = Some 0
    }
  ]
}

/// A poll whose commit is not approved yet: no clock has started.
let pendingPoll: PollShape = { openPoll with ClosesAt = None }

/// A poll whose close time passed while the page sat open.
let expiredPoll: PollShape = {
  openPoll with
      ClosesAt = Some(DateTimeOffset.UtcNow.AddMinutes -1.0)
}

let withPoll(commit: string, poll: PollShape) : string =
  commit.Replace(
    "\"starred\":false",
    "\"starred\":false,\"poll\":" + pollJson poll
  )

let strangerPollCommit(poll: PollShape) : string =
  withPoll(
    strangerCommitJson(
      "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d",
      "b8b9c0d",
      "It was DNS.",
      "2020-04-03T13:00:00Z"
    ),
    poll
  )

let ownPollCommit(poll: PollShape) : string = withPoll(ownCommitJson, poll)

/// The api of a poll page: the boot log answers the commit, and the
/// account answers the shell's aside.
let pollLogApiWith(commit: string) : FakeApi =
  let page =
    sprintf
      "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
      commit

  FakeApi(
    (fun request ->
      if request.Target.StartsWith "/api/v1/log/" then
        ok page
      else
        (match accountAnswer request with
         | ValueSome answer -> answer
         | ValueNone -> ok "{}")),
    (fun _ -> Ok imageBytes)
  )

/// The api of the voting test: the boot log answers the open poll, and
/// a vote answers the poll with the viewer's vote in.
let voteApiWith(initialCommit: string, votedCommit: string) : FakeApi =
  let page =
    sprintf
      "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
      initialCommit

  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok page
    elif request.Method = "POST" && request.Target.EndsWith "/poll/votes" then
      ok(sprintf "{\"data\":%s}" votedCommit)
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The api of the voting test over a stranger's poll.
let voteApi() =
  voteApiWith(strangerPollCommit openPoll, strangerPollCommit votedPoll)

/// One notification row as the inbox returns it.
let notificationJson =
  sprintf
    """{"id":"01a15752-d17c-7065-b076-99e5bdad596d","type":"replied","subject_id":null,"reason":null,"read_at":null,"created_at":"2026-10-20T04:17:19Z","actor":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},"actors":[{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null}],"group_id":"01a15751-bf69-7261-a486-e524bcb4d8e8","event_count":1,"actors_count":1,"unread_count":0,"commit":%s}"""
    commitJson

/// The inbox page.
let notificationsJson =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"filters\":{\"type\":\"all\"},\"read_at\":\"2026-10-20T04:20:00Z\"}"
    notificationJson

/// One grouped row: three starred events by two actors.
let groupedNotificationJson =
  sprintf
    """{"id":"01a15752-aaaa-7065-b076-99e5bdad596d","type":"starred","subject_id":null,"reason":null,"read_at":null,"created_at":"2026-10-20T05:17:19Z","actor":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},"actors":[{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},{"id":"01a151db-7fee-727b-a7ce-c911a7556243","username":"hubot","name":"The Hubot","avatar_url":null}],"group_id":"01a15751-bbbb-7261-a486-e524bcb4d8e8","event_count":3,"actors_count":2,"unread_count":0,"commit":%s}"""
    commitJson

/// The grouped inbox: one plain reply and one starred group.
let groupedInboxJson =
  sprintf
    "{\"data\":[%s,%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"filters\":{\"type\":\"all\"},\"read_at\":\"2026-10-20T04:20:00Z\"}"
    notificationJson
    groupedNotificationJson

/// One event inside a group: a starred notification with its own id
/// and commit body.
let groupEventJson(id: string, body: string) =
  let commit =
    commitJson
      .Replace(
        "\"id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\"",
        $"\"id\":\"{id}\""
      )
      .Replace("\"body\":\"It was DNS.\"", $"\"body\":\"{body}\"")

  sprintf
    """{"id":"%s","type":"starred","subject_id":null,"reason":null,"read_at":null,"created_at":"2026-10-20T05:17:19Z","actor":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},"actors":[{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null}],"group_id":"01a15751-bbbb-7261-a486-e524bcb4d8e8","event_count":1,"actors_count":1,"unread_count":0,"commit":%s}"""
    id
    commit

/// The group's events, as GET /api/v1/notifications/{id}/group
/// returns them.
let groupPageJson =
  sprintf
    "{\"data\":[%s,%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
    (groupEventJson("018f4c1e-1111-7c3a-9b2d-5e6f7a8b9c0d", "Starred once."))
    (groupEventJson("018f4c1e-2222-7c3a-9b2d-5e6f7a8b9c0d", "Starred again."))

/// The api of the inbox tests: the boot log, the grouped inbox, and
/// the group endpoint all answer from cans.
let groupedInboxApi() =
  FakeApi(fun request ->
    if request.Target.Contains "/group" then
      ok groupPageJson
    elif request.Target.StartsWith "/api/v1/notifications" then
      ok groupedInboxJson
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The api of the profile stats test: the boot log and the carrying
/// profile answer from cans.
let statsProfileApi() =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif request.Target = "/api/v1/users/octocat" then
      ok profileWithStatsJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The api of the account pages: the inbox, the profile, the saved
/// list, and the account all answer from cans.
let accountApi() =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/notifications" then
      ok notificationsJson
    elif request.Target = "/api/v1/users/octocat" then
      ok profileJson
    elif
      request.Target.StartsWith "/api/v1/users/octocat/commits"
      || request.Target.StartsWith "/api/v1/starred"
    then
      ok(
        sprintf
          "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
          commitJson
      )
    elif request.Target = "/api/v1/me" then
      ok meJson
    elif
      request.Method = "GET"
      && request.Target.StartsWith "/api/v1/commits/"
      && request.Target.Contains "/replies"
    then
      ok repliesPageJson
    elif
      request.Method = "GET" && request.Target.StartsWith "/api/v1/commits/"
    then
      ok threadPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// A reply that rides the feed card's preview.
let previewReplyJson =
  """{"id":"018f4c1e-8888-7c3a-9b2d-5e6f7a8b9c0d","short_hash":"e2f3a4b","body":"Preview reply one.","body_segments":[],"preview_segments":null,"mentions":[],"parent_id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","quoted_commit_id":null,"html_url":"https://dev-null.sh/@octocat/018f4c1e-8888-7c3a-9b2d-5e6f7a8b9c0d","created_at":"2026-10-20T04:21:19Z","stars_count":0,"replies_count":0,"status":"approved","is_pinned":false,"pasteis_count":0,"starred":false,"author":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},"images":[]}"""

/// A second preview reply.
let previewReplyTwoJson =
  """{"id":"018f4c1e-8889-7c3a-9b2d-5e6f7a8b9c0d","short_hash":"e2f3a4c","body":"Preview reply two.","body_segments":[],"preview_segments":null,"mentions":[],"parent_id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","quoted_commit_id":null,"html_url":"https://dev-null.sh/@octocat/018f4c1e-8889-7c3a-9b2d-5e6f7a8b9c0d","created_at":"2026-10-20T04:22:19Z","stars_count":0,"replies_count":0,"status":"approved","is_pinned":false,"pasteis_count":0,"starred":false,"author":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},"images":[]}"""

/// One reply the preview walk pulls in.
let expandedReplyJson(body: string, id: string) =
  sprintf
    """{"id":"%s","short_hash":"f4a5b6c","body":"%s","body_segments":[],"preview_segments":null,"mentions":[],"parent_id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","quoted_commit_id":null,"html_url":"https://dev-null.sh/@octocat/%s","created_at":"2026-10-20T04:23:19Z","stars_count":0,"replies_count":0,"status":"approved","is_pinned":false,"pasteis_count":0,"starred":false,"author":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},"images":[]}"""
    id
    body
    id

/// A reply nested under the feed preview's first reply, so the chain
/// nests one step deeper.
let feedNestedReplyJson =
  """{"id":"018f4c1e-6668-7c3a-9b2d-5e6f7a8b9c0d","short_hash":"c3d4e5f","body":"Nested in the feed.","body_segments":[],"preview_segments":null,"mentions":[],"parent_id":"018f4c1e-8888-7c3a-9b2d-5e6f7a8b9c0d","quoted_commit_id":null,"html_url":"https://dev-null.sh/@octocat/018f4c1e-6668-7c3a-9b2d-5e6f7a8b9c0d","created_at":"2026-10-20T04:26:19Z","stars_count":0,"replies_count":0,"status":"approved","is_pinned":false,"pasteis_count":0,"starred":false,"author":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},"images":[]}"""

/// The preview's first reply, carrying its own nested preview.
let previewReplyNestedJson =
  previewReplyJson.Replace(
    "\"replies_count\":0",
    "\"replies_count\":1,\"reply_preview\":{\"data\":["
    + feedNestedReplyJson
    + "],\"next_cursor\":null}"
  )

/// The first commit carrying a reply preview: two replies shown, three
/// behind the walk.
let previewCommitJson =
  commitJson.Replace(
    "\"replies_count\":3",
    "\"replies_count\":5,\"reply_preview\":{\"data\":["
    + previewReplyNestedJson
    + ","
    + previewReplyTwoJson
    + "],\"next_cursor\":\"next\"}"
  )

/// A reply-less commit as the API really sends it: the preview rides
/// along empty.
let quietCommitJson =
  commitJson
    .Replace(
      "\"id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\"",
      "\"id\":\"018f4c1e-7770-7c3a-9b2d-5e6f7a8b9c0d\""
    )
    .Replace("\"short_hash\":\"b8b9c0d\"", "\"short_hash\":\"a0b1c2d\"")
    .Replace("\"body\":\"It was DNS.\"", "\"body\":\"Quiet commit.\"")
    .Replace(
      "\"replies_count\":3",
      "\"replies_count\":0,\"reply_preview\":{\"data\":[],\"next_cursor\":null}"
    )

/// The log page: one commit with replies behind its preview, and one
/// reply-less commit whose preview rides empty.
let previewLogPageJson =
  sprintf
    "{\"data\":[%s,%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
    previewCommitJson
    quietCommitJson

/// The newest replies page whole, the answer to the preview walk.
let expandedRepliesPageJson =
  let items =
    [
      previewReplyJson
      previewReplyTwoJson
      expandedReplyJson(
        "Expanded reply one.",
        "018f4c1e-8890-7c3a-9b2d-5e6f7a8b9c0d"
      )
      expandedReplyJson(
        "Expanded reply two.",
        "018f4c1e-8891-7c3a-9b2d-5e6f7a8b9c0d"
      )
      expandedReplyJson(
        "Expanded reply three.",
        "018f4c1e-8892-7c3a-9b2d-5e6f7a8b9c0d"
      )
    ]
    |> String.concat ","

  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
    items

/// The api of the preview tests: the log carries a commit with a reply
/// preview, and the replies endpoint answers the newest page whole.
let previewLogApi() =
  FakeApi(fun request ->
    if
      request.Method = "GET"
      && request.Target.StartsWith
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/replies"
    then
      ok expandedRepliesPageJson
    elif request.Target.StartsWith "/api/v1/log/" then
      ok previewLogPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// A nested reply, one depth under the thread's root reply.
let nestedReplyJson =
  """{"id":"018f4c1e-6666-7c3a-9b2d-5e6f7a8b9c0d","short_hash":"a1b2c3d","body":"Nested under the root.","body_segments":[],"preview_segments":null,"mentions":[],"parent_id":"018f4c1e-7777-7c3a-9b2d-5e6f7a8b9c0d","quoted_commit_id":null,"html_url":"https://dev-null.sh/@octocat/018f4c1e-6666-7c3a-9b2d-5e6f7a8b9c0d","created_at":"2026-10-20T04:24:19Z","stars_count":0,"replies_count":0,"status":"approved","is_pinned":false,"pasteis_count":0,"starred":false,"author":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},"images":[]}"""

/// The reply the node walk pulls in under the root.
let deeperReplyJson =
  """{"id":"018f4c1e-5555-7c3a-9b2d-5e6f7a8b9c0d","short_hash":"b2c3d4e","body":"Deeper reply walked in.","body_segments":[],"preview_segments":null,"mentions":[],"parent_id":"018f4c1e-7777-7c3a-9b2d-5e6f7a8b9c0d","quoted_commit_id":null,"html_url":"https://dev-null.sh/@octocat/018f4c1e-5555-7c3a-9b2d-5e6f7a8b9c0d","created_at":"2026-10-20T04:25:19Z","stars_count":0,"replies_count":0,"status":"approved","is_pinned":false,"pasteis_count":0,"starred":false,"author":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},"images":[]}"""

/// The thread's root reply nesting a preview of its own.
let threadedReplyJson =
  replyJson.Replace(
    "\"replies_count\":0",
    "\"replies_count\":2,\"reply_preview\":{\"data\":["
    + nestedReplyJson
    + "],\"next_cursor\":null}"
  )

/// The replies page of the thread, its root reply nesting a preview.
let threadedRepliesPageJson =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
    threadedReplyJson

/// The api of the threaded rail tests: the root reply nests a preview,
/// and its own replies endpoint answers the deeper reply.
let threadedApi() =
  FakeApi(fun request ->
    if
      request.Method = "GET"
      && request.Target.StartsWith
        "/api/v1/commits/018f4c1e-7777-7c3a-9b2d-5e6f7a8b9c0d/replies"
    then
      ok(
        sprintf
          "{\"data\":[%s,%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
          nestedReplyJson
          deeperReplyJson
      )
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif
      request.Method = "GET"
      && request.Target.StartsWith
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/replies"
    then
      ok threadedRepliesPageJson
    elif
      request.Target.StartsWith
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
    then
      ok threadPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The first commit whose body mentions the author, for the hover
/// card over the handle. It also carries a link preview.
let mentionCommitJson =
  commitJson
    .Replace(
      "\"body\":\"It was DNS.\"",
      "\"body\":\"Thanks @octocat, it was DNS.\""
    )
    .Replace(
      "\"mentions\":[]",
      "\"mentions\":[{\"username\":\"octocat\",\"offset\":7,\"length\":8}]"
    )
    .Replace(
      "\"quoted_commit_id\":null,",
      "\"quoted_commit_id\":null,\"link_preview\":{\"url\":\"https://example.test/postmortem\",\"title\":\"The postmortem\",\"is_pending\":false},"
    )

/// The log page whose one commit carries a mention.
let mentionLogPageJson =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
    mentionCommitJson

/// The api of the mention tests.
let mentionLogApi() =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok mentionLogPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The first commit quoting the second one inline, so the quoted card
/// rides under the body.
let quotedCommitJson =
  commitJson.Replace(
    "\"quoted_commit_id\":null,",
    "\"quoted_commit_id\":\"018f4c1e-9999-7c3a-9b2d-5e6f7a8b9c0d\",\"quoted_commit\":"
    + secondCommitJson
    + ","
  )

/// The log page whose one commit quotes the second commit.
let quotedLogPageJson =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
    quotedCommitJson

/// The thread of the quoted commit itself.
let quotedThreadPageJson =
  sprintf "{\"data\":%s,\"ancestors\":[]}" secondCommitJson

/// The api of the quoted-card tests: the log carries a commit that
/// quotes the second one, and both commits answer as threads.
let quotedLogApi() =
  FakeApi(fun request ->
    if
      request.Target.StartsWith
        "/api/v1/commits/018f4c1e-9999-7c3a-9b2d-5e6f7a8b9c0d/replies"
    then
      ok
        "{\"data\":[],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
    elif
      request.Target.StartsWith
        "/api/v1/commits/018f4c1e-9999-7c3a-9b2d-5e6f7a8b9c0d"
    then
      ok quotedThreadPageJson
    elif request.Target.StartsWith "/api/v1/log/" then
      ok quotedLogPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))
