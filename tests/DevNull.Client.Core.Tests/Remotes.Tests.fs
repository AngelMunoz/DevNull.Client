module Tests.RemotesApi

open System
open Expecto
open DevNull.Client.Core
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types
open Tests.TestSupport

let envFor(http: IHttp) = Tests.Api.envFor http

let webmBytes: byte array = [| 0x1Auy; 0x45uy; 0xDFuy; 0xA3uy; 1uy; 2uy |]

let wavBytes: byte array = [|
  yield! "RIFF"B
  0uy
  0uy
  0uy
  0uy
  yield! "WAVE"B
  9uy
|]

/// The SHA-256 of `webmBytes`, in the lowercase hex the server hashes.
let webmHash =
  webmBytes
  |> System.Security.Cryptography.SHA256.HashData
  |> Convert.ToHexString
  |> _.ToLowerInvariant()

let barsJson = List.replicate 48 5 |> List.map string |> String.concat ","

let userJson =
  """{"id":"01a151db-7fee-727b-a7ce-c911a7556242","handle":"octocat","name":"The Octocat","avatar_url":null}"""

let messageJson =
  $"""{{"id":42,"user_id":"01a151db-7fee-727b-a7ce-c911a7556242","client_id":"018f4c1e-eeee-7c3a-9b2d-5e6f7a8b9c0d","body":"It was DNS.","body_segments":[{{"type":"text","text":"It was DNS."}}],"mentions":[{{"handle":"octocat","offset":0,"length":8}}],"reply_to_id":41,"reply_to":{{"id":41,"user_id":"01a151db-7fee-727b-a7ce-c911a7556242","body":"earlier","images_count":1,"has_voice":true}},"link_preview":{{"url":"https://example.test","title":null,"description":null,"image_url":null,"site_name":null,"is_pending":true,"embed":null}},"images":[{{"id":"018f4c1e-aaaa-7c3a-9b2d-5e6f7a8b9c0d","width":100,"height":50,"description":"pic","url":"/api/v1/remotes/images/018f4c1e-aaaa-7c3a-9b2d-5e6f7a8b9c0d"}}],"voice":{{"id":"018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d","duration_ms":1500,"waveform":[{barsJson}],"url":"/api/v1/remotes/voices/018f4c1e-bbbb-7c3a-9b2d-5e6f7a8b9c0d"}},"reactions":[{{"user_id":"01a151db-7fee-727b-a7ce-c911a7556242","emoji":"🎉"}}],"event":null,"subject_id":null,"user":null,"subject":null,"created_at":"2026-10-20T04:17:19Z"}}"""

let orgEventJson =
  $"""{{"id":43,"user_id":"01a151db-7fee-727b-a7ce-c911a7556242","client_id":"018f4c1e-dddd-7c3a-9b2d-5e6f7a8b9c0d","body":"","body_segments":[],"mentions":[],"reply_to_id":null,"images":[],"reactions":[],"event":"added","subject_id":"01a151db-7fee-727b-a7ce-c911a7556242","user":{userJson},"subject":{userJson},"created_at":"2026-10-20T04:18:19Z"}}"""

let threadJson =
  $"""{{"partner":{userJson},"messages":[{messageJson}],"has_older":true,"reactions_version":7,"reaction_changes":[{{"id":40,"reactions":[{{"user_id":"01a151db-7fee-727b-a7ce-c911a7556242","emoji":"🎉"}}]}}],"read_through_id":42,"partner_read_through_id":41,"partner_read_at":"2026-10-20T04:10:00.000000Z","partner_typing":true,"is_archived":false,"pinned_at":"2026-10-19T00:00:00.000000Z","muted_until_at":null,"access":"open"}}"""

let orgThreadJson =
  $"""{{"org":{{"id":"018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d","name":"the org"}},"messages":[{orgEventJson}],"has_older":false,"reactions_version":0,"reaction_changes":[],"read_through_id":43,"members":[{{"user":{userJson},"role":"maintainer","read_through_id":43}}],"typing":["octocat"],"is_archived":false,"pinned_at":null,"muted_until_at":null,"role":"maintainer","access":"open"}}"""

let inboxJson =
  $"""{{"data":[{{"id":"018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d","kind":"org","name":"the org","partner":null,"members":[{{"user":{userJson},"role":"maintainer"}}],"last_message":null,"last_message_at":"2026-10-20T04:00:00.000000Z","ahead":2,"last_incoming_message_id":44,"last_incoming_sender":"The Octocat","partner_read_through_id":0,"partner_read_at":null,"is_archived":false,"pinned_at":"2026-10-19T00:00:00.000000Z","muted_until_at":null}},{{"id":"018f4c1e-org2-7c3a-9b2d-5e6f7a8b9c0d","kind":"direct","name":null,"partner":{userJson},"members":null,"last_message":{messageJson},"last_message_at":"2026-10-18T04:00:00.000000Z","ahead":0,"last_incoming_message_id":null,"last_incoming_sender":null,"partner_read_through_id":9,"partner_read_at":null,"is_archived":true,"pinned_at":null,"muted_until_at":"2026-10-22T00:00:00.000000Z"}}],"has_more":true,"unread_count":2,"requests_count":1}}"""

let meJson =
  $"""{{"data":{{"id":"01a151db-7fee-727b-a7ce-c911a7556242","handle":"octocat","name":"The Octocat","avatar_url":null,"github_url":"https://github.com/octocat","html_url":"https://dev-null.sh/@octocat","feed_url":"https://dev-null.sh/@octocat/feed","created_at":"2026-01-01T00:00:00Z","pasteis_mode":false,"pasteis_remaining":5,"pasteis_resets_at":"2026-10-21T00:00:00Z","pasteis_received":0,"preferred_lane":null,"automatic_lane":2,"sponsors_url":null,"github_synced_at":null,"remotes":{{"from":"everyone","pull_receipts":true,"requests":true,"everyone":true,"unread_count":3}}}},"unread_notifications_count":1}}"""

let previewJson =
  """{"data":{"body":"It was DNS.","body_segments":[{"type":"text","text":"It was DNS."}],"preview_segments":null,"mentions":[]}}"""

let parseOutcome<'a>(outcome: Result<'a, TestError>) : 'a =
  match outcome with
  | Ok value -> value
  | Error error -> failtestf "%A" error

/// Runs one call against an empty success and asserts what went out.
let expectCall
  (call: IHttp -> Async<Result<'value, TestError>>)
  (method: string)
  (target: string)
  (body: string)
  =
  let http = CapturingHttp(ok "")
  call http |> Async.RunSynchronously |> ignore

  let sent = http.Requests |> List.exactlyOne
  Expect.equal method sent.Method "method"
  Expect.equal target sent.Target "target"
  Expect.equal body (bodyText sent) "body"

let voiceTests =
  testList "Remotes.voices" [
    test "a webm recording stages with its hash" {
      let outcome: Result<Commit.Voice, TestError> =
        Commit.Voices.parse("voice.webm", webmBytes)

      match outcome with
      | Ok voice ->
        Expect.equal "audio/webm" voice.ContentType "content type"
        Expect.equal webmHash voice.Hash "hash"
      | Error error -> failtestf "%A" error
    }

    test "a wav recording stages with its hash" {
      let outcome: Result<Commit.Voice, TestError> =
        Commit.Voices.parse("voice.wav", wavBytes)

      match outcome with
      | Ok voice -> Expect.equal "audio/wav" voice.ContentType "content type"
      | Error error -> failtestf "%A" error
    }

    test "another format is refused" {
      match Commit.Voices.parse("voice.mp3", [| 1uy |]) with
      | Error(UnsupportedVoice fileName) ->
        Expect.equal "voice.mp3" fileName "name"
      | outcome -> failtestf "expected UnsupportedVoice, got %A" outcome
    }

    test "an oversized recording is refused" {
      let big = Array.create (Commit.Voices.MaxBytes + 1) 0x1Auy
      big.[1] <- 0x45uy
      big.[2] <- 0xDFuy
      big.[3] <- 0xA3uy

      match Commit.Voices.parse("voice.webm", big) with
      | Error(VoiceTooLarge(fileName, size)) ->
        Expect.equal "voice.webm" fileName "name"
        Expect.equal (Commit.Voices.MaxBytes + 1) size "size"
      | outcome -> failtestf "expected VoiceTooLarge, got %A" outcome
    }

    test "a waveform takes 48 bars between 0 and 100" {
      let bars = parseOutcome(Commit.Voices.parseWaveform(List.replicate 48 5))
      Expect.equal 48 (Commit.Voices.bars bars |> List.length) "bars"

      match Commit.Voices.parseWaveform(List.replicate 47 5) with
      | Error(WrongWaveformCount count) -> Expect.equal 47 count "count"
      | outcome -> failtestf "expected WrongWaveformCount, got %A" outcome

      match Commit.Voices.parseWaveform(5 :: List.replicate 47 101) with
      | Error(WaveformBarOutOfRange bar) -> Expect.equal 101 bar "bar"
      | outcome -> failtestf "expected WaveformBarOutOfRange, got %A" outcome
    }
  ]

let refusalTests =
  testList "Remotes.push refusals" [
    test "an over-long body is refused before anything is sent" {
      let request: Remotes.PushRequest = {
        KeyFile = "key-file"
        Target = Remotes.Partner "octocat"
        Body = Some(String('x', 2001))
        ClientId = ClientIds.fresh()
        ReplyTo = None
        Card = Remotes.FirstLink
        Images = []
        ImageDescriptions = []
        Voice = None
      }

      let http = CapturingHttp(ok "")

      let outcome = Remotes.push(envFor http, request) |> Async.RunSynchronously

      Expect.isEmpty http.Requests "nothing is sent"

      match outcome with
      | Error(MessageBodyTooLong(_, length)) ->
        Expect.equal 2001 length "length"
      | error -> failtestf "expected MessageBodyTooLong, got %A" error
    }

    test "a voice note beside a body is refused" {
      let waveform =
        parseOutcome(Commit.Voices.parseWaveform(List.replicate 48 5))

      let request: Remotes.PushRequest = {
        KeyFile = "key-file"
        Target = Remotes.Partner "octocat"
        Body = Some "hello"
        ClientId = ClientIds.fresh()
        ReplyTo = None
        Card = Remotes.FirstLink
        Images = []
        ImageDescriptions = []
        Voice =
          Some(
            {
              FileName = "voice.webm"
              Bytes = webmBytes
            },
            waveform
          )
      }

      let outcome =
        Remotes.push(envFor(CapturingHttp(ok "")), request)
        |> Async.RunSynchronously

      match outcome with
      | Error(VoiceProhibited VoiceWithBody) -> ()
      | error -> failtestf "expected VoiceWithBody, got %A" error
    }

    test "a voice note beside images is refused" {
      let waveform =
        parseOutcome(Commit.Voices.parseWaveform(List.replicate 48 5))

      let request: Remotes.PushRequest = {
        KeyFile = "key-file"
        Target = Remotes.Partner "octocat"
        Body = None
        ClientId = ClientIds.fresh()
        ReplyTo = None
        Card = Remotes.FirstLink
        Images = [ Tests.Api.stagedImage "pic.png" ]
        ImageDescriptions = []
        Voice =
          Some(
            {
              FileName = "voice.webm"
              Bytes = webmBytes
            },
            waveform
          )
      }

      let outcome =
        Remotes.push(envFor(CapturingHttp(ok "")), request)
        |> Async.RunSynchronously

      match outcome with
      | Error(VoiceProhibited VoiceWithImages) -> ()
      | error -> failtestf "expected VoiceWithImages, got %A" error
    }
  ]

let pushTests =
  testList "Remotes.push" [
    testAsync "pushes text as one signed json post" {
      let clientId = ClientIds.fresh()

      let request: Remotes.PushRequest = {
        KeyFile = "key-file"
        Target = Remotes.Partner "octocat"
        Body = Some "It was DNS."
        ClientId = clientId
        ReplyTo = Some 41
        Card = Remotes.NoCard
        Images = []
        ImageDescriptions = []
        Voice = None
      }

      let body =
        $"{{\"client_id\":\"{ClientIds.value clientId}\",\"body\":\"It was DNS.\",\"reply_to_id\":41,\"link_preview_url\":null}}"

      let signer =
        signerWith(fun signed ->
          Expect.equal "devnull-api" signed.Namespace "namespace"

          Expect.equal
            $"1760000000\n00112233445566778899aabbccddeeff\nPOST\n/api/v1/remotes/@octocat/messages\n{body}"
            signed.Payload
            "signed message"

          Ok "signature")

      let env =
        Tests.Api.redeemEnv
          (CapturingHttp(ok $"{{\"data\":{messageJson}}}"))
          signer

      let! (result: Result<Message, TestError>) = Remotes.push(env, request)

      match result with
      | Ok message ->
        Expect.equal 42 message.Id "id"
        Expect.equal "It was DNS." message.Body "body"
        Expect.equal (Some 41) message.ReplyToId "reply"

        let parent = message.ReplyTo |> Option.get
        Expect.equal true parent.HasVoice "the ghost reports the voice note"
      | Error error -> failtestf "%A" error
    }

    testAsync "pushes a voice note as a multipart post" {
      let clientId = ClientIds.fresh()

      let waveform =
        parseOutcome(Commit.Voices.parseWaveform(List.replicate 48 5))

      let request: Remotes.PushRequest = {
        KeyFile = "key-file"
        Target = Remotes.Org "018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d"
        Body = None
        ClientId = clientId
        ReplyTo = None
        Card = Remotes.FirstLink
        Images = []
        ImageDescriptions = []
        Voice =
          Some(
            {
              FileName = "voice.webm"
              Bytes = webmBytes
            },
            waveform
          )
      }

      let payload =
        $"{{\"client_id\":\"{ClientIds.value clientId}\",\"voice_waveform\":[{barsJson}]}}"

      let signer =
        signerWith(fun signed ->
          Expect.equal "devnull-api-images" signed.Namespace "namespace"

          Expect.equal
            $"1760000000\n00112233445566778899aabbccddeeff\nPOST\n/api/v1/remotes/orgs/018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d/messages\n{webmHash}\n{payload}"
            signed.Payload
            "signed message"

          Ok "signature")

      let http = CapturingHttp(ok $"{{\"data\":{messageJson}}}")
      let env = Tests.Api.redeemEnv http signer

      let! (_: Result<Message, TestError>) = Remotes.push(env, request)

      let sent = http.Requests |> List.exactlyOne
      let form = multipartForm sent

      match form.Files with
      | [ payloadPart; voicePart ] ->
        Expect.equal "payload" payloadPart.Name "payload part"
        Expect.equal "message.json" payloadPart.FileName "payload file"

        Expect.equal
          payload
          (Text.Encoding.UTF8.GetString payloadPart.Content)
          "payload"

        Expect.equal "voice" voicePart.Name "voice part"
        Expect.equal "audio/webm" voicePart.ContentType "voice content type"
      | parts -> failtestf "expected a payload and a voice part, got %A" parts
    }
  ]

let decodeTests =
  testList "Remotes.decode" [
    test "reads one thread with its reaction changes" {
      match Thread.decode $"{{\"data\":{threadJson}}}" with
      | Ok thread ->
        Expect.equal PushOpen thread.Access "access"
        Expect.equal true thread.PartnerTyping "typing"
        Expect.equal true thread.HasOlder "has older"
        Expect.equal 7 thread.ReactionsVersion "version"

        let change = thread.ReactionChanges |> List.exactlyOne
        Expect.equal 40 change.MessageId "changed message"

        let message = thread.Messages |> List.exactlyOne

        Expect.equal
          (Some 1500)
          (message.Voice |> Option.map _.DurationMs)
          "voice"

        let bars = message.Voice |> Option.get |> _.Waveform
        Expect.equal 48 bars.Length "bars"

        let reaction = message.Reactions |> List.exactlyOne
        Expect.equal "🎉" reaction.Emoji "emoji"
        Expect.equal true thread.PinnedAt.IsSome "pinned"
        Expect.equal None thread.MutedUntilAt "muted"
      | Error failure -> failtestf "%A" failure
    }

    test "reads one org thread with its event message" {
      match OrgThread.decode $"{{\"data\":{orgThreadJson}}}" with
      | Ok thread ->
        Expect.equal (Some "the org") thread.Org.Name "name"
        Expect.equal (Some Maintainer) thread.Role "role"

        let threadMember = thread.Members |> List.exactlyOne
        Expect.equal (Some Maintainer) threadMember.Role "member role"
        Expect.equal [ "octocat" ] thread.Typing "typing"

        let message = thread.Messages |> List.exactlyOne
        Expect.equal (Some Added) message.Event "event"
        Expect.equal true message.Subject.IsSome "the event names its subject"
      | Error failure -> failtestf "%A" failure
    }

    test "reads one inbox page with a direct and an org row" {
      match Inbox.decode inboxJson with
      | Ok inbox ->
        Expect.equal true inbox.HasMore "has more"
        Expect.equal 2 inbox.UnreadCount "unread"
        Expect.equal 1 inbox.RequestsCount "requests"
        Expect.isEmpty (Map.toList inbox.Pins) "pins"

        match
          Inbox.decode(
            inboxJson.Replace(
              "\"has_more\":true",
              "\"pins\":{\"018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d\":null,\"018f4c1e-org2-7c3a-9b2d-5e6f7a8b9c0d\":\"2026-10-19T00:00:00.000000Z\"},\"has_more\":true"
            )
          )
        with
        | Ok pinned ->
          Expect.equal
            [
              ("018f4c1e-org2-7c3a-9b2d-5e6f7a8b9c0d",
               Some(DateTimeOffset.Parse "2026-10-19T00:00:00.000000Z"))
            ]
            (pinned.Pins |> Map.toList |> List.filter(fun (_, at) -> at.IsSome))
            "the pin map never read"
        | Error failure -> failtestf "%A" failure

        match inbox.Conversations with
        | [ org; direct ] ->
          Expect.equal Org org.Kind "org kind"
          Expect.equal (Some "the org") org.Name "org name"
          Expect.equal 2 org.Ahead "ahead"
          Expect.equal true org.PinnedAt.IsSome "org pinned"

          let orgMember = org.Members |> Option.get |> List.exactlyOne
          Expect.equal (Some Maintainer) orgMember.Role "member"

          Expect.equal Direct direct.Kind "direct kind"

          Expect.equal "octocat" (direct.Partner |> Option.get).Handle "partner"

          Expect.equal true direct.IsArchived "archived"
          Expect.equal 9 direct.PartnerReadThroughId "partner pulled"

          let message = direct.LastMessage |> Option.get
          Expect.equal 42 message.Id "last message"
        | rows -> failtestf "expected two rows, got %A" rows
      | Error failure -> failtestf "%A" failure
    }

    test "reads the smaller envelopes" {
      match PinResult.decode """{"pinned_at":null}""" with
      | Ok pin -> Expect.equal None pin.PinnedAt "unpinned"
      | Error failure -> failtestf "%A" failure

      match
        ThreadMuteResult.decode
          """{"muted_until_at":"2026-10-21T12:00:00.000000Z"}"""
      with
      | Ok mute -> Expect.equal true mute.MutedUntilAt.IsSome "muted"
      | Error failure -> failtestf "%A" failure

      match
        CreatedOrg.decode
          """{"org":{"id":"018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d"}}"""
      with
      | Ok org ->
        Expect.equal "018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d" org.Id "id"
      | Error failure -> failtestf "%A" failure

      match
        ReactionUses.decode
          """{"usage":[{"emoji":"🎉","count":3},{"emoji":"🚀","count":1}]}"""
      with
      | Ok uses ->
        Expect.equal 2 uses.Length "uses"

        let most = uses |> List.head
        Expect.equal ("🎉", 3) (most.Emoji, most.Count) "most used"
      | Error failure -> failtestf "%A" failure

      match
        Reactions.decode
          """{"reactions":[{"user_id":"01a151db-7fee-727b-a7ce-c911a7556242","emoji":"🎉"}]}"""
      with
      | Ok reactions -> Expect.equal 1 reactions.Length "reactions"
      | Error failure -> failtestf "%A" failure

      match
        RemoteCandidates.decode
          $"""{{"candidates":[{{"user":{userJson},"reason":"doesn't follow you"}},{{"user":{userJson},"reason":null}}]}}"""
      with
      | Ok(reached :: openCandidate :: []) ->
        Expect.equal (Some "doesn't follow you") reached.Reason "reason"
        Expect.equal None openCandidate.Reason "no reason"
      | outcome -> failtestf "expected two candidates, got %A" outcome

      match MessageLinkPreview.decode """{"link_preview":null}""" with
      | Ok None -> ()
      | outcome -> failtestf "expected no card, got %A" outcome
    }

    test "reads the account with its remotes settings" {
      match Me.decode meJson with
      | Ok me ->
        let remotes = me.Remotes |> Option.get
        Expect.equal FromEveryone remotes.From "from"
        Expect.equal true remotes.PullReceipts "pull receipts"
        Expect.equal 3 remotes.UnreadCount "unread"
      | Error failure -> failtestf "%A" failure
    }

    test "reads a draft preview" {
      match CommitPreview.decode previewJson with
      | Ok preview ->
        Expect.equal "It was DNS." preview.Body "body"
        Expect.equal None preview.PreviewSegments "no excerpt"
      | Error failure -> failtestf "%A" failure
    }
  ]

let endpointTests =
  testList "Remotes.endpoints" [
    test "reads the inbox with its tab and search" {
      expectCall
        (fun http ->
          Remotes.inbox(
            envFor http,
            {
              KeyFile = "key-file"
              Tab = TabFetched
              Search = Some "@Octo"
              Before = None
            }
          ))
        "GET"
        "/api/v1/remotes?tab=fetched&q=%40Octo"
        ""
    }

    test "polls one thread after a message" {
      expectCall
        (fun http ->
          Remotes.thread(
            envFor http,
            {
              KeyFile = "key-file"
              Partner = "octo cat"
              After = Some 40
              Before = None
              ReactionsAfter = Some 7
            }
          ))
        "GET"
        "/api/v1/remotes/@octo%20cat/messages?after=40&reactions_after=7"
        ""
    }

    test "scrolls one org thread back" {
      expectCall
        (fun http ->
          Remotes.orgThread(
            envFor http,
            {
              KeyFile = "key-file"
              OrgId = "018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d"
              After = None
              Before = Some 20
              ReactionsAfter = None
            }
          ))
        "GET"
        "/api/v1/remotes/orgs/018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d/messages?before=20"
        ""
    }

    test "pulls a thread through a message" {
      expectCall
        (fun http ->
          Remotes.markRead(
            envFor http,
            {
              KeyFile = "key-file"
              Target = Remotes.Partner "octocat"
              Through = 42
            }
          ))
        "PUT"
        "/api/v1/remotes/@octocat/read"
        "{\"through\":42}"
    }

    test "says the user is typing" {
      expectCall
        (fun http ->
          Remotes.typing(
            envFor http,
            {
              KeyFile = "key-file"
              Target = Remotes.Org "018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d"
            }
          ))
        "PUT"
        "/api/v1/remotes/orgs/018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d/typing"
        ""
    }

    test "pings a partner" {
      expectCall
        (fun http ->
          Remotes.ping(
            envFor http,
            {
              KeyFile = "key-file"
              Target = Remotes.Partner "octocat"
            }
          ))
        "POST"
        "/api/v1/remotes/@octocat/ping"
        ""
    }

    test "stashes and unstashes a thread" {
      expectCall
        (fun http ->
          Remotes.stash(
            envFor http,
            {
              KeyFile = "key-file"
              Target = Remotes.Partner "octocat"
            }
          ))
        "POST"
        "/api/v1/remotes/@octocat/archive"
        ""

      expectCall
        (fun http ->
          Remotes.unstash(
            envFor http,
            {
              KeyFile = "key-file"
              Target = Remotes.Partner "octocat"
            }
          ))
        "DELETE"
        "/api/v1/remotes/@octocat/archive"
        ""
    }

    test "mutes for a week and pins" {
      expectCall
        (fun http ->
          Remotes.mute(
            envFor http,
            {
              KeyFile = "key-file"
              Target = Remotes.Partner "octocat"
              Duration = MuteWeek
            }
          ))
        "PUT"
        "/api/v1/remotes/@octocat/mute"
        "{\"duration\":\"week\"}"

      expectCall
        (fun http ->
          Remotes.pin(
            envFor http,
            {
              KeyFile = "key-file"
              Target = Remotes.Org "018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d"
              Pinned = true
            }
          ))
        "PUT"
        "/api/v1/remotes/orgs/018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d/pin"
        "{\"pinned\":true}"
    }

    test "merges and drops a fetched request" {
      expectCall
        (fun http ->
          Remotes.merge(
            envFor http,
            {
              KeyFile = "key-file"
              Partner = "octocat"
            }
          ))
        "POST"
        "/api/v1/remotes/@octocat/merge"
        ""

      expectCall
        (fun http ->
          Remotes.drop(
            envFor http,
            {
              KeyFile = "key-file"
              Partner = "octocat"
            }
          ))
        "POST"
        "/api/v1/remotes/@octocat/drop"
        ""
    }

    test "reacts and takes a reaction back" {
      let request: Remotes.ReactionRequest = {
        KeyFile = "key-file"
        MessageId = 42
        Emoji = "🎉"
      }

      expectCall
        (fun http -> Remotes.react(envFor http, request))
        "POST"
        "/api/v1/remotes/messages/42/reactions"
        ("{\"emoji\":\"\\uD83C\\uDF89\"}")

      expectCall
        (fun http -> Remotes.unreact(envFor http, request))
        "DELETE"
        "/api/v1/remotes/messages/42/reactions"
        ("{\"emoji\":\"\\uD83C\\uDF89\"}")
    }

    test "asks for one message's link card" {
      expectCall
        (fun http ->
          Remotes.linkPreview(
            envFor http,
            { KeyFile = "key-file"; MessageId = 42 }
          ))
        "GET"
        "/api/v1/remotes/messages/42/link-preview"
        ""
    }

    test "searches candidates for a new org" {
      expectCall
        (fun http ->
          Remotes.candidates(
            envFor http,
            {
              KeyFile = "key-file"
              Search = Some "octo"
              Org = None
              For = Some Remotes.ForOrg
            }
          ))
        "GET"
        "/api/v1/remotes/candidates?q=octo&for=org"
        ""
    }

    test "starts, renames, and staffs an org" {
      expectCall
        (fun http ->
          Remotes.createOrg(
            envFor http,
            {
              KeyFile = "key-file"
              Name = Some "the org"
              Members = [ "octocat"; "torvalds" ]
            }
          ))
        "POST"
        "/api/v1/remotes/orgs"
        "{\"members\":[\"octocat\",\"torvalds\"],\"name\":\"the org\"}"

      expectCall
        (fun http ->
          Remotes.renameOrg(
            envFor http,
            {
              KeyFile = "key-file"
              OrgId = "018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d"
              Name = None
            }
          ))
        "PUT"
        "/api/v1/remotes/orgs/018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d"
        "{\"name\":null}"

      expectCall
        (fun http ->
          Remotes.addMembers(
            envFor http,
            {
              KeyFile = "key-file"
              OrgId = "018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d"
              Members = [ "octocat" ]
              ShareHistory = false
            }
          ))
        "POST"
        "/api/v1/remotes/orgs/018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d/members"
        "{\"members\":[\"octocat\"],\"share_history\":false}"

      expectCall
        (fun http ->
          Remotes.setMemberRole(
            envFor http,
            {
              KeyFile = "key-file"
              OrgId = "018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d"
              Member = "octocat"
              Role = Contributor
            }
          ))
        "PUT"
        "/api/v1/remotes/orgs/018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d/members/octocat"
        "{\"role\":\"contributor\"}"

      expectCall
        (fun http ->
          Remotes.removeMember(
            envFor http,
            {
              KeyFile = "key-file"
              OrgId = "018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d"
              Member = "octocat"
            }
          ))
        "DELETE"
        "/api/v1/remotes/orgs/018f4c1e-org1-7c3a-9b2d-5e6f7a8b9c0d/members/octocat"
        ""
    }

    test "saves the remotes settings" {
      expectCall
        (fun http ->
          Remotes.setEnabled(
            envFor http,
            { KeyFile = "key-file"; Enabled = true }
          ))
        "PUT"
        "/api/v1/me/remotes"
        "{\"enabled\":true}"

      expectCall
        (fun http ->
          Remotes.setPreferences(
            envFor http,
            {
              KeyFile = "key-file"
              From = FromFollowing
              PullReceipts = false
            }
          ))
        "PUT"
        "/api/v1/me/remote-preferences"
        "{\"from\":\"following\",\"pull_receipts\":false}"
    }
  ]

let previewTests =
  testList "Remotes.preview" [
    testAsync "previews a draft without keeping it" {
      let http = CapturingHttp(ok previewJson)

      let! (result: Result<CommitPreview, TestError>) =
        Commits.preview(
          envFor http,
          {
            KeyFile = "key-file"
            Body = "It was DNS."
          }
        )

      match result with
      | Ok preview ->
        Expect.equal "It was DNS." preview.Body "body"

        let sent = http.Requests |> List.exactlyOne
        Expect.equal "POST" sent.Method "method"
        Expect.equal "/api/v1/commits/preview" sent.Target "target"
        Expect.equal "{\"body\":\"It was DNS.\"}" (bodyText sent) "body"
      | Error error -> failtestf "%A" error
    }
  ]

let tests =
  testList "Api.remotes" [
    voiceTests
    refusalTests
    pushTests
    decodeTests
    endpointTests
    previewTests
  ]
