module DevNull.Client.UI.Tests.WorkbenchHardening

open System
open System.Reactive.Subjects
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive
open MVUish.Avalonia
open NXUI.Extensions

open DevNull.Client.Components
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Programs
open DevNull.Client.Stores
open DevNull.Client.Theme
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
  Session = None
  Source = LogSource Following
  KeyFile = "/tmp/devnull-test-id_ed25519"
  ViewerId = "01a151db-0000-0000-0000-000000000000"
  Pasteis = false
  Images = images
  Now = Observable.single now
  Refresh = Observable.empty
  Zone = zone
  LastVisit = None
  OnOpenExternal = ignore
  OnOpened = ignore
  OnQuote = ignore
  Restore = None
  OnReport = ignore
  OnDialog = ignore
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
  OnUseDefaultLayout = ignore
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

let private hasTextFragment(root: Control, fragment: string) : bool =
  root.GetVisualDescendants()
  |> Seq.exists(function
    | :? TextBlock as block ->
      block.Text <> null && block.Text.Contains fragment
    | _ -> false)

let private hasVisibleText(root: Control, text: string) : bool =
  root.GetVisualDescendants()
  |> Seq.exists(function
    | :? TextBlock as block -> block.Text = text && block.IsEffectivelyVisible
    | _ -> false)

let private hasClass(control: Control, name: string) : bool =
  control.Classes.Contains name

let private pressKey(source: Interactive, key: Key) : unit =
  source.RaiseEvent(
    KeyEventArgs(RoutedEvent = InputElement.KeyDownEvent, Key = key)
  )

let private starInRow(root: Control, body: string) : Button option =
  root.GetVisualDescendants()
  |> Seq.tryFind(function
    | :? TextBlock as block -> block.Text = body
    | _ -> false)
  |> Option.bind(fun found ->
    found.GetVisualAncestors()
    |> Seq.tryPick(function
      | :? Button as card when card.Classes.Contains "commit-row" -> Some card
      | _ -> None))
  |> Option.bind(fun card ->
    card.GetVisualDescendants()
    |> Seq.tryPick(function
      | :? Button as button when button.Name = "StarButton" -> Some button
      | _ -> None))

let private savedPageJson(json: string) : string =
  json
    .Replace("\"handle\":\"octocat\"", "\"handle\":\"ana\"")
    .Replace("\"name\":\"The Octocat\"", "\"name\":\"Ana\"")
    .Replace(
      "01a151db-7fee-727b-a7ce-c911a7556242",
      "01a151db-9999-727b-a7ce-c911a7556242"
    )
    .Replace("\"starred\":false", "\"starred\":true")

let private savedApi() : FakeApi =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/starred" then
      if request.Target.Contains "cursor=next" then
        ok(savedPageJson secondLogPageJson)
      else
        ok(savedPageJson logPageJson)
    elif request.Target.EndsWith "/star" then
      ok(
        sprintf
          "{\"data\":%s}"
          ((savedPageJson commitJson)
            .Replace("\"starred\":true", "\"starred\":false"))
      )
    elif request.Target.StartsWith "/api/v1/scheduled-commits" then
      ok emptyQueueJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

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
            (fun () -> searchCount api),
            fun count -> count = beforeSearch + 1
          )

        Expect.isTrue results "the search center did not read once on entry"

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
          "the hosted search dialog polled without a query"

        do!
          onUiAsync(fun () ->
            window.Close()
            (mounted :> IDisposable).Dispose()
            clock.OnCompleted()
            clock.Dispose())
      })

    testCaseAsync
      "the saved source drops an unstared node"
      (async {
        let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)

        let props: HistoryColumn.Props<TestEnv> = {
          historyProps env with
              Source = SavedSource
        }

        let page =
          match LogPage.decode logPageJson with
          | Ok page -> page
          | Error failure ->
            failwith $"the canned saved page never decoded: {failure.Message}"

        let initial, _ = HistoryColumn.init props

        let model, _ =
          HistoryColumn.update(props, HistoryColumn.Fetched(Ok page), initial)

        let commit =
          match page.Page.Items with
          | LiveCommit commit :: _ -> commit
          | _ -> failwith "the canned saved page carries no commit"

        let model, _ =
          HistoryColumn.update(
            props,
            HistoryColumn.StarDone(Ok { commit with Starred = Some false }),
            model
          )

        Expect.isEmpty model.Items "the unstared saved node stayed in the model"
      })

    testCaseAsync
      "the saved center lists the starred commits and removes an unstared row"
      (async {
        let api = savedApi()
        let env = TestEnv(signedInStore(), api :> IHttp)

        let props = {
          hostProps(env, Observable.single now) with
              Initial = WorkbenchSettings.parse "saved"
        }

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

        Expect.isTrue history "the saved history program never started"

        let! firstRow =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              hasText(root, "Saved")
              && hasText(root, "It was DNS.")
              && not(hasText(root, "This ref opens in a later phase."))
          )

        Expect.isTrue firstRow "the saved center never rendered its first row"

        Expect.exists
          api.Requests
          (fun request -> request.Target.StartsWith "/api/v1/starred")
          "the saved center never read Stars.index"

        let! more =
          waitForAsync(
            5000,
            (fun () -> buttonWithText(window, "Load more")),
            Option.isSome
          )

        Expect.isTrue more "the saved center offered no cursor"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Load more") |> Option.iter click)

        let! secondRow =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "Second commit.")
          )

        Expect.isTrue secondRow "the saved cursor page never appended"

        let! star =
          waitForAsync(
            5000,
            (fun () ->
              starInRow(window, "It was DNS.")
              |> Option.map(fun button -> button.IsEnabled)
              |> Option.defaultValue false),
            id
          )

        Expect.isTrue star "the saved row carried no enabled star toggle"

        do!
          onUiAsync(fun () ->
            starInRow(window, "It was DNS.") |> Option.iter click)

        let! sent =
          waitForAsync(
            5000,
            (fun () ->
              api.Requests
              |> List.exists(fun request -> request.Method = "DELETE")),
            id
          )

        Expect.isTrue
          sent
          (sprintf
            "the saved row never sent its unstar: %A"
            (api.Requests
             |> List.map(fun request -> request.Method, request.Target)))

        Expect.exists
          api.Requests
          (fun request ->
            request.Method = "DELETE"
            && request.Target.Contains "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d")
          (sprintf
            "the saved row unstared the wrong commit: %A"
            (api.Requests
             |> List.map(fun request -> request.Method, request.Target)))

        let! removed =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> not(hasVisibleText(root, "It was DNS."))
          )

        Expect.isTrue removed "the unstared saved row stayed in the center"

        let! stayed =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasVisibleText(root, "Second commit.")
          )

        Expect.isTrue stayed "the unstared row took the wrong row with it"

        do!
          onUiAsync(fun () ->
            window.Close()
            (mounted :> IDisposable).Dispose())
      })

    testCaseAsync
      "the inspector close control clears the checkout"
      (async {
        let api = workbenchApi()
        let env = TestEnv(signedInStore(), api :> IHttp)

        let checkoutId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

        let props = {
          hostProps(env, Observable.single now) with
              Initial = WorkbenchSettings.parse $"following|{checkoutId}"
        }

        let! window, mounted =
          probeUiAsync(fun () ->
            let view, handle = WorkbenchHost.mount(ui, props)

            let window = Window(Width = 1600.0, Height = 900.0, Content = view)

            window.Show()
            window, handle :?> WorkbenchHost.MountedHost)

        let! inspector =
          waitForAsync(
            8000,
            (fun () -> mounted.LiveInspector),
            fun count -> count = 1
          )

        Expect.isTrue inspector "the inspector never started"

        let! checkout =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasTextFragment(root, "CHECKOUT")
          )

        Expect.isTrue checkout "the inspector never rendered its checkout"

        do!
          onUiAsync(fun () ->
            controlByName(window, "InspectorClose")
            |> Option.iter(function
              | :? Button as button -> click button
              | _ -> ()))

        let! gone =
          waitForAsync(
            8000,
            (fun () -> mounted.LiveInspector),
            fun count -> count = 0
          )

        Expect.isTrue gone "the inspector stayed live after its close control"

        let! empty =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "No commit checked out.")
          )

        Expect.isTrue empty "the empty inspector state never returned"

        do!
          onUiAsync(fun () ->
            window.Close()
            (mounted :> IDisposable).Dispose())
      })

    testCaseAsync
      "the default layout row fires the host sink"
      (async {
        let api = workbenchApi()
        let env = TestEnv(signedInStore(), api :> IHttp)
        let calls = ResizeArray<unit>()

        let props = {
          hostProps(env, Observable.single now) with
              OnUseDefaultLayout = fun () -> calls.Add(())
        }

        let model, _ = WorkbenchHost.init props

        let subject = new BehaviorSubject<WorkbenchHost.Model>(model)

        let! window =
          probeUiAsync(fun () ->
            let view =
              WorkbenchHost.view
                props
                (subject :> IObservable<WorkbenchHost.Model>)
                ignore

            let window = Window(Width = 1600.0, Height = 900.0, Content = view)

            window.Show()
            window)

        let! refsRows =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              hasText(root, "Saved")
              && hasText(root, "Notifications")
              && hasText(root, "Default layout")
              && hasText(root, "Settings open in the default layout.")
          )

        Expect.isTrue refsRows "the refs tree never rendered its rows"

        do!
          onUiAsync(fun () ->
            controlByName(window, "UseDefaultLayout")
            |> Option.iter(function
              | :? Button as button -> click button
              | _ -> ()))

        let! fired =
          waitForAsync(5000, (fun () -> calls.Count), fun count -> count = 1)

        Expect.isTrue fired "the default layout row never fired its sink"

        do!
          onUiAsync(fun () ->
            window.Close()
            subject.OnCompleted()
            subject.Dispose())
      })

    testCaseAsync
      "the refs chrome carries the default layout row"
      (async {
        let! count =
          probeUiAsync(fun () ->
            let chrome =
              WorkbenchChrome.refs(
                {
                  Rows = WorkbenchChrome.rows(0, 0, 3)
                  Selected = following
                },
                ignore,
                ignore
              )

            (chrome :?> StackPanel).Children.Count)

        Expect.equal count 8 "the refs chrome lost its default layout child"
      })

    testCaseAsync
      "the question mark opens the shared workbench cheatsheet"
      (async {
        let api = workbenchApi()
        let env = TestEnv(signedInStore(), api :> IHttp)
        let props = hostProps(env, Observable.single now)
        let model, _ = WorkbenchHost.init props

        let subject = new BehaviorSubject<WorkbenchHost.Model>(model)

        let! window =
          probeUiAsync(fun () ->
            let view =
              WorkbenchHost.view
                props
                (subject :> IObservable<WorkbenchHost.Model>)
                ignore

            let window = Window(Width = 1600.0, Height = 900.0, Content = view)
            window.Show()
            window)

        do!
          onUiAsync(fun () ->
            controlByName(window, "WorkbenchRoot")
            |> Option.iter(fun root ->
              root.RaiseEvent(
                KeyEventArgs(
                  RoutedEvent = InputElement.KeyDownEvent,
                  Key = Key.OemQuestion,
                  KeyModifiers = KeyModifiers.Shift
                )
              )))

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              hasText(root, "Keyboard shortcuts")
              && hasText(root, "Fullscreen")
              && (controlByName(root, "ShortcutsCard") |> Option.isSome)
          )

        Expect.isTrue shown "the question mark never opened the cheatsheet"

        do!
          onUiAsync(fun () ->
            controlByName(window, "WorkbenchRoot")
            |> Option.iter(fun root ->
              root.RaiseEvent(
                KeyEventArgs(
                  RoutedEvent = InputElement.KeyDownEvent,
                  Key = Key.Escape
                )
              )))

        let! closed =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "WorkbenchHelp")),
            fun help ->
              help |> Option.exists(fun control -> not control.IsVisible)
          )

        Expect.isTrue closed "Escape did not close the workbench cheatsheet"

        do!
          onUiAsync(fun () ->
            window.Close()
            subject.OnCompleted()
            subject.Dispose())
      })

    testCaseAsync
      "an empty message panel collapses to no height"
      (async {
        let! window, empty, words =
          probeUiAsync(fun () ->
            let empty =
              PageChrome.collapsingMessagePanel(Observable.single "", Muted)

            let words =
              PageChrome.collapsingMessagePanel(
                Observable.single "Loading…",
                Muted
              )

            let window =
              Window(
                Width = 400.0,
                Height = 200.0,
                Content = StackPanel().Children([| empty; words |])
              )

            window.Show()
            window, empty, words)

        let! collapsed =
          waitForAsync(
            5000,
            (fun () -> empty),
            fun panel -> not panel.IsVisible
          )

        Expect.isTrue collapsed "the empty message panel stayed visible"

        let! emptyHeight, wordsHeight =
          probeUiAsync(fun () -> empty.Bounds.Height, words.Bounds.Height)

        Expect.isLessThan
          emptyHeight
          0.5
          "the empty message panel kept a line box"

        Expect.isGreaterThan
          wordsHeight
          0.0
          "the filled message panel lost its line box"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "the history column draws days and the last-visit marker"
      (async {
        let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)

        let twoDays =
          sprintf
            "{\"data\":[%s,%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
            (datedCommitJson(
              "0a0a0a0a-0000-0000-0000-000000000001",
              "aaa1111",
              "First landing.",
              "2020-04-03T12:00:00Z"
            ))
            (datedCommitJson(
              "0a0a0a0a-0000-0000-0000-000000000002",
              "bbb2222",
              "The week before.",
              "2020-04-01T12:00:00Z"
            ))

        let page =
          match LogPage.decode twoDays with
          | Ok page -> page
          | Error failure ->
            failwith $"the two-day page never decoded: {failure.Message}"

        let props = {
          historyProps env with
              LastVisit =
                Some(DateTimeOffset(2020, 4, 2, 0, 0, 0, TimeSpan.Zero))
        }

        let initial, _ = HistoryColumn.init props

        let model, _ =
          HistoryColumn.update(props, HistoryColumn.Fetched(Ok page), initial)

        let model = {
          model with
              Now = DateTimeOffset(2020, 4, 3, 12, 0, 0, TimeSpan.Zero)
        }

        let subject = new BehaviorSubject<HistoryColumn.Model>(model)

        let! window =
          probeUiAsync(fun () ->
            let view =
              HistoryColumn.view
                props
                (subject :> IObservable<HistoryColumn.Model>)
                (fun (_: HistoryColumn.Msg) -> ())

            let window = Window(Width = 1200.0, Height = 700.0, Content = view)
            window.Show()
            window)

        let! drawn =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              hasText(root, "Today")
              && hasText(root, "Wed, Apr 1")
              && hasText(root, "HEAD@{last visit}")
              && hasText(root, "First landing.")
              && hasText(root, "The week before.")
          )

        Expect.isTrue drawn "the history days or last-visit marker never drew"

        do!
          onUiAsync(fun () ->
            window.Close()
            subject.OnCompleted()
            subject.Dispose())
      })
  ]
