module DevNull.Client.UI.Tests.HistoryTab

open System
open FSharp.Control.Reactive
open Expecto
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.AttachmentSources
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Editor
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore
open DevNull.Client.UI.Tests.Fixtures

/// The workers one pure update needs: fixed values and no transport.
type FakeEnv() =
  let http: IHttp = {
    new IHttp with
      member _.Send(_: HttpRequest) = async {
        return Error { Message = "unused" }
      }

      member _.GetBytes(_: string) = async {
        return Error { Message = "unused" }
      }
  }

  let full = TestEnv(signedInStore(), http)


  interface IProvideClock with
    member _.Clock = {
      new IClock with
        member _.UnixNow() = 1760000000L
    }

  interface IProvideNonceSource with
    member _.NonceSource = {
      new INonceSource with
        member _.NewHex() = "00112233445566778899aabbccddeeff"
    }

  interface IProvideSigner with
    member _.Signer = {
      new ISigner with
        member _.Sign(_: SignRequest) = async { return Ok "signature" }
    }

  interface IProvideHttp with
    member _.Http = {
      new IHttp with
        member _.Send(_: HttpRequest) = async {
          return Error { Message = "unused" }
        }

        member _.GetBytes(_: string) = async {
          return Error { Message = "unused" }
        }
    }

  interface IProvideUiClock with
    member _.UiClock = {
      new UiClockService with
        member _.Minute = Observable.empty
    }

  interface IProvideImageCache with
    member _.ImageCache = {
      new ImageCacheService with
        member _.Get(_: string) = async { return None }
    }

  interface IProvideDispatcher with
    member _.Dispatcher = (full :> IProvideDispatcher).Dispatcher

  interface IProvideRemotes with
    member _.Remotes = (full :> IProvideRemotes).Remotes

  interface IProvideProfiles with
    member _.Profiles = (full :> IProvideProfiles).Profiles

  interface IProvideTranslations with
    member _.Translations = (full :> IProvideTranslations).Translations

  interface IProvideUserSettings with
    member _.UserSettings = (full :> IProvideUserSettings).UserSettings

  interface IProvideAttachmentSources with
    member _.Attachments = (full :> IProvideAttachmentSources).Attachments

  interface IProvideBrowser with
    member _.Browser = (full :> IProvideBrowser).Browser

let session: SignedInSession = {
  Session = {
    User = {
      Id = "viewer"
      Handle = "ana"
      Name = "Ana"
      AvatarUrl = None
    }
    DeviceFingerprint = "9F3A0000C1"
    UnreadNotificationsCount = 0
    PasteisMode = false
    AutomaticTranslation = false
    TermsVersion = ""
    TermsAccepted = Some true
    Remotes = None
  }
  KeyFile = "/tmp/devnull-test-key"
}

let props(log: LogKind) : HistoryTab.Props<FakeEnv> = {
  Env = FakeEnv()
  Session = session
  Log = log
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  Active = Observable.single true
  Now = fun () -> DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)
  Restore = ValueNone
  Refresh = Observable.empty
  OnSnapshot = ignore
  OnScroll = ignore
  OnOpen = ignore
  OnReplyRequested = ignore
  OnQuoteRequested = ignore
  OnOpenRepo = ignore
  OnFetch = ignore
}

let pageOf(json: string) =
  match LogPage.decode json with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned page never decoded: {failure.Message}"

let firstPage = pageOf logPageJson
let secondPage = pageOf secondLogPageJson

let pollPage(count: int) =
  logPageJson.Replace(
    "\"new_commits_count\":null",
    $"\"new_commits_count\":{count}"
  )
  |> pageOf

let pageWith(commit: string, count: int) =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"following\",\"is_ranked\":false,\"new_commits_count\":%d,\"suggestions\":[]}"
    commit
    count
  |> pageOf

let newerPage = pageWith(secondCommitJson, 1)

let lateCommitJson =
  secondCommitJson
    .Replace(
      "018f4c1e-9999-7c3a-9b2d-5e6f7a8b9c0d",
      "018f4c1e-7777-7c3a-9b2d-5e6f7a8b9c0d"
    )
    .Replace("2026-10-20T04:18:19Z", "2026-10-20T04:17:30Z")

let latePage = pageWith(lateCommitJson, 1)

let commitOf(node: CommitNode) =
  match node with
  | LiveCommit commit -> commit
  | Tombstone _ -> failwith "the fixture node is live"

let loaded =
  let model, _ =
    HistoryTab.State.update(
      props Following,
      HistoryTab.State.Fetched(Ok firstPage),
      HistoryTab.State.initial
    )

  model

let update(msg: HistoryTab.State.Msg, model: HistoryTab.State.Model) =
  HistoryTab.State.update(props Following, msg, model)

let tests =
  testList "editor history tab" [
    testCase "the first page fills the tab" (fun () ->
      let next, _ =
        update(
          HistoryTab.State.Fetched(Ok firstPage),
          HistoryTab.State.initial
        )

      Expect.equal next.Items.Length 1 "the first page's rows"
      Expect.equal next.NextCursor (Some "next") "the cursor"
      Expect.isFalse next.Loading "the loading flag"

      Expect.equal
        next.FetchedAt
        (Some(DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)))
        "the fetch moment")

    testCase "the next page appends under the loaded rows" (fun () ->
      let next, _ = update(HistoryTab.State.Appended(Ok secondPage), loaded)

      Expect.equal next.Items.Length 2 "the appended rows"
      Expect.equal next.NextCursor None "the cursor")

    testCase "the poll reports the page's own new count" (fun () ->
      let next, _ = update(HistoryTab.State.NewChecked(Ok(pollPage 3)), loaded)

      Expect.equal next.NewCount (Some 3) "the dirty count")

    testCase "a zero poll answer clears the dot" (fun () ->
      let withCount, _ =
        update(HistoryTab.State.NewChecked(Ok(pollPage 3)), loaded)

      let next, _ =
        update(HistoryTab.State.NewChecked(Ok(pollPage 0)), withCount)

      Expect.equal next.NewCount None "the cleared count")

    testCase "the poll runs only while the model carries it" (fun () ->
      Expect.isFalse
        (HistoryTab.State.polling HistoryTab.State.initial)
        "the fresh model polled"

      let active, _ =
        update(HistoryTab.State.ActiveChanged true, HistoryTab.State.initial)

      Expect.isTrue active.Polling "the active model did not poll"

      let idle, _ = update(HistoryTab.State.ActiveChanged false, active)

      Expect.isFalse idle.Polling "the idle model kept polling")

    testCase "no cursor asks for no page" (fun () ->
      let model = { loaded with NextCursor = None }
      let next, _ = update(HistoryTab.State.MoreRequested, model)

      Expect.equal next model "the model moved without a cursor")

    testCase
      "a heard newer commit rides the next poll and a page fetch clears it"
      (fun () ->
        let first = commitOf firstPage.Page.Items[0]
        let newer = commitOf newerPage.Page.Items[0]

        let heard, _ =
          update(HistoryTab.State.NewChecked(Ok newerPage), loaded)

        Expect.equal heard.HeardIds [ newer.Id ] "the heard ids"
        Expect.equal heard.NewCount (Some 1) "the dirty count"
        Expect.equal heard.Items.Length 1 "a newer commit merged"

        let head =
          heard.Head
          |> Option.defaultWith(fun () -> failwith "the head is unset")

        let request =
          HistoryTab.State.pollRequest(props Following, heard, head)

        Expect.equal request.Arrived [ newer.Id ] "the poll's arrived ids"
        Expect.equal request.Pending [ first.Id ] "the poll's pending ids"
        Expect.equal request.Through [ first.Id ] "the poll's loaded range"

        let cleared, _ = update(HistoryTab.State.Fetched(Ok firstPage), heard)

        Expect.equal cleared.HeardIds [] "the full fetch kept a heard id"
        Expect.equal cleared.NewCount None "the full fetch kept the dot")

    testCase "a late older commit lands between the loaded rows" (fun () ->
      let newer = commitOf secondPage.Page.Items[0]
      let older = commitOf firstPage.Page.Items[0]
      let late = commitOf latePage.Page.Items[0]

      let model = {
        loaded with
            Items = [ LiveCommit newer; LiveCommit older ]
            Head = Some newer.Id
      }

      let next, _ = update(HistoryTab.State.NewChecked(Ok latePage), model)

      Expect.equal
        (next.Items |> List.map(fun node -> (commitOf node).Id))
        [ newer.Id; late.Id; older.Id ]
        "the late commit did not land in place"

      Expect.equal next.HeardIds [] "the late commit was heard"
      Expect.equal next.NewCount (Some 1) "the dirty count")
  ]
