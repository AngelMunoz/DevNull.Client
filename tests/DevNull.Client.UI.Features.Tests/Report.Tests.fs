module DevNull.Client.UI.Tests.Report

// The report flow over the real shell: another author's approved
// commit offers the flag, the dialog stages up to three images with
// their descriptions, and the signed send answers success, a rate
// limit, a vanished commit, a plain refusal, or a lost connection.

open System
open System.Threading.Tasks
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.VisualTree
open Expecto
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Shell
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.App
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let reportPath = "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/reports"

let noContent: HttpResponse = {
  Status = 204
  Headers = []
  Body = ""
}

/// The api of the report tests: the log answers its canned page, the
/// report post answers the test's own handler, and the shell's account
/// reads keep their canned shapes.
let reportApiWith(log: string, post: HttpRequest -> HttpResponse) : FakeApi =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok log
    elif request.Method = "POST" && request.Target = reportPath then
      post request
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The canned log with its commit authored by another person, so the
/// viewer and the reported author read apart.
let hubotLog =
  logPageJson
    .Replace(
      "\"id\":\"01a151db-7fee-727b-a7ce-c911a7556242\"",
      "\"id\":\"01a151db-7fee-727b-a7ce-c911a7556243\""
    )
    .Replace("\"handle\":\"octocat\"", "\"handle\":\"hubot\"")
    .Replace("\"name\":\"The Octocat\"", "\"name\":\"The Hubot\"")

let reportApi(post: HttpRequest -> HttpResponse) : FakeApi =
  reportApiWith(hubotLog, post)

/// One log page carrying the viewer's own commit only.
let ownCommitLog =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
    ownCommitJson

let sendButton(root: Control) : Control option =
  controlByName(root, "ReportSend")

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

let openReportDialogAsync(window: Window) : Async<unit> = async {
  do! openMenuAsync window

  let! offered =
    waitForAsync(
      5000,
      (fun () -> menuRow(window, "Report commit")),
      Option.isSome
    )

  Expect.isTrue offered "the card offered no report control"

  do! onUiAsync(fun () -> menuRow(window, "Report commit") |> Option.iter click)

  let! opened =
    waitForAsync(
      5000,
      (fun () -> controlByName(window, "ReportDialog")),
      Option.isSome
    )

  Expect.isTrue opened "the report dialog never opened"
}

let chooseReasonAsync(window: Window, name: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, name)
    |> Option.iter(fun control ->
      (control :?> RadioButton).IsChecked <- Nullable true))

let typeDetailsAsync(window: Window, text: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, "ReportDetails")
    |> Option.iter(fun control -> (control :?> TextBox).Text <- text))

let attachAsync(window: Window) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, "ReportAttachButton")
    |> Option.iter(fun control -> click(control :?> Button)))

let clickSendAsync(window: Window) : Async<unit> =
  onUiAsync(fun () ->
    sendButton window |> Option.iter(fun control -> click(control :?> Button)))

let private postRequests(api: FakeApi) : HttpRequest list =
  api.Requests
  |> List.filter(fun request ->
    request.Method = "POST" && request.Target = reportPath)

let private payloadOf(request: HttpRequest) : string =
  match request.Body with
  | Multipart form ->
    let payload = form.Files |> List.find(fun file -> file.Name = "payload")

    System.Text.Encoding.UTF8.GetString payload.Content
  | _ -> failtest "expected a multipart report"

/// One api that holds the report post until the test opens its gate,
/// so the sending state can be captured.
type HeldReportApi(inner: IHttp, gate: TaskCompletionSource<unit>) =
  interface IHttp with
    member _.Send(request) = async {
      if request.Method = "POST" && request.Target = reportPath then
        do! gate.Task |> Async.AwaitTask

      return! inner.Send request
    }

    member _.GetBytes(target: string) = inner.GetBytes target

/// One api that loses the report post on the way out.
type OfflineReportApi(inner: IHttp) =
  interface IHttp with
    member _.Send(request) = async {
      if request.Method = "POST" && request.Target = reportPath then
        return Error({ Message = "the connection dropped" }: TransportFailure)
      else
        return! inner.Send request
    }

    member _.GetBytes(target: string) = inner.GetBytes target

let tests =
  testList "report commits" [
    testCaseAsync
      "an approved commit by another author offers report and opens the dialog"
      (async {
        let api = reportApi(fun _ -> noContent)

        let! window = startShell(signedInStore(), api :> IHttp)
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"

        do! openReportDialogAsync window

        let! titled = showsText(window, "Report commit")

        Expect.isTrue titled "the dialog never titled itself"

        let! privacy =
          seesVisible(
            window,
            "Your report is private. Attachments are visible only to you and moderators. Reporting does not hide this commit."
          )

        Expect.isTrue privacy "the dialog never stated its privacy"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "an own commit offers no report control"
      (async {
        let api = reportApiWith(ownCommitLog, fun _ -> noContent)

        let! window = startShell(signedInStore(), api :> IHttp)
        let! shown = showsText(window, "The Octocat")

        Expect.isTrue shown "the log never rendered"
        do! openMenuAsync window
        do! settleAsync()

        let! absent =
          probeUiAsync(fun () ->
            menuRow(window, "Report commit") |> Option.isNone)

        Expect.isTrue absent "the own commit offered report"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "a picked image stages a chip with its description and live count"
      (async {
        let api = reportApi(fun _ -> noContent)
        let attachments = FakeAttachments()

        attachments.PickResponse <- [ opaqueStagedImage "shot.png" ]

        let env =
          TestEnv(signedInStore(), api :> IHttp, attachments = attachments)

        let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
        let! _ = showAsync window

        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"

        do! openReportDialogAsync window
        do! attachAsync window

        let! staged =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "shot.png"))
          )

        Expect.isTrue staged "the picked file never staged"

        let! box =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "ImageDescription0")),
            Option.isSome
          )

        Expect.isTrue box "the staged chip carried no description box"

        let! preview =
          probeUiAsync(fun () ->
            controlByName(window, "ReportDialog")
            |> Option.bind(fun dialog ->
              dialog.GetVisualDescendants()
              |> Seq.tryPick(fun visual ->
                match visual with
                | :? Avalonia.Controls.Image as image ->
                  Some(image.Source <> null)
                | _ -> None)))

        Expect.equal preview (Some true) "the staged chip carried no preview"

        let! quiet = probeUiAsync(fun () -> hasText(window, "0/1000"))

        Expect.isFalse quiet "the counter spoke before any text"

        do!
          onUiAsync(fun () ->
            controlByName(window, "ImageDescription0")
            |> Option.iter(fun control ->
              (control :?> TextBox).Text <- "A screenshot"))

        let! counted =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "12/1000"))
          )

        Expect.isTrue counted "the description counter never counted"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "a typed description rides the multipart report"
      (async {
        let api = reportApi(fun _ -> noContent)
        let attachments = FakeAttachments()

        attachments.PickResponse <- [ stagedImage "shot.png" ]

        let env =
          TestEnv(signedInStore(), api :> IHttp, attachments = attachments)

        let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
        let! _ = showAsync window

        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"

        do! openReportDialogAsync window
        do! attachAsync window

        let! staged =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "shot.png"))
          )

        Expect.isTrue staged "the picked file never staged"

        do!
          onUiAsync(fun () ->
            controlByName(window, "ImageDescription0")
            |> Option.iter(fun control ->
              (control :?> TextBox).Text <- "The error page"))

        do! chooseReasonAsync(window, "ReportReasonSpam")
        do! clickSendAsync window

        let! posted =
          waitForAsync(
            8000,
            (fun () -> postRequests api),
            fun sent -> not sent.IsEmpty
          )

        Expect.isTrue posted "the report was never posted"

        let request = postRequests api |> List.exactlyOne

        let form =
          match request.Body with
          | Multipart form -> form
          | _ -> failtest "expected the report as multipart"

        Expect.equal
          [ "payload"; "images[]" ]
          (form.Files |> List.map _.Name)
          "the form parts"

        let payload = payloadOf request

        Expect.stringContains
          payload
          "\"image_descriptions\":[\"The error page\"]"
          "the payload carried the description"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "Send report waits for a reason"
      (async {
        let api = reportApi(fun _ -> noContent)

        let! window = startShell(signedInStore(), api :> IHttp)
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"
        do! openReportDialogAsync window

        let! disabled =
          probeUiAsync(fun () ->
            sendButton window
            |> Option.exists(fun control -> not control.IsEnabled))

        Expect.isTrue disabled "the send offered itself without a reason"

        do! chooseReasonAsync(window, "ReportReasonOther")

        let! enabled =
          waitForAsync(
            5000,
            (fun () -> sendButton window),
            fun control -> control |> Option.exists _.IsEnabled
          )

        Expect.isTrue enabled "the send stayed off after a reason"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "a sent report closes the dialog"
      (async {
        let api = reportApi(fun _ -> noContent)

        let! window = startShell(signedInStore(), api :> IHttp)
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"
        do! openReportDialogAsync window
        do! chooseReasonAsync(window, "ReportReasonSpam")
        do! clickSendAsync window

        let! closed =
          waitForAsync(
            8000,
            (fun () -> controlByName(window, "ReportDialog")),
            Option.isNone
          )

        Expect.isTrue closed "the report dialog stayed open after the send"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "a rate limit keeps the form and asks for a minute"
      (async {
        let api = reportApi(fun _ -> refused(429, "Too Many Attempts."))

        let! window = startShell(signedInStore(), api :> IHttp)
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"
        do! openReportDialogAsync window
        do! chooseReasonAsync(window, "ReportReasonSpam")
        do! typeDetailsAsync(window, "keep me")
        do! clickSendAsync window

        let! limited =
          showsText(
            window,
            "Too many reports. Please wait a minute and try again."
          )

        Expect.isTrue limited "the rate limit said nothing"

        let! kept =
          probeUiAsync(fun () -> controlByName(window, "ReportDialog"))

        Expect.isSome kept "the rate limit closed the dialog"

        let! details =
          probeUiAsync(fun () ->
            controlByName(window, "ReportDetails")
            |> Option.bind(fun control ->
              (control :?> TextBox).Text |> Option.ofObj))

        Expect.equal
          details
          (Some "keep me")
          "the rate limit cleared the details"

        let! reason =
          probeUiAsync(fun () ->
            controlByName(window, "ReportReasonSpam")
            |> Option.map(fun control ->
              (control :?> RadioButton).IsChecked = Nullable true))

        Expect.equal reason (Some true) "the rate limit cleared the reason"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "an empty pick keeps the rate-limit line"
      (async {
        let api = reportApi(fun _ -> refused(429, "Too Many Attempts."))
        let attachments = FakeAttachments()

        let env =
          TestEnv(signedInStore(), api :> IHttp, attachments = attachments)

        let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
        let! _ = showAsync window

        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"

        do! openReportDialogAsync window
        do! chooseReasonAsync(window, "ReportReasonSpam")
        do! clickSendAsync window

        let! limited =
          showsText(
            window,
            "Too many reports. Please wait a minute and try again."
          )

        Expect.isTrue limited "the rate limit said nothing"

        attachments.PickResponse <- []
        do! attachAsync window
        do! settleAsync()
        do! settleAsync()

        let! kept =
          probeUiAsync(fun () ->
            hasText(
              window,
              "Too many reports. Please wait a minute and try again."
            ))

        Expect.isTrue kept "the empty pick cleared the rate-limit line"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "another refusal says the commit is no longer available"
      (async {
        let api = reportApi(fun _ -> refused(500, "the server said no"))

        let! window = startShell(signedInStore(), api :> IHttp)
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"
        do! openReportDialogAsync window
        do! chooseReasonAsync(window, "ReportReasonUnsafe")
        do! clickSendAsync window

        let! refusedLine =
          showsText(window, "This commit is no longer available to report.")

        Expect.isTrue refusedLine "the refusal said nothing"

        let! kept =
          probeUiAsync(fun () -> controlByName(window, "ReportDialog"))

        Expect.isSome kept "the refusal closed the dialog"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "a vanished commit closes the dialog silently"
      (async {
        let api =
          reportApi(fun _ -> refused(404, "This commit isn't available."))

        let! window = startShell(signedInStore(), api :> IHttp)
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"
        do! openReportDialogAsync window
        do! chooseReasonAsync(window, "ReportReasonSpam")
        do! clickSendAsync window

        let! closed =
          waitForAsync(
            8000,
            (fun () -> controlByName(window, "ReportDialog")),
            Option.isNone
          )

        Expect.isTrue closed "the vanished commit left the dialog open"

        let! line =
          probeUiAsync(fun () ->
            hasText(window, "This commit is no longer available to report."))

        Expect.isFalse line "the vanished commit named a line"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "a lost connection says so and keeps the dialog"
      (async {
        let api = reportApi(fun _ -> noContent)
        let offline = OfflineReportApi(api :> IHttp)

        let! window = startShell(signedInStore(), offline :> IHttp)
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"
        do! openReportDialogAsync window
        do! chooseReasonAsync(window, "ReportReasonSpam")
        do! clickSendAsync window

        let! line =
          showsText(
            window,
            "Could not send your report. Check your connection and try again."
          )

        Expect.isTrue line "the lost connection said nothing"

        let! kept =
          probeUiAsync(fun () -> controlByName(window, "ReportDialog"))

        Expect.isSome kept "the lost connection closed the dialog"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "the report flow works from the commit thread"
      (async {
        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/log/" then
              ok logPageJson
            elif request.Target.Contains "/replies" then
              ok repliesPageJson
            elif request.Target.Contains "/related" then
              ok "{\"data\":[]}"
            elif
              request.Method = "GET"
              && request.Target.StartsWith "/api/v1/commits/"
            then
              ok threadPageJson
            elif request.Method = "POST" && request.Target = reportPath then
              noContent
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let! window = startShell(signedInStore(), api :> IHttp)
        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "It was DNS.")

        let! thread = showsText(window, "Back to the log")

        Expect.isTrue thread "the commit thread never opened"

        do! openReportDialogAsync window

        let! titled = showsText(window, "Report commit")

        Expect.isTrue titled "the thread's report dialog never opened"

        do! chooseReasonAsync(window, "ReportReasonSpam")
        do! clickSendAsync window

        let! closed =
          waitForAsync(
            10000,
            (fun () -> controlByName(window, "ReportDialog")),
            Option.isNone
          )

        Expect.isTrue closed "the thread's report dialog stayed open"

        let! sent =
          waitForAsync(
            10000,
            (fun () -> postRequests api),
            fun requests -> not requests.IsEmpty
          )

        Expect.isTrue sent "the thread's report never posted"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "a bad pick refuses and leaves the stage empty"
      (async {
        let api = reportApi(fun _ -> noContent)
        let attachments = FakeAttachments()

        attachments.PickResponse <- [
          {
            FileName = "notes.txt"
            Bytes = [| 1uy; 2uy; 3uy |]
          }
        ]

        let env =
          TestEnv(signedInStore(), api :> IHttp, attachments = attachments)

        let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
        let! _ = showAsync window

        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"

        do! openReportDialogAsync window
        do! attachAsync window

        let! refusedLine =
          showsText(window, "notes.txt is not a JPEG, PNG, or WebP image.")

        Expect.isTrue refusedLine "the bad pick said nothing"

        let! empty =
          probeUiAsync(fun () ->
            controlByName(window, "ImageDescription0") |> Option.isNone
            && not(hasText(window, "notes.txt")))

        Expect.isTrue empty "the bad pick changed the stage"

        do! onUiAsync(fun () -> window.Close())
      })
  ]
