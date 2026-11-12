module DevNull.Client.UI.Tests.WorkbenchHardening

open System
open System.Reactive.Subjects
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive
open MVUish.Avalonia

open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Programs
open DevNull.Client.Stores
open DevNull.Client.Workbench
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let private now = DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)

let private following =
  WorkbenchSettings.parse "following" |> WorkbenchState.refOf

let private notifications =
  WorkbenchSettings.parse "notifications" |> WorkbenchState.refOf

let private images = {
  new ImageCacheService with
    member _.Get(_url: string) = async { return None }
}

let private zone: Zone = {
  Info = TimeZoneInfo.Utc
  Label = "UTC"
}

let private historyProps(env: TestEnv) : HistoryColumn.Props<TestEnv> = {
  Env = env
  Log = Following
  KeyFile = "/tmp/devnull-test-id_ed25519"
  ViewerId = "01a151db-0000-0000-0000-000000000000"
  Pasteis = false
  Images = images
  Now = Observable.single now
  Refresh = Observable.empty
  Zone = zone
  OnOpenExternal = ignore
  OnOpened = ignore
  OnQuote = ignore
  Restore = None
  OnReport = ignore
}

let private hostProps
  (env: TestEnv, clock: IObservable<DateTimeOffset>)
  : WorkbenchHost.Props<TestEnv> = {
  Env = env
  Initial = WorkbenchSettings.initial
  Images = images
  Now = clock
  Zone = zone
  OnOpenAuthor = ignore
  OnOpenExternal = ignore
  OnStateChanged = ignore
}

let private getCount(api: FakeApi, path: string) : int =
  api.Requests
  |> List.filter(fun request ->
    request.Method = "GET" && request.Target.StartsWith path)
  |> List.length

let private logCount(api: FakeApi) : int = getCount(api, "/api/v1/log/")

let private notificationCount(api: FakeApi) : int =
  getCount(api, "/api/v1/notifications")

let private searchCount(api: FakeApi) : int =
  getCount(api, "/api/v1/search/commits")

let private controlByName(root: Control, name: string) : Control option =
  root.GetVisualDescendants()
  |> Seq.choose(function
    | :? Control as control when control.Name = name -> Some control
    | _ -> None)
  |> Seq.tryHead

let private clickName(window: Window, name: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, name)
    |> Option.iter(function
      | :? Button as button -> click button
      | _ -> ()))

let private hasTextFragment(root: Control, fragment: string) : bool =
  root.GetVisualDescendants()
  |> Seq.exists(function
    | :? TextBlock as block ->
      block.Text <> null && block.Text.Contains fragment
    | _ -> false)

let tests =
  testList "workbench hardening" [
    testCaseAsync
      "the shown history polls once a fake minute only while active"
      (async {
        let api = workbenchApi()
        let env = TestEnv(signedInStore(), api :> IHttp)
        let props = historyProps env
        let active = new BehaviorSubject<bool>(true)
        let minutes = new System.Reactive.Subjects.Subject<unit>()
        let seen = ResizeArray<HistoryColumn.Model>()

        let minuteMessages =
          Observable.combineLatest active minutes
          |> Observable.filter fst
          |> Observable.map(fun _ -> HistoryColumn.CheckNew)

        let program =
          Program.mkProgram
            (fun () -> HistoryColumn.init props)
            (fun msg model -> HistoryColumn.update(props, msg, model))
            (HistoryColumn.view props)
          |> Program.withSubscription(fun model ->
            seen.Add model

            Sub.batch [
              HistoryColumn.subscribe props model

              Sub.fromObservable
                "active"
                (active |> Observable.map HistoryColumn.PollingChanged)

              Sub.fromObservable "minutes" minuteMessages
            ])

        let! view, handle =
          probeUiAsync(fun () -> DevNull.Client.Programs.runPage(ui, program))

        let! window =
          probeUiAsync(fun () ->
            let window = Window(Width = 1200.0, Height = 700.0, Content = view)

            window.Show()
            window)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered its first page"
        do! settleAsync()

        let baseModel, _ = HistoryColumn.init props

        let ids(model: HistoryColumn.Model) =
          HistoryColumn.subscribe props model
          |> List.map(fun (id, _) -> sprintf "%A" id)
          |> String.concat "|"

        Expect.stringContains
          (ids baseModel)
          "since-poll"
          "the active history lost its poll subscription"

        Expect.isFalse
          ((ids { baseModel with Polling = false }).Contains "since-poll")
          "the inactive history kept its poll subscription"

        let before = logCount api
        Expect.isGreaterThan before 0 "the first page never read"

        for _ in 1..3 do
          do! onUiAsync(fun () -> minutes.OnNext(()))

        let! polled =
          waitForAsync(
            8000,
            (fun () -> logCount api),
            fun count -> count = before + 3
          )

        Expect.isTrue
          polled
          "three active fake minutes did not poll three times"

        Expect.equal (searchCount api) 0 "search ran without a query"

        do! onUiAsync(fun () -> active.OnNext false)

        let! inactive =
          waitForAsync(
            5000,
            (fun () ->
              seen
              |> Seq.tryLast
              |> Option.map _.Polling
              |> Option.defaultValue true),
            not
          )

        Expect.isTrue inactive "the history did not leave its active state"

        let idle = logCount api

        for _ in 1..3 do
          do! onUiAsync(fun () -> minutes.OnNext(()))

        do! Async.Sleep 250
        do! settleAsync()

        Expect.equal (logCount api) idle "an inactive history kept polling"

        Expect.equal (searchCount api) 0 "search polled without a query"

        do!
          onUiAsync(fun () ->
            window.Close()
            handle.Dispose()
            active.OnCompleted()
            active.Dispose()
            minutes.OnCompleted()
            minutes.Dispose())
      })

    testCaseAsync
      "the notifications and search centers read on entry only"
      (async {
        let api = workbenchApi()
        let env = TestEnv(signedInStore(), api :> IHttp)
        let clock = new BehaviorSubject<DateTimeOffset>(now)

        let props = hostProps(env, clock)

        let! window, mounted =
          probeUiAsync(fun () ->
            let view, handle = WorkbenchHost.mount(ui, props)

            let window = Window(Width = 1600.0, Height = 900.0, Content = view)

            window.Show()
            window, handle :?> WorkbenchHost.MountedHost)

        let! history =
          waitForAsync(
            8000,
            (fun () -> mounted.LiveHistory),
            fun count -> count = 1
          )

        Expect.isTrue history "the history program never started"

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the workbench never rendered its history"

        let beforeNotifications = notificationCount api

        do!
          onUiAsync(fun () ->
            mounted.Send(WorkbenchHost.RefSelected notifications))

        let! incoming =
          waitForAsync(
            8000,
            (fun () -> mounted.LiveNotifications),
            fun count -> count = 1
          )

        Expect.isTrue incoming "the notifications program never started"

        let! rows =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasTextFragment(root, "NOTIFICATIONS")
          )

        Expect.isTrue rows "the notifications center never rendered"

        let afterEntry = notificationCount api

        Expect.equal
          (afterEntry - beforeNotifications)
          1
          "the notifications center did not read once on entry"

        for minute in 1..3 do
          do! onUiAsync(fun () -> clock.OnNext(now.AddMinutes(float minute)))

        do! settleAsync()

        Expect.equal
          (notificationCount api)
          afterEntry
          "the notifications center kept reading after entry"

        do!
          onUiAsync(fun () -> mounted.Send(WorkbenchHost.RefSelected following))

        let! gone =
          waitForAsync(
            8000,
            (fun () -> mounted.LiveNotifications),
            fun count -> count = 0
          )

        Expect.isTrue gone "the notifications program stayed live"

        let beforeSearch = searchCount api

        do!
          onUiAsync(fun () -> mounted.Send(WorkbenchHost.SearchCommitted "dns"))

        let! searching =
          waitForAsync(
            8000,
            (fun () -> mounted.LiveSearch),
            fun count -> count = 1
          )

        Expect.isTrue searching "the search program never started"

        let! results =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasTextFragment(root, "Search · manual refresh")
          )

        Expect.isTrue results "the search center never rendered"

        let afterSearch = searchCount api

        Expect.equal
          (afterSearch - beforeSearch)
          1
          "the search center did not read once on entry"

        for minute in 1..3 do
          do! onUiAsync(fun () -> clock.OnNext(now.AddMinutes(float minute)))

        do! settleAsync()

        Expect.equal
          (searchCount api)
          afterSearch
          "the search center polled without a refresh"

        do! clickName(window, "SearchRefresh")

        let! refreshed =
          waitForAsync(
            8000,
            (fun () -> searchCount api),
            fun count -> count = afterSearch + 1
          )

        Expect.isTrue refreshed "the manual refresh never read"

        for minute in 1..3 do
          do! onUiAsync(fun () -> clock.OnNext(now.AddMinutes(float minute)))

        do! settleAsync()

        Expect.equal
          (searchCount api)
          (afterSearch + 1)
          "the search center polled after its manual refresh"

        do!
          onUiAsync(fun () ->
            window.Close()
            (mounted :> IDisposable).Dispose()
            clock.OnCompleted()
            clock.Dispose())
      })
  ]
