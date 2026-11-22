module DevNull.Client.UI.Tests.EditorHardening

open System
open System.Reactive.Subjects
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Editor
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.EditorFrame
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let controlByName(root: Control, name: string) =
  root.GetVisualDescendants()
  |> Seq.tryFind(fun visual -> visual.Name = name)
  |> Option.map(fun visual -> visual :?> Control)

let visible(root: Control, name: string) =
  controlByName(root, name)
  |> Option.exists(fun control -> control.IsEffectivelyVisible)

let clickName(root: Control, name: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(root, name)
    |> Option.bind(fun control ->
      match control with
      | :? Button as button -> Some button
      | _ ->
        control.GetVisualAncestors()
        |> Seq.tryPick(fun visual ->
          match visual with
          | :? Button as button -> Some button
          | _ -> None))
    |> Option.iter click)

let typeQuery(window: Window, text: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, "SearchInput")
    |> Option.iter(fun control ->
      match control with
      | :? TextBox as box -> box.Text <- text
      | _ -> ()))

let collect() : unit =
  for _ in 1..2 do
    GC.Collect()
    GC.WaitForPendingFinalizers()

  GC.Collect()

let logCount(api: FakeApi) =
  api.Requests
  |> List.filter(fun request ->
    request.Method = "GET" && request.Target.StartsWith "/api/v1/log/")
  |> List.length

let searchCount(api: FakeApi) =
  api.Requests
  |> List.filter(fun request -> request.Target.StartsWith "/api/v1/search/")
  |> List.length

let userJson(handle: string) =
  apiUserJson
    .Replace("\"handle\":\"octocat\"", $"\"handle\":\"{handle}\"")
    .Replace("\"name\":\"The Octocat\"", $"\"name\":\"{handle}\"")

let churnApi() =
  let users = [ "ana"; "bo"; "cy" ] |> List.map userJson |> String.concat ","

  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/search/profiles" then
      ok(sprintf "{\"users\":[%s],\"organizations\":[]}" users)
    elif request.Target.StartsWith "/api/v1/search/commits" then
      ok """{"data":[]}"""
    elif
      request.Target.StartsWith "/api/v1/users/"
      && request.Target.Contains "/commits"
    then
      ok logPageJson
    elif request.Target.StartsWith "/api/v1/users/" then
      ok profileJson
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif request.Target.Contains "/replies" then
      ok
        "{\"data\":[],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
    elif request.Target.StartsWith "/api/v1/commits/" then
      ok threadPageJson
    elif request.Target.StartsWith "/api/v1/scheduled-commits" then
      ok "{}"
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

let tests =
  testList "editor hardening" [
    testCaseAsync
      "three fake minutes poll once a minute while active and never search"
      (async {
        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/log/" then
              ok logPageJson
            elif request.Target.StartsWith "/api/v1/scheduled-commits" then
              ok "{}"
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let env = TestEnv(signedInStore(), api :> IHttp)
        let active = new BehaviorSubject<bool>(true)

        let props = {
          EditorFrame.props env with
              Active = active
        }

        let! window, handle =
          probeUiAsync(fun () ->
            let control, handle = EditorRoot.mount(ui, props)

            let window =
              Window(Width = 1600.0, Height = 900.0, Content = control)

            window.Show()
            window, handle)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"

        do! settleAsync()

        let before = logCount api
        Expect.isGreaterThan before 0 "the first page never read"

        let! polled =
          waitForAsync(
            7000,
            (fun () -> logCount api),
            fun count -> count >= before + 3
          )

        Expect.isTrue polled "three fake minutes never polled"

        Expect.equal (searchCount api) 0 "a search ran before a query"

        do! onUiAsync(fun () -> active.OnNext false)
        do! Async.Sleep 2200
        do! settleAsync()

        let idle = logCount api
        Expect.equal idle (before + 3) "an inactive stream kept polling"

        do! onUiAsync(fun () -> active.OnNext true)

        let! resumed =
          waitForAsync(
            4000,
            (fun () -> logCount api),
            fun count -> count = idle + 1
          )

        Expect.isTrue resumed "an active stream did not resume polling"
        Expect.equal (searchCount api) 0 "a search polled without a query"

        do!
          onUiAsync(fun () ->
            window.Close()
            handle.Dispose())
      })

    testCaseAsync
      "mountFor sends the terminal open and Escape closes it"
      (async {
        let env = TestEnv(signedInStore(), threadApi() :> IHttp)

        let! window, mounted =
          probeUiAsync(fun () ->
            let mounted = EditorRoot.mountFor(ui, EditorFrame.props env)

            let window =
              Window(Width = 1600.0, Height = 900.0, Content = mounted.Root)

            window.Show()
            window, mounted)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"
        Expect.isFalse (mounted.OverlayOpen()) "an overlay stood at rest"

        do! onUiAsync(fun () -> mounted.Send EditorRoot.OpenTerminal)

        let! panel =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "EditorPanel")
          )

        Expect.isTrue panel "the terminal panel never opened"

        Expect.isFalse
          (mounted.OverlayOpen())
          "the docked panel masked the host keys"

        let hostHits = ResizeArray<string>()

        do!
          onUiAsync(fun () ->
            DevNull.Client.Shortcuts.install {
              Root = window
              Actions = {
                Compose = fun () -> hostHits.Add "compose"
                Search = fun () -> ()
                Escape = fun () -> ()
                Navigate = fun _ -> ()
                ToggleFullScreen = fun () -> ()
                WalkCards = fun _ -> ()
              }
              Help = fun () -> hostHits.Add "help"
              OverlayOpen = mounted.OverlayOpen
            }

            window.RaiseEvent(
              KeyEventArgs(
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.OemQuestion
              )
            )

            window.RaiseEvent(
              KeyEventArgs(
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.C
              )
            ))

        Expect.sequenceEqual
          hostHits
          [ "help"; "compose" ]
          "the docked panel swallowed host keys"

        do! onUiAsync(fun () -> mounted.Send EditorRoot.OpenPalette)

        let! palette =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "SearchInput")
          )

        Expect.isTrue palette "the palette never opened over the panel"

        do! onUiAsync(fun () -> mounted.Send EditorRoot.Escape)

        let! paletteClosed =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(visible(root, "SearchInput"))
          )

        Expect.isTrue paletteClosed "Escape did not close the palette first"

        Expect.isFalse
          (mounted.OverlayOpen())
          "the panel still counted as an overlay"

        do! onUiAsync(fun () -> mounted.Send EditorRoot.Escape)

        let! closed =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(visible(root, "EditorPanel"))
          )

        Expect.isTrue closed "Escape did not close the panel"
        Expect.isFalse (mounted.OverlayOpen()) "the overlay probe stayed true"

        do!
          onUiAsync(fun () ->
            window.Close()
            (mounted :> IDisposable).Dispose())
      })

    testCaseAsync
      "the default layout affordance is the status strip's way back"
      (async {
        let api = threadApi()
        let env = TestEnv(signedInStore(), api :> IHttp)
        let mutable asked = false

        let props = {
          EditorFrame.props env with
              OnUseDefaultLayout = fun () -> asked <- true
        }

        let! window, handle =
          probeUiAsync(fun () ->
            let control, handle = EditorRoot.mount(ui, props)

            let window =
              Window(Width = 1600.0, Height = 900.0, Content = control)

            window.Show()
            window, handle)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"

        let! queueRow =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(window, "ExplorerOpen-QUEUE")
          )

        Expect.isTrue queueRow "the queue row never appeared"

        do! clickName(window, "EditorDefaultLayout")
        Expect.isTrue asked "the default layout affordance did nothing"

        let! privateHelp =
          probeUiAsync(fun () ->
            visible(window, "EditorHelp")
            || visible(window, "EditorHelpOverlay"))

        Expect.isFalse privateHelp "the editor kept its own help overlay"

        do!
          onUiAsync(fun () ->
            window.Close()
            handle.Dispose())
      })

    testCaseAsync
      "the frame churn drops its tab programs and root"
      (async {
        let api = churnApi()
        let env = TestEnv(signedInStore(), api :> IHttp)

        let mutable handle: IDisposable option = None

        let! window, weak =
          probeUiAsync(fun () ->
            let control, mounted = EditorRoot.mount(ui, EditorFrame.props env)

            let window =
              Window(Width = 1600.0, Height = 900.0, Content = control)

            window.Show()
            handle <- Some mounted
            window, WeakReference(control))

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"

        for _ in 1..20 do
          do!
            onUiAsync(fun () ->
              buttonWithText(window, "It was DNS.") |> Option.iter click)

          let! opened =
            waitForAsync(
              5000,
              (fun () -> ThreadTab.Live.current()),
              fun count -> count = 1
            )

          Expect.isTrue opened "the thread program never started"

          let commitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

          do!
            onUiAsync(fun () ->
              controlByName(window, $"EditorTabClose-{commitId}")
              |> Option.iter(fun control -> click(control :?> Button)))

          let! closed =
            waitForAsync(
              5000,
              (fun () -> ThreadTab.Live.current()),
              fun count -> count = 0
            )

          Expect.isTrue closed "the thread program outlived its tab"
          do! settleAsync()

        let handles = [ "ana"; "bo"; "cy" ]

        for handle in handles do
          do! clickName(window, "EditorSearch")

          let! opened =
            waitForAsync(5000, (fun () -> visible(window, "SearchInput")), id)

          Expect.isTrue opened "the palette never opened"

          do! typeQuery(window, "three")

          let index = List.findIndex ((=) handle) handles

          let! row =
            waitForAsync(
              5000,
              (fun () -> visible(window, $"SearchRow{index}")),
              id
            )

          Expect.isTrue row "the repository result never landed"

          do! clickName(window, $"SearchRow{index}")

          let! repo =
            waitForAsync(
              8000,
              (fun () -> RepoTab.Live.current()),
              fun count -> count = List.findIndex ((=) handle) handles + 1
            )

          Expect.isTrue repo "the repository program never started"

          let! paletteClosed =
            waitForAsync(
              5000,
              (fun () -> window),
              fun root -> not(visible(root, "SearchInput"))
            )

          Expect.isTrue paletteClosed "the search dialog stayed attached"

        Expect.equal (ThreadTab.Live.current()) 0 "a thread program remained"
        Expect.equal (RepoTab.Live.current()) 3 "a repository program remained"

        do! onUiAsync(fun () -> window.Content <- Panel())
        do! settleAsync()

        do!
          onUiAsync(fun () ->
            window.Close()

            handle |> Option.iter(fun mounted -> mounted.Dispose())

            handle <- None)

        collect()

        Expect.equal (ThreadTab.Live.current()) 0 "the thread programs survived"

        Expect.equal
          (RepoTab.Live.current())
          0
          "the repository programs survived"

        Expect.isFalse weak.IsAlive "the frame outlived its mount"
      })

    testCaseAsync
      "terminal and remotes mount lazily and only one explorer lives at desktop width"
      (async {
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
        | SignedOut -> failwith "the test needs a signed-in session"

        let inboxJson =
          """{"data":[{"id":"row-hubot","kind":"direct","name":null,"partner":{"id":"01a151db-7fee-727b-a7ce-c911a7556243","handle":"hubot","name":"The Hubot","avatar_url":null},"members":null,"last_message":null,"last_message_at":"2026-10-21T04:00:00Z","ahead":0,"last_incoming_message_id":null,"last_incoming_sender":null,"partner_read_through_id":0,"partner_read_at":null,"is_archived":false,"pinned_at":null,"muted_until_at":null}],"has_more":false,"unread_count":0,"requests_count":0}"""

        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/remotes" then
              ok inboxJson
            elif request.Target.StartsWith "/api/v1/log/" then
              ok logPageJson
            elif request.Target.StartsWith "/api/v1/scheduled-commits" then
              ok "{}"
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let env = TestEnv(store, api :> IHttp)

        let! window, mounted =
          probeUiAsync(fun () ->
            let mounted = EditorRoot.mountFor(ui, EditorFrame.props env)

            let window =
              Window(Width = 1600.0, Height = 900.0, Content = mounted.Root)

            window.Show()
            window, mounted)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"
        do! settleAsync()

        let! explorerCount, hasComposer =
          probeUiAsync(fun () ->
            let explorers =
              window.GetVisualDescendants()
              |> Seq.filter(fun visual -> visual.Name = "EditorExplorer")
              |> Seq.length

            explorers, controlByName(window, "ComposerBody").IsSome)

        Expect.equal
          explorerCount
          1
          "desktop width mounted duplicate explorer trees"

        Expect.isFalse hasComposer "terminal mounted its composer while closed"

        let remotesBefore =
          api.Requests
          |> List.filter(fun request ->
            request.Target.StartsWith "/api/v1/remotes")
          |> List.length

        Expect.equal
          remotesBefore
          0
          "remotes inbox polled before the panel opened"

        do! onUiAsync(fun () -> mounted.Send EditorRoot.OpenTerminal)

        let! composerShown =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ComposerBody")),
            Option.isSome
          )

        Expect.isTrue composerShown "opening terminal never mounted composer"

        let! composerWeak =
          probeUiAsync(fun () ->
            controlByName(window, "ComposerBody") |> Option.map WeakReference)

        do! onUiAsync(fun () -> mounted.Send EditorRoot.Escape)

        let! panelClosed =
          waitForAsync(
            5000,
            (fun () -> not(visible(window, "EditorPanel"))),
            id
          )

        Expect.isTrue panelClosed "closing terminal left panel open"
        do! settleAsync()
        collect()

        Expect.isFalse
          composerWeak.Value.IsAlive
          "composer survived closing the terminal panel"

        do!
          onUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? Button as button when ToolTip.GetTip button = box "Remotes" ->
                Some button
              | _ -> None)
            |> Option.iter click)

        let! listed =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> buttonWithText(root, "@hubot").IsSome
          )

        Expect.isTrue listed "remotes inbox never mounted on demand"

        let! hubotWeak =
          probeUiAsync(fun () ->
            buttonWithText(window, "@hubot") |> Option.map WeakReference)

        do! onUiAsync(fun () -> mounted.Send EditorRoot.Escape)

        let! remotesClosed =
          waitForAsync(
            5000,
            (fun () -> not(visible(window, "EditorPanel"))),
            id
          )

        Expect.isTrue remotesClosed "closing remotes left panel open"
        do! settleAsync()
        collect()

        Expect.isFalse
          hubotWeak.Value.IsAlive
          "remotes inbox survived closing the panel"

        do!
          onUiAsync(fun () ->
            window.Close()
            (mounted :> IDisposable).Dispose())
      })

    testCaseAsync
      "reloading the pinned history by clicking the active ref disposes previous rows"
      (async {
        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/log/" then
              ok logPageJson
            elif request.Target.StartsWith "/api/v1/scheduled-commits" then
              ok "{}"
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let env = TestEnv(signedInStore(), api :> IHttp)

        let! window, handle =
          probeUiAsync(fun () ->
            let control, handle = EditorRoot.mount(ui, EditorFrame.props env)

            let window =
              Window(Width = 1600.0, Height = 900.0, Content = control)

            window.Show()
            window, handle)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"
        do! settleAsync()

        let! oldRow =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(function
              | :? TextBlock as block when textBlockShows(block, "It was DNS.") ->
                Some(WeakReference block)
              | _ -> None))

        Expect.isSome oldRow "the initial history row never rendered"

        let before =
          api.Requests
          |> List.filter(fun request ->
            request.Target = "/api/v1/log/following")
          |> List.length

        do! clickName(window, "EditorExplorerRef-Following")

        let! reloaded =
          waitForAsync(
            8000,
            (fun () ->
              api.Requests
              |> List.filter(fun request ->
                request.Target = "/api/v1/log/following")
              |> List.length),
            fun count -> count = before + 1
          )

        Expect.isTrue reloaded "clicking the active ref never reloaded history"
        do! settleAsync()
        collect()

        Expect.isFalse
          oldRow.Value.IsAlive
          "the previous history rows survived reloading the active ref"

        do!
          onUiAsync(fun () ->
            window.Close()
            handle.Dispose())
      })

    testCaseAsync
      "thread tab releases replaced main card on star and survives tab switching"
      (async {
        let commitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

        let starredCommitJson =
          commitJson
            .Replace("\"is_starred\":false", "\"is_starred\":true")
            .Replace("\"stars_count\":3", "\"stars_count\":4")

        let api =
          FakeApi(fun request ->
            if
              request.Method = "POST"
              && request.Target = $"/api/v1/commits/{commitId}/star"
            then
              ok(sprintf "{\"data\":%s}" starredCommitJson)
            elif request.Target.Contains "/replies" then
              ok
                "{\"data\":[],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
            elif request.Target.StartsWith "/api/v1/commits/" then
              ok threadPageJson
            elif request.Target.StartsWith "/api/v1/log/" then
              ok logPageJson
            elif request.Target.StartsWith "/api/v1/scheduled-commits" then
              ok "{}"
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let env = TestEnv(signedInStore(), api :> IHttp)

        let! window, handle =
          probeUiAsync(fun () ->
            let control, handle = EditorRoot.mount(ui, EditorFrame.props env)

            let window =
              Window(Width = 1600.0, Height = 900.0, Content = control)

            window.Show()
            window, handle)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! threadShown =
          waitForAsync(
            8000,
            (fun () -> controlByName(window, "StarButton")),
            Option.isSome
          )

        Expect.isTrue threadShown "the thread main card never rendered"

        do! clickName(window, "EditorTab-history")
        do! settleAsync()
        do! clickName(window, $"EditorTab-{commitId}")

        let! returned =
          waitForAsync(5000, (fun () -> visible(window, "StarButton")), id)

        Expect.isTrue returned "the thread main card broke after tab switch"

        let! oldStar =
          probeUiAsync(fun () ->
            controlByName(window, "StarButton") |> Option.map WeakReference)

        do! clickName(window, "StarButton")

        let! starred =
          waitForAsync(
            8000,
            (fun () ->
              api.Requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target = $"/api/v1/commits/{commitId}/star")),
            id
          )

        Expect.isTrue starred "starring the main commit never completed"
        do! settleAsync()
        collect()

        Expect.isFalse
          oldStar.Value.IsAlive
          "the replaced thread main card stayed rooted after star"

        do!
          onUiAsync(fun () ->
            window.Close()
            handle.Dispose())
      })

    testCaseAsync
      "repo tab virtualizes followers and preserves pinned card across follow toggle and tab switch"
      (async {
        let pinnedProfileJson =
          profileJson
            .Replace(
              "\"pinned_commit\":null",
              $"\"pinned_commit\":{commitJson}"
            )
            .Replace("\"followers_count\":0", "\"followers_count\":100")

        let followersJson =
          let rows =
            [
              for index in 0..99 ->
                (userJson $"follower{index}")
                  .Replace(
                    "\"id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\"",
                    sprintf
                      "\"id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b%04x\""
                      index
                  )
            ]
            |> String.concat ","

          sprintf
            "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":100,\"next_cursor\":null,\"prev_cursor\":null}}"
            rows

        let api =
          FakeApi(fun request ->
            if
              request.Target.StartsWith "/api/v1/users/"
              && request.Target.Contains "/followers"
            then
              ok followersJson
            elif
              request.Target.StartsWith "/api/v1/users/"
              && request.Target.Contains "/commits"
            then
              ok
                "{\"data\":[],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
            elif
              request.Method = "POST"
              && request.Target.StartsWith "/api/v1/users/"
              && request.Target.Contains "/follow"
            then
              ok "{}"
            elif request.Target.StartsWith "/api/v1/users/" then
              ok pinnedProfileJson
            elif request.Target.StartsWith "/api/v1/log/" then
              ok logPageJson
            elif request.Target.StartsWith "/api/v1/scheduled-commits" then
              ok "{}"
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let env = TestEnv(signedInStore(), api :> IHttp)

        let! window, handle =
          probeUiAsync(fun () ->
            let control, handle = EditorRoot.mount(ui, EditorFrame.props env)

            let window =
              Window(Width = 1600.0, Height = 900.0, Content = control)

            window.Show()
            window, handle)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"

        do! clickName(window, "EditorExplorerViewer")

        let! readmeShown =
          waitForAsync(8000, (fun () -> visible(window, "RepoReadme")), id)

        Expect.isTrue readmeShown "the pinned readme card never rendered"

        let! pinnedBefore =
          probeUiAsync(fun () -> controlByName(window, "RepoReadme"))

        do! clickName(window, "RepoFollowers")

        let! followersLoaded =
          waitForAsync(
            8000,
            (fun () ->
              window.GetVisualDescendants()
              |> Seq.filter(fun visual ->
                visual.Name <> null && visual.Name.StartsWith "RepoList-")
              |> Seq.length),
            fun count -> count > 0
          )

        Expect.isTrue followersLoaded "the followers list never rendered"
        do! settleAsync()

        let! realizedFollowers, pinnedAfter =
          probeUiAsync(fun () ->
            let count =
              window.GetVisualDescendants()
              |> Seq.filter(fun visual ->
                visual.Name <> null && visual.Name.StartsWith "RepoList-")
              |> Seq.length

            count, controlByName(window, "RepoReadme"))

        Expect.isLessThan
          realizedFollowers
          30
          "the repo followers list realized too many of its hundred rows"

        Expect.isTrue
          (obj.ReferenceEquals(pinnedBefore.Value, pinnedAfter.Value))
          "toggling the followers list rebuilt the pinned commit card"

        do! clickName(window, "EditorTab-history")
        do! settleAsync()
        do! clickName(window, "EditorTab-repo:octocat")

        let! readmeReturned =
          waitForAsync(5000, (fun () -> visible(window, "RepoReadme")), id)

        Expect.isTrue
          readmeReturned
          "the repo pinned card broke after tab switching"

        do!
          onUiAsync(fun () ->
            window.Close()
            handle.Dispose())
      })
  ]
