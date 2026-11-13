module DevNull.Client.UI.Tests.ThreadModeTests

open System
open System.Reactive.Subjects
open Avalonia.Controls
open Avalonia.Input
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive
open MVUish.Avalonia

open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.Workbench
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

type Sinks = {
  Back: unit -> unit
  Thread: Commit -> unit
  External: string -> unit
  Quote: Commit -> unit
}

let noSinks: Sinks = {
  Back = ignore
  Thread = ignore
  External = ignore
  Quote = ignore
}

let images = {
  new ImageCacheService with
    member _.Get(_url: string) = async { return None }
}

let sessionOf(env: TestEnv) =
  match (env :> IProvideSession).Session.Current with
  | SignedIn session -> session
  | SignedOut -> failwith "the thread test needs a signed-in session"

let props(env: TestEnv, sinks: Sinks) : ThreadMode.Props<TestEnv> = {
  Env = env
  Session = sessionOf env
  CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
  KeyFile = "/tmp/devnull-test-id_ed25519"
  ViewerId = "01a151db-0000-0000-0000-000000000000"
  Pasteis = true
  Images = images
  Now = Observable.single(DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero))
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  OnBack = sinks.Back
  OnOpenThread = sinks.Thread
  OnOpenExternal = sinks.External
  OnQuote = sinks.Quote
  OnDialog = ignore
}

let threadPage =
  let json =
    threadPageJson.Replace(
      "\"ancestors\":[]",
      "\"ancestors\":[" + secondCommitJson + "]"
    )

  match CommitPage.decode json with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned thread never decoded: {failure.Message}"

let repliesPage =
  match CommitNode.decodePage repliesPageJson with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned replies never decoded: {failure.Message}"

let landedReply =
  match CommitNode.decodePage landedRepliesPageJson with
  | Ok page ->
    match page.Items with
    | [ _; LiveCommit reply ] -> reply
    | _ -> failwith "the landed reply page did not carry its reply"
  | Error failure ->
    failwith $"the landed reply page never decoded: {failure.Message}"

let readyModel: ThreadMode.Model = {
  CommitId = threadPage.Commit.Id
  Loading = false
  Error = None
  Commit = Some threadPage.Commit
  Ancestors = threadPage.Ancestors
  ReplyParticipants = threadPage.ReplyParticipants
  Replies = repliesPage.Items
  RepliesCursor = None
  RepliesLoading = false
  RepliesError = None
  PollChoices = Map.empty
  Now = DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)
}

let initialModel(sinks: Sinks) =
  let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)
  let p = props(env, sinks)
  let model, _ = ThreadMode.init p
  p, model

let shownModel(sinks: Sinks) =
  let p, initial = initialModel sinks
  let model, _ = ThreadMode.update(p, ThreadMode.Shown(Ok threadPage), initial)
  p, model

let repliesModel(sinks: Sinks) =
  let p, model = shownModel sinks

  let model, _ =
    ThreadMode.update(p, ThreadMode.RepliesFetched(Ok repliesPage), model)

  p, model

let run(cmd: Cmd<ThreadMode.Msg>) =
  cmd (fun _ -> ()) (fun (_, ex) -> failwith ex.Message)

let tests =
  testList "workbench thread mode" [
    testCase "the show lands the commit, its parents, and the participants"
    <| fun () ->
      let p, initial = initialModel noSinks

      Expect.isTrue initial.Loading "the thread starts reading"
      Expect.equal initial.CommitId p.CommitId "the thread id"

      let model, _ =
        ThreadMode.update(p, ThreadMode.Shown(Ok threadPage), initial)

      Expect.equal model.Commit (Some threadPage.Commit) "the commit"
      Expect.equal model.Ancestors threadPage.Ancestors "the parent chain"

      Expect.equal
        model.ReplyParticipants
        threadPage.ReplyParticipants
        "the reply participants"

      Expect.isFalse model.Loading "the show settled"
      Expect.isTrue model.RepliesLoading "the replies start after the show"
      Expect.isNone model.Error "no show error"

    testCase "the replies cursor lands and appends"
    <| fun () ->
      let p, model = shownModel noSinks

      let firstPage = {
        repliesPage with
            NextCursor = Some "next"
      }

      let model, _ =
        ThreadMode.update(p, ThreadMode.RepliesFetched(Ok firstPage), model)

      Expect.equal model.Replies firstPage.Items "the first page"
      Expect.equal model.RepliesCursor (Some "next") "the cursor"

      let model, _ =
        ThreadMode.update(p, ThreadMode.MoreRepliesRequested, model)

      Expect.isTrue model.RepliesLoading "the next page started"

      let secondPage = {
        repliesPage with
            Items = [ LiveCommit landedReply ]
            NextCursor = None
      }

      let model, _ =
        ThreadMode.update(p, ThreadMode.RepliesAppended(Ok secondPage), model)

      Expect.equal model.Replies.Length 2 "the appended replies"
      Expect.isNone model.RepliesCursor "the cursor is spent"

    testCase "a posted reply lands in the thread and raises the count"
    <| fun () ->
      let p, model = repliesModel noSinks
      let before = (Option.get model.Commit).RepliesCount

      let model, _ = ThreadMode.update(p, ThreadMode.Posted landedReply, model)

      match model.Replies with
      | LiveCommit first :: _ ->
        Expect.equal first.Id landedReply.Id "the posted reply is first"
      | _ -> failwith "the posted reply did not land"

      Expect.equal
        (Option.get model.Commit).RepliesCount
        (before |> Option.map((+) 1) |> Option.orElse(Some 1))
        "the thread count did not rise"

    testCaseAsync
      "Escape on the thread asks its host to go back"
      (async {
        let! asked =
          probeUiAsync(fun () ->
            let backs = ResizeArray<unit>()

            let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)

            let p =
              props(
                env,
                {
                  noSinks with
                      Back = fun () -> backs.Add()
                }
              )

            let subject = new BehaviorSubject<ThreadMode.Model>(readyModel)

            let view =
              ThreadMode.view
                p
                (subject :> IObservable<ThreadMode.Model>)
                (fun msg ->
                  match msg with
                  | ThreadMode.BackRequested -> backs.Add()
                  | _ -> ())

            let window = Window(Content = view.Center)
            window.Show()

            view.Center.RaiseEvent(
              KeyEventArgs(
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape
              )
            )

            window.Close()
            backs.Count)

        Expect.equal asked 1 "the escape did not ask the host to go back"
      })

    testCase "the back, thread, and quote intents leave through their sinks"
    <| fun () ->
      let backs = ResizeArray<unit>()
      let threads = ResizeArray<Commit>()
      let quoted = ResizeArray<Commit>()

      let sinks = {
        Back = fun () -> backs.Add()
        Thread = threads.Add
        External = ignore
        Quote = quoted.Add
      }

      let p, model = repliesModel sinks

      let model, backCmd = ThreadMode.update(p, ThreadMode.BackRequested, model)

      run backCmd
      Expect.equal backs.Count 1 "the back sink"

      let model, threadCmd =
        ThreadMode.update(p, ThreadMode.Opened landedReply, model)

      run threadCmd
      Expect.equal (threads |> Seq.toList) [ landedReply ] "the thread sink"

      let model, quoteCmd =
        ThreadMode.update(p, ThreadMode.QuoteRequested threadPage.Commit, model)

      run quoteCmd

      Expect.equal (quoted |> Seq.toList) [ threadPage.Commit ] "the quote sink"
  ]
