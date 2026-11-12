module DevNull.Client.UI.Tests.HistoryTab

open System
open FSharp.Control.Reactive
open Expecto
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.ImageCache
open DevNull.Client.Layouts.Editor
open DevNull.Client.Stores
open DevNull.Client.UiClock
open DevNull.Client.UI.Tests.Fixtures

/// The workers one pure update needs: fixed values and no transport.
type FakeEnv() =

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
  OnSnapshot = ignore
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
  ]
