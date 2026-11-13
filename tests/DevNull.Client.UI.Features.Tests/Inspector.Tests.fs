module DevNull.Client.UI.Tests.InspectorTests

open System
open Expecto
open FSharp.Control.Reactive
open MVUish.Avalonia

open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Workbench
open DevNull.Client.UI.Tests.Fixtures

type WorkbenchEnv() =

  let clock = FixedClock(1760000000L)
  let nonce = FixedNonceSource("00112233445566778899aabbccddeeff")

  let signer = {
    new ISigner with
      member _.Sign(_request: SignRequest) = async { return Ok "signature" }
  }

  let http = {
    new IHttp with
      member _.Send(_request: HttpRequest) = async {
        return
          Ok {
            Status = 200
            Headers = []
            Body = ""
          }
      }

      member _.GetBytes(_target: string) = async {
        return Error { Message = "no bytes" }
      }
  }

  interface IProvideClock with
    member _.Clock = clock

  interface IProvideNonceSource with
    member _.NonceSource = nonce

  interface IProvideSigner with
    member _.Signer = signer

  interface IProvideHttp with
    member _.Http = http

[<Struct>]
type Sinks = {
  Parent: string -> unit
  Child: string -> unit
  Author: string -> unit
  External: string -> unit
  Quote: Commit -> unit
}

let noSinks: Sinks = {
  Parent = ignore
  Child = ignore
  Author = ignore
  External = ignore
  Quote = ignore
}

let images = {
  new ImageCacheService with
    member _.Get(_url: string) = async { return None }
}

let props(env: WorkbenchEnv, sinks: Sinks) : Inspector.Props<WorkbenchEnv> = {
  Env = env
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
  OnCheckoutParent = sinks.Parent
  OnCheckoutChild = sinks.Child
  OnOpenAuthor = sinks.Author
  OnOpenExternal = sinks.External
  OnQuote = sinks.Quote
  OnClose = ignore
}

let showPage =
  match CommitPage.decode threadPageJson with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned thread never decoded: {failure.Message}"

let pageOfReplies(json: string) =
  match CommitNode.decodePage json with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned replies never decoded: {failure.Message}"

let secondReplyJson =
  replyJson
    .Replace(
      "018f4c1e-7777-7c3a-9b2d-5e6f7a8b9c0d",
      "018f4c1e-8888-7c3a-9b2d-5e6f7a8b9c0d"
    )
    .Replace("d1e2f3a", "e2f3a4b")
    .Replace("A reply.", "Another reply.")

let repliesJson(items: string, cursor: string option) =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":%s,\"prev_cursor\":null}}"
    items
    (match cursor with
     | Some value -> sprintf "\"%s\"" value
     | None -> "null")

let repliesCursorPage = pageOfReplies(repliesJson(replyJson, Some "next"))

let repliesSecondPage = pageOfReplies(repliesJson(secondReplyJson, None))

let initialModel(sinks: Sinks) =
  let p = props(WorkbenchEnv(), sinks)
  let model, _ = Inspector.init p
  p, model

let firstModel(sinks: Sinks) =
  let p, initial = initialModel sinks
  let model, _ = Inspector.update(p, Inspector.Shown(Ok showPage), initial)
  p, model

let repliesModel(sinks: Sinks) =
  let p, model = firstModel sinks

  let model, _ =
    Inspector.update(p, Inspector.RepliesFetched(Ok repliesCursorPage), model)

  p, model

let run(cmd: Cmd<Inspector.Msg>) =
  cmd (fun _ -> ()) (fun (_, ex) -> failwith ex.Message)

let tests =
  testList "workbench inspector" [
    testCase "the show lands the commit and its parent chain"
    <| fun () ->
      let p, initial = initialModel noSinks

      Expect.isTrue initial.Loading "the inspector starts reading"
      Expect.equal initial.CommitId p.CommitId "the checkout id"

      let model, _ = Inspector.update(p, Inspector.Shown(Ok showPage), initial)

      Expect.equal model.Commit (Some showPage.Commit) "the commit"
      Expect.equal model.Ancestors showPage.Ancestors "the ancestors"

      Expect.equal
        model.ReplyParticipants
        showPage.ReplyParticipants
        "the reply participants"

      Expect.isFalse model.Loading "the show settled"
      Expect.isTrue model.RepliesLoading "the replies start after the show"
      Expect.isNone model.Error "no show error"

    testCase "the replies cursor lands and appends"
    <| fun () ->
      let p, model = firstModel noSinks

      let model, _ =
        Inspector.update(
          p,
          Inspector.RepliesFetched(Ok repliesCursorPage),
          model
        )

      Expect.equal model.Replies repliesCursorPage.Items "the first page"
      Expect.equal model.RepliesCursor (Some "next") "the cursor"
      Expect.isFalse model.RepliesLoading "the first page settled"

      let model, _ = Inspector.update(p, Inspector.MoreRepliesRequested, model)

      Expect.isTrue model.RepliesLoading "the next page started"

      let model, _ =
        Inspector.update(
          p,
          Inspector.RepliesAppended(Ok repliesSecondPage),
          model
        )

      Expect.equal model.Replies.Length 2 "the appended replies"
      Expect.isNone model.RepliesCursor "the cursor is spent"
      Expect.isFalse model.RepliesLoading "the append settled"

    testCase "a failed replies read keeps the checkout alive"
    <| fun () ->
      let p, model = repliesModel noSinks
      let before = model.Replies

      let model, _ =
        Inspector.update(
          p,
          Inspector.RepliesFetched(Error(TransportFailed "down")),
          { model with Replies = before }
        )

      Expect.equal model.Commit (Some showPage.Commit) "the commit stayed"
      Expect.equal model.Replies before "the replies stayed"
      Expect.isSome model.RepliesError "the replies error"
      Expect.isNone model.Error "the show error stayed clear"

      let model, _ =
        Inspector.update(
          p,
          Inspector.RepliesAppended(Error(TransportFailed "down")),
          model
        )

      Expect.equal model.Commit (Some showPage.Commit) "the commit stayed again"
      Expect.equal model.Replies before "the appended failure kept the rows"

    testCase "a failed show keeps what the inspector read"
    <| fun () ->
      let p, model = firstModel noSinks

      let model, _ =
        Inspector.update(
          p,
          Inspector.Shown(Error(TransportFailed "down")),
          model
        )

      Expect.equal model.Commit (Some showPage.Commit) "the commit stayed"
      Expect.isSome model.Error "the show error"
      Expect.equal model.CommitId p.CommitId "the checkout id stayed"

    testCase
      "the parent, child, author, and quote intents leave through their sinks"
    <| fun () ->
      let walked = ResizeArray<string>()
      let children = ResizeArray<string>()
      let opened = ResizeArray<string>()
      let quoted = ResizeArray<Commit>()

      let sinks = {
        Parent = walked.Add
        Child = children.Add
        Author = opened.Add
        External = ignore
        Quote = quoted.Add
      }

      let p, model = firstModel sinks

      let model, parentCmd =
        Inspector.update(p, Inspector.ParentWalked "parent-id", model)

      Expect.equal
        model.Commit
        (Some showPage.Commit)
        "the parent walk kept the checkout"

      run parentCmd
      Expect.equal (walked |> Seq.toList) [ "parent-id" ] "the parent sink"

      let model, childCmd =
        Inspector.update(p, Inspector.ChildOpened "child-id", model)

      run childCmd
      Expect.equal (children |> Seq.toList) [ "child-id" ] "the child sink"

      let model, authorCmd =
        Inspector.update(p, Inspector.AuthorOpened "octocat", model)

      run authorCmd
      Expect.equal (opened |> Seq.toList) [ "octocat" ] "the author sink"

      let model, quoteCmd =
        Inspector.update(p, Inspector.QuoteRequested showPage.Commit, model)

      run quoteCmd
      Expect.equal (quoted |> Seq.toList) [ showPage.Commit ] "the quote sink"

    testCase "the star and pastel actions mark the commit busy"
    <| fun () ->
      let p, model = repliesModel noSinks
      let commit = showPage.Commit

      let model, _ = Inspector.update(p, Inspector.StarToggled commit, model)

      Expect.equal model.Busy (Some commit.Id) "the star is in flight"

      let starred = {
        commit with
            StarsCount = Some 8
            Starred = Some true
      }

      let model, _ = Inspector.update(p, Inspector.StarDone(Ok starred), model)

      Expect.isNone model.Busy "the star settled"
      Expect.equal model.Commit (Some starred) "the star result landed"

      let model, _ = Inspector.update(p, Inspector.PastelGiven commit, model)

      Expect.equal model.Busy (Some commit.Id) "the pastel is in flight"

      let pastelCommit = { commit with PasteisCount = 1 }

      let pastel: PastelResult = {
        Commit = pastelCommit
        PasteisRemaining = 5
        PasteisResetsAt = DateTimeOffset.UtcNow.AddDays 1.0
        AuthorPasteisReceived = 1
      }

      let model, _ = Inspector.update(p, Inspector.PastelDone(Ok pastel), model)

      Expect.isNone model.Busy "the pastel settled"
      Expect.equal model.Commit (Some pastelCommit) "the pastel result landed"
  ]
