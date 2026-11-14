module DevNull.Client.UI.Tests.EditorRemotesTests

// The Editor Remotes tabs: the Remotes activity opens the bottom-pane
// inbox tab, narrow widths keep it in the panel sheet, drafts hold per
// tab, closes drop their watches, the dirty dot marks read on select,
// the account-off panel enables, and the share landing opens the tab.

open System
open System.Text
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Editor
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let private viewerId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
let private hubotId = "01a151db-7fee-727b-a7ce-c911a7556243"
let private bobId = "01a151db-b0b0-727b-a7ce-c911a7556243"

let private hubotJson =
  """{"id":"01a151db-7fee-727b-a7ce-c911a7556243","handle":"hubot","name":"The Hubot","avatar_url":null}"""

let private bobJson =
  """{"id":"01a151db-b0b0-727b-a7ce-c911a7556243","handle":"bob","name":"Bob","avatar_url":null}"""

let private hubotUser: User = {
  Id = hubotId
  Handle = "hubot"
  Name = "The Hubot"
  AvatarUrl = None
  Bio = None
  Lane = None
  CommitsCount = None
  FollowersCount = None
  FollowingCount = None
  FollowedByFollowingCount = None
  IsFollowed = None
  FollowsYou = None
  Live = None
}

let private conversationRow(partnerJson: string, handle: string) =
  $"{{\"id\":\"row-{handle}\",\"kind\":\"direct\",\"name\":null,\"partner\":{partnerJson},\"members\":null,\"last_message\":null,\"last_message_at\":\"2026-10-21T04:00:00Z\",\"ahead\":0,\"last_incoming_message_id\":null,\"last_incoming_sender\":null,\"partner_read_through_id\":0,\"partner_read_at\":null,\"is_archived\":false,\"pinned_at\":null,\"muted_until_at\":null}}"

let private inboxPage(rows: string list) =
  let joined = String.concat "," rows

  sprintf
    "{\"data\":[%s],\"has_more\":false,\"unread_count\":0,\"requests_count\":0}"
    joined

let private emptyThreadFor(partnerJson: string) =
  $"{{\"data\":{{\"partner\":{partnerJson},\"messages\":[],\"has_older\":false,\"reactions_version\":0,\"reaction_changes\":[],\"read_through_id\":0,\"partner_read_through_id\":0,\"partner_read_at\":null,\"partner_typing\":false,\"is_archived\":false,\"pinned_at\":null,\"muted_until_at\":null,\"access\":\"open\"}}}}"

let private messageJson
  (id: int, userId: string, body: string, clientId: string)
  =
  let segments =
    if body = "" then
      "[]"
    else
      sprintf "[{\"type\":\"text\",\"text\":\"%s\"}]" body

  sprintf
    "{\"id\":%d,\"user_id\":\"%s\",\"client_id\":\"%s\",\"body\":\"%s\",\"body_segments\":%s,\"mentions\":[],\"reply_to_id\":null,\"images\":[],\"reactions\":[],\"event\":null,\"subject_id\":null,\"user\":null,\"subject\":null,\"created_at\":\"2026-10-20T04:00:00Z\"}"
    id
    userId
    clientId
    body
    segments

let private threadWith
  (partnerJson: string, messages: string list, readThrough: int)
  =
  let joined = String.concat "," messages

  sprintf
    "{\"data\":{\"partner\":%s,\"messages\":[%s],\"has_older\":false,\"reactions_version\":0,\"reaction_changes\":[],\"read_through_id\":%d,\"partner_read_through_id\":0,\"partner_read_at\":null,\"partner_typing\":false,\"is_archived\":false,\"pinned_at\":null,\"muted_until_at\":null,\"access\":\"open\"}}"
    partnerJson
    joined
    readThrough

let private meWithRemotes =
  meJson.Replace(
    "\"remotes\":null",
    "\"remotes\":{\"from\":\"everyone\",\"pull_receipts\":true,\"requests\":true,\"everyone\":true,\"unread_count\":0}"
  )

let private storeWithRemotes() : SessionStore =
  let store = signedInStore()

  match store.Current with
  | SignedIn current ->
    store.Refresh {
      current.Session with
          Remotes =
            Some {
              From = FromEveryone
              PullReceipts = true
              Requests = true
              Everyone = true
              UnreadCount = 0
            }
    }
  | SignedOut -> failwith "the remotes test needs a signed-in session"

  store

let private payloadOf(request: HttpRequest) =
  match request.Body with
  | Text text -> text
  | Multipart form ->
    form.Files
    |> List.tryFind(fun file -> file.Name = "payload")
    |> Option.map(fun file -> Encoding.UTF8.GetString file.Content)
    |> Option.defaultValue ""
  | Empty -> ""

let private clientIdOf(body: string) =
  let at = body.IndexOf "\"client_id\""

  if at < 0 then
    "test-client"
  else
    let tail = body.Substring at
    (tail.Split '"')[3]

let private remotesApi
  (hubotThread: string ref, bobThread: string ref)
  : FakeApi =
  FakeApi(fun request ->
    if
      request.Method = "PUT" && request.Target.StartsWith "/api/v1/me/remotes"
    then
      ok meWithRemotes
    elif
      request.Method = "POST"
      && request.Target.StartsWith "/api/v1/remotes/@hubot/messages"
    then
      let body = payloadOf request
      let client = clientIdOf body
      let stored = messageJson(44, viewerId, "landed", client)

      ok("{\"data\":" + stored + "}")
    elif
      request.Method = "POST"
      && request.Target.StartsWith "/api/v1/remotes/@bob/messages"
    then
      let body = payloadOf request
      let client = clientIdOf body
      let stored = messageJson(44, viewerId, "landed", client)

      ok("{\"data\":" + stored + "}")
    elif
      request.Method = "POST"
      && request.Target.StartsWith "/api/v1/remotes/@hubot/ping"
    then
      ok "{}"
    elif
      request.Method = "POST"
      && request.Target.StartsWith "/api/v1/remotes/@bob/ping"
    then
      ok "{}"
    elif request.Target.StartsWith "/api/v1/remotes/@hubot/messages" then
      ok hubotThread.Value
    elif request.Target.StartsWith "/api/v1/remotes/@bob/messages" then
      ok bobThread.Value
    elif request.Target.StartsWith "/api/v1/remotes/candidates" then
      ok """{"candidates":[]}"""
    elif request.Target.StartsWith "/api/v1/remotes" then
      if
        request.Target.Contains "tab=open"
        && not(request.Target.Contains "q=")
        && not(request.Target.Contains "before=")
      then
        ok(
          inboxPage [
            conversationRow(hubotJson, "hubot")
            conversationRow(bobJson, "bob")
          ]
        )
      else
        ok(inboxPage [])
    elif request.Target.StartsWith "/api/v1/log/" then
      ok emptyLogPageJson
    else
      match accountAnswer request with
      | ValueSome answer -> answer
      | ValueNone -> ok "{}")

let private apiWithThreads() =
  let hubotThread = ref(emptyThreadFor hubotJson)
  let bobThread = ref(emptyThreadFor bobJson)
  remotesApi(hubotThread, bobThread), hubotThread, bobThread

let private editorProps(env: TestEnv) =
  let baseProps = DevNull.Client.UI.Tests.EditorFrame.props env

  {
    baseProps with
        Active = Observable.single true
  }

let private mountEditorAsync(env: TestEnv, width: float) =
  probeUiAsync(fun () ->
    let control, handle = EditorRoot.mount(Harness.ui, editorProps env)

    let window = Window(Width = width, Height = 900.0, Content = control)
    window.Show()
    window, handle)

let private clickActivity(window: Window, name: string) =
  onUiAsync(fun () ->
    window.GetVisualDescendants()
    |> Seq.tryFind(fun visual -> visual.Name = $"EditorActivity-{name}")
    |> Option.iter(fun visual ->
      match visual with
      | :? Button as button -> click button
      | _ -> ()))

let private clickRow(window: Window, text: string) =
  onUiAsync(fun () -> buttonWithText(window, text) |> Option.iter click)

let private clickPing(window: Window, handle: string) =
  onUiAsync(fun () ->
    window.GetVisualDescendants()
    |> Seq.tryPick(fun visual ->
      match visual with
      | :? Button as button ->
        match ToolTip.GetTip button with
        | :? string as tip when tip = $"Ping @{handle}" -> Some button
        | _ -> None
      | _ -> None)
    |> Option.iter click)

let private clickClose(window: Window, name: string) =
  onUiAsync(fun () ->
    window.GetVisualDescendants()
    |> Seq.tryFind(fun visual -> visual.Name = name)
    |> Option.iter(fun visual ->
      match visual with
      | :? Button as button -> click button
      | _ -> ()))

let private explorerShown(window: Window) =
  window.GetVisualDescendants()
  |> Seq.exists(fun visual ->
    visual.Name = "EditorExplorer"
    && (match visual with
        | :? Control as control -> control.IsEffectivelyVisible
        | _ -> false))

let private flyoutShown(window: Window) =
  window.GetVisualDescendants()
  |> Seq.exists(fun visual ->
    visual.Name = "EditorSidebarFlyout"
    && (match visual with
        | :? Control as control -> control.IsEffectivelyVisible
        | _ -> false))

let private panelRemotesShown(window: Window) =
  window.GetVisualDescendants()
  |> Seq.exists(fun visual ->
    visual.Name = "EditorPanelRemotes"
    && (match visual with
        | :? Control as control -> control.IsEffectivelyVisible
        | _ -> false))

let private composerText(window: Window) =
  window.GetVisualDescendants()
  |> Seq.tryPick(fun visual ->
    match visual with
    | :? TextBox as box when box.Name = "RemotesComposer" -> Some box.Text
    | _ -> None)

let private setComposer(window: Window, text: string) =
  onUiAsync(fun () ->
    window.GetVisualDescendants()
    |> Seq.tryPick(fun visual ->
      match visual with
      | :? TextBox as box when box.Name = "RemotesComposer" -> Some box
      | _ -> None)
    |> Option.iter(fun box -> box.Text <- text))

let private selectTab(window: Window, name: string) =
  onUiAsync(fun () ->
    window.GetVisualDescendants()
    |> Seq.tryFind(fun visual -> visual.Name = name)
    |> Option.iter(fun visual ->
      match visual with
      | :? Button as button -> click button
      | _ -> ()))

let private closeAll(window: Window, handle: IDisposable) =
  onUiAsync(fun () ->
    window.Close()
    handle.Dispose())

let tests =
  testList "editor remotes" [
    testCaseAsync
      "remotes activity opens the bottom-pane inbox and history closes it"
      (async {
        let api, _, _ = apiWithThreads()
        let env = TestEnv(storeWithRemotes(), api :> IHttp)
        do! pumpAsync()

        let! window, handle = mountEditorAsync(env, 1600.0)

        do! clickActivity(window, "Remotes")

        let! listed =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "@hubot") |> Option.isSome)
          )

        Expect.isTrue listed "the remotes list never listed"

        let! panelOpen = probeUiAsync(fun () -> panelRemotesShown window)
        Expect.isTrue panelOpen "the panel never opened on the remotes tab"

        let! explorerStands = probeUiAsync(fun () -> explorerShown window)
        Expect.isTrue explorerStands "the sidebar explorer never stood"

        do! clickActivity(window, "History")

        let! back =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(panelRemotesShown root))
          )

        Expect.isTrue back "the remotes panel never closed"

        do! closeAll(window, handle)

        let! settled =
          waitForAsync(
            5000,
            (fun () -> RemoteThreadTab.Live.current()),
            (fun count -> count = 0)
          )

        Expect.isTrue settled "a remote program survived the layout"

        let! releasedOne =
          waitForAsync(
            5000,
            (fun () -> (env :> IProvideRemotes).Remotes.WatchedCount),
            (fun count -> count = 0)
          )

        Expect.isTrue releasedOne "a thread watch survived the layout"
      })

    testCaseAsync
      "narrow width keeps the inbox in the panel sheet and a click opens a tab"
      (async {
        let api, _, _ = apiWithThreads()
        let env = TestEnv(storeWithRemotes(), api :> IHttp)
        do! pumpAsync()

        let! window, handle = mountEditorAsync(env, 800.0)

        do! clickActivity(window, "Remotes")

        let! panelOpen =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> panelRemotesShown root)
          )

        Expect.isTrue panelOpen "the panel inbox never opened"

        let! noFlyout = probeUiAsync(fun () -> not(flyoutShown window))
        Expect.isTrue noFlyout "the narrow flyout stood in for the panel"

        let! listed =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "@hubot") |> Option.isSome)
          )

        Expect.isTrue listed "the panel list never listed"

        do! clickRow(window, "@hubot")

        let! tab =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root ->
              root.GetVisualDescendants()
              |> Seq.exists(fun visual ->
                visual.Name = "EditorTab-remote:hubot"))
          )

        Expect.isTrue tab "the panel click never opened the tab"

        let! inboxStands =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> panelRemotesShown root)
          )

        Expect.isTrue inboxStands "the inbox left the panel after the click"

        do! closeAll(window, handle)

        let! settled =
          waitForAsync(
            5000,
            (fun () -> RemoteThreadTab.Live.current()),
            (fun count -> count = 0)
          )

        Expect.isTrue settled "a remote program survived the layout"

        let! released =
          waitForAsync(
            5000,
            (fun () -> (env :> IProvideRemotes).Remotes.WatchedCount),
            (fun count -> count = 0)
          )

        Expect.isTrue released "a thread watch survived the layout"
      })

    testCaseAsync
      "two tabs hold their drafts and a send pushes once for its target"
      (async {
        let api, _, _ = apiWithThreads()
        let env = TestEnv(storeWithRemotes(), api :> IHttp)
        do! pumpAsync()

        let! window, handle = mountEditorAsync(env, 1600.0)

        do! clickActivity(window, "Remotes")

        let! listed =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "@hubot") |> Option.isSome)
          )

        Expect.isTrue listed "the remotes list never listed"

        do! clickRow(window, "@hubot")

        let! first =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> hasText(root, "The Hubot @hubot"))
          )

        Expect.isTrue first "the hubot tab never opened"

        do! clickRow(window, "@bob")

        let! second =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> hasText(root, "Bob @bob"))
          )

        Expect.isTrue second "the bob tab never opened"

        let! liveTwo =
          waitForAsync(
            5000,
            (fun () -> RemoteThreadTab.Live.current()),
            (fun count -> count = 2)
          )

        Expect.isTrue liveTwo "two live tab programs never stood"

        let! watchedTwo =
          waitForAsync(
            5000,
            (fun () -> (env :> IProvideRemotes).Remotes.WatchedCount),
            (fun count -> count = 2)
          )

        Expect.isTrue watchedTwo "two thread watches never stood"

        do! selectTab(window, "EditorTab-remote:hubot")
        do! settleAsync()
        do! setComposer(window, "hubot draft")
        do! settleAsync()

        do! selectTab(window, "EditorTab-remote:bob")
        do! settleAsync()
        do! setComposer(window, "bob draft")
        do! settleAsync()

        do! selectTab(window, "EditorTab-remote:hubot")

        let! hubotKept =
          waitForAsync(
            5000,
            (fun () -> composerText window),
            (fun text -> text = Some "hubot draft")
          )

        Expect.isTrue hubotKept "the hubot draft never held"

        do!
          onUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryFind(fun visual -> visual.Name = "EditorTab-remote:bob")
            |> Option.iter(fun visual ->
              match visual with
              | :? Button as button -> click button
              | _ -> ()))

        let! bobKept =
          waitForAsync(
            5000,
            (fun () -> composerText window),
            (fun text -> text = Some "bob draft")
          )

        Expect.isTrue bobKept "the bob draft never held"

        do! selectTab(window, "EditorTab-remote:hubot")
        do! settleAsync()

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Push") |> Option.iter click)

        let! pushed =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.filter(fun request ->
                request.Method = "POST"
                && request.Target.StartsWith "/api/v1/remotes/@hubot/messages")
              |> List.length
                =
                1)
          )

        Expect.isTrue pushed "the hubot push never went out once"

        let bobPushes =
          api.Requests
          |> List.filter(fun request ->
            request.Method = "POST"
            && request.Target.StartsWith "/api/v1/remotes/@bob/messages")
          |> List.length

        Expect.equal bobPushes 0 "a bob push rode along"

        do! closeAll(window, handle)

        let! settled =
          waitForAsync(
            5000,
            (fun () -> RemoteThreadTab.Live.current()),
            (fun count -> count = 0)
          )

        Expect.isTrue settled "a remote program survived the layout"

        let! released =
          waitForAsync(
            5000,
            (fun () -> (env :> IProvideRemotes).Remotes.WatchedCount),
            (fun count -> count = 0)
          )

        Expect.isTrue released "a thread watch survived the layout"
      })

    testCaseAsync
      "closing one tab drops its watch and closing the layout zeroes all"
      (async {
        let api, _, _ = apiWithThreads()
        let env = TestEnv(storeWithRemotes(), api :> IHttp)
        do! pumpAsync()

        let! window, handle = mountEditorAsync(env, 1600.0)

        do! clickActivity(window, "Remotes")

        let! listed =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "@hubot") |> Option.isSome)
          )

        Expect.isTrue listed "the remotes list never listed"

        do! clickRow(window, "@hubot")

        let! first =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> hasText(root, "The Hubot @hubot"))
          )

        Expect.isTrue first "the hubot tab never opened"

        do! clickRow(window, "@bob")

        let! second =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> hasText(root, "Bob @bob"))
          )

        Expect.isTrue second "the bob tab never opened"

        let! liveTwo =
          waitForAsync(
            5000,
            (fun () -> RemoteThreadTab.Live.current()),
            (fun count -> count = 2)
          )

        Expect.isTrue liveTwo "two live tab programs never stood"

        do! clickClose(window, "EditorTabClose-remote:hubot")

        let! liveOne =
          waitForAsync(
            5000,
            (fun () -> RemoteThreadTab.Live.current()),
            (fun count -> count = 1)
          )

        Expect.isTrue liveOne "the closed tab never dropped its program"

        let! watchedOne =
          waitForAsync(
            5000,
            (fun () -> (env :> IProvideRemotes).Remotes.WatchedCount),
            (fun count -> count = 1)
          )

        Expect.isTrue watchedOne "the closed tab never dropped its watch"

        let! otherStands =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Bob @bob"))
          )

        Expect.isTrue otherStands "the other tab never kept polling"

        do! closeAll(window, handle)

        let! settled =
          waitForAsync(
            5000,
            (fun () -> RemoteThreadTab.Live.current()),
            (fun count -> count = 0)
          )

        Expect.isTrue settled "a remote program survived the layout"

        let! released =
          waitForAsync(
            5000,
            (fun () -> (env :> IProvideRemotes).Remotes.WatchedCount),
            (fun count -> count = 0)
          )

        Expect.isTrue released "a thread watch survived the layout"
      })

    testCaseAsync
      "an inactive tab shows the dirty dot and selecting it marks read"
      (async {
        let api, _, bobThread = apiWithThreads()
        let env = TestEnv(storeWithRemotes(), api :> IHttp)
        do! pumpAsync()

        let! window, handle = mountEditorAsync(env, 1600.0)

        do! clickActivity(window, "Remotes")

        let! listed =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "@hubot") |> Option.isSome)
          )

        Expect.isTrue listed "the remotes list never listed"

        do! clickRow(window, "@hubot")

        let! first =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> hasText(root, "The Hubot @hubot"))
          )

        Expect.isTrue first "the hubot tab never opened"

        do! clickRow(window, "@bob")

        let! second =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> hasText(root, "Bob @bob"))
          )

        Expect.isTrue second "the bob tab never opened"

        do! selectTab(window, "EditorTab-remote:hubot")
        do! settleAsync()

        bobThread.Value <-
          threadWith(bobJson, [ messageJson(1, bobId, "hey", "c-1") ], 0)

        let! dotted =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> hasText(root, "●1"))
          )

        Expect.isTrue dotted "the inactive tab never showed the dirty dot"

        let noRead =
          api.Requests
          |> List.exists(fun request ->
            request.Method = "PUT"
            && request.Target.StartsWith "/api/v1/remotes/@bob/read")

        Expect.isFalse noRead "the inactive tab marked read too early"

        do! selectTab(window, "EditorTab-remote:bob")

        let! marked =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "PUT"
                && request.Target.StartsWith "/api/v1/remotes/@bob/read"))
          )

        Expect.isTrue marked "selecting the tab never marked read"

        let! cleared =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> not(hasText(root, "●1")))
          )

        Expect.isTrue cleared "the dirty dot never cleared"

        do! closeAll(window, handle)

        let! settled =
          waitForAsync(
            5000,
            (fun () -> RemoteThreadTab.Live.current()),
            (fun count -> count = 0)
          )

        Expect.isTrue settled "a remote program survived the layout"

        let! released =
          waitForAsync(
            5000,
            (fun () -> (env :> IProvideRemotes).Remotes.WatchedCount),
            (fun count -> count = 0)
          )

        Expect.isTrue released "a thread watch survived the layout"
      })

    testCaseAsync
      "the account-off panel enables and the list follows"
      (async {
        let api, _, _ = apiWithThreads()
        let env = TestEnv(signedInStore(), api :> IHttp)
        do! pumpAsync()

        let! window, handle = mountEditorAsync(env, 1600.0)

        do! clickActivity(window, "Remotes")

        let! panel =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "direct messages are off"))
          )

        Expect.isTrue panel "the enable panel never rendered"

        Expect.equal
          (RemoteThreadTab.Live.current())
          0
          "a remote program stood while off"

        Expect.equal
          (env :> IProvideRemotes).Remotes.WatchedCount
          0
          "a thread watch stood while off"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Turn on remotes") |> Option.iter click)

        let! enabled =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "PUT"
                && request.Target = "/api/v1/me/remotes"))
          )

        Expect.isTrue enabled "enabling never called the store"

        let! listed =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "@hubot") |> Option.isSome)
          )

        Expect.isTrue listed "the list never followed the enable"

        do! closeAll(window, handle)

        let! settled =
          waitForAsync(
            5000,
            (fun () -> RemoteThreadTab.Live.current()),
            (fun count -> count = 0)
          )

        Expect.isTrue settled "a remote program survived the layout"

        let! released =
          waitForAsync(
            5000,
            (fun () -> (env :> IProvideRemotes).Remotes.WatchedCount),
            (fun count -> count = 0)
          )

        Expect.isTrue released "a thread watch survived the layout"
      })

    testCaseAsync
      "the ping button sends a ping"
      (async {
        let api, _, _ = apiWithThreads()
        let env = TestEnv(storeWithRemotes(), api :> IHttp)
        do! pumpAsync()

        let! window, handle = mountEditorAsync(env, 1600.0)

        do! clickActivity(window, "Remotes")

        let! listed =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "@hubot") |> Option.isSome)
          )

        Expect.isTrue listed "the remotes list never listed"

        do! clickRow(window, "@hubot")

        let! first =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> hasText(root, "The Hubot @hubot"))
          )

        Expect.isTrue first "the hubot tab never opened"

        do! clickPing(window, "hubot")

        let! pinged =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target.StartsWith "/api/v1/remotes/@hubot/ping"))
          )

        Expect.isTrue pinged "the ping never went out"

        do! closeAll(window, handle)

        let! settled =
          waitForAsync(
            5000,
            (fun () -> RemoteThreadTab.Live.current()),
            (fun count -> count = 0)
          )

        Expect.isTrue settled "a remote program survived the layout"

        let! released =
          waitForAsync(
            5000,
            (fun () -> (env :> IProvideRemotes).Remotes.WatchedCount),
            (fun count -> count = 0)
          )

        Expect.isTrue released "a thread watch survived the layout"
      })

    testCaseAsync
      "a share landing opens the tab without reading the store target"
      (async {
        let api, _, _ = apiWithThreads()
        let env = TestEnv(storeWithRemotes(), api :> IHttp)
        do! pumpAsync()

        let! window, handle = mountEditorAsync(env, 1600.0)

        let! explorer =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> explorerShown root)
          )

        Expect.isTrue explorer "the explorer never stood first"

        (env :> IProvideRemotes).Remotes.SetOpenTarget(Some(Partner hubotUser))

        do! settleAsync()

        let! noTab =
          probeUiAsync(fun () ->
            buttonWithText(window, "The Hubot @hubot") |> Option.isNone)

        Expect.isTrue noTab "the frame read the store open target"

        do! clickActivity(window, "Remotes")

        let! listed =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "@hubot") |> Option.isSome)
          )

        Expect.isTrue listed "the remotes list never listed"

        do! clickRow(window, "@hubot")

        let! tab =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> hasText(root, "The Hubot @hubot"))
          )

        Expect.isTrue tab "the share path never opened the tab"

        let! activity =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              buttonWithText(root, "@hubot") |> Option.isSome
              && panelRemotesShown root)
          )

        Expect.isTrue activity "the panel inbox never stood with the tab"

        do! closeAll(window, handle)

        let! settled =
          waitForAsync(
            5000,
            (fun () -> RemoteThreadTab.Live.current()),
            (fun count -> count = 0)
          )

        Expect.isTrue settled "a remote program survived the layout"

        let! released =
          waitForAsync(
            5000,
            (fun () -> (env :> IProvideRemotes).Remotes.WatchedCount),
            (fun count -> count = 0)
          )

        Expect.isTrue released "a thread watch survived the layout"
      })
  ]
