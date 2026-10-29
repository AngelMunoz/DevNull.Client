module Tests.Scheduled

open System
open Expecto
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open Tests.TestSupport
open Tests.Api

let now = DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero)

let momentAt(offset: DateTimeOffset) : ScheduledMoment =
  let parsed: Result<ScheduledMoment, TestError> =
    ScheduledMoments.parse(now, offset)

  match parsed with
  | Ok moment -> moment
  | Error error -> failtestf "%A" error

let soon = momentAt(DateTimeOffset(2026, 10, 15, 13, 0, 0, TimeSpan.Zero))

let queuedJson =
  """{"id":"018f5a1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d","body":"Shipping tomorrow.","body_segments":[{"type":"text","text":"Shipping tomorrow."}],"preview_segments":null,"poll_options":null,"image_count":0,"scheduled_at":"2026-10-15T13:00:00+00:00","submitted_at":null,"status":"scheduled"}"""

let queueJson =
  $"""{{"data":[{queuedJson},{{"id":"018f5a1e-6f8a-7c3a-9b2d-5e6f7a8b9c0e","body":null,"body_segments":[],"preview_segments":null,"poll_options":null,"image_count":2,"scheduled_at":"2026-10-15T12:00:00+00:00","submitted_at":"2026-10-15T12:05:00+00:00","status":"pending"}}],"links":{{"first":"https://dev-null.sh/api/v1/scheduled-commits?page=1","last":"https://dev-null.sh/api/v1/scheduled-commits?page=1","prev":null,"next":null}},"meta":{{"current_page":1,"from":1,"last_page":1,"path":"https://dev-null.sh/api/v1/scheduled-commits","per_page":20,"to":2,"total":2}}}}"""

let conflictJson =
  """{"message":"This commit was already submitted or canceled. Refresh to see its status."}"""

let capJson =
  """{"message":"You can have up to 10 scheduled commits. Cancel one before scheduling another.","errors":{"scheduled_at":["You can have up to 10 scheduled commits. Cancel one before scheduling another."]}}"""

let createRequest: ScheduledCommits.CreateRequest = {
  KeyFile = "key-file"
  Body = "Shipping tomorrow."
  Images = []
  ImageDescriptions = []
  Poll = None
  Card = FirstLink
  ScheduledAt = soon
}

let momentTests =
  testList "ScheduledMoments" [
    test "a moment now or earlier refuses" {
      let boundary: Result<ScheduledMoment, TestError> =
        ScheduledMoments.parse(now, now)

      let past: Result<ScheduledMoment, TestError> =
        ScheduledMoments.parse(now, now.AddMinutes -1.0)

      match boundary, past with
      | Error(ScheduledInPast _), Error(ScheduledInPast moment) ->
        Expect.equal (now.AddMinutes -1.0) moment "the moment travels"
      | _ -> failtest "expected ScheduledInPast twice"
    }

    test "a moment beyond thirty days refuses" {
      let beyond: Result<ScheduledMoment, TestError> =
        ScheduledMoments.parse(now, now.AddDays 31.0)

      match beyond with
      | Error(ScheduledTooFar _) -> ()
      | _ -> failtest "expected ScheduledTooFar"
    }

    test "thirty days exactly still parse" {
      let edge: Result<ScheduledMoment, TestError> =
        ScheduledMoments.parse(now, now.AddDays 30.0)

      match edge with
      | Ok moment ->
        Expect.equal
          (now.AddDays 30.0)
          (ScheduledMoments.moment moment)
          "moment"
      | _ -> failtest "expected the edge to parse"
    }

    test "the wire keeps the offset and drops the fraction" {
      // 15:00 at +02:00 is 13:00 UTC, still after the noon `now`.
      let moment =
        momentAt(
          DateTimeOffset(2026, 10, 15, 15, 0, 30, TimeSpan.FromHours 2.0)
            .AddMilliseconds
            500.0
        )

      Expect.equal
        "2026-10-15T15:00:30+02:00"
        (ScheduledMoments.wire moment)
        "wire"
    }
  ]

let apiTests =
  testList "Api.scheduledCommits" [
    testAsync "reads the queue's offset page and its meta" {
      let http = CapturingHttp(ok queueJson)
      let env = envFor http

      let! (result: Result<ScheduledPage, TestError>) =
        ScheduledCommits.list(env, { KeyFile = "key-file"; Page = 1 })

      match result with
      | Ok page ->
        Expect.equal 2 page.Items.Length "items"

        let waiting = page.Items |> List.head
        Expect.equal (Some "Shipping tomorrow.") waiting.Body "waiting body"
        Expect.equal StillScheduled waiting.Status "waiting status"
        Expect.equal None waiting.SubmittedAt "not submitted"

        let submitted = page.Items |> List.item 1
        Expect.equal None submitted.Body "submitted body hidden"
        Expect.equal [] submitted.BodySegments "submitted segments empty"
        Expect.equal 2 submitted.ImageCount "images"
        Expect.equal (SubmittedAs Pending) submitted.Status "submitted status"

        Expect.equal
          (Some(DateTimeOffset(2026, 10, 15, 12, 5, 0, TimeSpan.Zero)))
          submitted.SubmittedAt
          "submitted at"

        Expect.equal 1 page.CurrentPage "current page"
        Expect.equal 1 page.LastPage "last page"
        Expect.equal 2 page.Total "total"

        let sent = http.Requests |> List.exactlyOne
        Expect.equal "GET" sent.Method "method"

        Expect.equal "/api/v1/scheduled-commits?page=1" sent.Target "target"
      | Error error -> failtestf "%A" error
    }

    testAsync "asks for a later page by number" {
      let http = CapturingHttp(ok queueJson)
      let env = envFor http

      let! (_: Result<ScheduledPage, TestError>) =
        ScheduledCommits.list(env, { KeyFile = "key-file"; Page = 3 })

      let sent = http.Requests |> List.exactlyOne

      Expect.equal "/api/v1/scheduled-commits?page=3" sent.Target "target"
    }

    test "reads every settled status" {
      let decodeStatus(raw: string) =
        match
          DevNull.Client.Core.ApiTypes.ScheduledCommits.decodePage(
            queueJson.Replace("\"pending\"", $"\"{raw}\"")
          )
        with
        | Ok page -> (page.Items |> List.item 1).Status
        | Error failure -> failtestf "%A" failure

      Expect.equal (SubmittedAs Approved) (decodeStatus "approved") "approved"
      Expect.equal (SubmittedAs Rejected) (decodeStatus "rejected") "rejected"
      Expect.equal CommitDeleted (decodeStatus "deleted") "deleted"

      Expect.equal
        (SubmittedAs(UnknownStatus "under_review"))
        (decodeStatus "under_review")
        "an unknown status stays raw"
    }

    testAsync "queues a commit and decodes the answer" {
      let http = CapturingHttp(ok $"{{\"data\":{queuedJson}}}")

      let signer =
        signerWith(fun signed ->
          Expect.equal "devnull-api" signed.Namespace "namespace"

          Expect.equal
            "1760000000\n00112233445566778899aabbccddeeff\nPOST\n/api/v1/scheduled-commits\n{\"body\":\"Shipping tomorrow.\",\"scheduled_at\":\"2026-10-15T13:00:00\\u002B00:00\"}"
            signed.Payload
            "signed message"

          Ok "signature")

      let env = redeemEnv http signer

      let! (result: Result<ScheduledCommit, TestError>) =
        ScheduledCommits.create(env, createRequest)

      match result with
      | Ok queued ->
        Expect.equal "018f5a1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d" queued.Id "id"

        Expect.equal
          (DateTimeOffset(2026, 10, 15, 13, 0, 0, TimeSpan.Zero))
          queued.ScheduledAt
          "scheduled at"

        let sent = http.Requests |> List.exactlyOne
        Expect.equal "POST" sent.Method "method"

        Expect.equal "/api/v1/scheduled-commits" sent.Target "target"
      | Error error -> failtestf "%A" error
    }

    testAsync "queues a poll as a fresh commit carries it" {
      let http = CapturingHttp(ok $"{{\"data\":{queuedJson}}}")

      let signer =
        signerWith(fun signed ->
          Expect.equal
            "1760000000\n00112233445566778899aabbccddeeff\nPOST\n/api/v1/scheduled-commits\n{\"body\":\"Tabs or spaces?\",\"poll\":{\"options\":[\"Tabs\",\"Spaces\"],\"duration\":1440},\"scheduled_at\":\"2026-10-15T13:00:00\\u002B00:00\"}"
            signed.Payload
            "signed message"

          Ok "signature")

      let request = {
        createRequest with
            Body = "Tabs or spaces?"

            Poll =
              Some(
                parsed(
                  PollSpecs.parse([ "Tabs"; "Spaces" ], PollDuration.OneDay)
                )
              )
      }

      let! (_: Result<ScheduledCommit, TestError>) =
        ScheduledCommits.create(redeemEnv http signer, request)

      let sent = http.Requests |> List.exactlyOne
      Expect.equal "POST" sent.Method "method"
    }

    testAsync "carries the link-card choice" {
      let http = CapturingHttp(ok $"{{\"data\":{queuedJson}}}")

      let request = {
        createRequest with
            Body =
              "Shipped https://example.com/first and https://example.com/second"
            Card = CardOf "https://example.com/second"
      }

      let! (_: Result<ScheduledCommit, TestError>) =
        ScheduledCommits.create(envFor http, request)

      let sent = http.Requests |> List.exactlyOne

      Expect.stringContains
        (bodyText sent)
        "\"link_preview_url\":\"https://example.com/second\""
        "the chosen link rides"

      let noCard = {
        createRequest with
            Body = "Shipped https://example.com/first"
            Card = NoCard
      }

      let http2 = CapturingHttp(ok $"{{\"data\":{queuedJson}}}")

      let! (_: Result<ScheduledCommit, TestError>) =
        ScheduledCommits.create(envFor http2, noCard)

      let sent2 = http2.Requests |> List.exactlyOne

      Expect.stringContains
        (bodyText sent2)
        "\"link_preview_url\":null"
        "no card rides as null"
    }

    testAsync "rides images as multipart with their descriptions" {
      let http = CapturingHttp(ok $"{{\"data\":{queuedJson}}}")

      let request = {
        createRequest with
            Body = "Shipped the dashboard."
            Images = [ stagedImage "wide.png"; stagedImage "tall.png" ]
            ImageDescriptions = [ Some "The dashboard"; None ]
      }

      let! (result: Result<ScheduledCommit, TestError>) =
        ScheduledCommits.create(envFor http, request)

      match result with
      | Ok _ ->
        let sent = http.Requests |> List.exactlyOne
        Expect.equal "POST" sent.Method "method"
        Expect.equal "/api/v1/scheduled-commits" sent.Target "target"

        let form = multipartForm sent

        let names = form.Files |> List.map _.Name

        Expect.equal [ "payload"; "images[]"; "images[]" ] names "part names"

        let payload = form.Files |> List.head

        Expect.equal
          "{\"body\":\"Shipped the dashboard.\",\"image_descriptions\":[\"The dashboard\",null],\"scheduled_at\":\"2026-10-15T13:00:00\\u002B00:00\"}"
          (System.Text.Encoding.UTF8.GetString payload.Content)
          "payload json"
      | Error error -> failtestf "%A" error
    }

    testAsync "a poll beside images refuses before sending" {
      let http = CapturingHttp(ok "{}")

      let request = {
        createRequest with
            Images = [ stagedImage "wide.png" ]

            Poll =
              Some(
                parsed(
                  PollSpecs.parse([ "Tabs"; "Spaces" ], PollDuration.OneDay)
                )
              )
      }

      let! (result: Result<ScheduledCommit, TestError>) =
        ScheduledCommits.create(envFor http, request)

      match result with
      | Error(PollProhibited PollWithImages) -> ()
      | _ -> failtest "expected PollProhibited"

      Expect.isEmpty http.Requests "nothing was sent"
    }

    testAsync "cancels one queued commit" {
      let http = CapturingHttp(ok "")
      let env = envFor http

      let! (result: Result<unit, TestError>) =
        ScheduledCommits.cancel(
          env,
          {
            KeyFile = "key-file"
            ScheduledCommitId = "018f5a1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
          }
        )

      match result with
      | Ok() ->
        let sent = http.Requests |> List.exactlyOne
        Expect.equal "DELETE" sent.Method "method"

        Expect.equal
          "/api/v1/scheduled-commits/018f5a1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
          sent.Target
          "target"
      | Error error -> failtestf "%A" error
    }

    testAsync "the cancel conflict surfaces verbatim" {
      let http = CapturingHttp(refused 409 conflictJson)
      let env = envFor http

      let! (result: Result<unit, TestError>) =
        ScheduledCommits.cancel(
          env,
          {
            KeyFile = "key-file"
            ScheduledCommitId = "an-id"
          }
        )

      match result with
      | Error(Refused(409, None, message)) ->
        Expect.equal
          "This commit was already submitted or canceled. Refresh to see its status."
          message
          "message"
      | _ -> failtest "expected the conflict refusal"
    }

    testAsync "submits one now and hides the body" {
      let http =
        CapturingHttp(
          ok
            """{"data":{"id":"018f5a1e-6f8a-7c3a-9b2d-5e6f7a8b9c0e","body":null,"body_segments":[],"preview_segments":null,"poll_options":null,"image_count":0,"scheduled_at":"2026-10-15T12:00:00+00:00","submitted_at":"2026-10-15T12:05:00+00:00","status":"pending"}}"""
        )

      let env = envFor http

      let! (result: Result<ScheduledCommit, TestError>) =
        ScheduledCommits.submitNow(
          env,
          {
            KeyFile = "key-file"
            ScheduledCommitId = "018f5a1e-6f8a-7c3a-9b2d-5e6f7a8b9c0e"
          }
        )

      match result with
      | Ok queued ->
        Expect.equal None queued.Body "body hidden"
        Expect.equal (SubmittedAs Pending) queued.Status "status"

        let sent = http.Requests |> List.exactlyOne
        Expect.equal "POST" sent.Method "method"

        Expect.equal
          "/api/v1/scheduled-commits/018f5a1e-6f8a-7c3a-9b2d-5e6f7a8b9c0e/submission"
          sent.Target
          "target"
      | Error error -> failtestf "%A" error
    }

    testAsync "the submit-now conflict surfaces verbatim" {
      let http = CapturingHttp(refused 409 conflictJson)
      let env = envFor http

      let! (result: Result<ScheduledCommit, TestError>) =
        ScheduledCommits.submitNow(
          env,
          {
            KeyFile = "key-file"
            ScheduledCommitId = "an-id"
          }
        )

      match result with
      | Error(Refused(409, None, message)) ->
        Expect.equal
          "This commit was already submitted or canceled. Refresh to see its status."
          message
          "message"
      | _ -> failtest "expected the conflict refusal"
    }

    testAsync "the ten-cap refusal surfaces verbatim" {
      let http = CapturingHttp(refused 422 capJson)
      let env = envFor http

      let! (result: Result<ScheduledCommit, TestError>) =
        ScheduledCommits.create(env, createRequest)

      match result with
      | Error(Refused(422, None, message)) ->
        Expect.equal
          "You can have up to 10 scheduled commits. Cancel one before scheduling another."
          message
          "message"
      | _ -> failtest "expected the cap refusal"
    }
  ]
