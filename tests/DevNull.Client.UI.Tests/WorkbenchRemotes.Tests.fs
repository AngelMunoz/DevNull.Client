module DevNull.Client.UI.Tests.WorkbenchRemotesTests

// The remotes ref in the workbench: the rail row with the unread
// count, the inbox in the center, one chat in the right pane, the
// exclusive pane, the enable panel while the account is off, the
// share landing, and the stored ref.

open System
open Avalonia.Controls
open DevNull.Client.ImageCache
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.Stores
open DevNull.Client.Workbench
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let private hubotJson =
  """{"id":"01a151db-7fee-727b-a7ce-c911a7556243","handle":"hubot","name":"The Hubot","avatar_url":null}"""

let private conversationRow =
  $"""{{"id":"row-hubot","kind":"direct","name":null,"partner":{hubotJson},"members":null,"last_message":null,"last_message_at":"2026-10-21T04:00:00Z","ahead":0,"last_incoming_message_id":null,"last_incoming_sender":null,"partner_read_through_id":0,"partner_read_at":null,"is_archived":false,"pinned_at":null,"muted_until_at":null}}"""

let private inboxPage(rows: string list) =
  $"""{{"data":[{String.concat "," rows}],"has_more":false,"unread_count":0,"requests_count":0}}"""

let private emptyThread =
  $"""{{"data":{{"partner":{hubotJson},"messages":[],"has_older":false,"reactions_version":0,"reaction_changes":[],"read_through_id":0,"partner_read_through_id":0,"partner_read_at":null,"partner_typing":false,"is_archived":false,"pinned_at":null,"muted_until_at":null,"access":"open"}}}}"""

let private meWithRemotes =
  meJson.Replace(
    "\"remotes\":null",
    "\"remotes\":{\"from\":\"everyone\",\"pull_receipts\":true,\"requests\":true,\"everyone\":true,\"unread_count\":0}"
  )

let private remotesApi() : FakeApi =
  FakeApi(fun request ->
    if
      request.Method = "PUT" && request.Target.StartsWith "/api/v1/me/remotes"
    then
      ok meWithRemotes
    elif request.Target.StartsWith "/api/v1/remotes/@hubot/messages" then
      ok emptyThread
    elif request.Target.StartsWith "/api/v1/remotes/candidates" then
      ok """{"candidates":[]}"""
    elif request.Target.StartsWith "/api/v1/remotes" then
      if
        request.Target.Contains "tab=open"
        && not(request.Target.Contains "q=")
        && not(request.Target.Contains "before=")
      then
        ok(inboxPage [ conversationRow ])
      else
        ok(inboxPage [])
    else
      ok "{}")

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

let private images = {
  new ImageCacheService with
    member _.Get(_url: string) = async { return None }
}

let private propsFor
  (env: TestEnv, initial: WorkbenchState)
  : WorkbenchHost.Props<TestEnv> = {
  Env = env
  Initial = initial
  Images = images
  Now = Observable.single DateTimeOffset.UtcNow
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  OnOpenAuthor = ignore
  OnOpenExternal = ignore
  OnStateChanged = ignore
  OnUseDefaultLayout = ignore
}

/// Mounts the host over the remotes ref and shows it.
let private mountRemotesAsync
  (env: TestEnv)
  : Async<Window * WorkbenchHost.MountedHost> =
  probeUiAsync(fun () ->
    let props = propsFor(env, Build.state(Build.remotes, None))
    let view, handle = WorkbenchHost.mount(Harness.ui, props)

    let window = Window(Width = 1600.0, Height = 900.0, Content = view)

    window.Show()
    window, handle :?> WorkbenchHost.MountedHost)

let private shownByName(root: Control, name: string) : bool =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? Control as control when control.Name = name -> Some control
    | _ -> None)
  |> Seq.exists _.IsVisible

let private buttonNamed(root: Control, name: string) : Button option =
  root.GetVisualDescendants()
  |> Seq.choose(function
    | :? Button as button when button.Name = name -> Some button
    | _ -> None)
  |> Seq.tryHead

let tests =
  testList "workbench remotes" [
    testCaseAsync
      "the remotes ref shows the inbox and starts one mount"
      (async {
        let env = TestEnv(storeWithRemotes(), remotesApi())
        do! pumpAsync()

        let! window, mounted = mountRemotesAsync env

        let! listed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "@hubot") |> Option.isSome)
          )

        Expect.isTrue listed "the conversation row never listed"
        Expect.equal mounted.LiveRemotes 1 "the mount never started"

        do!
          onUiAsync(fun () ->
            window.Close()
            (mounted :> IDisposable).Dispose())
      })

    testCaseAsync
      "opening a conversation shows the thread, and a checkout closes it"
      (async {
        let env = TestEnv(storeWithRemotes(), remotesApi())
        do! pumpAsync()

        let! window, mounted = mountRemotesAsync env

        let! listed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "@hubot") |> Option.isSome)
          )

        Expect.isTrue listed "the conversation row never listed"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "@hubot") |> Option.iter click)

        let! threadShown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> shownByName(root, "RemotesThreadHost"))
          )

        Expect.isTrue threadShown "the chat never opened in the right pane"

        do!
          onUiAsync(fun () ->
            mounted.Send(WorkbenchHost.CheckoutSet(Some "abc")))

        let! chatClosed =
          waitForAsync(
            5000,
            (fun () ->
              window, (env :> IProvideRemotes).Remotes.Current.OpenTarget),
            (fun (root, target) ->
              not(shownByName(root, "RemotesThreadHost")) && target.IsNone)
          )

        Expect.isTrue chatClosed "the checkout never closed the chat"

        let! inspectorShown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> shownByName(root, "InspectorRegion"))
          )

        Expect.isTrue inspectorShown "the inspector never took the right pane"

        do!
          onUiAsync(fun () ->
            window.Close()
            (mounted :> IDisposable).Dispose())
      })

    testCaseAsync
      "the thread close button checks the chat back out"
      (async {
        let env = TestEnv(storeWithRemotes(), remotesApi())
        do! pumpAsync()

        let! window, mounted = mountRemotesAsync env

        let! listed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "@hubot") |> Option.isSome)
          )

        Expect.isTrue listed "the conversation row never listed"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "@hubot") |> Option.iter click)

        let! closeShown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> buttonNamed(root, "ThreadClose") |> Option.isSome)
          )

        Expect.isTrue closeShown "the thread close button never stood"

        do!
          onUiAsync(fun () ->
            buttonNamed(window, "ThreadClose") |> Option.iter click)

        let! closed =
          waitForAsync(
            5000,
            (fun () ->
              window, (env :> IProvideRemotes).Remotes.Current.OpenTarget),
            (fun (root, target) ->
              not(shownByName(root, "RemotesThreadHost")) && target.IsNone)
          )

        Expect.isTrue closed "the close button never checked the chat out"

        do!
          onUiAsync(fun () ->
            window.Close()
            (mounted :> IDisposable).Dispose())
      })

    testCaseAsync
      "switching refs disposes the mount and back starts it again"
      (async {
        let env = TestEnv(storeWithRemotes(), remotesApi())
        do! pumpAsync()

        let! window, mounted = mountRemotesAsync env

        let! started =
          waitForAsync(
            5000,
            (fun () -> mounted.LiveRemotes),
            (fun count -> count = 1)
          )

        Expect.isTrue started "the mount never started"

        do!
          onUiAsync(fun () ->
            mounted.Send(WorkbenchHost.RefSelected Build.saved))

        let! stopped =
          waitForAsync(
            5000,
            (fun () -> mounted.LiveRemotes),
            (fun count -> count = 0)
          )

        Expect.isTrue stopped "switching refs never disposed the mount"

        do!
          onUiAsync(fun () ->
            mounted.Send(WorkbenchHost.RefSelected Build.remotes))

        let! restarted =
          waitForAsync(
            5000,
            (fun () -> mounted.LiveRemotes),
            (fun count -> count = 1)
          )

        Expect.isTrue restarted "returning never started the mount again"

        do!
          onUiAsync(fun () ->
            window.Close()
            (mounted :> IDisposable).Dispose())

        Expect.equal
          mounted.LiveRemotes
          0
          "disposing the layout never ended the mount"
      })

    testCaseAsync
      "the row stands while the account is off, and enabling starts the list"
      (async {
        let api = remotesApi()
        let env = TestEnv(signedInStore(), api)
        do! pumpAsync()

        let! window, mounted = mountRemotesAsync env

        let! panelShown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "direct messages are off"))
          )

        Expect.isTrue panelShown "the enable panel never rendered"

        Expect.equal
          mounted.LiveRemotes
          0
          "a mount stood while the account was off"

        let! rowShown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> buttonWithText(root, "Remotes") |> Option.isSome)
          )

        Expect.isTrue rowShown "the remotes row never stood while off"

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

        do!
          onUiAsync(fun () ->
            window.Close()
            (mounted :> IDisposable).Dispose())
      })

    testCaseAsync
      "a share landing opens the ref with the chat"
      (async {
        let env = TestEnv(storeWithRemotes(), remotesApi())
        do! pumpAsync()

        let! window, mounted =
          probeUiAsync(fun () ->
            let props = propsFor(env, Build.state(Build.saved, None))
            let view, handle = WorkbenchHost.mount(Harness.ui, props)

            let window = Window(Width = 1600.0, Height = 900.0, Content = view)

            window.Show()
            window, handle :?> WorkbenchHost.MountedHost)

        (env :> IProvideRemotes)
          .Remotes.SetOpenTarget(
            Some(
              Partner {
                Id = "01a151db-7fee-727b-a7ce-c911a7556243"
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
            )
          )

        let! landed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root ->
              buttonWithText(root, "@hubot") |> Option.isSome
              && shownByName(root, "RemotesThreadHost"))
          )

        Expect.isTrue landed "the share never landed on the ref with the chat"

        do!
          onUiAsync(fun () ->
            window.Close()
            (mounted :> IDisposable).Dispose())
      })

    testCase "a stored remotes ref reads back with no chat"
    <| fun () ->
      let stored = WorkbenchSettings.store(Build.state(Build.remotes, None))

      Expect.equal stored "remotes" "the stored token"

      let state = WorkbenchSettings.parse stored

      Expect.isTrue (Ref.remotes(WorkbenchState.refOf state)) "the parsed ref"

      Expect.equal
        (WorkbenchState.checkoutId state)
        None
        "a chat survived the restart"
  ]
