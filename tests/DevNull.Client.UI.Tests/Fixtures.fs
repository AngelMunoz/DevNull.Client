module DevNull.Client.UI.Tests.Fixtures

open Microsoft.Extensions.Logging.Abstractions
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.DeviceAuth
open DevNull.Client.Env
open DevNull.Client.SettingsStore
open DevNull.Client.Stores

/// The canned clock: one fixed unix second for every request.
type FixedClock(epoch: int64) =

  interface IClock with
    member _.UnixNow() = epoch

type FixedNonceSource(hex: string) =

  interface INonceSource with
    member _.NewHex() = hex

type FixedSigner(signature: string) =

  interface ISigner with
    member _.Sign _ = async { return Ok signature }

type FixedPkceSource(verifier: string) =

  interface IPkceSource with
    member _.NewVerifier() = verifier

/// One http worker that records every request and answers through the
/// given handler; no test reaches the network.
type FakeApi(handler: HttpRequest -> HttpResponse) =
  let requests = ResizeArray<HttpRequest>()

  new(body: string) =
    FakeApi(fun _ -> {
      Status = 200
      Headers = []
      Body = body
    })

  member _.Requests = requests |> Seq.toList

  interface IHttp with
    member _.Send(request) = async {
      requests.Add request
      return Ok(handler request)
    }

/// The app environment over fakes: fixed workers and the given store.
type TestEnv(store: SessionStore, http: IHttp, ?settings: AppSettingsStore) =

  let settingsStore =
    settings |> Option.defaultWith(fun () -> AppSettingsStore(None, ignore))

  let clock = FixedClock(1760000000L)
  let nonceSource = FixedNonceSource("00112233445566778899aabbccddeeff")
  let signer = FixedSigner("dGVzdC1zaWduYXR1cmU")

  let pkceSource =
    FixedPkceSource("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")

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

  interface IProvideSession with
    member _.Session = store

  interface IProvideSettings with
    member _.Settings = settingsStore

  interface IProvideLogging with
    member _.LoggerFactory = NullLoggerFactory.Instance

let sessionOf(username: string) : DeviceSession = {
  User = {
    Id = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
    Username = username
    Name = "The Octocat"
    AvatarUrl = None
  }
  UnreadNotificationsCount = 3
}

let signedInStore() =
  SessionStore(Some(sessionOf "octocat"), devicePrivateKeyPath, ignore)

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

/// One commit as the server returns it, with one author and one body.
let commitJson =
  """{"id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","short_hash":"b8b9c0d","body":"It was DNS.","body_segments":[],"preview_segments":null,"mentions":[],"parent_id":null,"quoted_commit_id":null,"html_url":"https://dev-null.sh/@octocat/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","created_at":"2026-10-20T04:17:19Z","stars_count":7,"replies_count":3,"status":"approved","is_pinned":false,"pasteis_count":0,"starred":false,"author":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},"images":[]}"""

/// A second commit for the next page of the log.
let secondCommitJson =
  """{"id":"018f4c1e-9999-7c3a-9b2d-5e6f7a8b9c0d","short_hash":"c0d1e2f","body":"Second commit.","body_segments":[],"preview_segments":null,"mentions":[],"parent_id":null,"quoted_commit_id":null,"html_url":"https://dev-null.sh/@octocat/018f4c1e-9999-7c3a-9b2d-5e6f7a8b9c0d","created_at":"2026-10-20T04:18:19Z","stars_count":0,"replies_count":0,"status":"approved","is_pinned":false,"pasteis_count":0,"starred":false,"author":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","username":"octocat","name":"The Octocat","avatar_url":null},"images":[]}"""

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

/// The api of the tests: log reads answer page one, then page two for
/// the cursor; a commit post answers the created commit.
let logApi() =
  FakeApi(fun request ->
    if request.Method = "POST" && request.Target = "/api/v1/commits" then
      ok(sprintf "{\"data\":%s}" commitJson)
    elif request.Target.Contains "cursor=next" then
      ok secondLogPageJson
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    else
      ok "{}")

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
      ok "{}")

/// The api of the empty-log test.
let emptyLogApi() = FakeApi(emptyLogPageJson)

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
  FakeApi(
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
  )

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
      ok "{}")

/// The signed-in account, as GET /api/v1/me returns it.
let meJson =
  """{"data":{"id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","username":"octocat","name":"The Octocat","avatar_url":null,"bio":"Builds things.","company":"GitHub","location":"San Francisco","website":"https://example.test","github_url":"https://github.com/octocat","html_url":"https://dev-null.sh/@octocat","feed_url":"https://dev-null.sh/@octocat/feed.atom","created_at":"2026-01-01T00:00:00Z","commits_count":7,"followers_count":11,"following_count":13,"lane":3,"pasteis_mode":true,"pasteis_remaining":7,"pasteis_resets_at":"2026-10-27T00:00:00Z","pasteis_received":12,"preferred_lane":null,"automatic_lane":4,"sponsors_url":null,"github_synced_at":null},"unread_notifications_count":3}"""

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

/// The author of the canned commits, as a bare user.
let apiUserJson =
  """{"id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","username":"octocat","name":"The Octocat","avatar_url":null,"bio":"Builds things.","company":"GitHub","location":"San Francisco","website":"https://example.test","github_url":"https://github.com/octocat","html_url":"https://dev-null.sh/@octocat","feed_url":"https://dev-null.sh/@octocat/feed.atom","created_at":"2026-01-01T00:00:00Z","commits_count":7,"followers_count":11,"following_count":13,"lane":3}"""

/// One profile with the viewer's relation, as GET /api/v1/users/{username} returns it.
let profileJson =
  sprintf
    """{"data":%s,"is_following":false,"follows_you":true,"is_muting":false,"pasteis_received":7,"activity":null,"streaks":null,"achievements":null,"pinned_commit":null}"""
    apiUserJson

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
    else
      ok "{}")
