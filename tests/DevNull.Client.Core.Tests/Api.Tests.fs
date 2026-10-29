module Tests.Api

open System
open Expecto
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open Tests.TestSupport

let request: Signing.Request = {
  KeyFile = "unused"
  Method = "post"
  Target = "/api/v1/commits"
  Body = "{\"body\":\"It was DNS.\"}"
}

let header name (headers: (string * string) list) =
  headers |> List.tryFind(fun (key, _) -> key = name) |> Option.map snd

let tests =
  testList "Api.signedRequest" [
    testAsync "signs the v1 message and builds the headers" {
      let signer =
        signerWith(fun signed ->
          Expect.equal "devnull-api" signed.Namespace "namespace"

          Expect.equal
            "1760000000\n00112233445566778899aabbccddeeff\nPOST\n/api/v1/commits\n{\"body\":\"It was DNS.\"}"
            signed.Payload
            "signed message"

          Ok "c2lnbmF0dXJl")

      let env =
        createSignEnv
          (FixedClock 1760000000L)
          (FixedNonceSource "00112233445566778899aabbccddeeff")
          signer

      let! (result: Result<HttpRequest, TestError>) =
        DevNull.Client.Core.Api.Signing.signedRequest(env, request)

      match result with
      | Ok signed ->
        Expect.equal "POST" signed.Method "method"
        Expect.equal "/api/v1/commits" signed.Target "target"
        Expect.equal "{\"body\":\"It was DNS.\"}" (bodyText signed) "body"

        Expect.equal
          (Some "1760000000")
          (header "X-Devnull-Timestamp" signed.Headers)
          "timestamp header"

        Expect.equal
          (Some "00112233445566778899aabbccddeeff")
          (header "X-Devnull-Nonce" signed.Headers)
          "nonce header"

        Expect.equal
          (Some "c2lnbmF0dXJl")
          (header "X-SSH-Signature" signed.Headers)
          "signature header"

        Expect.equal
          (Some "application/json")
          (header "Content-Type" signed.Headers)
          "content type"

        Expect.equal
          (Some DevNull.Client.Core.Http.userAgent)
          (header "User-Agent" signed.Headers)
          "the agent header"
      | Error error -> failtestf "%A" error
    }

    testAsync "keeps the query in the signed message" {
      let queryRequest = {
        request with
            Method = "GET"
            Target = "/api/v1/log/latest?cursor=abc"
            Body = ""
      }

      let signer =
        signerWith(fun signed ->
          Expect.equal
            "1760000000\n00112233445566778899aabbccddeeff\nGET\n/api/v1/log/latest?cursor=abc\n"
            signed.Payload
            "signed message"

          Ok "c2lnbmF0dXJl")

      let env =
        createSignEnv
          (FixedClock 1760000000L)
          (FixedNonceSource "00112233445566778899aabbccddeeff")
          signer

      let! (result: Result<HttpRequest, TestError>) =
        DevNull.Client.Core.Api.Signing.signedRequest(env, queryRequest)

      match result with
      | Ok signed ->
        Expect.equal "GET" signed.Method "method"
        Expect.equal "/api/v1/log/latest?cursor=abc" signed.Target "target"
        Expect.equal "" (bodyText signed) "body"
      | Error error -> failtestf "%A" error
    }

    testAsync "a malformed nonce fails before signing" {
      let signer = signerWith(fun _ -> failtest "the signer must not run")

      let env =
        createSignEnv
          (FixedClock 1760000000L)
          (FixedNonceSource "not-a-nonce")
          signer

      let! (result: Result<HttpRequest, TestError>) =
        DevNull.Client.Core.Api.Signing.signedRequest(env, request)

      match result with
      | Error(MalformedNonce raw) ->
        Expect.equal "not-a-nonce" raw "the raw nonce travels"
      | _ -> failtest "expected MalformedNonce"
    }

    testAsync "a signing failure surfaces the ssh-keygen message" {
      let signer =
        signerWith(fun _ ->
          Error {
            Message = "load pubkey: invalid format"
          })

      let env =
        createSignEnv
          (FixedClock 1760000000L)
          (FixedNonceSource "00112233445566778899aabbccddeeff")
          signer

      let! (result: Result<HttpRequest, TestError>) =
        DevNull.Client.Core.Api.Signing.signedRequest(env, request)

      match result with
      | Error(SigningFailed message) ->
        Expect.equal "load pubkey: invalid format" message "stderr travels"
      | _ -> failtest "expected SigningFailed"
    }
  ]

let deviceRequest: Device.AuthorizationRequest = {
  BaseAddress = "https://dev-null.sh/"
  Fingerprint = "SHA256:abc+/="
  DeviceName = "devnull on Windows"
  RedirectUri = "devnull://auth/callback"
}

let deviceTests =
  testList "Api.deviceAuthorization" [
    test "builds the browser authorization URL" {
      let url =
        DevNull.Client.Core.Api.Device.authorizationUrl(
          deviceRequest,
          "challenge",
          "state"
        )

      Expect.equal
        "https://dev-null.sh/mobile/authorize?fingerprint=SHA256%3Aabc%2B%2F%3D&device_name=devnull%20on%20Windows&code_challenge=challenge&code_challenge_method=S256&state=state&redirect_uri=devnull%3A%2F%2Fauth%2Fcallback"
        url
        "url"
    }

    test "generates the verifier, state, challenge and URL" {
      let env =
        createAuthorizationEnv
          (FixedNonceSource "00112233445566778899aabbccddeeff")
          (FixedPkceSource "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")

      let authorization =
        DevNull.Client.Core.Api.Device.beginAuthorization(env, deviceRequest)

      Expect.equal
        "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"
        authorization.Verifier
        "verifier"

      Expect.equal
        "00112233445566778899aabbccddeeff"
        authorization.State
        "state"

      Expect.equal
        "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"
        authorization.Challenge
        "challenge"

      Expect.equal
        "https://dev-null.sh/mobile/authorize?fingerprint=SHA256%3Aabc%2B%2F%3D&device_name=devnull%20on%20Windows&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256&state=00112233445566778899aabbccddeeff&redirect_uri=devnull%3A%2F%2Fauth%2Fcallback"
        authorization.Url
        "url"
    }
  ]

let redeemRequest: Device.RedeemRequest = {
  KeyFile = "key-file"
  Code = "the-code"
  Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"
}

let redeemBody =
  """{"code":"the-code","code_verifier":"dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"}"""

let redeemEnv (http: IHttp) (signer: ISigner) =
  createEnv {
    Clock = FixedClock 1760000000L
    NonceSource = FixedNonceSource "00112233445566778899aabbccddeeff"
    Signer = signer
    Http = http
  }

let envFor(http: IHttp) =
  redeemEnv http (FixedSigner(Ok "signature"))

let meJson =
  """{"data":{"id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","handle":"octocat","name":"The Octocat","avatar_url":"https://avatars.example/octocat.png","bio":"Builds things.","company":"GitHub","location":"San Francisco","website":"https://example.test","github_url":"https://github.com/octocat","html_url":"https://dev-null.sh/@octocat","feed_url":"https://dev-null.sh/@octocat/feed.atom","created_at":"2026-01-01T00:00:00Z","commits_count":7,"followers_count":11,"following_count":13,"lane":3,"pasteis_mode":true,"pasteis_remaining":7,"pasteis_resets_at":"2026-10-20T00:00:00Z","pasteis_received":12,"preferred_lane":null,"automatic_lane":4,"sponsors_url":null,"github_synced_at":null,"terms_version":"20261001","terms_accepted_at":"2026-10-02T00:00:00Z","terms_accepted":true,"remotes":null},"unread_notifications_count":3}"""

/// The same account before the terms were accepted.
let termsPendingJson =
  meJson.Replace(
    "\"terms_accepted_at\":\"2026-10-02T00:00:00Z\",\"terms_accepted\":true",
    "\"terms_accepted_at\":null,\"terms_accepted\":false"
  )

let redeemDeviceTests =
  testList "Api.redeemDevice" [
    testAsync "redeems the code and returns the signed-in user" {
      let response = meJson

      let http = CapturingHttp(ok response)

      let signer =
        signerWith(fun signed ->
          Expect.equal "devnull-api" signed.Namespace "namespace"

          Expect.equal
            (sprintf
              "1760000000\n00112233445566778899aabbccddeeff\nPOST\n/api/v1/devices\n%s"
              redeemBody)
            signed.Payload
            "signed message"

          Ok "c2lnbmF0dXJl")

      let env = redeemEnv http signer

      let! (result: Result<Me, TestError>) = Device.redeem(env, redeemRequest)

      match result with
      | Ok session ->
        Expect.equal
          "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
          session.Profile.User.Id
          "id"

        Expect.equal "octocat" session.Profile.User.Handle "username"
        Expect.equal "The Octocat" session.Profile.User.Name "name"

        Expect.equal
          (Some "https://avatars.example/octocat.png")
          session.Profile.User.AvatarUrl
          "avatar"

        Expect.equal 3 session.UnreadNotificationsCount "unread count"

        let sent = http.Requests |> List.exactlyOne
        Expect.equal "POST" sent.Method "method"
        Expect.equal "/api/v1/devices" sent.Target "target"
        Expect.equal redeemBody (bodyText sent) "body"

        Expect.equal
          (Some "1760000000")
          (header "X-Devnull-Timestamp" sent.Headers)
          "timestamp header"

        Expect.equal
          (Some "00112233445566778899aabbccddeeff")
          (header "X-Devnull-Nonce" sent.Headers)
          "nonce header"

        Expect.equal
          (Some "c2lnbmF0dXJl")
          (header "X-SSH-Signature" sent.Headers)
          "signature header"

        Expect.equal
          (Some "application/json")
          (header "Content-Type" sent.Headers)
          "content type"
      | Error error -> failtestf "%A" error
    }

    testAsync "a refused code surfaces the server code and message" {
      let response =
        """{"message":"This sign-in has expired or was already used. Sign in again in the app.","code":"invalid_grant"}"""

      let http = CapturingHttp(refused 400 response)
      let env = redeemEnv http (FixedSigner(Ok "signature"))

      let! (result: Result<Me, TestError>) = Device.redeem(env, redeemRequest)

      match result with
      | Error(Refused(400, Some InvalidGrant, message)) ->
        Expect.equal
          "This sign-in has expired or was already used. Sign in again in the app."
          message
          "message"
      | _ -> failtest "expected Refused with invalid_grant"
    }

    testAsync "an unknown error code stays raw" {
      let http =
        CapturingHttp(
          refused 503 """{"message":"Upstream failed.","code":"new_code"}"""
        )

      let env = redeemEnv http (FixedSigner(Ok "signature"))

      let! (result: Result<Me, TestError>) = Device.redeem(env, redeemRequest)

      match result with
      | Error(Refused(503, Some(Unknown "new_code"), "Upstream failed.")) -> ()
      | _ -> failtest "expected Refused with an unknown code"
    }

    testAsync "an error body that is not JSON keeps the status and raw body" {
      let http = CapturingHttp(refused 500 "upstream failed")
      let env = redeemEnv http (FixedSigner(Ok "signature"))

      let! (result: Result<Me, TestError>) = Device.redeem(env, redeemRequest)

      match result with
      | Error(Refused(500, None, "upstream failed")) -> ()
      | _ -> failtest "expected Refused with the raw body"
    }

    testAsync "a success answer that is not JSON is a decode failure" {
      let http = CapturingHttp(ok "<html>bad gateway</html>")
      let env = redeemEnv http (FixedSigner(Ok "signature"))

      let! (result: Result<Me, TestError>) = Device.redeem(env, redeemRequest)

      match result with
      | Error(DecodeFailed failure) -> Expect.equal "$" failure.Path "path"
      | _ -> failtest "expected DecodeFailed"
    }

    testAsync "a missing user name reports its path" {
      let http =
        CapturingHttp(
          ok
            """{"data":{"id":"user-id","handle":"octocat"},"unread_notifications_count":0}"""
        )

      let env = redeemEnv http (FixedSigner(Ok "signature"))

      let! (result: Result<Me, TestError>) = Device.redeem(env, redeemRequest)

      match result with
      | Error(DecodeFailed failure) -> Expect.equal failure.Path "name" "path"
      | _ -> failtest "expected DecodeFailed"
    }

    testAsync "a transport failure surfaces its message" {
      let http = CapturingHttp(Error { Message = "connection refused" })

      let env = redeemEnv http (FixedSigner(Ok "signature"))

      let! (result: Result<Me, TestError>) = Device.redeem(env, redeemRequest)

      match result with
      | Error(TransportFailed message) ->
        Expect.equal "connection refused" message "message"
      | _ -> failtest "expected TransportFailed"
    }
  ]

let commitJson =
  """{"id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","short_hash":"b8b9c0d","body":"It was DNS.","body_segments":[],"preview_segments":null,"mentions":[],"parent_id":null,"quoted_commit_id":null,"html_url":"https://dev-null.sh/@octocat/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","created_at":"2026-10-20T04:17:19Z","stars_count":2,"replies_count":1,"status":"approved","is_pinned":false,"pasteis_count":0,"author":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","handle":"octocat","name":"The Octocat","avatar_url":"https://avatars.example/octocat.png"},"images":[{"id":"018f4c1e-aaaa-7c3a-9b2d-5e6f7a8b9c0d","width":1600,"height":900,"description":"diagram","url":"https://dev-null.sh/commit-images/018f4c1e-aaaa-7c3a-9b2d-5e6f7a8b9c0d"}]}"""

let createdResponse = sprintf "{\"data\":%s}" commitJson

let pageResponse =
  sprintf "{\"data\":%s,\"ancestors\":[%s]}" commitJson commitJson

let commitRequest: Commits.CreateRequest = {
  KeyFile = "key-file"
  Body = "It was DNS."
  Target = Commits.New
  Images = []
  ImageDescriptions = []
  Poll = None
}

let commitApiTests =
  testList "Api.commits" [
    testAsync "creates a commit and decodes the response" {
      let http = CapturingHttp(ok createdResponse)

      let signer =
        signerWith(fun signed ->
          Expect.equal "devnull-api" signed.Namespace "namespace"

          Expect.equal
            "1760000000\n00112233445566778899aabbccddeeff\nPOST\n/api/v1/commits\n{\"body\":\"It was DNS.\"}"
            signed.Payload
            "signed message"

          Ok "signature")

      let env = redeemEnv http signer

      let! (result: Result<Commit, TestError>) =
        Commits.create(env, commitRequest)

      match result with
      | Ok commit ->
        Expect.equal "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d" commit.Id "id"

        Expect.equal "b8b9c0d" commit.ShortHash "short hash"
        Expect.equal "It was DNS." commit.Body "body"
        Expect.equal None commit.ParentId "parent"
        Expect.equal (Some 2) commit.StarsCount "stars"
        Expect.equal (Some 1) commit.RepliesCount "replies"
        Expect.equal Approved commit.Status "status"
        Expect.equal "octocat" commit.Author.Handle "author"

        let image = commit.Images |> List.exactlyOne
        Expect.equal 1600 image.Width "image width"
        Expect.equal (Some "diagram") image.Description "image description"

        let sent = http.Requests |> List.exactlyOne
        Expect.equal "POST" sent.Method "method"
        Expect.equal "/api/v1/commits" sent.Target "target"
        Expect.equal "{\"body\":\"It was DNS.\"}" (bodyText sent) "body"
      | Error error -> failtestf "%A" error
    }

    testAsync "creates a reply with the parent id" {
      let http = CapturingHttp(ok createdResponse)

      let signer =
        signerWith(fun signed ->
          Expect.equal
            "1760000000\n00112233445566778899aabbccddeeff\nPOST\n/api/v1/commits\n{\"body\":\"It was DNS.\",\"parent_id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\"}"
            signed.Payload
            "signed message"

          Ok "signature")

      let env = redeemEnv http signer

      let request = {
        commitRequest with
            Target = Commits.ReplyTo "018F4C1E-6F8A-7C3A-9B2D-5E6F7A8B9C0D"
      }

      let! (result: Result<Commit, TestError>) = Commits.create(env, request)

      match result with
      | Ok _ ->
        let sent = http.Requests |> List.exactlyOne

        Expect.equal
          "{\"body\":\"It was DNS.\",\"parent_id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\"}"
          (bodyText sent)
          "body"
      | Error error -> failtestf "%A" error
    }

    testAsync "creates a quoted commit" {
      let http = CapturingHttp(ok createdResponse)

      let request = {
        commitRequest with
            Target = Commits.Quote "018F4C1E-6F8A-7C3A-9B2D-5E6F7A8B9C0D"
      }

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, request)

      match result with
      | Ok _ ->
        let sent = http.Requests |> List.exactlyOne

        Expect.equal
          "{\"body\":\"It was DNS.\",\"quoted_commit_id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\"}"
          (bodyText sent)
          "body"
      | Error error -> failtestf "%A" error
    }

    testAsync "a commit without counts reports them as absent" {
      let json =
        commitJson.Replace("\"stars_count\":2,\"replies_count\":1,", "")

      let http = CapturingHttp(ok(sprintf "{\"data\":%s}" json))

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, commitRequest)

      match result with
      | Ok commit ->
        Expect.equal None commit.StarsCount "stars"
        Expect.equal None commit.RepliesCount "replies"
      | Error error -> failtestf "%A" error
    }

    testAsync "a validation error without a code keeps its message" {
      let http =
        CapturingHttp(
          refused
            422
            """{"message":"The body field is required.","errors":{"body":["The body field is required."]}}"""
        )

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, commitRequest)

      match result with
      | Error(Refused(422, None, message)) ->
        Expect.equal "The body field is required." message "message"
      | _ -> failtest "expected a code-less refusal"
    }

    testAsync "a bad nested object is a decode failure" {
      let json =
        commitJson.Replace(
          "\"parent_id\":null,",
          "\"parent_id\":null,\"parent\":{},"
        )

      let http = CapturingHttp(ok(sprintf "{\"data\":%s}" json))

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, commitRequest)

      match result with
      | Error(DecodeFailed _) -> ()
      | _ -> failtest "expected DecodeFailed"
    }

    testAsync "shows a commit with its ancestors" {
      let http = CapturingHttp(ok pageResponse)

      let signer =
        signerWith(fun signed ->
          Expect.equal
            "1760000000\n00112233445566778899aabbccddeeff\nGET\n/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\n"
            signed.Payload
            "signed message"

          Ok "signature")

      let env = redeemEnv http signer

      let request: Commits.ShowRequest = {
        KeyFile = "key-file"
        CommitId = "018F4C1E-6F8A-7C3A-9B2D-5E6F7A8B9C0D"
      }

      let! (result: Result<CommitPage, TestError>) = Commits.show(env, request)

      match result with
      | Ok page ->
        Expect.equal "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d" page.Commit.Id "id"

        let ancestor =
          page.Ancestors
          |> List.exactlyOne
          |> function
            | LiveCommit commit -> commit
            | Tombstone _ -> failtest "expected a live ancestor"

        Expect.equal ancestor.Id page.Commit.Id "ancestor id"

        let sent = http.Requests |> List.exactlyOne
        Expect.equal "GET" sent.Method "method"

        Expect.equal
          "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
          sent.Target
          "target"

        Expect.equal "" (bodyText sent) "body"
      | Error error -> failtestf "%A" error
    }

    testAsync "a missing commit surfaces the server code" {
      let http =
        CapturingHttp(
          refused
            404
            """{"message":"This commit isn't available.","code":"commit_unavailable"}"""
        )

      let env = redeemEnv http (FixedSigner(Ok "signature"))

      let request: Commits.ShowRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
      }

      let! (result: Result<CommitPage, TestError>) = Commits.show(env, request)

      match result with
      | Error(Refused(404,
                      Some CommitUnavailable,
                      "This commit isn't available.")) -> ()
      | _ -> failtest "expected Refused with commit_unavailable"
    }

    testAsync "a malformed commit answer is a decode failure" {
      let http = CapturingHttp(ok "not json")
      let env = redeemEnv http (FixedSigner(Ok "signature"))

      let! (result: Result<Commit, TestError>) =
        Commits.create(env, commitRequest)

      match result with
      | Error(DecodeFailed failure) -> Expect.equal "$" failure.Path "path"
      | _ -> failtest "expected DecodeFailed"
    }

    testAsync "a missing commit field reports its path" {
      let http =
        CapturingHttp(
          ok """{"data":{"id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"}}"""
        )

      let env = redeemEnv http (FixedSigner(Ok "signature"))

      let! (result: Result<Commit, TestError>) =
        Commits.create(env, commitRequest)

      match result with
      | Error(DecodeFailed failure) ->
        Expect.equal failure.Path "short_hash" "path"
      | _ -> failtest "expected DecodeFailed"
    }
  ]

let apiUserJson =
  """{"id":"01a151db-7fee-727b-a7ce-c911a7556242","handle":"octocat","name":"The Octocat","avatar_url":"https://avatars.example/octocat.png","bio":"Builds things.","company":"GitHub","location":"San Francisco","website":"https://example.test","github_url":"https://github.com/octocat","html_url":"https://dev-null.sh/@octocat","feed_url":"https://dev-null.sh/@octocat/feed.atom","created_at":"2026-01-01T00:00:00Z","commits_count":7,"followers_count":11,"following_count":13,"lane":3}"""

let sessionJson = meJson

let pageOf(items: string) =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":\"next\",\"prev_cursor\":null}}"
    items

let profileResponse =
  sprintf
    """{"data":%s,"is_following":false,"follows_you":true,"is_muting":false,"pasteis_received":7,"activity":{"scale":"day","buckets":[{"date":"2026-10-19T13:00:00Z","count":2}]},"streaks":{"longest":5,"current":3},"achievements":{"level":1,"rank":"user","xp":120,"next_level_xp":150,"level_progress":80,"xp_breakdown":{"commits":{"count":10,"multiplier":10,"xp":100},"streak":{"count":0,"multiplier":15,"xp":0},"stars_received":{"count":4,"multiplier":5,"xp":20},"replies":{"count":0,"multiplier":5,"xp":0},"stars_given":{"count":0,"multiplier":2,"xp":0},"luddite":{"count":0,"multiplier":-100,"xp":0}},"ranks":[{"rank":"user","level":1,"min_xp":0,"description":"Just here.","achieved":true}],"badges":[{"id":"init","name":"Init","description":"Joined.","icon":"*","unlocked":true,"progress":10,"target":1}]},"pinned_commit":%s}"""
    apiUserJson
    commitJson

let logResponse =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":\"next\",\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":4,\"suggestions\":[]}"
    commitJson

let notificationJson =
  sprintf
    """{"id":"01a15752-d17c-7065-b076-99e5bdad596d","type":"replied","subject_id":null,"reason":null,"read_at":null,"created_at":"2026-10-20T04:17:19Z","actor":%s,"group_id":"01a15751-bf69-7261-a486-e524bcb4d8e8","event_count":1,"actors_count":1,"unread_count":0,"commit":%s}"""
    apiUserJson
    commitJson

let notificationResponse =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"filters\":{\"type\":\"all\"},\"read_at\":\"2026-10-20T04:20:00Z\"}"
    notificationJson

let starResponse = sprintf "{\"data\":%s}" commitJson

let muteResponse = sprintf "{\"data\":%s,\"is_muting\":true}" apiUserJson

let blockResponse = sprintf "{\"data\":%s,\"is_blocking\":true}" apiUserJson

let expectRequest
  (name: string)
  (call: IHttp -> Async<Result<'value, TestError>>)
  (method: string)
  (target: string)
  =
  let http = CapturingHttp(ok "{}")
  call http |> Async.RunSynchronously |> ignore
  let sent = http.Requests |> List.exactlyOne
  Expect.equal method sent.Method $"{name} method"
  Expect.equal target sent.Target $"{name} target"

let tombstoneJson =
  """{"id":"018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d","parent_id":null,"deleted_at":"2026-10-20T04:20:00Z","replies_count":2}"""

let richCommitJson =
  """{"id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","short_hash":"b8b9c0d","body":"It was DNS.\n```fsharp\nlet x = 1\n```","parent_id":"018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d","html_url":"https://dev-null.sh/@octocat/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","created_at":"2026-10-20T04:17:19Z","stars_count":2,"replies_count":1,"status":"pending","author":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","handle":"octocat","name":"The Octocat","avatar_url":null},"images":[],"body_segments":[{"type":"text","text":"It was DNS."},{"type":"code","code":"let x = 1"},{"type":"block","language":"fsharp","html":"<span>let x = 1</span>"}],"preview_segments":[{"type":"text","text":"It was DNS."}],"mentions":[{"handle":"octocat","offset":0,"length":2}],"is_pinned":true,"starred":true,"pasteis_count":5,"quoted_commit_id":"018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d","parent":{"id":"018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d","parent_id":null,"deleted_at":"2026-10-20T04:20:00Z","replies_count":2},"quoted_commit":{"id":"018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d","parent_id":null,"deleted_at":"2026-10-20T04:20:00Z","replies_count":2},"poll":{"id":"018f4c1e-cccc-7c3a-9b2d-5e6f7a8b9c0d","closes_at":null,"is_closed":false,"has_voted":true,"total_votes":3,"options":[{"id":"018f4c1e-dddd-7c3a-9b2d-5e6f7a8b9c0d","label":"Yes","votes_count":2,"voted":true,"percentage":67}]},"link_preview":{"url":"https://example.test","title":"Example","description":null,"image_url":null,"site_name":"Example","is_pending":false,"embed":{"provider":"youtube","kind":"video","src":"https://youtube.com/embed/x","aspect_ratio":"16 / 9","height":null,"poster":null}},"reply_preview":{"data":[{"id":"018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d","parent_id":null,"deleted_at":"2026-10-20T04:20:00Z","replies_count":2}],"next_cursor":"abc"}}"""

let richCommitTests =
  testList "Api.richCommits" [
    testAsync "decodes the full commit shape" {
      let http = CapturingHttp(ok(sprintf "{\"data\":%s}" richCommitJson))

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, commitRequest)

      match result with
      | Ok commit ->
        Expect.equal 3 commit.BodySegments.Length "segments"

        match commit.BodySegments with
        | [ TextSegment text; CodeSegment code; BlockSegment(language, html) ] ->
          Expect.equal "It was DNS." text "text"
          Expect.equal "let x = 1" code "code"
          Expect.equal (Some "fsharp") language "language"
          Expect.equal "<span>let x = 1</span>" html "html"
        | _ -> failtest "expected three segments"

        Expect.equal
          (Some 1)
          (commit.PreviewSegments |> Option.map List.length)
          "preview"

        Expect.equal 1 commit.Mentions.Length "mentions"
        Expect.equal true commit.IsPinned "pinned"
        Expect.equal 5 commit.PasteisCount "pasteis"
        Expect.equal (Some true) commit.Starred "starred"

        match commit.Parent with
        | Some(Tombstone tombstone) ->
          Expect.equal (Some 2) tombstone.RepliesCount "parent replies"
        | _ -> failtest "expected a tombstone parent"

        Expect.equal true commit.QuotedCommit.IsSome "quoted"

        match commit.Poll with
        | Some poll ->
          Expect.equal (Some 3) poll.TotalVotes "total votes"
          let option = poll.Options |> List.exactlyOne
          Expect.equal "Yes" option.Label "label"
          Expect.equal (Some 2) option.VotesCount "votes"
          Expect.equal (Some 67) option.Percentage "percentage"
        | None -> failtest "expected a poll"

        match commit.LinkPreview with
        | Some link ->
          Expect.equal "Example" (link.Title |> Option.defaultValue "") "title"

          match link.Embed with
          | Some embed -> Expect.equal "youtube" embed.Provider "provider"
          | None -> failtest "expected an embed"
        | None -> failtest "expected a link preview"

        match commit.ReplyPreview with
        | Some preview ->
          Expect.equal (Some "abc") preview.NextCursor "next cursor"
          Expect.equal 1 preview.Data.Length "reply data"
        | None -> failtest "expected a reply preview"
      | Error error -> failtestf "%A" error
    }

    testAsync "decodes a tombstone page" {
      let http = CapturingHttp(ok(pageOf tombstoneJson))

      let request: Commits.RepliesRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Newest = false
        Cursor = None
      }

      let! (result: Result<Page<CommitNode>, TestError>) =
        Commits.replies(envFor http, request)

      match result with
      | Ok page ->
        match page.Items |> List.exactlyOne with
        | Tombstone tombstone ->
          Expect.equal "018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d" tombstone.Id "id"

          Expect.equal (Some 2) tombstone.RepliesCount "replies"

          Expect.equal
            (Some(DateTimeOffset(2026, 10, 20, 4, 20, 0, TimeSpan.Zero)))
            (Some tombstone.DeletedAt)
            "deleted"
        | LiveCommit _ -> failtest "expected a tombstone"
      | Error error -> failtestf "%A" error
    }
  ]

let devicesListJson =
  """{"data":[{"id":"018f4c1e-dev0-7c3a-9b2d-5e6f7a8b9c0d","name":"This Fedora desktop","fingerprint":"SHA256:OwnDeviceFingerprint0123456789012345","approved_at":"2026-10-01T00:00:00Z","last_used_at":"2026-10-28T00:00:00Z"},{"id":"018f4c1e-dev1-7c3a-9b2d-5e6f7a8b9c0d","name":"A Mac laptop","fingerprint":"SHA256:AnotherDeviceFingerprint0123456789012","approved_at":"2026-09-01T00:00:00Z","last_used_at":null}]}"""

let devicesTests =
  testList "Api.devices" [
    testAsync "reads the approved devices" {
      let http = CapturingHttp(ok devicesListJson)

      let! (result: Result<Device list, TestError>) =
        Devices.list(envFor http, { KeyFile = "key-file" })

      match result with
      | Ok devices ->
        Expect.equal 2 devices.Length "count"

        let own = List.head devices

        Expect.equal "This Fedora desktop" own.Name "name"

        Expect.equal
          (Some(DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero)))
          own.LastUsedAt
          "last used"

        let other = List.item 1 devices

        Expect.equal "A Mac laptop" other.Name "name"
        Expect.equal None other.LastUsedAt "never used"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "GET" sent.Method "method"
      Expect.equal "/api/v1/me/devices" sent.Target "target"
    }

    testAsync "revokes one device" {
      expectRequest
        "revoke device"
        (fun http ->
          Devices.revoke(
            envFor http,
            {
              KeyFile = "key-file"
              DeviceId = "018f4c1e-dev1-7c3a-9b2d-5e6f7a8b9c0d"
            }
          ))
        "DELETE"
        "/api/v1/me/devices/018f4c1e-dev1-7c3a-9b2d-5e6f7a8b9c0d"
    }
  ]

let accountApiTests =
  testList "Api.account" [
    testAsync "reads the current user" {
      let http = CapturingHttp(ok sessionJson)

      let! (result: Result<Me, TestError>) =
        Me.get(envFor http, { KeyFile = "key-file" })

      match result with
      | Ok session ->
        Expect.equal "octocat" session.Profile.User.Handle "username"
        Expect.equal 3 session.UnreadNotificationsCount "unread"
        Expect.equal true session.PasteisMode "pasteis mode"
        Expect.equal 7 session.PasteisRemaining "pasteis remaining"
        Expect.equal 12 session.PasteisReceived "pasteis received"
        Expect.equal 4 session.AutomaticLane "automatic lane"
        Expect.equal None session.PreferredLane "preferred lane"
        Expect.equal None session.SponsorsUrl "sponsors"
        Expect.equal "20261001" session.TermsVersion "terms version"
        Expect.equal true session.TermsAccepted "terms accepted"

        Expect.equal
          (Some(DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero)))
          session.TermsAcceptedAt
          "terms accepted at"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "GET" sent.Method "method"
      Expect.equal "/api/v1/me" sent.Target "target"
    }

    testAsync "reads the terms as not accepted" {
      let http = CapturingHttp(ok termsPendingJson)

      let! (result: Result<Me, TestError>) =
        Me.get(envFor http, { KeyFile = "key-file" })

      match result with
      | Ok session ->
        Expect.equal "20261001" session.TermsVersion "terms version"
        Expect.equal false session.TermsAccepted "terms accepted"
        Expect.equal None session.TermsAcceptedAt "terms accepted at"
      | Error error -> failtestf "%A" error
    }

    testAsync "accepts the terms" {
      let http = CapturingHttp(ok meJson)

      let request: Me.TermsRequest = {
        KeyFile = "key-file"
        Version = "20261001"
      }

      let! (result: Result<Me, TestError>) =
        Me.acceptTerms(envFor http, request)

      match result with
      | Ok session -> Expect.equal true session.TermsAccepted "terms accepted"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "PUT" sent.Method "method"
      Expect.equal "/api/v1/me/terms" sent.Target "target"
      Expect.equal """{"version":"20261001"}""" (bodyText sent) "body"
    }

    testAsync "turns pasteis mode on" {
      let http = CapturingHttp(ok sessionJson)

      let request: Me.PasteisModeRequest = {
        KeyFile = "key-file"
        Enabled = true
      }

      let! (result: Result<Me, TestError>) =
        Me.setPasteisMode(envFor http, request)

      match result with
      | Ok _ -> ()
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "PUT" sent.Method "method"
      Expect.equal "/api/v1/me/pasteis-mode" sent.Target "target"
      Expect.equal """{"enabled":true}""" (bodyText sent) "body"
    }

    testAsync "sets a custom lane" {
      let http = CapturingHttp(ok sessionJson)

      let request: Me.LaneRequest = {
        KeyFile = "key-file"
        Lane = Custom "#1a2b3c"
      }

      let! (result: Result<Me, TestError>) = Me.setLane(envFor http, request)

      match result with
      | Ok _ -> ()
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "PUT" sent.Method "method"
      Expect.equal "/api/v1/me/lane" sent.Target "target"
      Expect.equal """{"lane":null,"color":"#1a2b3c"}""" (bodyText sent) "body"
    }

    testAsync "an out-of-range lane index fails before the request" {
      let http = CapturingHttp(ok sessionJson)

      let request: Me.LaneRequest = {
        KeyFile = "key-file"
        Lane = Palette 6
      }

      let! (result: Result<Me, TestError>) = Me.setLane(envFor http, request)

      match result with
      | Error(InvalidLaneIndex 6) -> ()
      | _ -> failtest "expected InvalidLaneIndex"

      Expect.equal [] http.Requests "no request"
    }

    testAsync "an invalid lane color fails before the request" {
      let http = CapturingHttp(ok sessionJson)

      let request: Me.LaneRequest = {
        KeyFile = "key-file"
        Lane = Custom "#ABC"
      }

      let! (result: Result<Me, TestError>) = Me.setLane(envFor http, request)

      match result with
      | Error(InvalidLaneColor "#ABC") -> ()
      | _ -> failtest "expected InvalidLaneColor"

      Expect.equal [] http.Requests "no request"
    }

    testAsync "reads a profile" {
      let http = CapturingHttp(ok profileResponse)

      let request: Users.Request = {
        KeyFile = "key-file"
        Username = "octo cat"
      }

      let! (result: Result<ProfileDetails, TestError>) =
        Users.get(envFor http, request)

      match result with
      | Ok details ->
        Expect.equal "octocat" details.Profile.User.Handle "username"
        Expect.equal (Some(LaneIndex 3)) details.Profile.Lane "lane"
        Expect.equal false details.IsFollowing "is following"
        Expect.equal true details.FollowsYou "follows you"
        Expect.equal 7 details.PasteisReceived "pasteis"
        Expect.equal true details.Activity.IsSome "activity"
        Expect.equal (Some 3) (details.Streaks |> Option.map _.Current) "streak"

        Expect.equal
          (Some 1)
          (details.Achievements |> Option.map _.Level)
          "achievement level"

        Expect.equal
          (Some(Some 150))
          (details.Achievements |> Option.map _.NextLevelXp)
          "next level xp"

        Expect.equal true details.PinnedCommit.IsSome "pinned"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "/api/v1/users/octo%20cat" sent.Target "target"
    }

    testAsync "a root profile with no next level decodes" {
      let response =
        profileResponse.Replace(
          "\"next_level_xp\":150",
          "\"next_level_xp\":null"
        )

      let http = CapturingHttp(ok response)

      let request: Users.Request = {
        KeyFile = "key-file"
        Username = "octocat"
      }

      let! (result: Result<ProfileDetails, TestError>) =
        Users.get(envFor http, request)

      match result with
      | Ok details ->
        Expect.equal
          (Some None)
          (details.Achievements |> Option.map _.NextLevelXp)
          "next level xp"
      | Error error -> failtestf "%A" error
    }
  ]

let listApiTests =
  testList "Api.lists" [
    testAsync "reads user commits with the replies filter" {
      let http = CapturingHttp(ok(pageOf commitJson))

      let request: Users.CommitsRequest = {
        KeyFile = "key-file"
        Username = "octocat"
        Replies = true
        Cursor = Some "abc"
      }

      let! (result: Result<Page<CommitNode>, TestError>) =
        Users.commits(envFor http, request)

      match result with
      | Ok page ->
        Expect.equal 1 page.Items.Length "items"
        Expect.equal (Some "next") page.NextCursor "next"
        Expect.equal 20 page.PerPage "per page"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne

      Expect.equal
        "/api/v1/users/octocat/commits?replies=1&cursor=abc"
        sent.Target
        "target"
    }

    testAsync "reads a log with since and cursor" {
      let http = CapturingHttp(ok logResponse)

      let request: Logs.Request = {
        KeyFile = "key-file"
        Log = Latest
        Since = Some "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Cursor = Some "abc"
      }

      let! (result: Result<LogPage, TestError>) = Logs.get(envFor http, request)

      match result with
      | Ok page ->
        Expect.equal Latest page.Log "log"
        Expect.equal false page.IsRanked "ranked"
        Expect.equal (Some 4) page.NewCommitsCount "new commits"
        Expect.equal 1 page.Page.Items.Length "items"
        Expect.equal [] page.Suggestions "suggestions"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne

      Expect.equal
        "/api/v1/log/latest?since=018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d&cursor=abc"
        sent.Target
        "target"
    }

    testAsync "an invalid since fails before the request" {
      let http = CapturingHttp(ok logResponse)

      let request: Logs.Request = {
        KeyFile = "key-file"
        Log = Latest
        Since = Some "not-a-uuid"
        Cursor = None
      }

      let! (result: Result<LogPage, TestError>) = Logs.get(envFor http, request)

      match result with
      | Error(InvalidCommitId "not-a-uuid") -> ()
      | _ -> failtest "expected InvalidCommitId"

      Expect.equal [] http.Requests "no request"
    }

    testAsync "reads replies newest first" {
      let http = CapturingHttp(ok(pageOf commitJson))

      let request: Commits.RepliesRequest = {
        KeyFile = "key-file"
        CommitId = "018F4C1E-6F8A-7C3A-9B2D-5E6F7A8B9C0D"
        Newest = true
        Cursor = None
      }

      let! (result: Result<Page<CommitNode>, TestError>) =
        Commits.replies(envFor http, request)

      match result with
      | Ok page -> Expect.equal 1 page.Items.Length "items"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne

      Expect.equal
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/replies?newest=1"
        sent.Target
        "target"
    }

    testAsync "reads the notification inbox" {
      let http = CapturingHttp(ok notificationResponse)

      let request: Notifications.ListRequest = {
        KeyFile = "key-file"
        Filter = Some "all"
        Through = Some(DateTimeOffset(2026, 10, 20, 4, 20, 0, TimeSpan.Zero))
        Cursor = Some "abc"
      }

      let! (result: Result<NotificationPage, TestError>) =
        Notifications.list(envFor http, request)

      match result with
      | Ok page ->
        Expect.equal "all" page.FilterType "filter"
        Expect.equal 1 page.Page.Items.Length "items"

        let item = page.Page.Items |> List.exactlyOne
        Expect.equal "replied" item.Type "type"
        Expect.equal true item.Commit.IsSome "commit"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne

      let expected =
        "/api/v1/notifications?type=all&through="
        + Uri.EscapeDataString "2026-10-20T04:20:00Z"
        + "&cursor=abc"

      Expect.equal expected sent.Target "target"
    }

    testAsync "a malformed node is a decode failure" {
      let response =
        """{"data":[{"id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"}],"links":{},"meta":{"per_page":20,"next_cursor":null,"prev_cursor":null}}"""

      let http = CapturingHttp(ok response)

      let request: Commits.RepliesRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Newest = false
        Cursor = None
      }

      let! (result: Result<Page<CommitNode>, TestError>) =
        Commits.replies(envFor http, request)

      match result with
      | Error(DecodeFailed _) -> ()
      | _ -> failtest "expected DecodeFailed"
    }
  ]

let actionApiTests =
  testList "Api.actions" [
    testAsync "deletes the account" {
      let http = CapturingHttp(ok "")

      let! (result: Result<unit, TestError>) =
        Me.delete(envFor http, { KeyFile = "key-file" })

      match result with
      | Ok() -> ()
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "DELETE" sent.Method "method"
      Expect.equal "/api/v1/me" sent.Target "target"
    }

    testAsync "reports a commit" {
      let http = CapturingHttp(ok "")

      let request: Commits.ReportRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Reason = Spam
        Details = Some "repeat"
      }

      let! (result: Result<unit, TestError>) =
        Commits.report(envFor http, request)

      match result with
      | Ok() -> ()
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "POST" sent.Method "method"

      Expect.equal
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/reports"
        sent.Target
        "target"

      Expect.equal
        """{"reason":"spam","details":"repeat"}"""
        (bodyText sent)
        "body"
    }

    testAsync
      "report details over five hundred characters fail before the request" {
      let http = CapturingHttp(ok "")

      let request: Commits.ReportRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Reason = Spam
        Details = Some(String.replicate 501 "x")
      }

      let! (result: Result<unit, TestError>) =
        Commits.report(envFor http, request)

      match result with
      | Error(ReportDetailsTooLong 501) -> ()
      | _ -> failtest "expected ReportDetailsTooLong"

      Expect.equal [] http.Requests "no request"
    }

    testAsync "stars a commit" {
      let http = CapturingHttp(ok starResponse)

      let request: Stars.ActionRequest = {
        KeyFile = "key-file"
        CommitId = "018F4C1E-6F8A-7C3A-9B2D-5E6F7A8B9C0D"
      }

      let! (result: Result<Commit, TestError>) =
        Stars.star(envFor http, request)

      match result with
      | Ok commit ->
        Expect.equal "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d" commit.Id "id"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "POST" sent.Method "method"

      Expect.equal
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/star"
        sent.Target
        "target"
    }

    testAsync "mutes a user" {
      let http = CapturingHttp(ok muteResponse)

      let request: Users.Request = {
        KeyFile = "key-file"
        Username = "octocat"
      }

      let! (result: Result<MuteResult, TestError>) =
        Users.mute(envFor http, request)

      match result with
      | Ok result ->
        Expect.equal true result.IsMuting "muting"
        Expect.equal "octocat" result.User.Handle "username"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "POST" sent.Method "method"
      Expect.equal "/api/v1/users/octocat/mute" sent.Target "target"
    }

    testAsync "blocks a user" {
      let http = CapturingHttp(ok blockResponse)

      let request: Users.Request = {
        KeyFile = "key-file"
        Username = "octocat"
      }

      let! (result: Result<BlockResult, TestError>) =
        Users.block(envFor http, request)

      match result with
      | Ok result ->
        Expect.equal true result.IsBlocking "blocking"
        Expect.equal "octocat" result.User.Handle "username"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "POST" sent.Method "method"
      Expect.equal "/api/v1/users/octocat/block" sent.Target "target"
    }

    testAsync "follows a user" {
      let http = CapturingHttp(ok "")

      let request: Users.Request = {
        KeyFile = "key-file"
        Username = "octocat"
      }

      let! (result: Result<unit, TestError>) =
        Users.follow(envFor http, request)

      match result with
      | Ok() -> ()
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "POST" sent.Method "method"
      Expect.equal "/api/v1/users/octocat/follow" sent.Target "target"
    }

    testAsync "restores a deleted commit within its window" {
      let uuid = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

      let commitRequest: Commits.ActionRequest = {
        KeyFile = "key-file"
        CommitId = uuid
      }

      let http = CapturingHttp(ok(sprintf """{"data":%s}""" commitJson))

      let! (result: Result<Commit, TestError>) =
        Commits.restore(envFor http, commitRequest)

      match result with
      | Ok commit -> Expect.equal "It was DNS." commit.Body "body"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "POST" sent.Method "method"

      Expect.equal
        ("/api/v1/commits/" + uuid + "/restoration")
        sent.Target
        "target"
    }

    test "sends the expected methods and paths for the remaining actions" {
      let uuid = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

      let commitRequest: Commits.ActionRequest = {
        KeyFile = "key-file"
        CommitId = uuid
      }

      let starAction: Stars.ActionRequest = {
        KeyFile = "key-file"
        CommitId = uuid
      }

      let pastelAction: Pasteis.ActionRequest = {
        KeyFile = "key-file"
        CommitId = uuid
      }

      let userRequest: Users.Request = {
        KeyFile = "key-file"
        Username = "octocat"
      }

      let starredRequest: Stars.ListRequest = {
        KeyFile = "key-file"
        Cursor = Some "abc"
      }

      let cursorRequest: Users.CursorRequest = {
        KeyFile = "key-file"
        Cursor = Some "abc"
      }

      let groupRequest: Notifications.GroupRequest = {
        KeyFile = "key-file"
        NotificationId = "01a151db-7fee-727b-a7ce-c911a7556242"
        Filter = Some "all"
        Cursor = None
      }

      expectRequest
        "sync github"
        (fun http -> Me.syncGithub(envFor http, { KeyFile = "key-file" }))
        "PUT"
        "/api/v1/me/github"

      expectRequest
        "delete commit"
        (fun http -> Commits.delete(envFor http, commitRequest))
        "DELETE"
        ("/api/v1/commits/" + uuid)

      expectRequest
        "give pastel"
        (fun http -> Pasteis.give(envFor http, pastelAction))
        "POST"
        ("/api/v1/commits/" + uuid + "/pasteis")

      expectRequest
        "unstar commit"
        (fun http -> Stars.unstar(envFor http, starAction))
        "DELETE"
        ("/api/v1/commits/" + uuid + "/star")

      expectRequest
        "unfollow user"
        (fun http -> Users.unfollow(envFor http, userRequest))
        "DELETE"
        "/api/v1/users/octocat/follow"

      expectRequest
        "unmute user"
        (fun http -> Users.unmute(envFor http, userRequest))
        "DELETE"
        "/api/v1/users/octocat/mute"

      expectRequest
        "unblock user"
        (fun http -> Users.unblock(envFor http, userRequest))
        "DELETE"
        "/api/v1/users/octocat/block"

      expectRequest
        "list starred"
        (fun http -> Stars.index(envFor http, starredRequest))
        "GET"
        "/api/v1/starred?cursor=abc"

      expectRequest
        "list muted"
        (fun http -> Users.muted(envFor http, cursorRequest))
        "GET"
        "/api/v1/me/muted?cursor=abc"

      expectRequest
        "list blocked"
        (fun http -> Users.blocked(envFor http, cursorRequest))
        "GET"
        "/api/v1/me/blocked?cursor=abc"

      expectRequest
        "list followers"
        (fun http ->
          Users.followers(
            envFor http,
            {
              KeyFile = "key-file"
              Username = "octocat"
              Cursor = Some "abc"
            }
          ))
        "GET"
        "/api/v1/users/octocat/followers?cursor=abc"

      expectRequest
        "list following"
        (fun http ->
          Users.following(
            envFor http,
            {
              KeyFile = "key-file"
              Username = "octocat"
              Cursor = Some "abc"
            }
          ))
        "GET"
        "/api/v1/users/octocat/following?cursor=abc"

      expectRequest
        "notification group"
        (fun http -> Notifications.group(envFor http, groupRequest))
        "GET"
        "/api/v1/notifications/01a151db-7fee-727b-a7ce-c911a7556242/group?type=all"
    }
  ]

/// Minimal PNG bytes, so `CommitImages.parse` accepts the staged image.
let pngBytes: byte array = [|
  137uy
  80uy
  78uy
  71uy
  13uy
  10uy
  26uy
  10uy
  0uy
  0uy
  0uy
  0uy
|]

let stagedImage(fileName: string) : ImageAttachment = {
  FileName = fileName
  Bytes = pngBytes
}

/// The SHA-256 of `pngBytes`, in the lowercase hex the server hashes.
let pngHash =
  pngBytes
  |> System.Security.Cryptography.SHA256.HashData
  |> Convert.ToHexString
  |> _.ToLowerInvariant()

/// Reads the value a parse produced, failing the test when it refused.
let parsed<'a>(outcome: Result<'a, TestError>) : 'a =
  match outcome with
  | Ok value -> value
  | Error error -> failtestf "%A" error

let imageCommitTests =
  testList "Api.image commits" [
    testAsync "signs the images message under the images namespace" {
      let signer =
        signerWith(fun signed ->
          Expect.equal "devnull-api-images" signed.Namespace "namespace"

          Ok "signature")

      let env =
        createSignEnv
          (FixedClock 1760000000L)
          (FixedNonceSource "00112233445566778899aabbccddeeff")
          signer

      let imageRequest: Signing.ImageRequest = {
        KeyFile = "key-file"
        Method = "POST"
        Target = "/api/v1/commits"
        Body = "{\"body\":\"It was DNS.\"}"
        Images =
          parsed(
            DevNull.Client.Core.Commit.CommitImages.parse [
              stagedImage "a.png"
            ]
          )
      }

      let! (result: Result<HttpRequest, TestError>) =
        Signing.signedImageRequest(env, imageRequest)

      match result with
      | Ok signed ->
        Expect.equal "POST" signed.Method "method"

        let form = multipartForm signed

        let names = form.Files |> List.map _.Name
        Expect.equal [ "payload"; "images[]" ] names "part names"

        let payload = form.Files |> List.head
        Expect.equal "application/json" payload.ContentType "payload type"

        Expect.equal
          "{\"body\":\"It was DNS.\"}"
          (System.Text.Encoding.UTF8.GetString payload.Content)
          "payload json"

        Expect.isEmpty form.Fields "the multipart carries no fields"
      | Error error -> failtestf "%A" error
    }

    testAsync "a commit with images posts as multipart v1" {
      let http = CapturingHttp(ok createdResponse)

      let signer =
        signerWith(fun signed ->
          Expect.equal "devnull-api-images" signed.Namespace "namespace"

          Expect.equal
            $"1760000000\n00112233445566778899aabbccddeeff\nPOST\n/api/v1/commits\n%s{pngHash}\n{{\"body\":\"It was DNS.\",\"parent_id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\"}}"
            signed.Payload
            "signed message"

          Ok "signature")

      let request = {
        commitRequest with
            Target = Commits.ReplyTo "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
            Images = [ stagedImage "diagram.png" ]
      }

      let! (result: Result<Commit, TestError>) =
        Commits.create(redeemEnv http signer, request)

      match result with
      | Ok _ ->
        let sent = http.Requests |> List.exactlyOne

        let form = multipartForm sent
        Expect.equal 2 (List.length form.Files) "the payload and one image"

        let payload = form.Files |> List.head
        Expect.equal "payload" payload.Name "payload part"

        Expect.equal
          "{\"body\":\"It was DNS.\",\"parent_id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\"}"
          (System.Text.Encoding.UTF8.GetString payload.Content)
          "payload json"

        let image = form.Files |> List.item 1
        Expect.equal "images[]" image.Name "image part"
        Expect.equal "diagram.png" image.FileName "image name"
      | Error error -> failtestf "%A" error
    }

    testAsync "a commit with no images posts as plain signed json" {
      let http = CapturingHttp(ok createdResponse)

      let signer =
        signerWith(fun signed ->
          Expect.equal "devnull-api" signed.Namespace "namespace"
          Ok "signature")

      let! (result: Result<Commit, TestError>) =
        Commits.create(redeemEnv http signer, commitRequest)

      match result with
      | Ok _ ->
        let sent = http.Requests |> List.exactlyOne
        Expect.equal "{\"body\":\"It was DNS.\"}" (bodyText sent) "body"
      | Error error -> failtestf "%A" error
    }

    testAsync "a described image post carries its description" {
      let http = CapturingHttp(ok createdResponse)

      let request = {
        commitRequest with
            Images = [ stagedImage "diagram.png" ]
            ImageDescriptions = [ Some "a diagram" ]
      }

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, request)

      match result with
      | Ok _ ->
        let sent = http.Requests |> List.exactlyOne
        let form = multipartForm sent

        let payload = form.Files |> List.find(fun file -> file.Name = "payload")

        Expect.equal
          "{\"body\":\"It was DNS.\",\"image_descriptions\":[\"a diagram\"]}"
          (System.Text.Encoding.UTF8.GetString payload.Content)
          "payload json"
      | Error error -> failtestf "%A" error
    }

    testAsync "a partly described post pads one null per undescribed image" {
      let http = CapturingHttp(ok createdResponse)

      let request = {
        commitRequest with
            Images = [ stagedImage "one.png"; stagedImage "two.png" ]
            ImageDescriptions = [ Some "only the first" ]
      }

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, request)

      match result with
      | Ok _ ->
        let sent = http.Requests |> List.exactlyOne
        let form = multipartForm sent

        let payload = form.Files |> List.find(fun file -> file.Name = "payload")

        Expect.equal
          "{\"body\":\"It was DNS.\",\"image_descriptions\":[\"only the first\",null]}"
          (System.Text.Encoding.UTF8.GetString payload.Content)
          "payload json"
      | Error error -> failtestf "%A" error
    }

    testAsync "an over-long description fails before the request" {
      let http = CapturingHttp(ok createdResponse)

      let request = {
        commitRequest with
            Images = [ stagedImage "diagram.png" ]
            ImageDescriptions = [ Some(String.replicate 1001 "x") ]
      }

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, request)

      match result with
      | Error(DescriptionTooLong(0, 1001)) ->
        Expect.isEmpty http.Requests "no request left"
      | other -> failtestf "expected the description bound, got %A" other
    }

    testAsync "more descriptions than images fails before the request" {
      let http = CapturingHttp(ok createdResponse)

      let request = {
        commitRequest with
            Images = [ stagedImage "diagram.png" ]
            ImageDescriptions = [ Some "one"; Some "two" ]
      }

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, request)

      match result with
      | Error(TooManyDescriptions 2) ->
        Expect.isEmpty http.Requests "no request left"
      | other -> failtestf "expected the description count, got %A" other
    }
  ]

let pollTests =
  testList "Api.polls" [
    testAsync "parses a poll of two to four distinct options" {
      let spec = parsed(PollSpecs.parse([ "Yes"; "No" ], OneDay))

      Expect.equal [ "Yes"; "No" ] (PollSpecs.options spec) "options"

      Expect.equal 1440 (PollDurations.wire(PollSpecs.duration spec)) "duration"
    }

    testAsync "refuses fewer than two options" {
      let parsedOutcome: Result<PollSpec, TestError> =
        PollSpecs.parse([ "Only" ], OneHour)

      Expect.equal (Error(TooFewPollOptions 1)) parsedOutcome "too few"
    }

    testAsync "refuses more than four options" {
      let parsedOutcome: Result<PollSpec, TestError> =
        PollSpecs.parse([ "a"; "b"; "c"; "d"; "e" ], OneHour)

      Expect.equal (Error(TooManyPollOptions 5)) parsedOutcome "too many"
    }

    testAsync "refuses options that repeat ignoring case" {
      let parsedOutcome: Result<PollSpec, TestError> =
        PollSpecs.parse([ "Yes"; "yes" ], OneHour)

      Expect.equal (Error(DuplicatePollOption "yes")) parsedOutcome "duplicate"
    }

    testAsync "refuses an option past fifty characters" {
      let long = new String('x', 51)

      let parsedOutcome: Result<PollSpec, TestError> =
        PollSpecs.parse([ "Yes"; long ], OneHour)

      Expect.equal (Error(PollOptionTooLong(long, 51))) parsedOutcome "too long"
    }

    testAsync "a commit with a poll carries the poll in its json" {
      let http = CapturingHttp(ok createdResponse)

      let poll = parsed(PollSpecs.parse([ "Ship it"; "Hold" ], ThreeDays))

      let request = { commitRequest with Poll = Some poll }

      let! (_: Result<Commit, TestError>) = Commits.create(envFor http, request)

      let sent = http.Requests |> List.exactlyOne

      Expect.equal
        "{\"body\":\"It was DNS.\",\"poll\":{\"options\":[\"Ship it\",\"Hold\"],\"duration\":4320}}"
        (bodyText sent)
        "poll json"
    }

    testAsync "a poll on a reply is refused before any request" {
      let http = CapturingHttp(ok createdResponse)

      let poll = parsed(PollSpecs.parse([ "Yes"; "No" ], OneHour))

      let request = {
        commitRequest with
            Target = Commits.ReplyTo "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
            Poll = Some poll
      }

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, request)

      Expect.equal (Error(PollProhibited PollOnReply)) result "refused"
      Expect.isEmpty http.Requests "no request left the client"
    }

    testAsync "a poll on a quote is refused before any request" {
      let http = CapturingHttp(ok createdResponse)

      let poll = parsed(PollSpecs.parse([ "Yes"; "No" ], OneHour))

      let request = {
        commitRequest with
            Target = Commits.Quote "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
            Poll = Some poll
      }

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, request)

      Expect.equal (Error(PollProhibited PollOnQuote)) result "refused"
      Expect.isEmpty http.Requests "no request left the client"
    }

    testAsync "a poll with images is refused before any request" {
      let http = CapturingHttp(ok createdResponse)

      let poll = parsed(PollSpecs.parse([ "Yes"; "No" ], OneHour))

      let request = {
        commitRequest with
            Images = [ stagedImage "a.png" ]
            Poll = Some poll
      }

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, request)

      Expect.equal (Error(PollProhibited PollWithImages)) result "refused"
      Expect.isEmpty http.Requests "no request left the client"
    }

    testAsync "votes one option of a commit poll" {
      let http = CapturingHttp(ok createdResponse)

      let request: Commits.VoteRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        OptionId = "01a151db-7fee-727b-a7ce-c911a7556243"
      }

      let! (_: Result<Commit, TestError>) = Commits.vote(envFor http, request)

      let sent = http.Requests |> List.exactlyOne

      Expect.equal "POST" sent.Method "method"

      Expect.equal
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/poll/votes"
        sent.Target
        "target"

      Expect.equal
        "{\"option\":\"01a151db-7fee-727b-a7ce-c911a7556243\"}"
        (bodyText sent)
        "body"
    }

    testAsync "reads the new posting error codes" {
      Expect.equal TimedOut (ErrorCodes.parse "timed_out") "timed out"
      Expect.equal Banned (ErrorCodes.parse "banned") "banned"

      Expect.equal
        CommitRulesRequired
        (ErrorCodes.parse "commit_rules_required")
        "commit rules"

      Expect.equal
        (Unknown "later")
        (ErrorCodes.parse "later")
        "unknown stays raw"

      Expect.equal "timed_out" (ErrorCodes.wire TimedOut) "wire"
    }
  ]
