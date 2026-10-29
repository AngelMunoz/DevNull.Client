module DevNull.Client.UI.Tests.Scheduled

// The scheduled queue through the real shell: the composer's Schedule
// action queues a commit inside the server's window, the queue page
// lists the entries with their statuses, Commit now keeps the body and
// settles the row, Cancel takes one out, and a conflict surfaces the
// server's message verbatim and refreshes.

open System
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto

open DevNull.Client.Core.Http
open DevNull.Client.UI.Tests.App
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

/// One queue entry as raw json.
let queuedJson(id: string, body: string, status: string) =
  let scheduledAt =
    DateTimeOffset.UtcNow
      .AddDays(2.0)
      .ToUniversalTime()
      .ToString("yyyy-MM-dd'T'HH:mm:sszzz")

  let submittedAt =
    if status = "scheduled" then
      "null"
    else
      sprintf
        "\"%s\""
        (DateTimeOffset.UtcNow
          .ToUniversalTime()
          .ToString("yyyy-MM-dd'T'HH:mm:sszzz"))

  sprintf
    """{"id":"%s","body":%s,"body_segments":[{"type":"text","text":%s}],"preview_segments":null,"poll_options":null,"image_count":0,"scheduled_at":"%s","submitted_at":%s,"status":"%s"}"""
    id
    (if status = "scheduled" then
       sprintf "\"%s\"" body
     else
       "null")
    (sprintf "\"%s\"" body)
    scheduledAt
    submittedAt
    status

let queueJson =
  sprintf
    """{"data":[%s,%s],"links":{"first":null,"last":null,"prev":null,"next":null},"meta":{"current_page":1,"from":1,"last_page":1,"path":"","per_page":20,"to":2,"total":2}}"""
    (queuedJson(
      "018f5a1e-0001-7c3a-9b2d-5e6f7a8b9c0d",
      "Shipping tomorrow.",
      "scheduled"
    ))
    (queuedJson(
      "018f5a1e-0002-7c3a-9b2d-5e6f7a8b9c0d",
      "Already sent.",
      "approved"
    ))

let createdJson =
  sprintf
    """{"data":%s}"""
    (queuedJson(
      "018f5a1e-0003-7c3a-9b2d-5e6f7a8b9c0d",
      "Queued from the test",
      "scheduled"
    ))

let submittedJson =
  sprintf
    """{"data":%s}"""
    (queuedJson(
      "018f5a1e-0001-7c3a-9b2d-5e6f7a8b9c0d",
      "Shipping tomorrow.",
      "pending"
    ))

let conflictJson =
  """{"message":"This commit was already submitted or canceled. Refresh to see its status."}"""

let refused status body : HttpResponse = {
  Status = status
  Headers = []
  Body = body
}

/// The api of the queue tests: the log and account answer as the other
/// tests' do, and the scheduled routes answer the queue story. The
/// delete status is the caller's: 200 settles, 409 conflicts.
let scheduledApi(deleteStatus: int) =
  FakeApi(
    (fun request ->
      if request.Target.StartsWith "/api/v1/scheduled-commits" then
        if request.Method = "GET" then
          ok queueJson
        elif
          request.Method = "POST" && request.Target.EndsWith "/submission"
        then
          ok submittedJson
        elif request.Method = "POST" then
          createdJson |> ok
        elif request.Method = "DELETE" && deleteStatus = 409 then
          refused 409 conflictJson
        else
          ok ""
      elif request.Target.StartsWith "/api/v1/log/" then
        ok logPageJson
      else
        (match accountAnswer request with
         | ValueSome answer -> answer
         | ValueNone -> ok "{}"))
  )

/// Opens the composer's schedule panel and picks the second day.
let pickTomorrowAsync(window: Window) : Async<unit> = async {
  let! button =
    waitForAsync(
      5000,
      (fun () ->
        window.GetVisualDescendants()
        |> Seq.tryFind(fun visual -> visual.Name = "ScheduleButton")
        |> Option.map(fun visual -> visual :?> Control)),
      Option.isSome
    )

  Expect.isTrue button "the schedule button never appeared"

  do!
    onUiAsync(fun () ->
      window.GetVisualDescendants()
      |> Seq.tryFind(fun visual -> visual.Name = "ScheduleButton")
      |> Option.iter(fun visual -> click(visual :?> Button)))

  let! panel =
    waitForAsync(
      5000,
      (fun () ->
        window.GetVisualDescendants()
        |> Seq.tryFind(fun visual -> visual.Name = "ScheduleDay")),
      Option.isSome
    )

  Expect.isTrue panel "the schedule panel never appeared"

  do!
    onUiAsync(fun () ->
      window.GetVisualDescendants()
      |> Seq.iter(fun visual ->
        match visual with
        | :? ComboBox as picker when picker.Name = "ScheduleDay" ->
          picker.SelectedIndex <- 1
        | _ -> ()))
}

/// True when a TextBlock under the root carries the fragment inside a
/// longer line, as the error line prefixes the server's message.
let showsFragment(window: Window, fragment: string) : Async<bool> =
  waitForAsync(
    8000,
    (fun () -> window),
    (fun root ->
      root.GetVisualDescendants()
      |> Seq.exists(fun visual ->
        match visual with
        | :? TextBlock as block ->
          block.Text <> null && block.Text.Contains fragment
        | _ -> false))
  )

/// True once no TextBlock under the root carries the fragment.
let showsFragmentGone(window: Window, fragment: string) : Async<bool> =
  waitForAsync(
    8000,
    (fun () -> window),
    (fun root ->
      root.GetVisualDescendants()
      |> Seq.forall(fun visual ->
        match visual with
        | :? TextBlock as block ->
          not(block.Text <> null && block.Text.Contains fragment)
        | _ -> true))
  )

let tests =
  testList "scheduled queue" [
    testCaseAsync
      "the composer queues a commit for a picked moment"
      (async {
        let api = scheduledApi(200)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! pickTomorrowAsync(window)

        // The submit button names what it now does.
        let! renamed =
          waitForAsync(
            5000,
            (fun () -> buttonWithText(window, "Schedule commit")),
            Option.isSome
          )

        Expect.isTrue renamed "the button never became Schedule commit"

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun box ->
              box.Text <- "Queued from the test"

              buttonWithText(window, "Schedule commit") |> Option.iter click))

        let! posted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target = "/api/v1/scheduled-commits"))
          )

        Expect.isTrue posted "the commit was never queued"

        let post =
          api.Requests
          |> List.find(fun request ->
            request.Method = "POST"
            && request.Target = "/api/v1/scheduled-commits")

        let postBody =
          match post.Body with
          | HttpBody.Text text -> text
          | _ -> ""

        Expect.stringContains postBody "Queued from the test" "the typed body"

        Expect.stringContains
          postBody
          "\"scheduled_at\""
          "the picked moment rides"

        // A queued commit leaves the box empty, the way a posted one does.
        let! cleared =
          waitForAsync(
            5000,
            (fun () -> textBoxWithWatermark(window, "What did you ship?")),
            (fun box ->
              box
              |> Option.map(fun control -> control.Text = "")
              |> Option.defaultValue false)
          )

        Expect.isTrue cleared "the composer kept the queued body"
      })

    testCaseAsync
      "the queue page submits now, keeps the body, and cancels"
      (async {
        let api = scheduledApi(200)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! pickTomorrowAsync(window)

        // The panel's link is the queue's only door, as on the site.
        do! clickWhenShown(window, "Scheduled commits →")

        let! listed = showsText(window, "Shipping tomorrow.")

        Expect.isTrue listed "the waiting row never rendered"

        let! settledRow = showsText(window, "Approved")

        Expect.isTrue settledRow "the submitted row never rendered"

        // Commit now settles the row in place: the body stays, the
        // status moves. The settled row is the page's only "Submitted
        // for review".
        do! clickWhenShown(window, "Commit now")

        let! settled = showsText(window, "Submitted for review")

        Expect.isTrue settled "the row never settled"

        let submitted =
          api.Requests
          |> List.exists(fun request ->
            request.Method = "POST" && request.Target.EndsWith "/submission")

        Expect.isTrue submitted "the submission never went out"

        // Cancel takes the settled row out; its body goes with it.
        do!
          onUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryFind(fun visual -> visual.Name = "CancelScheduled")
            |> Option.iter(fun visual -> click(visual :?> Button)))

        let! deleted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests |> List.exists(fun request -> request.Method = "DELETE"))
          )

        Expect.isTrue deleted "the cancel never went out"

        let! gone = showsFragmentGone(window, "Shipping tomorrow.")

        Expect.isTrue gone "the canceled row never left"
      })

    testCaseAsync
      "a conflict surfaces the server's message and refreshes"
      (async {
        let api = scheduledApi(409)
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"

        do! pickTomorrowAsync(window)

        do! clickWhenShown(window, "Scheduled commits →")

        let! listed = showsText(window, "Shipping tomorrow.")

        Expect.isTrue listed "the waiting row never rendered"

        let readsBefore =
          api.Requests
          |> List.filter(fun request -> request.Method = "GET")
          |> List.length

        do!
          onUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryFind(fun visual -> visual.Name = "CancelScheduled")
            |> Option.iter(fun visual -> click(visual :?> Button)))

        let! deleted =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "DELETE"
                && request.Target.StartsWith "/api/v1/scheduled-commits"))
          )

        Expect.isTrue deleted "the cancel never went out"

        let! conflict =
          showsFragment(
            window,
            "This commit was already submitted or canceled. Refresh to see its status."
          )

        Expect.isTrue conflict "the server's message never surfaced"

        // The message asks for a refresh; the page answers it.
        let readsOf(requests: HttpRequest list) =
          requests
          |> List.filter(fun request -> request.Method = "GET")
          |> List.length

        let! reread =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            (fun requests -> readsOf requests > readsBefore)
          )

        Expect.isTrue reread "the queue never refreshed"
      })
  ]
