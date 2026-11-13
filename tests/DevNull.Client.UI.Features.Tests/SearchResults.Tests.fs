module DevNull.Client.UI.Tests.SearchResultsTests

open System
open System.Reactive.Subjects
open Avalonia.Controls
open Expecto
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Programs
open DevNull.Client.Workbench
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let images = {
  new ImageCacheService with
    member _.Get(_url: string) = async { return None }
}

let results =
  match SearchCommits.decode(sprintf "{\"data\":[%s]}" commitJson) with
  | Ok results -> results
  | Error failure ->
    failwith $"the canned commit search never decoded: {failure.Message}"

type Sinks = {
  Thread: Commit -> unit
  Quote: Commit -> unit
  External: string -> unit
  Close: unit -> unit
}

let noSinks: Sinks = {
  Thread = ignore
  Quote = ignore
  External = ignore
  Close = ignore
}

let props(env: TestEnv, sinks: Sinks) : SearchResults.Props<TestEnv> = {
  Env = env
  Session = None
  KeyFile = "/tmp/devnull-test-id_ed25519"
  ViewerId = "01a151db-0000-0000-0000-000000000000"
  Pasteis = true
  Images = images
  Now = Observable.single(DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero))
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  Term = "dns"
  OnOpenThread = sinks.Thread
  OnQuote = sinks.Quote
  OnOpenExternal = sinks.External
  OnClose = sinks.Close
  OnReportCommit = ignore
  OnShare = ignore
}

let initialModel(sinks: Sinks) =
  let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)
  let p = props(env, sinks)
  let model, _ = SearchResults.init p
  p, model

let run(cmd: Cmd<SearchResults.Msg>) =
  cmd (fun _ -> ()) (fun (_, ex) -> failwith ex.Message)

let tests =
  testList "workbench search results" [
    testCase "the commit read lands without a cursor"
    <| fun () ->
      let p, model = initialModel noSinks
      Expect.isTrue model.Loading "the search did not start"
      Expect.equal model.Term "dns" "the normalized term"

      let model, _ =
        SearchResults.update(p, SearchResults.Fetched(Ok results), model)

      Expect.equal model.Results results "the result rows"
      Expect.isFalse model.Loading "the read stayed busy"
      Expect.isNone model.Error "the read failed"

    testCase "the server's slow-down answer stays in the error state"
    <| fun () ->
      let p, model = initialModel noSinks

      let refusal =
        Refused(
          429,
          Some RateLimited,
          "Slow down: too many searches. Give it a few seconds."
        )

      let model, _ =
        SearchResults.update(p, SearchResults.Fetched(Error refusal), model)

      Expect.equal model.Error (Some refusal) "the refusal was dropped"
      Expect.isFalse model.Loading "the refusal kept the read busy"

      Expect.isTrue
        ((describe refusal).Contains "Slow down")
        "the refusal lost the server words"

    testCaseAsync
      "the slow-down words render in the error state"
      (async {
        let refusal =
          Refused(
            429,
            Some RateLimited,
            "Slow down: too many searches. Give it a few seconds."
          )

        let! window, _ =
          probeUiAsync(fun () ->
            let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)
            let p = props(env, noSinks)

            let model: SearchResults.Model = {
              Term = "dns"
              Loading = false
              Error = Some refusal
              Results = []
              PollChoices = Map.empty
              Now = DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)
            }

            let subject = new BehaviorSubject<SearchResults.Model>(model)

            let view =
              SearchResults.view
                p
                (subject :> IObservable<SearchResults.Model>)
                (fun (_: SearchResults.Msg) -> ())

            let window = Window(Content = view)
            window.Show()
            window, subject)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, describe refusal)
          )

        Expect.isTrue shown "the slow-down words never rendered"

        do! probeUiAsync(fun () -> window.Close())
      })

    testCase "a manual refresh is the only second read"
    <| fun () ->
      let p, model = initialModel noSinks

      let model, _ =
        SearchResults.update(p, SearchResults.Fetched(Ok results), model)

      let model, _ =
        SearchResults.update(p, SearchResults.RefreshRequested, model)

      Expect.isTrue model.Loading "the manual refresh did not start"
      Expect.equal model.Results results "the refresh dropped the rows"

    testCase "the row and close intents leave through their sinks"
    <| fun () ->
      let threads = ResizeArray<Commit>()
      let quoted = ResizeArray<Commit>()
      let external = ResizeArray<string>()
      let closes = ResizeArray<unit>()

      let p, model =
        initialModel {
          Thread = threads.Add
          Quote = quoted.Add
          External = external.Add
          Close = fun () -> closes.Add()
        }

      let row = (Option.get(List.tryHead results))

      let commit =
        match row with
        | LiveCommit commit -> commit
        | Tombstone _ -> failwith "the canned search carried a tombstone"

      let model, threadCmd =
        SearchResults.update(p, SearchResults.Opened commit, model)

      run threadCmd
      Expect.equal (threads |> Seq.toList) [ commit ] "the thread sink"

      let model, quoteCmd =
        SearchResults.update(p, SearchResults.QuoteRequested commit, model)

      run quoteCmd
      Expect.equal (quoted |> Seq.toList) [ commit ] "the quote sink"

      let model, externalCmd =
        SearchResults.update(
          p,
          SearchResults.ExternalOpened "https://example.test",
          model
        )

      run externalCmd

      Expect.equal
        (external |> Seq.toList)
        [ "https://example.test" ]
        "the external sink"

      let model, closeCmd =
        SearchResults.update(p, SearchResults.CloseRequested, model)

      run closeCmd
      Expect.equal (closes |> Seq.toList) [ () ] "the close sink"

    testCaseAsync
      "the search center reads once and never polls"
      (async {
        let! api, window, handle =
          probeUiAsync(fun () ->
            let api =
              FakeApi(fun request ->
                if request.Target.StartsWith "/api/v1/search/commits" then
                  ok(sprintf "{\"data\":[%s]}" commitJson)
                else
                  ok "{}")

            let env = TestEnv(signedInStore(), api :> IHttp)
            let p = props(env, noSinks)

            let view, handle =
              DevNull.Client.Programs.runPage(
                (env :> DevNull.Client.Env.IProvideDispatcher).Dispatcher,
                SearchResults.program p
              )

            let window = Window(Content = view)
            window.Show()
            api, window, handle)

        let reads() =
          api.Requests
          |> List.filter(fun request ->
            request.Target.StartsWith "/api/v1/search/commits")
          |> List.length

        let! first = waitForAsync(5000, reads, fun count -> count >= 1)

        Expect.isTrue first "the search center never made its first read"

        do! Async.Sleep 1200
        do! pumpAsync()

        Expect.equal (reads()) 1 "the search center polled"

        do!
          probeUiAsync(fun () ->
            handle.Dispose()
            window.Close())
      })
  ]
