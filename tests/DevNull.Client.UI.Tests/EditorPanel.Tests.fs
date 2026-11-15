module DevNull.Client.UI.Tests.EditorPanel

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Headless
open Avalonia.Input
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Core.Http
open DevNull.Client.Editor
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.EditorFrame
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let commitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
let linkedNotificationId = "01a15752-d17c-7065-b076-99e5bdad596d"
let unlinkedNotificationId = "01a15752-d17c-7065-b076-99e5bdad596e"
let followedNotificationId = "01a15752-d17c-7065-b076-99e5bdad596f"

let controlByName(root: Control, name: string) =
  root.GetVisualDescendants()
  |> Seq.tryFind(fun visual -> visual.Name = name)
  |> Option.map(fun visual -> visual :?> Control)

let visible(root: Control, name: string) =
  controlByName(root, name)
  |> Option.exists(fun control -> control.IsEffectivelyVisible)

let textOf(root: Control, name: string) : string =
  controlByName(root, name)
  |> Option.bind(fun control ->
    match control with
    | :? TextBlock as block -> Some block.Text
    | _ -> None)
  |> Option.defaultValue ""

let clickName(root: Control, name: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(root, name)
    |> Option.iter(fun control ->
      match control with
      | :? Button as button -> click button
      | _ -> ()))

let queueJson(total: int) =
  sprintf
    """{"data":[],"links":{"first":null,"last":null,"prev":null,"next":null},"meta":{"current_page":1,"from":0,"last_page":1,"path":"","per_page":20,"to":0,"total":%d}}"""
    total

let unlinkedNotification =
  notificationJson
    .Replace(
      $"\"id\":\"{linkedNotificationId}\"",
      $"\"id\":\"{unlinkedNotificationId}\""
    )
    .Replace($"\"commit\":{commitJson}", "\"commit\":null")
    .Replace("\"unread_count\":0", "\"unread_count\":2")

let followedNotification =
  notificationJson
    .Replace(
      $"\"id\":\"{linkedNotificationId}\"",
      $"\"id\":\"{followedNotificationId}\""
    )
    .Replace("\"type\":\"replied\"", "\"type\":\"followed\"")
    .Replace($"\"commit\":{commitJson}", "\"commit\":null")

let twoNotificationsJson =
  sprintf
    """{"data":[%s,%s],"links":{},"meta":{"per_page":20,"next_cursor":null,"prev_cursor":null},"filters":{"type":"all"},"read_at":"2026-10-20T04:20:00Z"}"""
    notificationJson
    unlinkedNotification

let manyKindsJson =
  let kinds = [
    "followed"
    "replied"
    "quoted"
    "starred"
    "mentioned"
    "leveled_up"
    "approved"
    "rejected"
    "first_commit"
  ]

  let row (index: int) (kind: string) =
    notificationJson
      .Replace(
        "01a15752-d17c-7065-b076-99e5bdad596d",
        sprintf "01a15752-d17c-7065-b076-99e5bdad59%02x" index
      )
      .Replace("\"type\":\"replied\"", sprintf "\"type\":\"%s\"" kind)

  let rows = kinds |> List.mapi row |> String.concat ","

  sprintf
    """{"data":[%s],"links":{},"meta":{"per_page":20,"next_cursor":null,"prev_cursor":null},"filters":{"type":"all"},"read_at":"2026-10-20T04:20:00Z"}"""
    rows

let scheduledListJson =
  """{"data":[{"id":"018f5a1e-6f8a-7c3a-9b2d-5e6f7a8b9c0e","body":"Queue me","body_segments":[],"preview_segments":null,"poll_options":null,"image_count":0,"scheduled_at":"2026-12-20T05:00:00+00:00","submitted_at":null,"status":"scheduled"}],"links":{},"meta":{"current_page":1,"from":1,"last_page":1,"path":"","per_page":20,"to":1,"total":1}}"""

let savedListJson =
  sprintf
    """{"data":[%s],"links":{},"meta":{"per_page":20,"next_cursor":null,"prev_cursor":null}}"""
    commitJson

let listApi() =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/scheduled-commits" then
      ok scheduledListJson
    elif request.Target.StartsWith "/api/v1/starred" then
      ok savedListJson
    elif request.Target.Contains "/replies" then
      ok repliesPageJson
    elif request.Target.StartsWith "/api/v1/commits/" then
      ok threadPageJson
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

let pagedNotificationsApi(first: string, second: string) =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/notifications" then
      if request.Target.Contains "cursor=next" then
        ok second
      else
        ok first
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

let failingNotificationsApi(first: string) =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/notifications" then
      if request.Target.Contains "cursor=next" then
        refused(500, "The notifications page is unavailable.")
      else
        ok first
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

let panelApi(notifications: string, total: int) =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/scheduled-commits" then
      ok(queueJson total)
    elif request.Target.StartsWith "/api/v1/notifications" then
      ok notifications
    elif request.Target.Contains "/replies" then
      ok repliesPageJson
    elif request.Target.StartsWith "/api/v1/commits/" then
      ok threadPageJson
    elif request.Method = "POST" && request.Target = "/api/v1/commits" then
      ok(sprintf "{\"data\":%s}" commitJson)
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

let mountAt
  (width: float, height: float, api: FakeApi)
  : Async<Window * IDisposable> = async {
  let env = TestEnv(signedInStore(), api :> IHttp)
  let props = EditorFrame.props env

  return!
    probeUiAsync(fun () ->
      let control, handle = EditorRoot.mount(ui, props)

      let window = Window(Width = width, Height = height, Content = control)

      window.Show()
      window, handle)
}

let pressMiddle(window: Window, name: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, name)
    |> Option.iter(fun control ->
      let center =
        control.TranslatePoint(
          Point(control.Bounds.Width / 2.0, control.Bounds.Height / 2.0),
          window
        )

      if center.HasValue then
        window.MouseDown(
          center.Value,
          MouseButton.Middle,
          RawInputModifiers.None
        )

        window.MouseUp(
          center.Value,
          MouseButton.Middle,
          RawInputModifiers.None
        )))

let close(window: Window, handle: IDisposable) : Async<unit> =
  onUiAsync(fun () ->
    window.Close()
    handle.Dispose())

let openProblems(window: Window) : Async<unit> = async {
  do! clickName(window, "EditorActivity-Problems")

  let! shown =
    waitForAsync(
      8000,
      (fun () -> window),
      fun root -> visible(root, "EditorPanel")
    )

  Expect.isTrue shown "the problems panel never showed"
}

let tests =
  testList "editor panel headless" [
    testCaseAsync
      "all notification kinds become scrollable tabs at 380 px"
      (async {
        let api = panelApi(manyKindsJson, 0)
        let! window, handle = mountAt(380.0, 900.0, api)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"
        do! openProblems(window)

        let! tabs =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              [
                "FOLLOWS"
                "REPLIES"
                "QUOTES"
                "STARS"
                "MENTIONS"
                "LEVELS"
                "APPROVED"
                "REJECTED"
                "FIRST COMMITS"
              ]
              |> List.forall(fun label ->
                controlByName(root, $"EditorPanelTab-{label}") |> Option.isSome)
          )

        Expect.isTrue tabs "a notification kind tab never appeared"

        let! scroll =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              controlByName(root, "EditorPanelTabs")
              |> Option.bind(function
                | :? ScrollViewer as view ->
                  Some(
                    view.HorizontalScrollBarVisibility =
                      ScrollBarVisibility.Auto
                    && view.Extent.Width > view.Viewport.Width
                  )
                | _ -> None)
              |> Option.defaultValue false
          )

        Expect.isTrue scroll "the tab row did not scroll at 380 px"

        do! close(window, handle)
      })

    testCaseAsync
      "the queue count reads the scheduled index"
      (async {
        let api = panelApi("", 3)
        let! window, handle = mountAt(1600.0, 900.0, api)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"

        let! count =
          waitForAsync(
            5000,
            (fun () -> textOf(window, "ExplorerCount-QUEUE")),
            fun text -> text = "3"
          )

        Expect.isTrue count "the queue count never reached three"

        do! close(window, handle)
      })

    testCaseAsync
      "the saved and queue rows open closable deduped list tabs"
      (async {
        let api = listApi()
        let! window, handle = mountAt(1600.0, 900.0, api)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"

        do! clickName(window, "ExplorerOpen-SAVED")

        let! saved =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "EditorListTab-SAVED")
          )

        Expect.isTrue saved "the saved list did not open"

        let! savedRows =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue savedRows "the saved rows never rendered"

        let! savedTabs =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.filter(fun visual ->
              match visual with
              | :? Button as button -> button.Name = "EditorTab-saved"
              | _ -> false)
            |> Seq.length)

        Expect.equal savedTabs 1 "the saved list tab was not deduped"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! opened =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, $"EditorTab-{commitId}")),
            Option.isSome
          )

        Expect.isTrue opened "the saved row did not open its thread"

        do! clickName(window, "EditorTabClose-saved")

        let! closed =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(visible(root, "EditorListTab-SAVED"))
          )

        Expect.isTrue closed "the saved list tab did not close"

        do! clickName(window, "ExplorerOpen-QUEUE")

        let! queued =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "EditorListTab-QUEUE")
          )

        Expect.isTrue queued "the queue list did not open"

        let! queuedRows =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "Queue me")
          )

        Expect.isTrue queuedRows "the scheduled row never rendered"

        let! status = probeUiAsync(fun () -> textOf(window, "ScheduledStatus"))

        Expect.equal status "Scheduled" "the queue row lost its status"

        do! close(window, handle)
      })

    testCaseAsync
      "middle-click closes a non-pinned tab and leaves the pinned tab"
      (async {
        let api = panelApi("", 0)
        let! window, handle = mountAt(1600.0, 900.0, api)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"

        do! pressMiddle(window, "EditorTab-history")

        let! pinned =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "EditorTab-history")),
            Option.isSome
          )

        Expect.isTrue pinned "the pinned tab closed on a middle click"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, $"EditorTab-{commitId}")),
            Option.isSome
          )

        Expect.isTrue thread "the thread tab never opened"

        do! pressMiddle(window, $"EditorTab-{commitId}")

        let! closed =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(visible(root, $"EditorTab-{commitId}"))
          )

        Expect.isTrue closed "the non-pinned tab ignored the middle click"

        do! close(window, handle)
      })

    testCaseAsync
      "a long terminal draft keeps the commit action in view"
      (async {
        let api = panelApi("", 0)
        let! window, handle = mountAt(1600.0, 900.0, api)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"
        do! openProblems(window)
        do! clickName(window, "EditorPanelTab-Terminal")

        let! box =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "What did you ship?")),
            Option.isSome
          )

        Expect.isTrue box "the terminal composer never showed"

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun body -> body.Text <- String('x', 4000)))

        do! settleAsync()

        let! inView =
          probeUiAsync(fun () ->
            match
              controlByName(window, "ComposerBody"),
              buttonWithText(window, "Commit")
            with
            | Some body, Some submit ->
              let point =
                submit.TranslatePoint(
                  Point(submit.Bounds.Width / 2.0, submit.Bounds.Height / 2.0),
                  window
                )

              body.Bounds.Height <= 181.0
              && submit.IsEffectivelyVisible
              && point.HasValue
              && point.Value.Y > 0.0
              && point.Value.Y < window.ClientSize.Height
            | _ -> false)

        Expect.isTrue
          inView
          "the long draft pushed the commit action out of view"

        do! close(window, handle)
      })

    testCaseAsync
      "a problem with a commit opens its thread and one without a commit does nothing"
      (async {
        let api = panelApi(twoNotificationsJson, 0)
        let! window, handle = mountAt(1600.0, 900.0, api)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"
        do! openProblems(window)

        let! linked =
          waitForAsync(
            5000,
            (fun () ->
              controlByName(window, $"ProblemRow-{linkedNotificationId}")),
            Option.isSome
          )

        let! unlinked =
          waitForAsync(
            5000,
            (fun () ->
              controlByName(window, $"ProblemRow-{unlinkedNotificationId}")),
            Option.isSome
          )

        Expect.isTrue linked "the linked problem row never rendered"
        Expect.isTrue unlinked "the unlinked problem row never rendered"

        do! clickName(window, $"ProblemRow-{linkedNotificationId}")

        let! opened =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, $"EditorTab-{commitId}")),
            Option.isSome
          )

        Expect.isTrue opened "the problem did not open its thread tab"

        do! clickName(window, $"ProblemRow-{unlinkedNotificationId}")
        do! settleAsync()

        let! stillThere =
          probeUiAsync(fun () ->
            controlByName(window, $"EditorTab-{commitId}") |> Option.isSome)

        Expect.isTrue stillThere "the thread tab vanished"

        let! tabs =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.filter(fun visual ->
              match visual with
              | :? Button as button ->
                button.Name <> null
                && button.Name.StartsWith("EditorTab-")
                && not(button.Name.StartsWith("EditorTabClose-"))
              | _ -> false)
            |> Seq.length)

        Expect.equal tabs 2 "the unlinked row opened a tab"

        do! close(window, handle)
      })

    testCaseAsync
      "Load more appends the next notification page and drops the duplicate"
      (async {
        let first =
          sprintf
            """{"data":[%s],"links":{},"meta":{"per_page":20,"next_cursor":"next","prev_cursor":null},"filters":{"type":"all"},"read_at":"2026-10-20T04:20:00Z"}"""
            notificationJson

        let second =
          sprintf
            """{"data":[%s,%s,%s],"links":{},"meta":{"per_page":20,"next_cursor":null,"prev_cursor":null},"filters":{"type":"all"},"read_at":"2026-10-20T04:20:00Z"}"""
            unlinkedNotification
            followedNotification
            notificationJson

        let api = pagedNotificationsApi(first, second)
        let! window, handle = mountAt(1600.0, 900.0, api)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"
        do! openProblems(window)

        let! firstRow =
          waitForAsync(
            5000,
            (fun () ->
              controlByName(window, $"ProblemRow-{linkedNotificationId}")),
            Option.isSome
          )

        Expect.isTrue firstRow "the first notification page never rendered"

        let! more =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "EditorPanelLoadMore")
          )

        Expect.isTrue more "the first page offered no Load more control"
        do! clickName(window, "EditorPanelLoadMore")

        let! secondRow =
          waitForAsync(
            8000,
            (fun () ->
              controlByName(window, $"ProblemRow-{unlinkedNotificationId}")),
            Option.isSome
          )

        Expect.isTrue secondRow "the second notification page never appended"

        let! followedTab =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "EditorPanelTab-FOLLOWS")
          )

        Expect.isTrue followedTab "the appended page's new kind never appeared"

        let! linkedRows =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.filter(fun visual ->
              visual.Name = $"ProblemRow-{linkedNotificationId}")
            |> Seq.length)

        Expect.equal linkedRows 1 "the overlapping notification row doubled"

        let! stillMore =
          probeUiAsync(fun () -> visible(window, "EditorPanelLoadMore"))

        Expect.isFalse
          stillMore
          "the exhausted cursor kept the Load more control"

        do! close(window, handle)
      })

    testCaseAsync
      "a failed Load more keeps the loaded notification rows"
      (async {
        let first =
          sprintf
            """{"data":[%s],"links":{},"meta":{"per_page":20,"next_cursor":"next","prev_cursor":null},"filters":{"type":"all"},"read_at":"2026-10-20T04:20:00Z"}"""
            notificationJson

        let api = failingNotificationsApi first
        let! window, handle = mountAt(1600.0, 900.0, api)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"
        do! openProblems(window)

        let! firstRow =
          waitForAsync(
            5000,
            (fun () ->
              controlByName(window, $"ProblemRow-{linkedNotificationId}")),
            Option.isSome
          )

        Expect.isTrue firstRow "the first notification page never rendered"

        let! more =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "EditorPanelLoadMore")
          )

        Expect.isTrue more "the failed-page test never offered Load more"
        do! clickName(window, "EditorPanelLoadMore")

        let! notifications =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            fun requests ->
              requests
              |> List.exists(fun request ->
                request.Target.StartsWith "/api/v1/notifications"
                && request.Target.Contains "cursor=next")
          )

        Expect.isTrue notifications "the failed page never left the client"

        let! error =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              hasText(
                root,
                "dev-null.sh refused the request (HTTP 500): The notifications page is unavailable."
              )
          )

        Expect.isTrue error "the failed page did not show its error"

        let! retained =
          probeUiAsync(fun () ->
            visible(window, $"ProblemRow-{linkedNotificationId}")
            && visible(window, "EditorPanelLoadMore"))

        Expect.isTrue
          retained
          "the failed page cleared the loaded rows or cursor"

        do! close(window, handle)
      })

    testCaseAsync
      "a post from the panel refreshes the pinned history and closes the panel"
      (async {
        let api = panelApi("", 0)
        let! window, handle = mountAt(1600.0, 900.0, api)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"
        do! openProblems(window)
        do! clickName(window, "EditorPanelTab-Terminal")

        let! composer =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "What did you ship?")),
            Option.isSome
          )

        Expect.isTrue composer "the terminal composer never showed"

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun box -> box.Text <- "a panel post"))

        do! settleAsync()

        let before =
          api.Requests
          |> List.filter(fun request ->
            request.Target.StartsWith "/api/v1/log/")
          |> List.length

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Commit") |> Option.iter click)

        let! closed =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> not(visible(root, "EditorPanel"))
          )

        Expect.isTrue closed "the panel stayed open after the post"

        let! refreshed =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            fun requests ->
              (requests
               |> List.filter(fun request ->
                 request.Target.StartsWith "/api/v1/log/")
               |> List.length)
                =
                before + 1
          )

        Expect.isTrue refreshed "the history did not refresh once"

        let! tabs =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.filter(fun visual ->
              match visual with
              | :? Button as button ->
                button.Name <> null
                && button.Name.StartsWith("EditorTab-")
                && not(button.Name.StartsWith("EditorTabClose-"))
              | _ -> false)
            |> Seq.length)

        Expect.equal tabs 1 "the pinned tab was not alone"

        do! close(window, handle)
      })

    testCaseAsync
      "a reply intent arms the terminal with the thread participants"
      (async {
        let api = panelApi("", 0)
        let! window, handle = mountAt(1600.0, 900.0, api)

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

        let! thread =
          waitForAsync(
            8000,
            (fun () -> controlByName(window, $"EditorTab-{commitId}")),
            Option.isSome
          )

        Expect.isTrue thread "the thread tab never opened"

        let! replyButton =
          waitForAsync(
            8000,
            (fun () -> controlByName(window, "ReplyButton")),
            Option.isSome
          )

        Expect.isTrue replyButton "the reply control never rendered"
        do! clickName(window, "ReplyButton")

        let! composer =
          waitForAsync(
            8000,
            (fun () -> textBoxWithWatermark(window, "Write your reply…")),
            Option.isSome
          )

        Expect.isTrue composer "the reply composer never showed"

        let! participants =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "@mona") && hasText(root, "@hubot")
          )

        Expect.isTrue participants "the thread participants were not armed"

        let! panelOpen = probeUiAsync(fun () -> visible(window, "EditorPanel"))

        Expect.isTrue panelOpen "the reply panel stayed shut"

        do! close(window, handle)
      })

    testCaseAsync
      "E8: the quote terminal opens over the dimmed frame at 380 px"
      (async {
        let api = panelApi("", 0)
        let! window, handle = mountAt(380.0, 640.0, api)

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

        let! thread =
          waitForAsync(
            8000,
            (fun () -> controlByName(window, $"EditorTab-{commitId}")),
            Option.isSome
          )

        Expect.isTrue thread "the thread tab never opened"

        let! quoteButton =
          waitForAsync(
            8000,
            (fun () -> controlByName(window, "QuoteButton")),
            Option.isSome
          )

        Expect.isTrue quoteButton "the quote control never rendered"
        do! clickName(window, "QuoteButton")

        let! composer =
          waitForAsync(
            8000,
            (fun () -> textBoxWithWatermark(window, "Add your take")),
            Option.isSome
          )

        Expect.isTrue composer "the quote composer never showed"

        let! overlay =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              visible(root, "EditorPanel") && visible(root, "EditorPanelDim")
          )

        Expect.isTrue overlay "the panel did not dim the narrow frame"

        do! clickName(window, "EditorPanelClose")

        let! closed =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              not(visible(root, "EditorPanel"))
              && not(visible(root, "EditorPanelDim"))
          )

        Expect.isTrue closed "the overlay did not close"

        do! close(window, handle)
      })
  ]
