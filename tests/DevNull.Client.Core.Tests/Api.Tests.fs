module Tests.Api

open System
open System.Text.RegularExpressions
open Expecto
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Completion
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types
open Tests.TestSupport

let request: Signing.Request = {
  KeyFile = "unused"
  Method = "post"
  Target = "/api/v1/commits"
  Body = "{\"body\":\"It was DNS.\"}"
  Headers = []
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
  AppName = "DevNull Client"
  AppId = "windows:sh.dev-null.client"
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
        "https://dev-null.sh/mobile/authorize?fingerprint=SHA256%3Aabc%2B%2F%3D&device_name=devnull%20on%20Windows&app_name=DevNull%20Client&app_id=windows%3Ash.dev-null.client&code_challenge=challenge&code_challenge_method=S256&state=state&redirect_uri=devnull%3A%2F%2Fauth%2Fcallback"
        url
        "url"
    }

    test "the app parameters match the wire pattern" {
      let url =
        DevNull.Client.Core.Api.Device.authorizationUrl(
          deviceRequest,
          "challenge",
          "state"
        )

      let query =
        Uri(url)
          .Query.TrimStart('?')
          .Split('&', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map(fun part ->
          match part.Split([| '=' |], 2) with
          | [| name; value |] -> name, Uri.UnescapeDataString value
          | _ -> part, "")
        |> Map.ofArray

      let appName = Map.find "app_name" query
      let appId = Map.find "app_id" query

      Expect.equal "DevNull Client" appName "app name"

      Expect.isTrue
        (Regex.IsMatch(appId, "^[a-z]+:[A-Za-z0-9._-]+$"))
        "app id shape"

      Expect.isLessThanOrEqual appName.Length 100 "app name length"
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
        "https://dev-null.sh/mobile/authorize?fingerprint=SHA256%3Aabc%2B%2F%3D&device_name=devnull%20on%20Windows&app_name=DevNull%20Client&app_id=windows%3Ash.dev-null.client&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256&state=00112233445566778899aabbccddeeff&redirect_uri=devnull%3A%2F%2Fauth%2Fcallback"
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

/// The thread read of the page fixture, carrying two participants the
/// parent's mentions reached: the rows ride the envelope in order.
let participantsResponse =
  sprintf
    "{\"data\":%s,\"ancestors\":[],\"reply_participants\":[{\"id\":\"01a15638-1111-733c-89b3-373a1a0c538a\",\"handle\":\"mona\"},{\"id\":\"01a15638-2222-733c-89b3-373a1a0c538a\",\"handle\":\"hubot\"}]}"
    commitJson

let commitRequest: Commits.CreateRequest = {
  KeyFile = "key-file"
  Body = "It was DNS."
  Target = Commits.New
  Images = []
  ImageDescriptions = []
  Poll = None
  ReplyRecipients = []
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
        Expect.equal "octocat" (Author.handle commit.Author) "author"

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
            "1760000000\n00112233445566778899aabbccddeeff\nPOST\n/api/v1/commits\n{\"body\":\"It was DNS.\",\"parent_id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\",\"reply_recipients\":[]}"
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
          "{\"body\":\"It was DNS.\",\"parent_id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\",\"reply_recipients\":[]}"
          (bodyText sent)
          "body"
      | Error error -> failtestf "%A" error
    }

    testAsync "a reply posts its recipients with the parent id" {
      let http = CapturingHttp(ok createdResponse)

      let request = {
        commitRequest with
            Target = Commits.ReplyTo "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
            ReplyRecipients = [ "01a15638-1111-733c-89b3-373a1a0c538a" ]
      }

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, request)

      match result with
      | Ok _ ->
        let sent = http.Requests |> List.exactlyOne

        Expect.equal
          "{\"body\":\"It was DNS.\",\"parent_id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\",\"reply_recipients\":[\"01a15638-1111-733c-89b3-373a1a0c538a\"]}"
          (bodyText sent)
          "body"
      | Error error -> failtestf "%A" error
    }

    testAsync "a reply past the participants bound fails before the request" {
      let http = CapturingHttp(ok createdResponse)

      let recipients = [ for index in 1..101 -> $"person-{index}" ]

      let request = {
        commitRequest with
            Target = Commits.ReplyTo "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
            ReplyRecipients = recipients
      }

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, request)

      match result with
      | Error(TooManyReplyParticipants 101) ->
        Expect.isEmpty http.Requests "no request left"
      | other -> failtestf "expected the participant bound, got %A" other
    }

    testAsync "a reply at the participant bound posts" {
      let http = CapturingHttp(ok createdResponse)

      let recipients = [ for index in 1..100 -> $"person-{index}" ]

      let request = {
        commitRequest with
            Target = Commits.ReplyTo "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
            ReplyRecipients = recipients
      }

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, request)

      match result with
      | Ok _ -> Expect.hasLength http.Requests 1 "the reply went out"
      | other -> failtestf "expected the bound to pass, got %A" other
    }

    testAsync "a recipient the body also mentions counts once" {
      let http = CapturingHttp(ok createdResponse)

      // One hundred selections, one of them the handle the body also
      // mentions: the union counts the person once and the reply goes
      // out. One more distinct mention pushes it past the bound.
      let recipients = "alice" :: [ for index in 1..99 -> $"person-{index}" ]

      let mentioned = {
        commitRequest with
            Target = Commits.ReplyTo "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
            Body = "hey @alice"
            ReplyRecipients = recipients
      }

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, mentioned)

      match result with
      | Ok _ -> Expect.hasLength http.Requests 1 "the union counted once"
      | other -> failtestf "expected the overlap to pass, got %A" other

      let distinct = { mentioned with Body = "hey @bob" }

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, distinct)

      match result with
      | Error(TooManyReplyParticipants 101) ->
        Expect.hasLength http.Requests 1 "no second request left"
      | other -> failtestf "expected the bound, got %A" other
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

    test "the thread read decodes its reply participants in order" {
      match CommitPage.decode participantsResponse with
      | Ok page ->
        Expect.equal
          [
            {
              Id = "01a15638-1111-733c-89b3-373a1a0c538a"
              Handle = "mona"
            }
            {
              Id = "01a15638-2222-733c-89b3-373a1a0c538a"
              Handle = "hubot"
            }
          ]
          page.ReplyParticipants
          "participants"
      | Error failure -> failtestf "%A" failure
    }

    test "a thread read without reply participants answers none" {
      match CommitPage.decode pageResponse with
      | Ok page -> Expect.equal [] page.ReplyParticipants "no participants"
      | Error failure -> failtestf "%A" failure
    }

    test "a body's mentions read once each, in order" {
      Expect.equal
        [ "alice"; "bob" ]
        (CommitMentions.handles(Values.body "hey @alice, meet @bob and @alice"))
        "distinct handles"
    }

    test "a body without mention triggers carries no handles" {
      Expect.equal
        []
        (CommitMentions.handles(Values.body "see #12 or write foo@bar"))
        "no handles"
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
  """{"id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","short_hash":"b8b9c0d","body":"It was DNS.\n```fsharp\nlet x = 1\n```","parent_id":"018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d","html_url":"https://dev-null.sh/@octocat/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","created_at":"2026-10-20T04:17:19Z","stars_count":2,"replies_count":1,"status":"pending","author":{"id":"01a151db-7fee-727b-a7ce-c911a7556242","handle":"octocat","name":"The Octocat","avatar_url":null},"images":[],"body_segments":[{"type":"text","text":"It was DNS."},{"type":"code","code":"let x = 1"},{"type":"block","language":"fsharp","html":"<span>let x = 1</span>"}],"preview_segments":[{"type":"text","text":"It was DNS."}],"mentions":[{"handle":"octocat","offset":0,"length":2,"kind":"user"}],"is_pinned":true,"starred":true,"pasteis_count":5,"quoted_commit_id":"018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d","parent":{"id":"018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d","parent_id":null,"deleted_at":"2026-10-20T04:20:00Z","replies_count":2},"quoted_commit":{"id":"018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d","parent_id":null,"deleted_at":"2026-10-20T04:20:00Z","replies_count":2},"poll":{"id":"018f4c1e-cccc-7c3a-9b2d-5e6f7a8b9c0d","closes_at":null,"is_closed":false,"has_voted":true,"total_votes":3,"options":[{"id":"018f4c1e-dddd-7c3a-9b2d-5e6f7a8b9c0d","label":"Yes","votes_count":2,"voted":true,"percentage":67}]},"link_preview":{"url":"https://example.test","title":"Example","description":null,"image_url":null,"site_name":"Example","is_pending":false,"embed":{"provider":"youtube","kind":"video","src":"https://youtube.com/embed/x","aspect_ratio":"16 / 9","height":null,"poster":null}},"reply_preview":{"data":[{"id":"018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d","parent_id":null,"deleted_at":"2026-10-20T04:20:00Z","replies_count":2}],"next_cursor":"abc"}}"""

/// One rich commit whose single mention carries the kind named.
let commitWithMention(kind: string) =
  richCommitJson.Replace(
    "\"mentions\":[{\"handle\":\"octocat\",\"offset\":0,\"length\":2,\"kind\":\"user\"}]",
    $"\"mentions\":[{{\"handle\":\"devnull\",\"offset\":4,\"length\":7,\"kind\":\"{kind}\"}}]"
  )

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
        | [ TextSegment text
            CodeSegment code
            BlockSegment(language, filename, html) ] ->
          Expect.equal "It was DNS." text "text"
          Expect.equal "let x = 1" code "code"
          Expect.equal (Some "fsharp") language "language"
          Expect.equal None filename "filename"
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

    testAsync "decodes a block with its filename" {
      let titled =
        richCommitJson.Replace(
          "{\"type\":\"block\",\"language\":\"fsharp\",\"html\":\"<span>let x = 1</span>\"}",
          "{\"type\":\"block\",\"language\":\"fsharp\",\"filename\":\"Program.fs\",\"html\":\"<span>let x = 1</span>\"}"
        )

      let http = CapturingHttp(ok(sprintf "{\"data\":%s}" titled))

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, commitRequest)

      match result with
      | Ok commit ->
        match commit.BodySegments with
        | [ TextSegment _; CodeSegment _; BlockSegment(language, filename, _) ] ->
          Expect.equal (Some "fsharp") language "language"
          Expect.equal (Some "Program.fs") filename "filename"
        | _ -> failtest "expected three segments"
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

    testAsync "a mention's kind rides into the decoded commit" {
      let http =
        CapturingHttp(
          ok(sprintf "{\"data\":%s}" (commitWithMention "organization"))
        )

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, commitRequest)

      match result with
      | Ok commit ->
        let mention = commit.Mentions |> List.exactlyOne
        Expect.equal "devnull" mention.Handle "handle"
        Expect.equal OrganizationMention mention.Kind "kind"
      | Error error -> failtestf "%A" error
    }

    testAsync "an unknown mention kind stays raw" {
      let http =
        CapturingHttp(ok(sprintf "{\"data\":%s}" (commitWithMention "webring")))

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, commitRequest)

      match result with
      | Ok commit ->
        let mention = commit.Mentions |> List.exactlyOne
        Expect.equal (UnknownMention "webring") mention.Kind "kind"
      | Error error -> failtestf "%A" error
    }
  ]

let orgAuthor =
  "{\"type\":\"organization\",\"handle\":\"devnull\",\"name\":\"DevNull\",\"avatar_url\":\"https://avatars.example/devnull.png\",\"bio\":\"The org.\",\"website\":\"https://dev-null.sh\",\"github_url\":null,\"html_url\":\"https://dev-null.sh/org/devnull\",\"is_followed\":false,\"can_claim\":false,\"settings_url\":null,\"lane\":null,\"followers_count\":11,\"admins\":[{\"id\":\"01a151db-7fee-727b-a7ce-c911a7556242\",\"handle\":\"octocat\",\"name\":\"The Octocat\",\"avatar_url\":null}]}"

let orgCommitJson =
  commitJson.Replace(
    "\"author\":{\"id\":\"01a151db-7fee-727b-a7ce-c911a7556242\",\"handle\":\"octocat\",\"name\":\"The Octocat\",\"avatar_url\":\"https://avatars.example/octocat.png\"}",
    "\"author\":" + orgAuthor
  )

let authorTests =
  testList "Api.authors" [
    test "a commit without a type still decodes as a person" {
      match
        CommitNode.decodePage(
          sprintf
            "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
            commitJson
        )
      with
      | Ok page ->
        match page.Items |> List.exactlyOne with
        | LiveCommit commit ->
          Expect.equal "octocat" (Author.handle commit.Author) "handle"
          Expect.isFalse (Author.isOrganization commit.Author) "person"
        | Tombstone _ -> failtest "expected a live commit"
      | Error failure -> failtestf "%A" failure
    }

    test "a commit typed user decodes as a person" {
      let typed =
        commitJson.Replace(
          "\"author\":{\"id\"",
          "\"author\":{\"type\":\"user\",\"id\""
        )

      match
        CommitNode.decodePage(
          sprintf
            "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
            typed
        )
      with
      | Ok page ->
        match page.Items |> List.exactlyOne with
        | LiveCommit commit ->
          Expect.equal "octocat" (Author.handle commit.Author) "handle"
          Expect.isFalse (Author.isOrganization commit.Author) "person"
        | Tombstone _ -> failtest "expected a live commit"
      | Error failure -> failtestf "%A" failure
    }

    test "an organization commit decodes as an organization" {
      match
        CommitNode.decodePage(
          sprintf
            "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
            orgCommitJson
        )
      with
      | Ok page ->
        match page.Items |> List.exactlyOne with
        | LiveCommit commit ->
          Expect.isTrue (Author.isOrganization commit.Author) "organization"
          Expect.equal "devnull" (Author.handle commit.Author) "handle"
          Expect.equal "DevNull" (Author.name commit.Author) "name"

          match Author.organization commit.Author with
          | Some org ->
            Expect.equal 1 org.Admins.Length "admins"
            Expect.equal "octocat" org.Admins.Head.Handle "admin handle"
          | None -> failtest "expected an organization"
        | Tombstone _ -> failtest "expected a live commit"
      | Error failure -> failtestf "%A" failure
    }
  ]

let notificationOrgJson =
  sprintf
    """{"id":"01a15752-dddd-7065-b076-99e5bdad596d","type":"replied","subject_id":null,"reason":null,"read_at":null,"created_at":"2026-10-20T04:17:19Z","actor":%s,"actors":[%s],"organization":%s,"group_id":"01a15751-cccc-7261-a486-e524bcb4d8e8","event_count":1,"actors_count":1,"unread_count":0,"commit":%s}"""
    orgAuthor
    orgAuthor
    orgAuthor
    commitJson

let notificationOrgTests =
  testList "Api.notificationOrganization" [
    test "a notification with an organization and an org actor decodes" {
      match
        Notification.decodePage(
          sprintf
            "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
            notificationOrgJson
        )
      with
      | Ok page ->
        let item = page.Items |> List.exactlyOne

        Expect.isTrue
          (Author.isOrganization item.Actor)
          "the actor is an organization"

        Expect.equal "devnull" (Author.handle item.Actor) "actor handle"
        Expect.equal 1 item.Actors.Length "actors"

        Expect.isTrue
          (item.Actors |> List.forall Author.isOrganization)
          "actors are organizations"

        match item.Organization with
        | Some org -> Expect.equal "devnull" org.Handle "organization handle"
        | None -> failtest "expected an organization context"
      | Error failure -> failtestf "%A" failure
    }

    test "a person notification carries no organization" {
      let nulled =
        notificationJson.Replace(
          "\"group_id\"",
          "\"organization\":null,\"group_id\""
        )

      match
        Notification.decodePage(
          sprintf
            "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
            nulled
        )
      with
      | Ok page ->
        let item = page.Items |> List.exactlyOne
        Expect.isNone item.Organization "no organization context"

        Expect.isFalse
          (Author.isOrganization item.Actor)
          "the actor is a person"

        Expect.equal "octocat" (Author.handle item.Actor) "actor handle"
      | Error failure -> failtestf "%A" failure
    }
  ]

let devicesListJson =
  """{"data":[{"id":"018f4c1e-dev0-7c3a-9b2d-5e6f7a8b9c0d","name":"This Fedora desktop","app_name":null,"fingerprint":"SHA256:OwnDeviceFingerprint0123456789012345","approved_at":"2026-10-01T00:00:00Z","last_used_at":"2026-10-28T00:00:00Z"},{"id":"018f4c1e-dev1-7c3a-9b2d-5e6f7a8b9c0d","name":"A Mac laptop","app_name":"DevNull Client","fingerprint":"SHA256:AnotherDeviceFingerprint0123456789012","approved_at":"2026-09-01T00:00:00Z","last_used_at":null},{"id":"018f4c1e-dev2-7c3a-9b2d-5e6f7a8b9c0d","name":"A Linux desktop","fingerprint":"SHA256:ThirdDeviceFingerprint01234567890123","approved_at":"2026-08-01T00:00:00Z","last_used_at":null}]}"""

let devicesTests =
  testList "Api.devices" [
    testAsync "reads the approved devices" {
      let http = CapturingHttp(ok devicesListJson)

      let! (result: Result<Device list, TestError>) =
        Devices.list(envFor http, { KeyFile = "key-file" })

      match result with
      | Ok devices ->
        Expect.equal 3 devices.Length "count"

        let own = List.head devices

        Expect.equal "This Fedora desktop" own.Name "name"
        Expect.equal None own.AppName "own app name"

        Expect.equal
          (Some(DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero)))
          own.LastUsedAt
          "last used"

        let other = List.item 1 devices

        Expect.equal "A Mac laptop" other.Name "name"
        Expect.equal (Some "DevNull Client") other.AppName "other app name"
        Expect.equal None other.LastUsedAt "never used"

        let missing = List.item 2 devices

        Expect.equal "A Linux desktop" missing.Name "name"
        Expect.equal None missing.AppName "missing app name"
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

    testAsync "a profile with bio profiles decodes its map" {
      let response =
        profileResponse.Replace(
          "\"lane\":3}",
          "\"lane\":3,\"bio_profiles\":{\"octocat\":\"octocat\",\"devnull\":\"DevNull\"}}"
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
          (Map.ofList [ "octocat", "octocat"; "devnull", "DevNull" ])
          details.Profile.BioProfiles
          "bio profiles"
      | Error error -> failtestf "%A" error
    }

    testAsync "a profile without bio profiles decodes an empty map" {
      let http = CapturingHttp(ok profileResponse)

      let request: Users.Request = {
        KeyFile = "key-file"
        Username = "octocat"
      }

      let! (result: Result<ProfileDetails, TestError>) =
        Users.get(envFor http, request)

      match result with
      | Ok details ->
        Expect.isTrue (Map.isEmpty details.Profile.BioProfiles) "empty"
      | Error error -> failtestf "%A" error
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

    testAsync "reads one public organization profile" {
      let response =
        """{"data":{"handle":"devnull","name":"DevNull","avatar_url":"https://avatars.example/devnull.png","bio":null,"location":"The Internet","website":null,"github_url":"https://github.com/devnull","html_url":"https://dev-null.sh/@devnull"}}"""

      let http = CapturingHttp(ok response)

      let request: Organizations.Request = {
        KeyFile = "key-file"
        Handle = "devnull"
      }

      let! (result: Result<OrganizationProfile, TestError>) =
        Organizations.get(envFor http, request)

      match result with
      | Ok profile ->
        Expect.equal "devnull" profile.Handle "handle"
        Expect.equal "DevNull" profile.Name "name"

        Expect.equal
          (Some "https://avatars.example/devnull.png")
          profile.AvatarUrl
          "avatar"

        Expect.equal None profile.Bio "bio"
        Expect.equal (Some "The Internet") profile.Location "location"
        Expect.equal None profile.Website "website"

        Expect.equal
          (Some "https://github.com/devnull")
          profile.GithubUrl
          "github"

        Expect.equal "https://dev-null.sh/@devnull" profile.HtmlUrl "page"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "/api/v1/organizations/devnull" sent.Target "target"
    }

    testAsync "reads the new organization profile fields" {
      let response =
        """{"data":{"handle":"devnull","name":"DevNull","avatar_url":null,"bio":null,"location":null,"website":null,"github_url":null,"html_url":"https://dev-null.sh/org/devnull","is_following":true,"can_claim":true,"settings_url":"https://dev-null.sh/org/devnull/settings","lane":3,"followers_count":12,"admins":[{"id":"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0e","handle":"admin","name":"The Admin"}]}}"""

      let http = CapturingHttp(ok response)

      let request: Organizations.Request = {
        KeyFile = "key-file"
        Handle = "devnull"
      }

      let! (result: Result<OrganizationProfile, TestError>) =
        Organizations.get(envFor http, request)

      match result with
      | Ok profile ->
        Expect.equal true profile.IsFollowing "is following"
        Expect.equal true profile.CanClaim "can claim"

        Expect.equal
          (Some "https://dev-null.sh/org/devnull/settings")
          profile.SettingsUrl
          "settings url"

        Expect.equal (Some(LaneIndex 3)) profile.Lane "lane"
        Expect.equal (Some 12) profile.FollowersCount "followers count"
        Expect.equal 1 profile.Admins.Length "admins"
        Expect.equal "admin" (List.head profile.Admins).Handle "first admin"
      | Error error -> failtestf "%A" error
    }

    testAsync "the new organization profile fields default when absent" {
      let response =
        """{"data":{"handle":"devnull","name":"DevNull","avatar_url":null,"bio":null,"location":null,"website":null,"github_url":null,"html_url":"https://dev-null.sh/org/devnull"}}"""

      let http = CapturingHttp(ok response)

      let request: Organizations.Request = {
        KeyFile = "key-file"
        Handle = "devnull"
      }

      let! (result: Result<OrganizationProfile, TestError>) =
        Organizations.get(envFor http, request)

      match result with
      | Ok profile ->
        Expect.equal false profile.IsFollowing "is following"
        Expect.equal false profile.CanClaim "can claim"
        Expect.equal None profile.SettingsUrl "settings url"
        Expect.equal None profile.Lane "lane"
        Expect.equal None profile.FollowersCount "followers count"
        Expect.equal [] profile.Admins "admins"
      | Error error -> failtestf "%A" error
    }

    testAsync "an unknown organization stays the refusal path" {
      let http =
        CapturingHttp(
          refused 404 """{"message":"No organization named that."}"""
        )

      let request: Organizations.Request = {
        KeyFile = "key-file"
        Handle = "ghosts"
      }

      let! (result: Result<OrganizationProfile, TestError>) =
        Organizations.get(envFor http, request)

      match result with
      | Error(Refused(404, None, "No organization named that.")) -> ()
      | _ -> failtest "expected Refused with the server's words"
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

    testAsync "reads an organization's mention page" {
      let http = CapturingHttp(ok(pageOf commitJson))

      let request: Organizations.MentionsRequest = {
        KeyFile = "key-file"
        Handle = "devnull"
        Cursor = Some "abc"
      }

      let! (result: Result<Page<CommitNode>, TestError>) =
        Organizations.mentions(envFor http, request)

      match result with
      | Ok page ->
        Expect.equal 1 page.Items.Length "items"
        Expect.equal (Some "next") page.NextCursor "next"
        Expect.equal 20 page.PerPage "per page"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne

      Expect.equal
        "/api/v1/organizations/devnull/mentions?cursor=abc"
        sent.Target
        "target"
    }

    testAsync "reads an organization's commits page" {
      let http = CapturingHttp(ok(pageOf orgCommitJson))

      let request: Organizations.MentionsRequest = {
        KeyFile = "key-file"
        Handle = "devnull"
        Cursor = Some "abc"
      }

      let! (result: Result<Page<CommitNode>, TestError>) =
        Organizations.commits(envFor http, request)

      match result with
      | Ok page ->
        Expect.equal 1 page.Items.Length "items"

        match page.Items.Head with
        | LiveCommit commit ->
          Expect.equal "devnull" (Author.handle commit.Author) "author"
          Expect.isTrue (Author.isOrganization commit.Author) "organization"
        | Tombstone _ -> failtest "expected a live commit"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne

      Expect.equal
        "/api/v1/organizations/devnull/commits?cursor=abc"
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
        Arrived = []
        Pending = []
        Everything = false
        Through = []
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
        Arrived = []
        Pending = []
        Everything = false
        Through = []
      }

      let! (result: Result<LogPage, TestError>) = Logs.get(envFor http, request)

      match result with
      | Error(InvalidCommitId "not-a-uuid") -> ()
      | _ -> failtest "expected InvalidCommitId"

      Expect.equal [] http.Requests "no request"
    }

    testAsync "the poll extras ride the target and the header" {
      let idIndex index =
        sprintf "018f4c1e-6f8a-7c3a-9b2d-%012x" index

      let http = CapturingHttp(ok logResponse)

      let request: Logs.Request = {
        KeyFile = "key-file"
        Log = Latest
        Since = Some(idIndex 1)
        Cursor = None
        Arrived = [ for index in 1..105 -> idIndex index ]
        Pending = [ idIndex 201 ]
        Everything = true
        Through = [ for index in 1..25 -> idIndex index ]
      }

      let! (result: Result<LogPage, TestError>) = Logs.get(envFor http, request)

      match result with
      | Ok _ -> ()
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne

      Expect.isTrue
        (sent.Target.StartsWith "/api/v1/log/latest?since=")
        "since first"

      Expect.equal
        100
        (sent.Target.Split("arrived[]=").Length - 1)
        "arrived count capped"

      Expect.equal
        1
        (sent.Target.Split("pending[]=").Length - 1)
        "pending count"

      Expect.isTrue (sent.Target.Contains("everything=1")) "everything flag"

      Expect.equal
        (List.init 20 (fun index -> idIndex(index + 1)) |> String.concat ",")
        (header "X-Log-Through" sent.Headers |> Option.defaultValue "")
        "through header capped in order"

      Expect.isTrue
        (sent.Target.Contains("arrived[]=" + idIndex 1))
        "first arrived first"
    }

    testAsync "an invalid arrived id fails before the request" {
      let http = CapturingHttp(ok logResponse)

      let request: Logs.Request = {
        KeyFile = "key-file"
        Log = Latest
        Since = None
        Cursor = None
        Arrived = [ "not-a-uuid" ]
        Pending = []
        Everything = false
        Through = []
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

    testAsync "reads one commit's stargazers newest first" {
      let http = CapturingHttp(ok(pageOf apiUserJson))

      let request: Commits.StargazersRequest = {
        KeyFile = "key-file"
        CommitId = "018F4C1E-6F8A-7C3A-9B2D-5E6F7A8B9C0D"
        Cursor = None
      }

      let! (result: Result<Page<User>, TestError>) =
        Commits.stargazers(envFor http, request)

      match result with
      | Ok page ->
        Expect.equal 1 page.Items.Length "items"

        let user = page.Items |> List.exactlyOne
        Expect.equal "octocat" user.Handle "handle"
        Expect.equal "The Octocat" user.Name "name"
        Expect.equal (Some "next") page.NextCursor "next cursor"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne

      Expect.equal
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/stars"
        sent.Target
        "target"
    }

    testAsync "reads the next stargazers page through its cursor" {
      let http = CapturingHttp(ok(pageOf apiUserJson))

      let request: Commits.StargazersRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Cursor = Some "next"
      }

      let! (result: Result<Page<User>, TestError>) =
        Commits.stargazers(envFor http, request)

      match result with
      | Ok page -> Expect.equal 1 page.Items.Length "items"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne

      Expect.equal
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/stars?cursor=next"
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

    testAsync "decodes a followed-organizations page and its cursor" {
      // The server loads the followers count and the admins only on
      // some reads, so a row with both and a row with neither decode
      // the same way.
      let row =
        """{"handle":"devnull","name":"DevNull","avatar_url":null,"bio":"A quiet place for loud commits.","lane":2,"is_followed":true,"followers_count":11,"admins":["octocat"]}"""

      let plainRow =
        """{"handle":"octopus","name":"The Octopus","avatar_url":"https://avatars.example/octopus.png","bio":null,"lane":null,"is_followed":false}"""

      let response =
        sprintf
          "{\"data\":[%s,%s],\"links\":{},\"meta\":{\"per_page\":30,\"next_cursor\":\"next\",\"prev_cursor\":null}}"
          row
          plainRow

      match FollowedOrganization.decodePage response with
      | Ok page ->
        Expect.equal 2 page.Items.Length "items"
        Expect.equal (Some "next") page.NextCursor "next"
        Expect.equal 30 page.PerPage "per page"

        let (first, second) =
          match page.Items with
          | [ first; second ] -> (first, second)
          | _ -> failtest "expected two organizations"

        Expect.equal "devnull" first.Handle "first handle"
        Expect.equal "DevNull" first.Name "first name"
        Expect.equal None first.AvatarUrl "first avatar"
        Expect.isSome first.Bio "first bio"
        Expect.equal (Some(LaneIndex 2)) first.Lane "first lane"
        Expect.equal true first.IsFollowed "first follow"

        Expect.equal "octopus" second.Handle "second handle"
        Expect.equal "The Octopus" second.Name "second name"

        Expect.equal
          (Some "https://avatars.example/octopus.png")
          second.AvatarUrl
          "second avatar"

        Expect.equal None second.Bio "second bio"
        Expect.equal None second.Lane "second lane"
        Expect.equal false second.IsFollowed "second follow"
      | Error error -> failtestf "%A" error
    }

    testAsync "reads a followed-organizations page" {
      let http =
        CapturingHttp(
          ok(
            pageOf
              """{"handle":"devnull","name":"DevNull","avatar_url":null,"bio":null,"lane":null,"is_followed":true}"""
          )
        )

      let request: Users.ListRequest = {
        KeyFile = "key-file"
        Username = "octocat"
        Cursor = Some "abc"
      }

      let! (result: Result<Page<FollowedOrganization>, TestError>) =
        Users.followedOrganizations(envFor http, request)

      match result with
      | Ok page ->
        Expect.equal 1 page.Items.Length "items"
        Expect.equal (Some "next") page.NextCursor "next"
        Expect.equal 20 page.PerPage "per page"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne

      Expect.equal
        "/api/v1/users/octocat/following/organizations?cursor=abc"
        sent.Target
        "target"
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
        Images = []
        ImageDescriptions = []
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
        Images = []
        ImageDescriptions = []
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

    testAsync "pins one of the author's own commits" {
      let http = CapturingHttp(ok(sprintf """{"data":%s}""" richCommitJson))

      let request: CommitPins.ActionRequest = {
        KeyFile = "key-file"
        CommitId = "018F4C1E-6F8A-7C3A-9B2D-5E6F7A8B9C0D"
      }

      let! (result: Result<Commit, TestError>) =
        CommitPins.pin(envFor http, request)

      match result with
      | Ok commit ->
        Expect.equal true commit.IsPinned "the answer carries the new pin"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "PUT" sent.Method "method"
      Expect.equal "" (bodyText sent) "the pin carries no body"

      Expect.equal
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/pin"
        sent.Target
        "target"
    }

    testAsync "unpins the pinned commit" {
      let http = CapturingHttp(ok starResponse)

      let request: CommitPins.ActionRequest = {
        KeyFile = "key-file"
        CommitId = "018F4C1E-6F8A-7C3A-9B2D-5E6F7A8B9C0D"
      }

      let! (result: Result<Commit, TestError>) =
        CommitPins.unpin(envFor http, request)

      match result with
      | Ok commit ->
        Expect.equal false commit.IsPinned "the answer carries the pin off"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "DELETE" sent.Method "method"

      Expect.equal
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/pin"
        sent.Target
        "target"
    }

    testAsync "reads the related commits, closest first" {
      let http =
        CapturingHttp(
          ok(sprintf """{"data":[%s,%s]}""" richCommitJson commitJson)
        )

      let request: Commits.ShowRequest = {
        KeyFile = "key-file"
        CommitId = "018F4C1E-6F8A-7C3A-9B2D-5E6F7A8B9C0D"
      }

      let! (result: Result<CommitNode list, TestError>) =
        Commits.related(envFor http, request)

      match result with
      | Ok nodes ->
        Expect.equal 2 nodes.Length "two related commits"

        match nodes with
        | LiveCommit first :: _ ->
          Expect.equal
            "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
            first.Id
            "closest first"
        | _ -> failtest "expected live commits"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "GET" sent.Method "method"

      Expect.equal
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/related"
        sent.Target
        "target"
    }

    testAsync "a commit with nothing close answers an empty related list" {
      let http = CapturingHttp(ok """{"data":[]}""")

      let request: Commits.ShowRequest = {
        KeyFile = "key-file"
        CommitId = "018F4C1E-6F8A-7C3A-9B2D-5E6F7A8B9C0D"
      }

      let! (result: Result<CommitNode list, TestError>) =
        Commits.related(envFor http, request)

      match result with
      | Ok nodes -> Expect.equal 0 nodes.Length "nothing related"
      | Error error -> failtestf "%A" error
    }

    test "the profile search reads both arrays it answers" {
      match
        SearchProfiles.decode
          """{"users":[{"id":"01a15638-ae71-733c-89b3-373a1a0c538a","handle":"kai-kou","name":"kai kou","avatar_url":null,"bio":"Writes about Rails.","lane":3,"is_followed":true,"follows_you":true}],"organizations":[{"handle":"acme","name":"Acme","avatar_url":"https://example/acme.png","bio":"Ships everything.","website":"https://acme.example","github_url":"https://github.com/acme","html_url":"https://dev-null.sh/org/acme","lane":3,"followers_count":12,"is_followed":true},{"handle":"void","name":"The Void","avatar_url":null,"bio":null,"website":null,"github_url":null,"html_url":null,"lane":null,"is_followed":false}]}"""
      with
      | Ok found ->
        Expect.equal 1 found.Users.Length "one person"
        Expect.equal 2 found.Organizations.Length "two organizations"

        match found.Users with
        | user :: _ ->
          Expect.equal (Some true) user.IsFollowed "the person's follow flag"

          Expect.equal (Some true) user.FollowsYou "follows the viewer"
        | _ -> failtest "expected one person"

        match found.Organizations with
        | acme :: bare :: _ ->
          Expect.equal "acme" acme.Handle "the handle"

          Expect.equal (Some "Ships everything.") acme.Bio "the bio"

          Expect.equal (Some "https://acme.example") acme.Website "the site"

          Expect.equal
            (Some "https://github.com/acme")
            acme.GithubUrl
            "the github line"

          Expect.equal
            (Some "https://dev-null.sh/org/acme")
            acme.HtmlUrl
            "the page address"

          Expect.equal (Some(Lane.LaneIndex 3)) acme.Lane "the lane"

          Expect.equal (Some 12) acme.FollowersCount "the follower count"

          Expect.equal true acme.IsFollowed "followed"

          Expect.equal "void" bare.Handle "the second handle"

          Expect.equal None bare.Bio "a null bio stays none"

          Expect.equal None bare.Lane "a null lane stays none"

          Expect.equal None bare.FollowersCount "an absent count stays none"

          Expect.equal false bare.IsFollowed "not followed"
        | _ -> failtest "expected two organizations"
      | Error failure -> failtestf "%A" failure
    }

    testAsync "searches profiles for people and organizations" {
      let http =
        CapturingHttp(
          ok
            """{"users":[{"id":"01a15638-ae71-733c-89b3-373a1a0c538a","handle":"kai-kou","name":"kai kou","avatar_url":null,"bio":"Writes about Rails.","lane":3,"is_followed":false,"follows_you":false}],"organizations":[]}"""
        )

      let request: Search.ProfilesRequest = {
        KeyFile = "key-file"
        Term = "kai"
      }

      let! (result: Result<ProfileMatches, TestError>) =
        Search.profiles(envFor http, request)

      match result with
      | Ok found ->
        Expect.equal 1 found.Users.Length "one person found"

        Expect.equal 0 found.Organizations.Length "no organizations found"

        match found.Users with
        | user :: _ ->
          Expect.equal "kai-kou" user.Handle "the handle"

          Expect.equal (Some "Writes about Rails.") user.Bio "the bio"
        | _ -> failtest "expected one person"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "GET" sent.Method "method"
      Expect.equal "/api/v1/search/profiles?q=kai" sent.Target "target"
    }

    testAsync "searches commits by meaning with the limit asked for" {
      let http = CapturingHttp(ok(sprintf """{"data":[%s]}""" commitJson))

      let request: Search.CommitsRequest = {
        KeyFile = "key-file"
        Term = "rails hosting"
        Limit = Some 5
      }

      let! (result: Result<CommitNode list, TestError>) =
        Search.commits(envFor http, request)

      match result with
      | Ok nodes -> Expect.equal 1 nodes.Length "one commit found"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne

      Expect.equal
        "/api/v1/search/commits?q=rails%20hosting&limit=5"
        sent.Target
        "target"
    }

    testAsync "a commit search without a limit asks for a page" {
      let http = CapturingHttp(ok """{"data":[]}""")

      let request: Search.CommitsRequest = {
        KeyFile = "key-file"
        Term = "rails"
        Limit = None
      }

      let! (_: Result<CommitNode list, TestError>) =
        Search.commits(envFor http, request)

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "/api/v1/search/commits?q=rails" sent.Target "target"
    }

    testCase "the people term drops its at and its blanks" (fun () ->
      Expect.equal
        (ValueSome "octocat")
        (SearchTerms.peopleTerm "@octocat")
        "the at goes"

      Expect.equal
        (ValueSome "kai kou")
        (SearchTerms.peopleTerm " kai kou ")
        "the edges trim"

      Expect.equal ValueNone (SearchTerms.peopleTerm " @ ") "nothing left")

    testCase "the commit term holds the server's bounds" (fun () ->
      Expect.equal
        ValueNone
        (SearchTerms.commitTerm " ab ")
        "under the minimum"

      Expect.equal
        (ValueSome "abc")
        (SearchTerms.commitTerm " abc ")
        "at the minimum"

      Expect.equal
        ValueNone
        (SearchTerms.commitTerm(String('a', 101)))
        "over the maximum")

    testAsync "suggests the people and organizations to mention" {
      let http =
        CapturingHttp(
          ok
            """{"data":[{"id":"01a15638-ae71-733c-89b3-373a1a0c538a","handle":"kai-kou","name":"kai kou","avatar_url":null}],"organizations":[{"handle":"acme","name":"Acme","avatar_url":null}]}"""
        )

      let request: Suggestions.MentionsRequest = {
        KeyFile = "key-file"
        Query = "kai"
        Reply = ValueSome "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Quote = ValueNone
      }

      let! (result: Result<MentionResults, TestError>) =
        Suggestions.mentions(envFor http, request)

      match result with
      | Ok found ->
        Expect.equal 1 found.Users.Length "one person"

        Expect.equal
          "acme"
          (List.exactlyOne found.Organizations).Handle
          "one organization"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "GET" sent.Method "method"

      Expect.equal
        "/api/v1/search/mentions?q=kai&reply=018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        sent.Target
        "target"
    }

    testAsync "a bare @ asks with an empty query" {
      let http = CapturingHttp(ok """{"data":[],"organizations":[]}""")

      let request: Suggestions.MentionsRequest = {
        KeyFile = "key-file"
        Query = ""
        Reply = ValueNone
        Quote = ValueSome "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
      }

      let! (_: Result<MentionResults, TestError>) =
        Suggestions.mentions(envFor http, request)

      let sent = http.Requests |> List.exactlyOne

      Expect.equal
        "/api/v1/search/mentions?q=&quote=018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        sent.Target
        "target"
    }

    testAsync "reads the issue a number names, or none" {
      let http =
        CapturingHttp(
          ok
            """{"issue":{"number":12,"title":"It was DNS","url":"https://github.com/dev-null/sh/issues/12"}}"""
        )

      let request: Suggestions.IssueRequest = {
        KeyFile = "key-file"
        Number = IssueNumber.parse "12" |> ValueOption.get
      }

      let! (result: Result<Issue option, TestError>) =
        Suggestions.issue(envFor http, request)

      match result with
      | Ok(Some issue) ->
        Expect.equal 12 issue.Number "number"
        Expect.equal "It was DNS" issue.Title "title"
      | other -> failtestf "%A" other

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "/api/v1/search/issues?q=12" sent.Target "target"

      let none = CapturingHttp(ok """{"issue":null}""")

      let! (empty: Result<Issue option, TestError>) =
        Suggestions.issue(envFor none, request)

      Expect.equal (Ok None) empty "no issue"
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

    testAsync "follows an organization" {
      let http = CapturingHttp(ok "")

      let request: Organizations.Request = {
        KeyFile = "key-file"
        Handle = "acme"
      }

      let! (result: Result<unit, TestError>) =
        Organizations.follow(envFor http, request)

      match result with
      | Ok() -> ()
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "POST" sent.Method "method"
      Expect.equal "/api/v1/organizations/acme/follow" sent.Target "target"
    }

    testAsync "unfollows an organization" {
      let http = CapturingHttp(ok "")

      let request: Organizations.Request = {
        KeyFile = "key-file"
        Handle = "acme"
      }

      let! (result: Result<unit, TestError>) =
        Organizations.unfollow(envFor http, request)

      match result with
      | Ok() -> ()
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "DELETE" sent.Method "method"
      Expect.equal "/api/v1/organizations/acme/follow" sent.Target "target"
    }

    testAsync "claims an organization" {
      let http = CapturingHttp(ok "")

      let request: Organizations.Request = {
        KeyFile = "key-file"
        Handle = "acme"
      }

      let! (result: Result<unit, TestError>) =
        Organizations.claim(envFor http, request)

      match result with
      | Ok() -> ()
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "POST" sent.Method "method"
      Expect.equal "/api/v1/organizations/acme/claim" sent.Target "target"
    }

    testAsync "suggests who to follow with the round and profile context" {
      let people =
        [
          for index in 1..6 ->
            $"{{\"id\":\"018f5a1e-000{index}-7c3a-9b2d-5e6f7a8b9c0d\",\"handle\":\"person{index}\",\"name\":\"Person {index}\",\"followers_count\":{index},\"follows_you\":true,\"followed_by_following_count\":{6 - index},\"is_followed\":false}}"
        ]
        |> String.concat ","

      let http = CapturingHttp(ok(sprintf """{"data":[%s]}""" people))

      let request: Users.SuggestedRequest = {
        KeyFile = "key-file"
        Round = 2
        Context = Users.Profile "octocat"
      }

      let! (result: Result<User list, TestError>) =
        Users.suggested(envFor http, request)

      match result with
      | Ok users ->
        Expect.equal 6 users.Length "six suggestions"

        let first = List.head users

        Expect.equal (Some 5) first.FollowedByFollowingCount "known followers"
        Expect.equal (Some true) first.FollowsYou "follows you"
        Expect.equal (Some false) first.IsFollowed "not followed"
      | Error error -> failtestf "%A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "GET" sent.Method "method"

      Expect.equal
        "/api/v1/me/suggested-users?round=2&handle=octocat"
        sent.Target
        "target"
    }

    testAsync "suggests who to follow for a commit thread" {
      let uuid = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
      let http = CapturingHttp(ok """{"data":[]}""")

      let request: Users.SuggestedRequest = {
        KeyFile = "key-file"
        Round = 0
        Context = Users.CommitThread uuid
      }

      let! (result: Result<User list, TestError>) =
        Users.suggested(envFor http, request)

      Expect.equal (Ok []) result "no suggestions"

      let sent = http.Requests |> List.exactlyOne

      Expect.equal
        ("/api/v1/me/suggested-users?round=0&commit_id=" + uuid)
        sent.Target
        "target"
    }

    testAsync "dismisses a suggested user" {
      let http = CapturingHttp(ok "")

      let request: Users.Request = {
        KeyFile = "key-file"
        Username = "octocat"
      }

      let! (result: Result<unit, TestError>) =
        Users.dismiss(envFor http, request)

      Expect.equal (Ok()) result "dismissed"

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "POST" sent.Method "method"

      Expect.equal "/api/v1/users/octocat/dismiss" sent.Target "target"
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
            $"1760000000\n00112233445566778899aabbccddeeff\nPOST\n/api/v1/commits\n%s{pngHash}\n{{\"body\":\"It was DNS.\",\"parent_id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\",\"reply_recipients\":[]}}"
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
          "{\"body\":\"It was DNS.\",\"parent_id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\",\"reply_recipients\":[]}"
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

    testAsync "a report with one described image posts multipart" {
      let http = CapturingHttp(ok "")

      let signer =
        signerWith(fun signed ->
          Expect.equal "devnull-api-images" signed.Namespace "namespace"

          Expect.equal
            ("1760000000\n00112233445566778899aabbccddeeff\nPOST\n"
             + "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/reports\n"
             + pngHash
             + "\n{\"reason\":\"spam\",\"details\":\"repeat\",\"image_descriptions\":[\"the screenshot\"]}")
            signed.Payload
            "signed message"

          Ok "signature")

      let request: Commits.ReportRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Reason = Spam
        Details = Some "repeat"
        Images = [ stagedImage "shot.png" ]
        ImageDescriptions = [ Some "the screenshot" ]
      }

      let! (result: Result<unit, TestError>) =
        Commits.report(redeemEnv http signer, request)

      match result with
      | Ok() ->
        let sent = http.Requests |> List.exactlyOne

        Expect.equal
          "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/reports"
          sent.Target
          "target"

        let form = multipartForm sent

        Expect.equal
          [ "payload"; "images[]" ]
          (form.Files |> List.map _.Name)
          "parts"

        Expect.isEmpty form.Fields "the multipart carries no fields"

        let payload = form.Files |> List.find(fun file -> file.Name = "payload")
        Expect.equal "report.json" payload.FileName "payload name"
        Expect.equal "application/json" payload.ContentType "payload type"

        Expect.equal
          "{\"reason\":\"spam\",\"details\":\"repeat\",\"image_descriptions\":[\"the screenshot\"]}"
          (System.Text.Encoding.UTF8.GetString payload.Content)
          "payload json"

        let image = form.Files |> List.find(fun file -> file.Name = "images[]")
        Expect.equal "shot.png" image.FileName "image name"
      | Error error -> failtestf "%A" error
    }

    testAsync "a report's undescribed image rides a null slot" {
      let http = CapturingHttp(ok "")

      let request: Commits.ReportRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Reason = Harassment
        Details = None
        Images = [ stagedImage "shot.png"; stagedImage "after.png" ]
        ImageDescriptions = [ Some "the error page" ]
      }

      let! (result: Result<unit, TestError>) =
        Commits.report(envFor http, request)

      match result with
      | Ok() ->
        let form = http.Requests |> List.exactlyOne |> multipartForm

        let payload = form.Files |> List.find(fun file -> file.Name = "payload")

        Expect.equal
          "{\"reason\":\"harassment\",\"image_descriptions\":[\"the error page\",null]}"
          (System.Text.Encoding.UTF8.GetString payload.Content)
          "payload json"
      | Error error -> failtestf "%A" error
    }

    testAsync "a report that describes nothing omits the descriptions field" {
      let http = CapturingHttp(ok "")

      let request: Commits.ReportRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Reason = Unsafe
        Details = None
        Images = [ stagedImage "shot.png" ]
        ImageDescriptions = []
      }

      let! (result: Result<unit, TestError>) =
        Commits.report(envFor http, request)

      match result with
      | Ok() ->
        let form = http.Requests |> List.exactlyOne |> multipartForm

        let payload = form.Files |> List.find(fun file -> file.Name = "payload")

        Expect.equal
          "{\"reason\":\"unsafe\"}"
          (System.Text.Encoding.UTF8.GetString payload.Content)
          "payload json"
      | Error error -> failtestf "%A" error
    }

    testAsync "a report with a fourth image fails before the request" {
      let http = CapturingHttp(ok "")

      let request: Commits.ReportRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Reason = Spam
        Details = None
        Images = [
          stagedImage "a.png"
          stagedImage "b.png"
          stagedImage "c.png"
          stagedImage "d.png"
        ]
        ImageDescriptions = []
      }

      let! (result: Result<unit, TestError>) =
        Commits.report(envFor http, request)

      match result with
      | Error(TooManyImages 4) -> Expect.isEmpty http.Requests "no request left"
      | other -> failtestf "expected the image count, got %A" other
    }

    testAsync "a report with a non-image fails before the request" {
      let http = CapturingHttp(ok "")

      let request: Commits.ReportRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Reason = Spam
        Details = None
        Images = [
          {
            FileName = "notes.txt"
            Bytes = [| 1uy; 2uy; 3uy |]
          }
        ]
        ImageDescriptions = []
      }

      let! (result: Result<unit, TestError>) =
        Commits.report(envFor http, request)

      match result with
      | Error(UnsupportedImage "notes.txt") ->
        Expect.isEmpty http.Requests "no request left"
      | other -> failtestf "expected the format refusal, got %A" other
    }

    testAsync "a report with an oversized image fails before the request" {
      let http = CapturingHttp(ok "")

      let oversized = Array.zeroCreate 10240001

      let request: Commits.ReportRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Reason = Spam
        Details = None
        Images = [
          {
            FileName = "huge.png"
            Bytes = oversized
          }
        ]
        ImageDescriptions = []
      }

      let! (result: Result<unit, TestError>) =
        Commits.report(envFor http, request)

      match result with
      | Error(ImageTooLarge("huge.png", 10240001)) ->
        Expect.isEmpty http.Requests "no request left"
      | other -> failtestf "expected the size refusal, got %A" other
    }

    testAsync "a report with an over-long description fails before the request" {
      let http = CapturingHttp(ok "")

      let request: Commits.ReportRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Reason = Spam
        Details = None
        Images = [ stagedImage "shot.png" ]
        ImageDescriptions = [ Some(String.replicate 1001 "x") ]
      }

      let! (result: Result<unit, TestError>) =
        Commits.report(envFor http, request)

      match result with
      | Error(DescriptionTooLong(0, 1001)) ->
        Expect.isEmpty http.Requests "no request left"
      | other -> failtestf "expected the description bound, got %A" other
    }

    testAsync
      "a report with more descriptions than images fails before the request" {
      let http = CapturingHttp(ok "")

      let request: Commits.ReportRequest = {
        KeyFile = "key-file"
        CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
        Reason = Spam
        Details = None
        Images = [ stagedImage "shot.png" ]
        ImageDescriptions = [ Some "one"; Some "two" ]
      }

      let! (result: Result<unit, TestError>) =
        Commits.report(envFor http, request)

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

let settingsJson =
  """{"data":{"timezone":"Europe/Budapest","show_local_time":true,"translation_language":"ja","automatic_translation":false,"translation_languages":[{"code":"ja","name":"Japanese","native_name":"日本語"},{"code":"fr","name":"French","native_name":"français"}],"is_admin":false}}"""

let defaultSettingsJson =
  """{"data":{"timezone":null,"show_local_time":false,"translation_language":null,"automatic_translation":true,"translation_languages":[{"code":"en","name":"English","native_name":"English"}],"is_admin":false}}"""

let translationJson =
  """{"translation":{"language":"en","source_language":"de","source_name":"German","available":true,"translated":{"body_segments":[{"type":"text","text":"Good morning"}],"preview_segments":null}}}"""

let settingsRequest: Me.SettingsRequest = { KeyFile = "key" }

let translateRequest: Commits.TranslateRequest = {
  KeyFile = "key"
  CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
  Language = languageOf "ja"
}

let settingsTests =
  testList "Api.settings" [
    testAsync "reads the settings only the person sees" {
      let http = CapturingHttp(ok settingsJson)

      let! (result: Result<UserSettings, TestError>) =
        Me.settings(envFor http, settingsRequest)

      match result with
      | Ok settings ->
        Expect.equal
          (ZoneOf("Europe/Budapest", true))
          settings.Timezone
          "timezone"

        Expect.equal
          (Some "ja")
          (settings.TranslationLanguage |> Option.map _.Code)
          "language"

        Expect.equal false settings.AutomaticTranslation "automatic"
        Expect.equal 2 settings.Languages.Length "languages"
        Expect.equal "Japanese" settings.Languages.Head.Name "english name"
        Expect.equal "日本語" settings.Languages.Head.NativeName "native name"
      | Error error -> failtestf "expected settings: %A" error
    }

    testAsync "follows the device and translates automatically until chosen" {
      let http = CapturingHttp(ok defaultSettingsJson)

      let! (result: Result<UserSettings, TestError>) =
        Me.settings(envFor http, settingsRequest)

      match result with
      | Ok settings ->
        Expect.equal FollowDevice settings.Timezone "timezone"
        Expect.equal None settings.TranslationLanguage "language"
        Expect.equal true settings.AutomaticTranslation "automatic"
      | Error error -> failtestf "expected settings: %A" error
    }

    testAsync "saves the timezone with its local-time choice" {
      let http = CapturingHttp(ok meJson)

      let! (result: Result<Me, TestError>) =
        Me.setTimezone(
          envFor http,
          {
            KeyFile = "key"
            Timezone = ZoneOf("Europe/Budapest", true)
          }
        )

      Expect.isOk result "answer"

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "PUT" sent.Method "method"
      Expect.equal "/api/v1/me/timezone" sent.Target "target"

      Expect.equal
        "{\"timezone\":\"Europe/Budapest\",\"show_local_time\":true}"
        (match sent.Body with
         | Text body -> body
         | _ -> "")
        "body"
    }

    testAsync "clears the timezone to follow the device" {
      let http = CapturingHttp(ok meJson)

      let! (_: Result<Me, TestError>) =
        Me.setTimezone(
          envFor http,
          {
            KeyFile = "key"
            Timezone = FollowDevice
          }
        )

      let sent = http.Requests |> List.exactlyOne

      Expect.equal
        "{\"timezone\":null,\"show_local_time\":false}"
        (match sent.Body with
         | Text body -> body
         | _ -> "")
        "body"
    }

    testAsync "turns automatic translation on and off" {
      let http = CapturingHttp(ok meJson)

      let! (_: Result<Me, TestError>) =
        Me.setAutomaticTranslation(
          envFor http,
          { KeyFile = "key"; Enabled = true }
        )

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "PUT" sent.Method "method"
      Expect.equal "/api/v1/me/automatic-translation" sent.Target "target"

      Expect.equal
        "{\"enabled\":true}"
        (match sent.Body with
         | Text body -> body
         | _ -> "")
        "body"
    }

    testAsync "saves the translation language, and clears it to the device" {
      let http = CapturingHttp(ok meJson)

      let! (_: Result<Me, TestError>) =
        Me.setTranslationLanguage(
          envFor http,
          {
            KeyFile = "key"
            Language =
              Some {
                Code = "ja"
                Name = "Japanese"
                NativeName = "日本語"
              }
          }
        )

      let! (_: Result<Me, TestError>) =
        Me.setTranslationLanguage(
          envFor http,
          { KeyFile = "key"; Language = None }
        )

      match http.Requests with
      | [ chosen; cleared ] ->
        Expect.equal "PUT" chosen.Method "method"
        Expect.equal "/api/v1/me/translation-language" chosen.Target "target"

        Expect.equal
          "{\"translation_language\":\"ja\"}"
          (match chosen.Body with
           | Text body -> body
           | _ -> "")
          "chosen"

        Expect.equal
          "{\"translation_language\":null}"
          (match cleared.Body with
           | Text body -> body
           | _ -> "")
          "cleared"
      | _ -> failtest "expected two requests"
    }

    testAsync "translates one commit into the language asked for" {
      let http = CapturingHttp(ok translationJson)

      let! (result: Result<TranslationPayload, TestError>) =
        Commits.translate(envFor http, translateRequest)

      match result with
      | Ok payload ->
        Expect.equal translationJson payload.Json "raw JSON"

        match payload.Translation with
        | Some translation ->
          Expect.equal "en" translation.Language "language"
          Expect.equal (Some "German") translation.SourceName "source"
          Expect.equal true translation.Available "available"

          match translation.Translated with
          | Some body ->
            match body.BodySegments with
            | [ TextSegment text ] -> Expect.equal "Good morning" text "body"
            | _ -> failtest "expected one text segment"
          | None -> failtest "expected a translated body"
        | None -> failtest "expected a translation"
      | Error error -> failtestf "expected a translation: %A" error

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "POST" sent.Method "method"

      Expect.equal
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/translation"
        sent.Target
        "target"

      Expect.equal
        "{\"language\":\"ja\"}"
        (match sent.Body with
         | Text body -> body
         | _ -> "")
        "body"
    }

    testAsync "answers the retryable translation failure verbatim" {
      let http =
        CapturingHttp(
          refused
            503
            """{"message":"Could not translate this commit. Try again."}"""
        )

      let! (result: Result<TranslationPayload, TestError>) =
        Commits.translate(envFor http, translateRequest)

      match result with
      | Error(Refused(503, None, message)) ->
        Expect.equal
          "Could not translate this commit. Try again."
          message
          "message"
      | other -> failtestf "expected a 503 refusal: %A" other
    }

    testAsync "decodes the translation every signed commit carries" {
      let body =
        richCommitJson.Replace(
          "\"reply_preview\"",
          "\"translation\":{\"language\":\"en\",\"source_language\":\"de\",\"source_name\":\"German\",\"available\":true,\"translated\":{\"body_segments\":[{\"type\":\"text\",\"text\":\"Good morning\"}],\"preview_segments\":null}},\"reply_preview\""
        )

      let http = CapturingHttp(ok(sprintf "{\"data\":%s}" body))

      let! (result: Result<Commit, TestError>) =
        Commits.create(envFor http, commitRequest)

      match result with
      | Ok commit ->
        match commit.Translation with
        | Some translation ->
          Expect.equal "en" translation.Language "language"
          Expect.equal true translation.Available "available"
        | None -> failtest "expected the commit's translation"
      | Error error -> failtestf "expected a commit: %A" error
    }
  ]
