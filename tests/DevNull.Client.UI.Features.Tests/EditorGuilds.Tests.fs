module DevNull.Client.UI.Tests.EditorGuilds

// The guilds feature in the editor: the directory tab's parking
// rules, the board tab's pages and snapshot, and the tab mounts the
// frame opens and closes.

open System
open Expecto
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Editor
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.GuildBoard
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Guilds
open DevNull.Client.UI.Tests.Harness

let private now = DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)

let private zone: DevNull.Client.Formatting.Zone = {
  Info = TimeZoneInfo.Utc
  Label = "UTC"
}

let private session: SignedInSession = {
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

let private guildsProps(env: TestEnv) : GuildsTab.Props<TestEnv> = {
  Env = env
  Session = session
  Zone = zone
  Now = fun () -> now
  OnOpenGuild = ignore
  OnCount = ignore
}

let private guildProps(env: TestEnv) : GuildTab.Props<TestEnv> = {
  Env = env
  Session = session
  Handle = "rust"
  Zone = zone
  Now = fun () -> now
  Refresh = Observable.empty
  OnSnapshot = ignore
  OnOpenThread = ignore
  OnReply = ignore
  OnQuote = ignore
  OnOpenRepo = ignore
  OnShared = ignore
  OnOpenGuild = ignore
  OnStage = ignore
}

let private ask(query: string option, page: int) : GuildsTab.State.Ask = {
  Query = query
  Page = page
}

let private pageOf(rows: Guild list) : GuildsPage = {
  Items = rows
  CurrentPage = 1
  LastPage = 3
  Total = 74
}

let private directoryModel: GuildsTab.State.Model = {
  Term = ""
  Rows = []
  Settled = ValueNone
  Out = ValueSome(ask(None, 1))
  Wanted = ValueNone
  Total = 0
  LastPage = 1
  Refused = false
}

let tests =
  testList "editor guilds" [
    testCase "the directory applies only the waited-for answer" (fun () ->
      let env = TestEnv(signedInStore(), (directoryApi Map.empty :> IHttp))
      let props = guildsProps env

      let stale, _ =
        GuildsTab.State.update(
          props,
          GuildsTab.State.Arrived(ask(Some "x", 1), Ok(pageOf [ devGuild ])),
          directoryModel
        )

      Expect.equal stale.Rows [] "a stale answer filled the rows"
      Expect.equal stale.Settled ValueNone "a stale answer settled"

      let landed, _ =
        GuildsTab.State.update(
          props,
          GuildsTab.State.Arrived(ask(None, 1), Ok(pageOf [ devGuild ])),
          directoryModel
        )

      Expect.equal
        (landed.Rows |> List.map _.Handle)
        [ "dev" ]
        "the waited answer did not land"

      Expect.equal landed.Total 74 "the total did not land"
      Expect.equal landed.LastPage 3 "the last page did not land")

    testCase "the directory parks the newest ask behind the read out" (fun () ->
      let env = TestEnv(signedInStore(), (directoryApi Map.empty :> IHttp))
      let props = guildsProps env

      let parked, _ =
        GuildsTab.State.update(
          props,
          GuildsTab.State.Arrived(ask(None, 1), Ok(pageOf [ devGuild ])),
          {
            directoryModel with
                Wanted = ValueSome(ask(Some "ops", 1))
          }
        )

      Expect.equal
        parked.Out
        (ValueSome(ask(Some "ops", 1)))
        "the parked ask did not go out"

      Expect.equal parked.Wanted ValueNone "the parked ask stayed parked")

    testCase "a 404 refuses the directory and another failure keeps the rows"
    <| fun () ->
      let env = TestEnv(signedInStore(), (directoryApi Map.empty :> IHttp))
      let props = guildsProps env

      let refused, _ =
        GuildsTab.State.update(
          props,
          GuildsTab.State.Arrived(
            ask(None, 1),
            Error(Refused(404, None, "not found"))
          ),
          directoryModel
        )

      Expect.isTrue refused.Refused "the 404 did not refuse the directory"

      let kept, _ =
        GuildsTab.State.update(
          props,
          GuildsTab.State.Arrived(
            ask(None, 1),
            Error(Refused(429, None, "busy"))
          ),
          {
            directoryModel with
                Rows = [ devGuild ]
          }
        )

      Expect.isFalse kept.Refused "a rate limit read as refused"

      Expect.equal
        (kept.Rows |> List.map _.Handle)
        [ "dev" ]
        "the failure dropped the rows"

    testCase "the same search twice asks once" (fun () ->
      let env = TestEnv(signedInStore(), (directoryApi Map.empty :> IHttp))
      let props = guildsProps env

      let settled = {
        directoryModel with
            Term = "dev"
            Settled = ValueSome(ask(Some "dev", 1))
            Out = ValueNone
      }

      let again, _ =
        GuildsTab.State.update(props, GuildsTab.State.SearchAsked, settled)

      Expect.equal again.Out ValueNone "the settled search went out again")

    testCaseAsync "the board tab reads its guild and holds the pin first"
    <| async {
      let env = TestEnv(signedInStore(), (boardApi() :> IHttp))
      let props = guildProps env

      let! board =
        Guilds.show(
          env,
          {
            KeyFile = session.KeyFile
            Handle = "rust"
            Page = 1
          }
        )

      let board =
        match board with
        | Ok answered -> answered
        | Error error -> failwith $"the board never arrived: {describe error}"

      let model, _ = GuildTab.State.init props

      let landed, _ =
        GuildTab.State.update(
          props,
          GuildTab.State.BoardArrived(1, Ok board),
          model
        )

      let snapshot = GuildTab.State.snapshot landed

      Expect.equal
        (snapshot.Guild |> Option.map _.Handle)
        (Some "rust")
        "the snapshot lost the guild"

      Expect.isSome snapshot.FetchedAt "the fetch time stayed empty"

      let ordered = GuildBoard.ordered landed.Board

      Expect.isTrue (ordered |> List.head |> snd) "the pinned row does not lead"

      let more, _ =
        GuildTab.State.update(props, GuildTab.State.BoardMore, landed)

      Expect.equal
        more.Board.Wanted
        (ValueSome 2)
        "the second page was not wanted"
    }

    testCase "a 404 refuses the board and another failure marks it failed"
    <| fun () ->
      let env = TestEnv(signedInStore(), (boardApi() :> IHttp))
      let props = guildProps env
      let model, _ = GuildTab.State.init props

      let refused, _ =
        GuildTab.State.update(
          props,
          GuildTab.State.BoardArrived(1, Error(Refused(404, None, "not found"))),
          model
        )

      Expect.isTrue refused.Board.Refused "the 404 did not refuse the board"
      Expect.isFalse refused.Board.Failed "the 404 read as a plain failure"

      let failed, _ =
        GuildTab.State.update(
          props,
          GuildTab.State.BoardArrived(1, Error(Refused(500, None, "down"))),
          model
        )

      Expect.isTrue failed.Board.Failed "the 500 did not mark the board failed"
      Expect.isFalse failed.Board.Refused "the 500 read as refused"

    testCaseAsync "the directory and board tabs mount and dispose once"
    <| async {
      let! outcome =
        probeUiAsync(fun () ->
          let env = TestEnv(signedInStore(), (boardApi() :> IHttp))

          let directoryBase = GuildsTab.Live.current()
          let boardBase = GuildTab.Live.current()

          let dirEnv =
            TestEnv(signedInStore(), (directoryApi Map.empty :> IHttp))

          let _, directoryHandle =
            GuildsTab.mount(
              (dirEnv :> IProvideDispatcher).Dispatcher,
              guildsProps dirEnv
            )

          let _, boardHandle =
            GuildTab.mount(
              (env :> IProvideDispatcher).Dispatcher,
              guildProps env
            )

          let mounted =
            GuildsTab.Live.current() = directoryBase + 1
            && GuildTab.Live.current() = boardBase + 1

          directoryHandle.Dispose()
          boardHandle.Dispose()
          directoryHandle.Dispose()

          let disposed =
            GuildsTab.Live.current() = directoryBase
            && GuildTab.Live.current() = boardBase

          mounted, disposed)

      let mounted, disposed = outcome

      Expect.isTrue mounted "the tabs did not mount"
      Expect.isTrue disposed "the tabs did not dispose exactly once"
    }
  ]
