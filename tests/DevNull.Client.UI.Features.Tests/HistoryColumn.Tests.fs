module DevNull.Client.UI.Tests.HistoryColumnTests

open System
open Expecto
open FSharp.Control.Reactive

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

  interface DevNull.Client.Env.IProvideDispatcher with
    member _.Dispatcher = Unchecked.defaultof<Avalonia.Threading.IDispatcher>

  interface DevNull.Client.Profiles.IProvideProfiles with
    member _.Profiles = Unchecked.defaultof<DevNull.Client.Profiles.IProfiles>

  interface DevNull.Client.TranslationCache.IProvideTranslations with
    member _.Translations =
      Unchecked.defaultof<DevNull.Client.TranslationCache.TranslationCache>

  interface DevNull.Client.UserSettingsStore.IProvideUserSettings with
    member _.UserSettings =
      Unchecked.defaultof<DevNull.Client.UserSettingsStore.UserSettingsStore>

  interface DevNull.Client.Env.IProvideLogging with
    member _.LoggerFactory =
      Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance

let images = {
  new ImageCacheService with
    member _.Get(_url: string) = async { return None }
}

let props(log: LogKind) : HistoryColumn.Props<WorkbenchEnv> = {
  Env = WorkbenchEnv()
  Session = None
  Source = LogSource log
  KeyFile = "/tmp/devnull-test-id_ed25519"
  ViewerId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
  Pasteis = true
  Images = images
  Flyout = ValueNone
  Now = Observable.single(DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero))
  Refresh = Observable.empty
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  LastVisit = None
  OnOpenExternal = ignore
  OnOpened = ignore
  OnReply = ignore
  OnQuote = ignore
  Restore = None
  OnReport = ignore
  OnDialog = ignore
}

let pageOf(json: string) =
  match LogPage.decode json with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned page never decoded: {failure.Message}"

let countedPageOf(json: string, count: int) =
  pageOf(
    json.Replace("\"new_commits_count\":null", $"\"new_commits_count\":{count}")
  )

let newCountPage(count: int) = countedPageOf(logPageJson, count)

let commitOf(page: LogPage) =
  match page.Page.Items with
  | LiveCommit commit :: _ -> commit
  | _ -> failwith "the canned page carries no live commit"

let firstModel(log: LogKind) =
  let p = props log
  let initial, _ = HistoryColumn.init p
  let page = pageOf logPageJson

  let model, _ =
    HistoryColumn.update(p, HistoryColumn.Fetched(Ok page), initial)

  p, model

let tests =
  testList "workbench history column" [
    testCase "the first page lands with its head and cursor"
    <| fun () ->
      let p = props Following
      let initial, _ = HistoryColumn.init p

      Expect.isTrue initial.Loading "the column starts loading"

      let page = pageOf logPageJson

      let model, _ =
        HistoryColumn.update(p, HistoryColumn.Fetched(Ok page), initial)

      Expect.equal model.Items page.Page.Items "the page rows"
      Expect.equal model.NextCursor (Some "next") "the next cursor"

      Expect.equal
        model.Head
        (Some "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d")
        "the head"

      Expect.isFalse model.Loading "the load settled"
      Expect.isTrue model.FetchedAt.IsSome "the fetch time"

    testCase "the cursor page appends under the loaded rows"
    <| fun () ->
      let _, first = firstModel Latest
      let next = pageOf secondLogPageJson

      let model, _ =
        HistoryColumn.update(
          props Latest,
          HistoryColumn.Appended(Ok next),
          first
        )

      Expect.equal model.Items.Length 2 "the appended rows"
      Expect.isNone model.NextCursor "the cursor is spent"

    testCase "the poll sets the new count and the pill fetch clears it"
    <| fun () ->
      let p, first = firstModel Following

      let counted, _ =
        HistoryColumn.update(
          p,
          HistoryColumn.NewChecked(Ok(newCountPage 3)),
          first
        )

      Expect.equal counted.NewCount (Some 3) "the poll count"

      let cleared, _ =
        HistoryColumn.update(p, HistoryColumn.NewRequested, counted)

      Expect.isNone cleared.NewCount "the pill is spent"

    testCase "a failed poll keeps the count it had"
    <| fun () ->
      let p, first = firstModel Following

      let counted, _ =
        HistoryColumn.update(
          p,
          HistoryColumn.NewChecked(Ok(newCountPage 3)),
          first
        )

      let kept, _ =
        HistoryColumn.update(
          p,
          HistoryColumn.NewChecked(Error(TransportFailed "down")),
          counted
        )

      Expect.equal kept.NewCount (Some 3) "the count survived"

    testCase
      "a heard newer commit rides the next poll and a page fetch clears it"
    <| fun () ->
      let p, first = firstModel Following

      let heard, _ =
        HistoryColumn.update(
          p,
          HistoryColumn.NewChecked(Ok(countedPageOf(secondLogPageJson, 1))),
          first
        )

      Expect.equal
        heard.HeardIds
        [ "018f4c1e-9999-7c3a-9b2d-5e6f7a8b9c0d" ]
        "the newer commit was heard"

      Expect.equal
        heard.Items.Length
        1
        "the newer commit stays out of the loaded rows"

      let cleared, _ =
        HistoryColumn.update(
          p,
          HistoryColumn.NewFetched(Ok(newCountPage 0)),
          heard
        )

      Expect.isEmpty cleared.HeardIds "the page fetch cleared the heard set"

    testCase "the polling flag gates the one-minute poll"
    <| fun () ->
      let p, first = firstModel Following

      let off, _ =
        HistoryColumn.update(p, HistoryColumn.PollingChanged false, first)

      Expect.isFalse off.Polling "the poll is off"

      let on, _ =
        HistoryColumn.update(p, HistoryColumn.PollingChanged true, off)

      Expect.isTrue on.Polling "the poll is on"

    testCase "a star toggle marks its commit busy"
    <| fun () ->
      let p, first = firstModel Following
      let commit = commitOf(pageOf logPageJson)

      let busy, _ =
        HistoryColumn.update(p, HistoryColumn.StarToggled commit, first)

      Expect.equal busy.Busy (Some commit.Id) "the star is in flight"

    testCase "a poll choice lands in the column's map"
    <| fun () ->
      let p, first = firstModel Following
      let commit = commitOf(pageOf logPageJson)

      let option: PollOption = {
        Id = "option-a"
        Label = "A"
        VotesCount = None
        Voted = false
        Percentage = None
      }

      let chosen, _ =
        HistoryColumn.update(
          p,
          HistoryColumn.PollChoiceChanged(commit, option, true),
          first
        )

      Expect.equal
        chosen.PollChoices
        (Map.ofList [ commit.Id, option.Id ])
        "the chosen option"
  ]
