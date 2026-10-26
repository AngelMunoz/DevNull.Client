module DevNull.Client.UI.Tests.Remotes

// The remotes store: the whole chat state machine over a scripted
// api — polls merge and dedupe, refused pushes retry under their own
// client id, a live poll cannot clobber a reaction in flight, pins
// rebuild the inbox depth, an older save never undoes a newer mute,
// and the watcher's timers all stop on dispose.

open System
open System.Collections.Generic
open System.Threading.Tasks
open System.Reactive.Subjects
open Expecto
open FSharp.UMX
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let viewerId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

let partnerId = "01a151db-7fee-727b-a7ce-c911a7556243"

let userOf(id: string, username: string, name: string) : User = {
  Id = id
  Username = username
  Name = name
  AvatarUrl = None
  Lane = None
  CommitsCount = None
  FollowersCount = None
  FollowingCount = None
  IsFollowed = None
  FollowsYou = None
}

let hubot = userOf(partnerId, "hubot", "The Hubot")

let bob = userOf("01a151db-b0b0-727b-a7ce-c911a7556243", "bob", "Bob")

let jsonBool(flag: bool) = if flag then "true" else "false"

let userJsonOf(id: string, username: string, name: string) =
  $"""{{"id":"{id}","username":"{username}","name":"{name}","avatar_url":null}}"""

let hubotJson = userJsonOf(partnerId, "hubot", "The Hubot")

let bobJson = userJsonOf(bob.Id, "bob", "Bob")

let messageJson(id: int, userId: string, body: string, clientId: string) =
  let segments =
    if body = "" then
      "[]"
    else
      $"""[{{"type":"text","text":"{body}"}}]"""

  $"""{{"id":{id},"user_id":"{userId}","client_id":"{clientId}","body":"{body}","body_segments":{segments},"mentions":[],"reply_to_id":null,"images":[],"reactions":[],"event":null,"subject_id":null,"user":null,"subject":null,"created_at":"2026-10-20T04:00:00Z"}}"""

let threadJson
  (
    messages: string list,
    readThrough: int,
    changes: string,
    typing: bool,
    version: int
  ) =
  $"""{{"data":{{"partner":{hubotJson},"messages":[{String.concat "," messages}],"has_older":false,"reactions_version":{version},"reaction_changes":[{changes}],"read_through_id":{readThrough},"partner_read_through_id":{readThrough - 1},"partner_read_at":null,"partner_typing":{jsonBool typing},"is_archived":false,"pinned_at":null,"muted_until_at":null,"access":"open"}}}}"""

let reactionChangeJson(messageId: int, userId: string, emoji: string) =
  $"""{{"id":{messageId},"reactions":[{{"user_id":"{userId}","emoji":"{emoji}"}}]}}"""

let conversationJson(partner: string, ahead: int, at: string) =
  $"""{{"id":"row-{at}","kind":"direct","name":null,"partner":{partner},"members":null,"last_message":null,"last_message_at":"{at}","ahead":{ahead},"last_incoming_message_id":null,"last_incoming_sender":null,"partner_read_through_id":0,"partner_read_at":null,"is_archived":false,"pinned_at":null,"muted_until_at":null}}"""

let inboxPageJson
  (rows: string list, hasMore: bool, unread: int, requests: int)
  =
  $"""{{"data":[{String.concat "," rows}],"has_more":{jsonBool hasMore},"unread_count":{unread},"requests_count":{requests}}}"""

let orgThreadJson =
  $"""{{"data":{{"org":{{"id":"org-1","name":"the org"}},"messages":[],"has_older":false,"reactions_version":0,"reaction_changes":[],"read_through_id":0,"members":[{{"user":{hubotJson},"role":"maintainer","read_through_id":0}}],"typing":["hubot"],"is_archived":false,"pinned_at":null,"muted_until_at":null,"role":"maintainer","access":"open"}}}}"""

let meWithRemotesJson =
  meJson.Replace(
    "\"remotes\":null",
    "\"remotes\":{\"from\":\"everyone\",\"pull_receipts\":true,\"requests\":true,\"everyone\":true,\"unread_count\":3}"
  )

let messagesRoute = "/api/v1/remotes/@hubot/messages"

let reactionsJson(userIds: string list, emoji: string) =
  let rows =
    userIds
    |> List.map(fun userId -> $"""{{"user_id":"{userId}","emoji":"{emoji}"}}""")
    |> String.concat ","

  $"""{{"reactions":[{rows}]}}"""

/// One http worker that answers from a script: each route's replies
/// come off a queue and the last one repeats, a route can be held
/// back until its gate opens, and every request is recorded. No test
/// reaches the network.
type ScriptedApi() =

  let requests = ResizeArray<HttpRequest>()

  let queues = Dictionary<string, Queue<int * string>>()

  let gates = Dictionary<string, TaskCompletionSource<unit>>()

  let routeOf(target: string) = target.Split('?')[0]

  /// Scripts one route: each request pops the next reply, and the
  /// last reply repeats.
  member _.Answer(route: string, [<ParamArray>] replies: (int * string) array) =
    queues[route] <- Queue(replies)

  /// Holds one route's replies back until `Open` fires.
  member _.Hold(route: string) =
    gates[route] <-
      TaskCompletionSource<unit>(
        TaskCreationOptions.RunContinuationsAsynchronously
      )

  /// Lets a held route answer.
  member _.Open(route: string) =
    match gates.TryGetValue route with
    | true, gate -> gate.TrySetResult() |> ignore
    | false, _ -> ()

  member _.Requests = requests |> Seq.toList

  member _.RequestsTo(route: string) =
    requests
    |> Seq.filter(fun request -> request.Target.StartsWith route)
    |> Seq.toList

  interface IHttp with

    member _.Send(request: HttpRequest) = async {
      requests.Add request

      let route = routeOf request.Target

      match gates.TryGetValue route with
      | true, gate -> do! Async.AwaitTask gate.Task
      | false, _ -> ()

      let (status, body) =
        match queues.TryGetValue route with
        | true, queue when queue.Count > 1 -> queue.Dequeue()
        | true, queue when queue.Count = 1 -> queue.Peek()
        | _ -> 200, "{}"

      return
        Ok {
          Status = status
          Headers = []
          Body = body
        }
    }

    member _.GetBytes(target: string) = async {
      return
        Error(
          {
            Message = $"no canned bytes for {target}"
          }
          : TransportFailure
        )
    }

let bodyText(request: HttpRequest) =
  match request.Body with
  | Text text -> text
  | other -> failtestf "expected a text body, got %A" other

/// The store over a scripted api, with the session it refreshes. The
/// first pump starts the headless session off the main thread, so the
/// session claims the global dispatcher before any store emission can
/// touch it from a pool thread (tests/README.md).
let remotesSession(api: ScriptedApi) = async {
  do! pumpAsync()

  let session = signedInStore()
  let env = TestEnv(session, api :> IHttp)

  return session, (env :> IProvideRemotes).Remotes
}

let threadIds(store: RemotesStore) : int list =
  store.Current.Threads["hubot"].Messages |> List.map _.Id

let outgoing(body: string) : OutgoingMessage = {
  Body = Some body
  ReplyTo = None
  Card = LinkCard.FirstLink
  Images = []
  ImageDescriptions = []
  Voice = None
}

let tests =
  testList "remotes store" [
    testCaseAsync
      "overlapping thread polls merge without duplicates"
      (async {
        let api = ScriptedApi()

        let pageOne =
          threadJson(
            [
              messageJson(40, partnerId, "earlier", "c-40")
              messageJson(41, partnerId, "middle", "c-41")
              messageJson(42, partnerId, "latest", "c-42")
            ],
            40,
            "",
            false,
            0
          )

        let pageTwo =
          threadJson(
            [
              messageJson(41, partnerId, "middle", "c-41")
              messageJson(42, partnerId, "latest", "c-42")
              messageJson(43, partnerId, "fresh", "c-43")
            ],
            43,
            "",
            false,
            0
          )

        api.Answer(messagesRoute, (200, pageOne), (200, pageTwo))

        let! _, store = remotesSession api
        let target = Partner hubot

        do! store.PollThread target
        do! store.PollThread target

        let thread = store.Current.Threads["hubot"]

        Expect.equal
          (threadIds store)
          [ 40; 41; 42; 43 ]
          "the pages never merged"

        Expect.equal thread.ReadThroughId 43 "the read through never advanced"

        Expect.equal thread.Head 40 "the head never took the opening read"

        Expect.equal thread.HeadUntil 42 "the head never took the opening tail"

        let polls = api.RequestsTo messagesRoute

        Expect.equal polls.Length 2 "two polls never went out"

        Expect.isTrue
          (polls[1].Target.Contains "after=42")
          "the second poll never asked for the tail"
      })

    testCaseAsync
      "a poll drops the pending send whose message came back"
      (async {
        let api = ScriptedApi()

        api.Answer(messagesRoute, (500, "{}"))

        let! _, store = remotesSession api
        let target = Partner hubot

        do! store.Push(target, outgoing "staged")

        let staged = store.Current.Threads["hubot"].Pending

        Expect.equal staged.Length 1 "the staged send never landed"

        Expect.isTrue staged[0].Rejected "the refusal never marked the send"

        let clientId = UMX.untag staged[0].ClientId

        api.Answer(
          messagesRoute,
          (200,
           threadJson(
             [ messageJson(50, viewerId, "echoed", clientId) ],
             50,
             "",
             false,
             0
           ))
        )

        do! store.PollThread target

        let thread = store.Current.Threads["hubot"]

        Expect.equal
          thread.Pending.Length
          0
          "the echoed message never dropped the stage"

        Expect.equal (threadIds store) [ 50 ] "the echoed message never merged"
      })

    testCaseAsync
      "a refused push retries under its own client id"
      (async {
        let api = ScriptedApi()

        api.Answer(messagesRoute, (500, "{}"))

        let! _, store = remotesSession api
        let target = Partner hubot

        do! store.Push(target, outgoing "first try")

        let staged = store.Current.Threads["hubot"].Pending[0]

        let clientId = UMX.untag staged.ClientId

        let echoed = messageJson(44, viewerId, "again", clientId)

        api.Answer(messagesRoute, (200, $"{{\"data\":{echoed}}}"), (200, "{}"))

        do! store.Retry(target, staged.ClientId)

        let pushes =
          api.RequestsTo messagesRoute
          |> List.filter(fun request -> request.Method = "POST")

        Expect.equal pushes.Length 2 "the retry never reused the push"

        for push in pushes do
          let body = bodyText push

          Expect.isTrue
            (body.Contains clientId)
            "a push never carried the staged client id"

        Expect.equal
          store.Current.Threads["hubot"].Pending.Length
          0
          "the stage never cleared"

        Expect.equal (threadIds store) [ 44 ] "the message never merged"
      })

    testCaseAsync
      "a poll never resurrects a reaction mid-flight"
      (async {
        let api = ScriptedApi()

        let opened =
          threadJson(
            [ messageJson(40, partnerId, "hello", "c-40") ],
            40,
            "",
            false,
            0
          )

        let later =
          threadJson([], 40, reactionChangeJson(40, partnerId, "🎉"), false, 1)

        api.Answer(messagesRoute, (200, opened), (200, later))

        let reactionsRoute = "/api/v1/remotes/messages/40/reactions"

        api.Answer(reactionsRoute, (200, reactionsJson([ viewerId ], "🎉")))

        api.Hold reactionsRoute

        let! _, store = remotesSession api
        let target = Partner hubot

        do! store.PollThread target

        let! toggle = Async.StartChild(store.ToggleReaction(target, 40, "🎉"))

        let! held =
          waitForAsync(
            5000,
            (fun () -> api.RequestsTo reactionsRoute),
            (fun requests -> not requests.IsEmpty)
          )

        Expect.isTrue held "the reaction request never went out"

        do! store.PollThread target

        let midFlight = store.Current.Threads["hubot"].Messages[0].Reactions

        Expect.equal midFlight.Length 1 "the poll clobbered the live reaction"

        Expect.equal
          midFlight[0].UserId
          viewerId
          "the live reaction never stayed"

        api.Open reactionsRoute

        let! took = toggle

        Expect.isTrue took "the server never took the reaction"

        let settled = store.Current.Threads["hubot"].Messages[0].Reactions

        Expect.equal
          (settled |> List.map(fun reaction -> reaction.UserId, reaction.Emoji))
          [ (viewerId, "🎉") ]
          "the settled reactions never matched the server"
      })

    testCaseAsync
      "mark-read goes out only while focused"
      (async {
        let api = ScriptedApi()

        let opened =
          threadJson(
            [
              messageJson(40, partnerId, "one", "c-40")
              messageJson(41, partnerId, "two", "c-41")
              messageJson(42, partnerId, "three", "c-42")
            ],
            40,
            "",
            false,
            0
          )

        api.Answer(messagesRoute, (200, opened))
        api.Answer("/api/v1/remotes/@hubot/read", (200, ""))

        let! _, store = remotesSession api
        let target = Partner hubot

        do! store.PollThread target

        let! unfocused = store.MarkRead(target, false)

        Expect.isFalse unfocused "an unfocused read went out"

        Expect.equal
          (api.RequestsTo "/api/v1/remotes/@hubot/read").Length
          0
          "an unfocused read request went out"

        let! focused = store.MarkRead(target, true)

        Expect.isTrue focused "a focused read never went out"

        let! sent =
          waitForAsync(
            5000,
            (fun () -> api.RequestsTo "/api/v1/remotes/@hubot/read"),
            (fun requests -> not requests.IsEmpty)
          )

        Expect.isTrue sent "the read request never went out"

        Expect.equal
          store.Current.Threads["hubot"].ReadThroughId
          42
          "the read through never advanced"
      })

    testCaseAsync
      "a pin move rebuilds the loaded depth without duplicates"
      (async {
        let api = ScriptedApi()

        let carolAt = "2026-10-20T04:03:00Z"
        let daveAt = "2026-10-20T04:02:00Z"
        let hubotAt = "2026-10-20T04:01:00Z"
        let bobAt = "2026-10-20T04:00:00Z"

        let carolJson =
          userJsonOf("01a151db-ca10-727b-a7ce-c911a7556243", "carol", "Carol")

        let daveJson =
          userJsonOf("01a151db-da7e-727b-a7ce-c911a7556243", "dave", "Dave")

        let bobPinned = "\"2026-10-26T00:00:00.000000Z\""

        let pageOne =
          inboxPageJson(
            [
              conversationJson(bobJson, 0, bobAt)
              conversationJson(hubotJson, 0, hubotAt)
            ],
            true,
            5,
            0
          )

        let pageTwo =
          inboxPageJson(
            [
              conversationJson(carolJson, 0, carolAt)
              conversationJson(daveJson, 0, daveAt)
            ],
            true,
            5,
            0
          )

        let bobMoved =
          conversationJson(bobJson, 0, bobAt)
            .Replace("\"pinned_at\":null", "\"pinned_at\":" + bobPinned)

        let rebuildOne =
          inboxPageJson(
            [ bobMoved; conversationJson(hubotJson, 0, hubotAt) ],
            true,
            5,
            0
          )

        let rebuildTwo =
          inboxPageJson(
            [
              conversationJson(carolJson, 0, carolAt)
              conversationJson(daveJson, 0, daveAt)
            ],
            false,
            5,
            0
          )

        api.Answer(
          "/api/v1/remotes",
          (200, pageOne),
          (200, pageTwo),
          (200, rebuildOne),
          (200, rebuildTwo)
        )

        api.Answer(
          "/api/v1/remotes/@bob/pin",
          (200, """{"pinned_at":"2026-10-26T00:00:00.000000Z"}""")
        )

        let! _, store = remotesSession api

        do! store.LoadTab TabOpen
        do! store.LoadMore TabOpen

        let! outcome = store.SetPinned(Partner bob, true)

        Expect.isOk outcome "the pin never landed"

        let keys =
          store.Current.Open.Items
          |> Option.defaultValue []
          |> List.map ChatTargets.summaryKey

        Expect.equal
          keys
          [ "bob"; "carol"; "dave"; "hubot" ]
          "the rebuild never reordered"

        Expect.equal (List.distinct keys) keys "the rebuild duplicated a row"
      })

    testCaseAsync
      "an older mute save never undoes a newer one"
      (async {
        let api = ScriptedApi()

        api.Answer(
          "/api/v1/remotes",
          (200,
           inboxPageJson(
             [ conversationJson(hubotJson, 2, "2026-10-20T04:00:00Z") ],
             false,
             2,
             0
           ))
        )

        api.Hold "/api/v1/remotes"

        api.Answer(
          "/api/v1/remotes/@hubot/mute",
          (200, """{"muted_until_at":"2026-11-01T00:00:00.000000Z"}""")
        )

        let! _, store = remotesSession api

        Async.Start(store.LoadTab TabOpen)

        let! held =
          waitForAsync(
            5000,
            (fun () -> api.RequestsTo "/api/v1/remotes"),
            (fun requests -> not requests.IsEmpty)
          )

        Expect.isTrue held "the tab load never left"

        let! saved = store.SetMuted(Partner hubot, MuteWeek)

        Expect.isOk saved "the mute never landed"

        api.Open "/api/v1/remotes"

        let! landed =
          waitForAsync(
            5000,
            (fun () -> store.Current.Open.Items),
            (fun items -> items |> Option.isSome)
          )

        Expect.isTrue landed "the held page never landed"

        let preference = store.Current.MutePreferences["hubot"].Value

        Expect.equal
          preference
          (Some(DateTimeOffset.Parse "2026-11-01T00:00:00.000000Z"))
          "the older page undid the newer mute"
      })

    testCaseAsync
      "a stash move the server refuses rolls back"
      (async {
        let api = ScriptedApi()

        api.Answer(
          "/api/v1/remotes",
          (200,
           inboxPageJson(
             [ conversationJson(hubotJson, 2, "2026-10-20T04:00:00Z") ],
             false,
             2,
             0
           ))
        )

        api.Answer("/api/v1/remotes/@hubot/archive", (500, "{}"))

        let! _, store = remotesSession api

        do! store.LoadTab TabOpen

        store.RememberTarget(Partner hubot)

        let! moved = store.SetArchived(Partner hubot, true)

        Expect.isFalse moved "a refused stash reported success"

        let rows = store.Current.Open.Items |> Option.defaultValue []

        Expect.equal rows.Length 1 "the row never came back"

        Expect.isFalse
          store.Current.Threads["hubot"].IsArchived
          "the thread flag never rolled back"
      })

    testCaseAsync
      "merging a fetched request moves the row into the open list"
      (async {
        let api = ScriptedApi()

        api.Answer(
          "/api/v1/remotes",
          (200,
           inboxPageJson(
             [ conversationJson(hubotJson, 3, "2026-10-20T04:00:00Z") ],
             false,
             0,
             1
           ))
        )

        api.Answer("/api/v1/remotes/@hubot/merge", (200, ""))

        let! _, store = remotesSession api

        do! store.LoadTab TabFetched

        let! merged = store.SettleRequest(hubot, true)

        Expect.isTrue merged "the merge never landed"

        let openRows = store.Current.Open.Items |> Option.defaultValue []

        Expect.equal
          (openRows |> List.map ChatTargets.summaryKey)
          [ "hubot" ]
          "the row never moved"

        Expect.equal openRows[0].Ahead 0 "the moved row kept its ahead count"

        let fetched = store.Current.Fetched.Items |> Option.defaultValue []

        Expect.isEmpty fetched "the fetched tab never emptied"

        Expect.equal
          store.Current.Requests
          (Some 0)
          "the request count never fell"
      })

    testCaseAsync
      "the inbox poll zeroes the ahead of a watched thread"
      (async {
        let api = ScriptedApi()

        api.Answer(
          "/api/v1/remotes",
          (200,
           inboxPageJson(
             [ conversationJson(hubotJson, 2, "2026-10-20T04:00:00Z") ],
             false,
             5,
             0
           ))
        )

        let! _, store = remotesSession api

        use reading = store.WatchThread(Partner hubot)

        do! store.PollInbox()

        let rows = store.Current.Open.Items |> Option.defaultValue []

        Expect.equal rows[0].Ahead 0 "a watched row kept its ahead count"

        Expect.equal
          store.Current.Unread
          (Some 3)
          "the unread total never dropped the watched ahead"
      })

    testCaseAsync
      "typing pings throttle to one per three seconds"
      (async {
        let api = ScriptedApi()

        api.Answer("/api/v1/remotes/@hubot/typing", (200, ""))

        let! _, store = remotesSession api

        do! store.PingTyping(Partner hubot)
        do! store.PingTyping(Partner hubot)

        do! Async.Sleep 50

        Expect.equal
          (api.RequestsTo "/api/v1/remotes/@hubot/typing").Length
          1
          "the throttle never held the second ping"
      })

    testCaseAsync
      "an org thread poll carries its members and typing"
      (async {
        let api = ScriptedApi()

        api.Answer("/api/v1/remotes/orgs/org-1/messages", (200, orgThreadJson))

        let! _, store = remotesSession api

        do! store.PollThread(Org { Id = "org-1"; Name = Some "the org" })

        let thread = store.Current.Threads["org:org-1"]

        Expect.equal thread.Members.Length 1 "the members never merged"

        Expect.equal thread.Typing [ "hubot" ] "the typing never merged"

        Expect.equal thread.Role (Some Maintainer) "the role never merged"
      })

    testCaseAsync
      "a settings save rides the account"
      (async {
        let api = ScriptedApi()

        api.Answer("/api/v1/me/remote-preferences", (200, meWithRemotesJson))

        let! session, store = remotesSession api

        let! saved = store.SetPreferences(FromEveryone, true)

        Expect.isOk saved "the preferences never landed"

        // The session store's value follows the UI thread, so the
        // refresh the save posted needs one pump before it reads.
        do! pumpAsync()

        let took =
          match session.Current with
          | SignedIn entry -> entry.Session.Remotes.IsSome
          | SignedOut -> false

        Expect.isTrue took "the session never took the settings"
      })

    testCaseAsync
      "the watcher stops every timer on dispose"
      (async {
        let api = ScriptedApi()

        let lively =
          threadJson(
            [ messageJson(40, partnerId, "hello", "c-40") ],
            40,
            "",
            true,
            0
          )

        api.Answer(messagesRoute, (200, lively))
        api.Answer("/api/v1/remotes", (200, "{}"))

        let! _, store = remotesSession api
        let watcher = store.Watcher
        let gate = new BehaviorSubject<bool>(true)

        let stop = watcher.Start gate

        let reading = watcher.WatchThread(Partner hubot)
        let inbox = watcher.WatchInbox()

        let! ticked =
          waitForAsync(
            5000,
            (fun () -> api.RequestsTo messagesRoute),
            (fun requests -> not requests.IsEmpty)
          )

        Expect.isTrue ticked "the thread loop never ticked"

        let! inboxTicked =
          waitForAsync(
            5000,
            (fun () -> api.RequestsTo "/api/v1/remotes"),
            (fun requests -> not requests.IsEmpty)
          )

        Expect.isTrue inboxTicked "the inbox loop never ticked"

        do! Async.Sleep 100

        let settled = api.Requests.Length

        stop.Dispose()
        reading.Dispose()
        inbox.Dispose()

        let! released =
          waitForAsync(
            5000,
            (fun () -> store.WatchedCount),
            (fun count -> count = 0)
          )

        Expect.isTrue released "a watch count survived the dispose"

        do! Async.Sleep 1900

        Expect.equal api.Requests.Length settled "a timer survived the dispose"
      })
  ]
