module DevNull.Client.UI.Tests.Share

// The share flow over the real shell: the card's offer follows the
// account and the commit status, the picker asks with the share
// intent and gates the people out of reach, and Send posts the
// commit's own link into the recipient's thread.

open System
open System.Text.RegularExpressions
open System.Threading.Tasks
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.VisualTree
open Expecto
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.DeviceAuth
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.RouteStore
open DevNull.Client.Shell
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.App
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness
open DevNull.Client.UI.Tests.Remotes

/// One signed-in session with the direct messages on.
let remotesStoreOn() : SessionStore =
  SessionStore.create {
    Initial =
      Some(
        {
          sessionOf "octocat" with
              Remotes =
                Some {
                  From = FromEveryone
                  PullReceipts = true
                  Requests = false
                  Everyone = true
                  UnreadCount = 0
                }
        }
      )
    KeyFile = devicePrivateKeyPath
    Forget = ignore
    Dispatcher = Harness.ui
  }

let candidateJson(user: string, reason: string option) =
  match reason with
  | Some why -> $"""{{"user":{user},"reason":"{why}"}}"""
  | None -> $"""{{"user":{user},"reason":null}}"""

let candidatesAnswer(rows: string list) =
  ok($"""{{"candidates":[{String.concat "," rows}]}}""")

/// The commit URL the share test sends, kept off every real host.
let shareUrl =
  "https://example.test/@octocat/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

/// The canned log with the commit URL replaced, so the send assertion
/// reads a neutral address.
let shareLogJson =
  Regex.Replace(
    logPageJson,
    "\"html_url\":\"[^\"]+\"",
    "\"html_url\":\"" + shareUrl + "\""
  )

/// The focused commit page with the same neutral URL, so the thread's
/// card shares the link the log carries.
let shareThreadPageJson =
  Regex.Replace(
    threadPageJson,
    "\"html_url\":\"[^\"]+\"",
    "\"html_url\":\"" + shareUrl + "\""
  )

let private withStatus(status: string) =
  shareLogJson.Replace(
    "\"status\":\"approved\"",
    "\"status\":\"" + status + "\""
  )

let private threadAnswer(body: string option) =
  match body with
  | Some text ->
    threadJson(
      [ messageJson(501, partnerId, text, "share-client") ],
      0,
      "",
      false,
      0
    )
  | None -> threadJson([], 0, "", false, 0)

/// The answer a push gets: the message the client sent, under the
/// client id the request carried, so its staged copy swaps out.
let sharedPushAnswer(request: HttpRequest, body: string) : HttpResponse =
  let payload = payloadText request

  let clientId =
    Regex.Match(payload, "\"client_id\":\"([^\"]+)\"").Groups[1].Value

  ok("{\"data\":" + messageJson(501, viewerId, body, clientId) + "}")

/// The answer one share test scripts: the log, the candidates, the
/// push, and the thread the remotes page mounts over.
[<Struct>]
type ShareAnswer = {
  Log: string
  Candidates: HttpRequest -> HttpResponse
  Post: HttpRequest -> HttpResponse
  Sent: string option
}

/// The api of the share tests: the log answers from its can, the
/// candidates from the test's own answer, the push from the test's
/// own answer, and the remotes page mounts over an empty inbox.
let shareApi(answer: ShareAnswer) : FakeApi =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok answer.Log
    elif request.Target.StartsWith "/api/v1/remotes/candidates" then
      answer.Candidates request
    elif request.Method = "POST" && request.Target.EndsWith "/messages" then
      answer.Post request
    elif request.Target.StartsWith "/api/v1/remotes/@" then
      ok(threadAnswer answer.Sent)
    elif request.Target.StartsWith "/api/v1/remotes" then
      ok(inboxPageJson([], false, 0, 0))
    else
      (match accountAnswer request with
       | ValueSome response -> response
       | ValueNone -> ok "{}"))

/// One api that holds a pushed message until the test opens its gate,
/// so the staged bubble can be checked before the server answers.
type private HeldMessagesApi(inner: IHttp, gate: TaskCompletionSource<unit>) =
  interface IHttp with
    member _.Send(request) = async {
      if request.Method = "POST" && request.Target.EndsWith "/messages" then
        do! gate.Task |> Async.AwaitTask

      return! inner.Send request
    }

    member _.GetBytes(target: string) = inner.GetBytes target

let startShareShell(store: SessionStore, api: IHttp) : Async<Window * TestEnv> =
  async {
    let env = TestEnv(store, api)
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    let! shown = showAsync window
    return shown, env
  }

/// The menu's popup card content, once the menu stood open: the rows
/// live under the popup's child, which roots under the popup layer
/// while the menu stands.
let menuContent(root: Control) : Control option =
  controlByName(root, "CommitMenuFlyout")
  |> Option.bind(fun control ->
    match control with
    | :? Popup as popup -> Option.ofObj popup.Child
    | _ -> None)

/// One row of the open menu by its words.
let menuRow(root: Control, label: string) : Button option =
  menuContent root |> Option.bind(fun content -> buttonWithText(content, label))

/// The confirm card of the one open destructive ask.
let confirmContent(root: Control) : Control option =
  menuContent root
  |> Option.bind(fun content ->
    content.GetVisualDescendants()
    |> Seq.tryPick(fun visual ->
      match visual with
      | :? Popup as popup when popup.Name = "ConfirmFlyout" && popup.IsOpen ->
        Some popup
      | _ -> None))
  |> Option.bind(fun popup -> Option.ofObj popup.Child)

/// Opens the card's More menu through its trigger.
let openMenuAsync(window: Window) : Async<unit> = async {
  let! trigger =
    waitForAsync(
      5000,
      (fun () -> controlByName(window, "CommitMenuButton")),
      Option.isSome
    )

  Expect.isTrue trigger "the card offered no menu trigger"

  do!
    onUiAsync(fun () ->
      controlByName(window, "CommitMenuButton")
      |> Option.iter(fun control -> click(control :?> Button)))
}

/// Clicks one menu row by its words once it shows.
let clickMenuRowAsync(window: Window, label: string) : Async<unit> = async {
  let! row =
    waitForAsync(5000, (fun () -> menuRow(window, label)), Option.isSome)

  Expect.isTrue row $"the menu never carried the {label} row"

  do! onUiAsync(fun () -> menuRow(window, label) |> Option.iter click)
}

let openSharePickerAsync(window: Window) : Async<unit> = async {
  do! openMenuAsync window

  let! offered =
    waitForAsync(
      5000,
      (fun () -> menuRow(window, "Send in Remotes")),
      Option.isSome
    )

  Expect.isTrue offered "the card offered no share control"

  do! clickMenuRowAsync(window, "Send in Remotes")

  let! picker =
    waitForAsync(
      5000,
      (fun () -> controlByName(window, "ShareSearch")),
      Option.isSome
    )

  Expect.isTrue picker "the share picker never opened"
}

let typeShareTermAsync(window: Window, term: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, "ShareSearch")
    |> Option.iter(fun control -> (control :?> TextBox).Text <- term))

let pickShareCandidateAsync(window: Window, index: int) : Async<unit> = async {
  let name = $"ShareCandidate{index}"

  let! present =
    waitForAsync(5000, (fun () -> controlByName(window, name)), Option.isSome)

  Expect.isTrue present $"the share candidate {index} never listed"

  do!
    onUiAsync(fun () ->
      controlByName(window, name)
      |> Option.iter(fun control -> click(control :?> Button)))
}

let showsWords(window: Window, words: string) : Async<bool> =
  waitForAsync(
    5000,
    (fun () -> window),
    (fun root ->
      root.GetVisualDescendants()
      |> Seq.choose(fun visual ->
        match visual with
        | :? TextBlock as block -> Some block
        | _ -> None)
      |> Seq.exists(fun block ->
        (block.Text |> Option.ofObj)
        |> Option.exists(fun text -> text.Contains words)
        && effectiveVisible block))
  )

let private postedRequest(api: FakeApi) : HttpRequest option =
  api.Requests
  |> List.tryFind(fun request ->
    request.Method = "POST" && request.Target.EndsWith "/messages")

let private candidateRequests(api: FakeApi) : HttpRequest list =
  api.Requests
  |> List.filter(fun request ->
    request.Target.StartsWith "/api/v1/remotes/candidates")

let tests =
  testList "share commits" [
    testCaseAsync
      "an approved commit offers share while the remotes are on"
      (async {
        let api =
          shareApi {
            Log = shareLogJson
            Candidates = (fun _ -> candidatesAnswer [])
            Post = (fun _ -> ok "{}")
            Sent = None
          }

        let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        do! openMenuAsync window

        let! share =
          waitForAsync(
            5000,
            (fun () -> menuRow(window, "Send in Remotes")),
            Option.isSome
          )

        Expect.isTrue share "the approved card offered no share control"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "the offer stays absent when the remotes are off"
      (async {
        let api =
          shareApi {
            Log = shareLogJson
            Candidates = (fun _ -> candidatesAnswer [])
            Post = (fun _ -> ok "{}")
            Sent = None
          }

        let! window, _ = startShareShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"
        do! openMenuAsync window
        do! settleAsync()

        let! absent =
          probeUiAsync(fun () ->
            menuRow(window, "Send in Remotes") |> Option.isNone)

        Expect.isTrue absent "the card offered share with the remotes off"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "the offer stays absent on a commit that is not approved"
      (async {
        for status in [ "pending"; "rejected" ] do
          let api =
            shareApi {
              Log = withStatus status
              Candidates = (fun _ -> candidatesAnswer [])
              Post = (fun _ -> ok "{}")
              Sent = None
            }

          let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)

          let! shown = showsText(window, "The Octocat")
          Expect.isTrue shown $"the {status} log never rendered"
          do! openMenuAsync window
          do! settleAsync()

          let! absent =
            probeUiAsync(fun () ->
              menuRow(window, "Send in Remotes") |> Option.isNone)

          Expect.isTrue absent $"the {status} card offered share"

          do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "a card host without the slot draws no share control"
      (async {
        let api =
          shareApi {
            Log = shareLogJson
            Candidates = (fun _ -> candidatesAnswer [])
            Post = (fun _ -> ok "{}")
            Sent = None
          }

        let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Profile")

        let! profile = showsText(window, "Builds things.")
        Expect.isTrue profile "the profile page never rendered"

        let! commits = showsText(window, "It was DNS.")
        Expect.isTrue commits "the profile feed never rendered its commit"

        do! openMenuAsync window
        do! settleAsync()

        let! absent =
          probeUiAsync(fun () ->
            menuRow(window, "Send in Remotes") |> Option.isNone)

        Expect.isTrue absent "the profile card offered share"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "the picker waits for a term and asks for share candidates"
      (async {
        let api =
          shareApi {
            Log = shareLogJson
            Candidates =
              (fun _ ->
                candidatesAnswer [
                  candidateJson(bobJson, Some "doesn't follow you")
                  candidateJson(hubotJson, None)
                ])
            Post = (fun _ -> ok "{}")
            Sent = None
          }

        let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        do! openSharePickerAsync window
        do! settleAsync()

        Expect.isEmpty (candidateRequests api) "the picker asked without a term"

        let! length =
          probeUiAsync(fun () ->
            controlByName(window, "ShareSearch")
            |> Option.map(fun control -> (control :?> TextBox).MaxLength))

        Expect.equal length (Some 100) "the picker input passed the cap"

        do! typeShareTermAsync(window, "@hub")

        let! first =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ShareCandidate0")),
            Option.isSome
          )

        Expect.isTrue first "the picker listed nobody"

        let asked = candidateRequests api
        Expect.isNonEmpty asked "the picker never asked"

        let last = List.last asked

        Expect.stringContains
          last.Target
          "for=share"
          "the read named another intent"

        Expect.stringContains last.Target "q=hub" "the read kept the raw handle"

        let! rows =
          probeUiAsync(fun () ->
            match
              controlByName(window, "ShareCandidate0"),
              controlByName(window, "ShareCandidate1")
            with
            | Some first, Some second ->
              first.IsEnabled,
              second.IsEnabled,
              hasText(first, "The Hubot"),
              hasText(first, "@hubot"),
              hasText(second, "can't push: doesn't follow you")
            | _ -> false, true, false, false, false)

        Expect.equal
          rows
          (true, false, true, true, true)
          "the picker did not sort or gate its rows"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "the picker leaves an empty answer quiet"
      (async {
        let answered = TaskCompletionSource<unit>()

        let api =
          shareApi {
            Log = shareLogJson
            Candidates =
              (fun _ ->
                answered.TrySetResult(()) |> ignore
                candidatesAnswer [])
            Post = (fun _ -> ok "{}")
            Sent = None
          }

        let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        do! openSharePickerAsync window
        do! typeShareTermAsync(window, "nobody")
        do! Async.AwaitTask answered.Task
        do! settleAsync()

        let! empty =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ShareNoMatches")),
            fun control ->
              control
              |> Option.exists(fun visual ->
                effectiveVisible visual && hasText(visual, "No one matches."))
          )

        Expect.isTrue empty "the empty answer showed no quiet line"

        let! rows =
          probeUiAsync(fun () -> controlByName(window, "ShareCandidate0"))

        Expect.isNone rows "the empty answer listed a row"

        let! term =
          probeUiAsync(fun () ->
            controlByName(window, "ShareSearch")
            |> Option.bind(fun control ->
              (control :?> TextBox).Text |> Option.ofObj))

        Expect.equal term (Some "nobody") "the empty answer cleared the input"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "a failed candidates read drops its rows and shows its reason"
      (async {
        let api =
          shareApi {
            Log = shareLogJson
            Candidates =
              (fun request ->
                if request.Target.Contains "q=hub" then
                  candidatesAnswer [ candidateJson(hubotJson, None) ]
                else
                  refused(500, "the server said no"))
            Post = (fun _ -> ok "{}")
            Sent = None
          }

        let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        do! openSharePickerAsync window
        do! typeShareTermAsync(window, "nobody")

        let! refusedFirst = showsWords(window, "the server said no")
        Expect.isTrue refusedFirst "the picker hid the first refusal"

        let! initialRows =
          probeUiAsync(fun () -> controlByName(window, "ShareCandidate0"))

        Expect.isNone initialRows "the first refusal kept a row"

        let! initialTerm =
          probeUiAsync(fun () ->
            controlByName(window, "ShareSearch")
            |> Option.bind(fun control ->
              (control :?> TextBox).Text |> Option.ofObj))

        Expect.equal
          initialTerm
          (Some "nobody")
          "the first refusal cleared the input"

        do! typeShareTermAsync(window, "hub")

        let! loaded =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ShareCandidate0")),
            Option.isSome
          )

        Expect.isTrue loaded "the picker never listed the first answer"

        do! typeShareTermAsync(window, "other")

        let! failure = showsWords(window, "the server said no")
        Expect.isTrue failure "the picker hid the second refusal"

        let! gone =
          probeUiAsync(fun () -> controlByName(window, "ShareCandidate0"))

        Expect.isNone gone "the refused read kept the last rows"

        let! term =
          probeUiAsync(fun () ->
            controlByName(window, "ShareSearch")
            |> Option.bind(fun control ->
              (control :?> TextBox).Text |> Option.ofObj))

        Expect.equal term (Some "other") "the refused read cleared the input"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "picking a recipient opens the confirm step"
      (async {
        let api =
          shareApi {
            Log = shareLogJson
            Candidates =
              (fun _ -> candidatesAnswer [ candidateJson(hubotJson, None) ])
            Post = (fun _ -> ok "{}")
            Sent = None
          }

        let! window, _ = startShareShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        do! openSharePickerAsync window
        do! typeShareTermAsync(window, "hub")
        do! pickShareCandidateAsync(window, 0)

        let! title = showsText(window, "Send commit to @hubot?")
        Expect.isTrue title "the confirm step never named the recipient"

        let! description =
          showsText(window, "Send a link to this commit in Remotes.")

        Expect.isTrue description "the confirm step never described the send"

        let! preview = showsText(window, "@octocat")
        Expect.isTrue preview "the confirm step never showed the commit"

        let! send =
          waitForAsync(
            5000,
            (fun () -> buttonWithText(window, "Send")),
            Option.isSome
          )

        Expect.isTrue send "the confirm step offered no Send"

        let! cancel =
          waitForAsync(
            5000,
            (fun () -> buttonWithText(window, "Cancel")),
            Option.isSome
          )

        Expect.isTrue cancel "the confirm step offered no Cancel"

        do! clickWhenShown(window, "Cancel")

        let! closed =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ShareSearch")),
            Option.isNone
          )

        Expect.isTrue closed "Cancel left the share dialog open"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "Send stages the commit link before it opens the thread"
      (async {
        let gate = TaskCompletionSource<unit>()

        let inner =
          shareApi {
            Log = shareLogJson
            Candidates =
              (fun _ -> candidatesAnswer [ candidateJson(hubotJson, None) ])
            Post = (fun request -> sharedPushAnswer(request, shareUrl))
            Sent = None
          }

        let api = HeldMessagesApi(inner :> IHttp, gate)

        let! window, env = startShareShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        do! openSharePickerAsync window
        do! typeShareTermAsync(window, "hub")
        do! pickShareCandidateAsync(window, 0)

        let store = (env :> IProvideRemotes).Remotes
        let firstOpen = TaskCompletionSource<RemotesState>()

        use watch =
          store.Observe.Subscribe(fun state ->
            match state.OpenTarget with
            | Some(Partner user) when user.Handle = "hubot" ->
              firstOpen.TrySetResult state |> ignore
            | _ -> ())

        do! clickWhenShown(window, "Send")

        let! opened =
          waitForAsync(5000, (fun () -> firstOpen.Task.IsCompleted), id)

        Expect.isTrue opened "the sent share opened no thread"

        let! atOpen = Async.AwaitTask firstOpen.Task

        let stagedAtOpen =
          atOpen.Threads
          |> Map.tryFind "hubot"
          |> Option.bind(fun thread ->
            thread.Pending
            |> List.tryFind(fun pending ->
              pending.Message.Body = Some shareUrl))

        Expect.isSome
          stagedAtOpen
          "the open target landed before the commit was staged"

        gate.SetResult(())

        let! delivered =
          waitForAsync(
            5000,
            (fun () ->
              store.Current.Threads
              |> Map.tryFind "hubot"
              |> Option.map(fun thread -> thread.Pending.IsEmpty)),
            fun empty -> empty = Some true
          )

        Expect.isTrue delivered "the sent share stayed staged"

        let! closed =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ShareSearch")),
            Option.isNone
          )

        Expect.isTrue closed "the share dialog stayed open"

        let posted = postedRequest inner

        match posted with
        | None -> failtest "the share sent no message"
        | Some request ->
          Expect.equal
            request.Target
            "/api/v1/remotes/@hubot/messages"
            "the share targeted another conversation"

          let payload = payloadText request

          Expect.stringContains
            payload
            $"\"body\":\"{shareUrl}\""
            "the message did not carry the commit link"

          Expect.isFalse
            (payload.Contains "reply_to_id")
            "the share answered a message"

          Expect.isFalse
            (payload.Contains "link_preview_url")
            "the share chose its own card"

          Expect.isFalse
            (payload.Contains "image_descriptions")
            "the share carried images"

          Expect.isFalse
            (payload.Contains "voice_waveform")
            "the share carried voice"

        let! carried =
          waitForAsync(
            5000,
            (fun () ->
              store.Current.Threads
              |> Map.tryFind "hubot"
              |> Option.map(fun thread ->
                thread.Messages
                |> List.exists(fun message -> message.Body = shareUrl))),
            fun carried -> carried = Some true
          )

        Expect.isTrue carried "the thread never carried the commit link"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "a refused send keeps the rejected bubble and opens the thread"
      (async {
        let api =
          shareApi {
            Log = shareLogJson
            Candidates =
              (fun _ -> candidatesAnswer [ candidateJson(hubotJson, None) ])
            Post = (fun _ -> refused(403, "the server said no"))
            Sent = None
          }

        let! window, env = startShareShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        do! openSharePickerAsync window
        do! typeShareTermAsync(window, "hub")
        do! pickShareCandidateAsync(window, 0)
        do! clickWhenShown(window, "Send")

        let store = (env :> IProvideRemotes).Remotes

        let! opened =
          waitForAsync(
            5000,
            (fun () -> store.Current.OpenTarget),
            fun target ->
              match target with
              | Some(Partner user) -> user.Handle = "hubot"
              | _ -> false
          )

        Expect.isTrue opened "the refused share opened no thread"

        let! rejected =
          waitForAsync(
            5000,
            (fun () ->
              store.Current.Threads
              |> Map.tryFind "hubot"
              |> Option.map(fun thread ->
                thread.Pending
                |> List.exists(fun pending ->
                  pending.Message.Body = Some shareUrl && pending.Rejected))),
            fun rejected -> rejected = Some true
          )

        Expect.isTrue rejected "the refused share lost its rejected bubble"

        let! closed =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ShareSearch")),
            Option.isNone
          )

        Expect.isTrue closed "the share dialog stayed open after the refusal"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "the share flow works from the commit thread"
      (async {
        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/log/" then
              ok shareLogJson
            elif request.Target.Contains "/replies" then
              ok repliesPageJson
            elif request.Target.Contains "/related" then
              ok "{\"data\":[]}"
            elif request.Target.StartsWith "/api/v1/commits/" then
              ok shareThreadPageJson
            elif request.Target.StartsWith "/api/v1/remotes/candidates" then
              candidatesAnswer [ candidateJson(hubotJson, None) ]
            elif
              request.Method = "POST" && request.Target.EndsWith "/messages"
            then
              sharedPushAnswer(request, shareUrl)
            elif request.Target.StartsWith "/api/v1/remotes/@" then
              ok(threadJson([], 0, "", false, 0))
            elif request.Target.StartsWith "/api/v1/remotes" then
              ok(inboxPageJson([], false, 0, 0))
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let! window, env = startShareShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "It was DNS.")

        let! thread = showsText(window, "Back to the log")
        Expect.isTrue thread "the commit thread never opened"

        do! openSharePickerAsync window
        do! typeShareTermAsync(window, "hub")
        do! pickShareCandidateAsync(window, 0)

        let! titled = showsText(window, "Send commit to @hubot?")

        Expect.isTrue
          titled
          "the thread's confirm step never named the recipient"

        do! clickWhenShown(window, "Send")

        let! closed =
          waitForAsync(
            10000,
            (fun () -> controlByName(window, "ShareSearch")),
            Option.isNone
          )

        Expect.isTrue closed "the thread's share dialog stayed open"

        let! sent =
          waitForAsync(10000, (fun () -> postedRequest api), Option.isSome)

        Expect.isTrue sent "the thread's share sent no message"

        match postedRequest api with
        | None -> failtest "the thread's share sent no message"
        | Some request ->
          Expect.equal
            request.Target
            "/api/v1/remotes/@hubot/messages"
            "the thread's share targeted another conversation"

          let payload = payloadText request

          Expect.stringContains
            payload
            $"\"body\":\"{shareUrl}\""
            "the thread's message did not carry the commit link"

        let routes = (env :> IProvideRoutes).Routes

        let! navigated =
          waitForAsync(
            10000,
            (fun () -> routes.Current),
            fun path -> path.StartsWith "/remotes"
          )

        Expect.isTrue navigated "the share did not navigate to the remotes page"

        do! onUiAsync(fun () -> window.Close())
      })
  ]
