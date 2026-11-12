module DevNull.Client.UI.Tests.WorkbenchHostTests

open System
open System.Collections.Generic
open Expecto
open FSharp.Control.Reactive
open MVUish.Avalonia

open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Workbench
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let private now = DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)

let private checkoutA = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
let private checkoutB = "018f4c1e-9999-7c3a-9b2d-5e6f7a8b9c0d"

let private api =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif request.Target.Contains "/replies" then
      ok repliesPageJson
    elif request.Target.StartsWith "/api/v1/commits/" then
      ok threadPageJson
    else
      ok "{}")

let private images = {
  new ImageCacheService with
    member _.Get(_url: string) = async { return None }
}

let private hostProps
  (env: TestEnv, initial: WorkbenchState)
  : WorkbenchHost.Props<TestEnv> = {
  Env = env
  Initial = initial
  Images = images
  Now = Observable.single now
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  OnOpenAuthor = ignore
  OnOpenExternal = ignore
  OnStateChanged = ignore
}

let private following =
  WorkbenchSettings.parse "following" |> WorkbenchState.refOf

let private latest = WorkbenchSettings.parse "latest" |> WorkbenchState.refOf

let private trending =
  WorkbenchSettings.parse "trending" |> WorkbenchState.refOf

let private followingCommit =
  match LogPage.decode logPageJson with
  | Ok page ->
    match page.Page.Items with
    | LiveCommit commit :: _ -> commit
    | _ -> failwith "the canned log carries no live commit"
  | Error failure -> failwith $"the canned log never decoded: {failure.Message}"

let private emptyQueueJson =
  """{"data":[],"links":{"first":null,"last":null,"prev":null,"next":null},"meta":{"current_page":1,"from":0,"last_page":1,"path":"","per_page":20,"to":0,"total":0}}"""

let private countingApi() =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/scheduled-commits" then
      ok emptyQueueJson
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif request.Target.Contains "/replies" then
      ok repliesPageJson
    elif request.Target.StartsWith "/api/v1/commits/" then
      ok threadPageJson
    else
      ok "{}")

let private runEngine
  (
    props: WorkbenchHost.Props<TestEnv>,
    model: WorkbenchHost.Model,
    cmd: Cmd<WorkbenchHost.Msg>
  ) =
  let queue = Queue<WorkbenchHost.Msg>()

  let dispatch(msg: WorkbenchHost.Msg) = queue.Enqueue msg

  let fail(_: string, ex: exn) = failwith ex.Message

  cmd dispatch fail

  let rec loop(model: WorkbenchHost.Model) =
    if queue.Count = 0 then
      model
    else
      let msg = queue.Dequeue()
      let next, nextCmd = WorkbenchHost.update(props, msg, model)
      nextCmd dispatch fail
      loop next

  loop model

let tests =
  testList "workbench host" [
    testCaseAsync
      "the host keeps one history program and restores each place"
      (async {
        let! observations, finalLive =
          probeUiAsync(fun () ->
            let env = TestEnv(signedInStore(), api :> IHttp)
            let props = hostProps(env, WorkbenchSettings.initial)
            let model, cmd = WorkbenchHost.init props
            let model = runEngine(props, model, cmd)

            Expect.equal model.LiveHistory 1 "the first history program"

            let place: HistoryColumn.Place = {
              Offset = 120.0
              Head = Some "head-a"
            }

            let model, _ =
              WorkbenchHost.update(
                props,
                WorkbenchHost.HistoryReported(
                  following,
                  { Place = place; FetchedAt = Some now }
                ),
                model
              )

            let switches = [
              latest
              following
              trending
              latest
              following
              trending
              latest
              following
              trending
              following
            ]

            let observations =
              switches
              |> List.map(fun entry ->
                let model, cmd =
                  WorkbenchHost.update(
                    props,
                    WorkbenchHost.RefSelected entry,
                    model
                  )

                let model = runEngine(props, model, cmd)

                let restore = model.History |> Option.bind _.Restore

                let live = model.LiveHistory
                let checkout = model.CheckoutId
                (entry, live, restore, checkout))

            model.History |> Option.iter(fun child -> child.Handle.Dispose())

            model.Inspector |> Option.iter(fun child -> child.Handle.Dispose())

            observations, model.LiveHistory)

        Expect.isTrue
          (observations |> List.forall(fun (_, live, _, _) -> live <= 1))
          "a second history program stayed live"

        Expect.equal finalLive 1 "the last history program"

        Expect.isTrue
          (observations
           |> List.exists(fun (entry, _, restore, _) ->
             entry = following
             && restore = Some { Offset = 120.0; Head = Some "head-a" }))
          "the saved place never came back"

        Expect.isTrue
          (observations
           |> List.forall(fun (_, _, _, checkout) -> checkout.IsNone))
          "a ref switch changed the checkout"
      })

    testCaseAsync
      "the host disposes and restarts the inspector on a checkout change"
      (async {
        let! state =
          probeUiAsync(fun () ->
            let initial = WorkbenchSettings.parse("following|" + checkoutA)

            let env = TestEnv(signedInStore(), api :> IHttp)
            let props = hostProps(env, initial)
            let model, cmd = WorkbenchHost.init props
            let model = runEngine(props, model, cmd)

            let first = model.Inspector

            let model, cmd =
              WorkbenchHost.update(
                props,
                WorkbenchHost.CheckoutSet(Some checkoutB),
                model
              )

            let model = runEngine(props, model, cmd)
            let second = model.Inspector

            let changed =
              match first, second with
              | Some left, Some right ->
                left.CommitId = checkoutA
                && right.CommitId = checkoutB
                && not(obj.ReferenceEquals(left.View, right.View))
              | _ -> false

            let model, cmd =
              WorkbenchHost.update(
                props,
                WorkbenchHost.CheckoutSet None,
                model
              )

            let model = runEngine(props, model, cmd)
            let cleared = model.Inspector.IsNone && model.LiveInspector = 0

            let model, cmd =
              WorkbenchHost.update(
                props,
                WorkbenchHost.CheckoutSet(Some checkoutA),
                model
              )

            let model = runEngine(props, model, cmd)

            model.Inspector |> Option.iter(fun child -> child.Handle.Dispose())

            changed,
            cleared,
            model.Inspector |> Option.map _.CommitId,
            model.LiveInspector)

        let changed, cleared, finalCheckout, finalLive = state

        Expect.isTrue changed "the inspector did not restart on the new id"
        Expect.isTrue cleared "the inspector did not stop on no id"
        Expect.equal finalCheckout (Some checkoutA) "the last checkout"
        Expect.equal finalLive 1 "the last inspector program"
      })

    testCaseAsync
      "the host emits ref and checkout changes as plain state"
      (async {
        let! states =
          probeUiAsync(fun () ->
            let env = TestEnv(signedInStore(), api :> IHttp)
            let states = ResizeArray<WorkbenchState>()

            let props = {
              hostProps(env, WorkbenchSettings.initial) with
                  OnStateChanged = states.Add
            }

            let model, cmd = WorkbenchHost.init props
            let model = runEngine(props, model, cmd)

            let model, cmd =
              WorkbenchHost.update(
                props,
                WorkbenchHost.RefSelected latest,
                model
              )

            let model = runEngine(props, model, cmd)

            let model, cmd =
              WorkbenchHost.update(
                props,
                WorkbenchHost.CheckoutSet(Some checkoutA),
                model
              )

            let model = runEngine(props, model, cmd)

            model.History |> Option.iter(fun child -> child.Handle.Dispose())

            model.Inspector |> Option.iter(fun child -> child.Handle.Dispose())

            states |> Seq.map WorkbenchSettings.store |> Seq.toList)

        Expect.equal
          states
          [ "latest"; "latest|" + checkoutA ]
          "the state changes did not ride the sink"
      })

    testCaseAsync
      "the mount handle ends every child it recorded"
      (async {
        let! _view, mounted =
          probeUiAsync(fun () ->
            let env = TestEnv(signedInStore(), api :> IHttp)
            let props = hostProps(env, WorkbenchSettings.initial)
            let view, handle = WorkbenchHost.mount(Harness.ui, props)
            view, handle :?> WorkbenchHost.MountedHost)

        let! first =
          waitForAsync(
            5000,
            (fun () -> mounted.LiveHistory),
            fun live -> live = 1
          )

        Expect.isTrue first "the first history program never landed"

        Expect.isTrue
          (mounted.Created.Length >= 1)
          "no child handle was recorded"

        let sendAsync(msg: WorkbenchHost.Msg, expectedCreated: int) = async {
          do! onUiAsync(fun () -> mounted.Send msg)

          let! grown =
            waitForAsync(
              5000,
              (fun () -> mounted.Created.Length),
              fun count -> count >= expectedCreated
            )

          Expect.isTrue
            grown
            $"the child handle count stopped at {mounted.Created.Length}"
        }

        do! sendAsync(WorkbenchHost.RefSelected latest, 2)

        let! historyOne =
          waitForAsync(
            5000,
            (fun () -> mounted.LiveHistory),
            fun live -> live = 1
          )

        Expect.isTrue
          historyOne
          "the second history program did not stay single"

        do! sendAsync(WorkbenchHost.CheckoutSet(Some checkoutA), 3)

        let! inspectorOne =
          waitForAsync(
            5000,
            (fun () -> mounted.LiveInspector),
            fun live -> live = 1
          )

        Expect.isTrue inspectorOne "the first inspector program never landed"

        do! sendAsync(WorkbenchHost.RefSelected trending, 4)

        do! sendAsync(WorkbenchHost.CheckoutSet(Some checkoutB), 5)
        do! sendAsync(WorkbenchHost.RefSelected following, 6)

        do! onUiAsync(fun () -> mounted.Send(WorkbenchHost.CheckoutSet None))

        let! inspectorGone =
          waitForAsync(
            5000,
            (fun () -> mounted.LiveInspector),
            fun live -> live = 0
          )

        Expect.isTrue inspectorGone "the inspector did not stop on no id"

        do! sendAsync(WorkbenchHost.CheckoutSet(Some checkoutA), 7)

        let created = mounted.Created

        do!
          onUiAsync(fun () ->
            (mounted :> IDisposable).Dispose()
            (mounted :> IDisposable).Dispose())

        Expect.isTrue
          (created |> List.forall(fun handle -> handle.Disposed))
          "a child handle the mount saw stayed live"

        Expect.equal mounted.LiveHistory 0 "the history count stayed up"
        Expect.equal mounted.LiveInspector 0 "the inspector count stayed up"
        Expect.isTrue mounted.HostDisposed "the host handle stayed live"
      })

    testCase "the staging strip opens and closes"
    <| fun () ->
      let env = TestEnv(signedInStore(), api :> IHttp)
      let props = hostProps(env, WorkbenchSettings.initial)
      let model, _ = WorkbenchHost.init props

      Expect.isFalse model.StagingOpen "the staging area starts closed"

      let opened, _ =
        WorkbenchHost.update(props, WorkbenchHost.StagingToggled, model)

      Expect.isTrue opened.StagingOpen "the staging area did not open"

      let closed, _ =
        WorkbenchHost.update(props, WorkbenchHost.StagingToggled, opened)

      Expect.isFalse closed.StagingOpen "the staging area did not close"

    testCase "a quote opens the staging area with its target"
    <| fun () ->
      let env = TestEnv(signedInStore(), api :> IHttp)
      let props = hostProps(env, WorkbenchSettings.initial)
      let model, _ = WorkbenchHost.init props

      let quoted, _ =
        WorkbenchHost.update(props, WorkbenchHost.Quote followingCommit, model)

      Expect.isTrue quoted.StagingOpen "the quote did not open staging"

      Expect.equal
        quoted.Staging.Quote
        (Some followingCommit)
        "the quote target did not reach staging"

    testCase "the queue count feeds the refs tree row"
    <| fun () ->
      let env = TestEnv(signedInStore(), api :> IHttp)
      let props = hostProps(env, WorkbenchSettings.initial)
      let model, _ = WorkbenchHost.init props

      let page: ScheduledPage = {
        Items = []
        CurrentPage = 1
        LastPage = 1
        Total = 2
      }

      let loaded, _ =
        WorkbenchHost.update(props, WorkbenchHost.QueueFetched(Ok page), model)

      Expect.equal loaded.Staging.Queue.Total 2 "the queue total did not land"

      let scheduled =
        WorkbenchSettings.parse "scheduled" |> WorkbenchState.refOf

      let row =
        WorkbenchChrome.rows(0, loaded.Staging.Queue.Total, 0)
        |> List.find(fun entry -> entry.Ref = scheduled)

      Expect.equal
        row.Count
        (Some 2)
        "the scheduled row did not carry the count"

    testCaseAsync
      "a posted commit refreshes the shown history once"
      (async {
        let api = countingApi()

        let! _view, mounted =
          probeUiAsync(fun () ->
            let env = TestEnv(signedInStore(), api :> IHttp)
            let props = hostProps(env, WorkbenchSettings.initial)
            let view, handle = WorkbenchHost.mount(Harness.ui, props)
            view, handle :?> WorkbenchHost.MountedHost)

        let logCount() =
          api.Requests
          |> List.filter(fun request ->
            request.Target.StartsWith "/api/v1/log/")
          |> List.length

        let! first = waitForAsync(5000, logCount, fun count -> count >= 1)

        Expect.isTrue first "the first history read never landed"

        do!
          onUiAsync(fun () ->
            mounted.Send(WorkbenchHost.StagingPosted followingCommit))

        let! second = waitForAsync(5000, logCount, fun count -> count >= 2)

        Expect.isTrue second "the post did not refresh the history"

        do! settleAsync()

        Expect.equal
          (logCount())
          2
          "the post refreshed the history more than once"

        do! onUiAsync(fun () -> (mounted :> IDisposable).Dispose())
      })

    testCaseAsync
      "the thread back keeps the history program and its place"
      (async {
        let! (sameDuring,
              sameAfter,
              opened,
              placeKept,
              liveHistory,
              liveThread,
              threadId) =
          probeUiAsync(fun () ->
            let env = TestEnv(signedInStore(), api :> IHttp)
            let props = hostProps(env, WorkbenchSettings.initial)
            let model, cmd = WorkbenchHost.init props
            let model = runEngine(props, model, cmd)

            let historyHandle = model.History |> Option.map _.Handle

            let sameHandle(candidate: IDisposable option) =
              match historyHandle, candidate with
              | Some left, Some right -> obj.ReferenceEquals(left, right)
              | None, None -> true
              | _ -> false

            let place: HistoryColumn.Place = {
              Offset = 222.0
              Head = Some "head-a"
            }

            let model, _ =
              WorkbenchHost.update(
                props,
                WorkbenchHost.HistoryReported(
                  following,
                  { Place = place; FetchedAt = Some now }
                ),
                model
              )

            let model, cmd =
              WorkbenchHost.update(
                props,
                WorkbenchHost.OpenThread checkoutA,
                model
              )

            let model = runEngine(props, model, cmd)

            let during = model.History |> Option.map _.Handle

            let opened =
              model.ThreadId = Some checkoutA
              && model.CheckoutId = Some checkoutA
              && model.LiveThread = 1

            let model, cmd =
              WorkbenchHost.update(props, WorkbenchHost.ThreadClosed, model)

            let model = runEngine(props, model, cmd)

            let after = model.History |> Option.map _.Handle

            let placeKept =
              model.Places
              |> Map.tryFind following
              |> Option.exists(fun kept -> kept.Offset = place.Offset)

            model.History |> Option.iter(fun child -> child.Handle.Dispose())

            model.Inspector |> Option.iter(fun child -> child.Handle.Dispose())

            model.Thread |> Option.iter(fun child -> child.Handle.Dispose())

            sameHandle during,
            sameHandle after,
            opened,
            placeKept,
            model.LiveHistory,
            model.LiveThread,
            model.ThreadId)

        Expect.isTrue sameDuring "the thread open replaced the history program"

        Expect.isTrue sameAfter "the thread back replaced the history program"
        Expect.isTrue opened "the thread did not open its checkout"
        Expect.isTrue placeKept "the thread back dropped the history place"
        Expect.equal liveHistory 1 "the history program count changed"
        Expect.equal liveThread 0 "the thread program stayed live"
        Expect.isNone threadId "the thread id stayed"
      })

    testCase "the fold and counts are model data"
    <| fun () ->
      let env = TestEnv(signedInStore(), api :> IHttp)
      let props = hostProps(env, WorkbenchSettings.initial)
      let model, _ = WorkbenchHost.init props

      Expect.equal model.SavedCount 0 "the saved count is zero"

      let expectedUnread =
        match (env :> IProvideSession).Session.Current with
        | SignedIn session -> session.Session.UnreadNotificationsCount
        | SignedOut -> 0

      Expect.equal
        model.UnreadCount
        expectedUnread
        "the unread count comes from the session"

      let wide, _ =
        WorkbenchHost.update(props, WorkbenchHost.WidthChanged 1600.0, model)

      Expect.isFalse wide.Folded "the wide host folded"

      let narrow, _ =
        WorkbenchHost.update(props, WorkbenchHost.WidthChanged 1000.0, wide)

      Expect.isTrue narrow.Folded "the narrow host did not fold"

      let edge, _ =
        WorkbenchHost.update(props, WorkbenchHost.WidthChanged 1100.0, narrow)

      Expect.isFalse edge.Folded "the 1100-px host folded"

      let changed =
        match (env :> IProvideSession).Session.Current with
        | SignedIn session ->
          SignedIn {
            session with
                Session = {
                  session.Session with
                      UnreadNotificationsCount = 7
                }
          }
        | SignedOut -> SignedOut

      let model, _ =
        WorkbenchHost.update(props, WorkbenchHost.SessionChanged changed, model)

      Expect.equal model.UnreadCount 7 "the session count did not land"
  ]
