module DevNull.Client.UI.Tests.WorkbenchGuilds

// The guilds feature in the workbench: the refs the rail adds, the
// center children they mount, the staging aim the composer opens with,
// and the shared board order both layouts read.

open System
open System.Collections.Generic
open Avalonia.Controls
open Expecto
open FSharp.Control.Reactive
open MVUish.Avalonia

open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.GuildBoard
open DevNull.Client.ImageCache
open DevNull.Client.Workbench
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.App
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Guilds
open DevNull.Client.UI.Tests.Harness

let private now = DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)

let private images = {
  new ImageCacheService with
    member _.Get(_url: string) = async { return None }
}

let private hostProps
  (env: TestEnv, initial: WorkbenchState)
  : WorkbenchHost.Props<TestEnv> = {
  Env = env
  Initial = initial
  Images = images
  Now = Observable.single now
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  OnOpenAuthor = ignore
  OnOpenExternal = ignore
  OnStateChanged = ignore
  OnUseDefaultLayout = ignore
}

let private following =
  WorkbenchSettings.parse "following" |> WorkbenchState.refOf

let private emptyQueueJson =
  """{"data":[],"links":{"first":null,"last":null,"prev":null,"next":null},"meta":{"current_page":1,"from":0,"last_page":1,"path":"","per_page":20,"to":0,"total":0}}"""

/// The api the workbench walk reads: the log, the queue, the guild
/// directory, and the dev and rust boards.
let private guildsWorkbenchApi =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/scheduled-commits" then
      ok emptyQueueJson
    elif request.Target.StartsWith "/api/v1/guilds/dev" then
      ok(boardJsonOf(devGuild, [ commitJson ], 1, None))
    elif request.Target.StartsWith "/api/v1/guilds/rust" then
      ok(boardPageJson([ commitJson ], 1, None))
    elif request.Target.StartsWith "/api/v1/guilds" then
      ok(guildsPageOf([ devGuild ], 1, 1))
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif request.Target.Contains "/replies" then
      ok repliesPageJson
    elif request.Target.StartsWith "/api/v1/commits/" then
      ok threadPageJson
    else
      ok "{}")

let private runEngine
  (
    props: WorkbenchHost.Props<TestEnv>,
    model: WorkbenchHost.Model,
    cmd: Cmd<WorkbenchHost.Msg>
  ) =
  let queue = Queue<WorkbenchHost.Msg>()

  let dispatch(msg: WorkbenchHost.Msg) = queue.Enqueue msg

  let fail(_: string, ex: exn) = failwith ex.Message

  cmd dispatch fail

  let rec loop(model: WorkbenchHost.Model) =
    if queue.Count = 0 then
      model
    else
      let msg = queue.Dequeue()
      let next, nextCmd = WorkbenchHost.update(props, msg, model)
      nextCmd dispatch fail
      loop next

  loop model

let private release(model: WorkbenchHost.Model) =
  model.History |> Option.iter(fun child -> child.Handle.Dispose())
  model.Inspector |> Option.iter(fun child -> child.Handle.Dispose())
  model.Guild |> Option.iter(fun child -> child.Handle.Dispose())

  model.GuildRegistry |> Option.iter(fun child -> child.Handle.Dispose())

let private boardNodes =
  match LogPage.decode logPageJson with
  | Ok page -> page.Page.Items
  | Error failure -> failwith $"the canned log never decoded: {failure.Message}"

let tests =
  testList "workbench guilds" [
    testCase "the board order leads with the pinned commit once"
    <| fun () ->
      let head = boardNodes |> List.head

      let board = {
        GuildBoard.empty with
            Commits = boardNodes
            Pinned = Some head
      }

      let ordered = GuildBoard.ordered board

      Expect.equal (ordered |> List.head) (head, true) "the pinned row leads"

      Expect.equal
        (ordered |> List.filter snd |> List.length)
        1
        "the pinned commit stands once"

      Expect.equal ordered.Length boardNodes.Length "the pin added no row"

    testCase "a board without a pin keeps its rows plain"
    <| fun () ->
      let board = {
        GuildBoard.empty with
            Commits = boardNodes
      }

      Expect.equal
        (GuildBoard.ordered board)
        (boardNodes |> List.map(fun node -> node, false))
        "the plain order"

    testCaseAsync
      "the guild refs mount one center child at a time"
      (async {
        let! (registry, board, backToHistory) =
          probeUiAsync(fun () ->
            let env = TestEnv(signedInStore(), guildsWorkbenchApi :> IHttp)

            let props = hostProps(env, WorkbenchSettings.initial)
            let model, cmd = WorkbenchHost.init props
            let model = runEngine(props, model, cmd)

            let model, cmd =
              WorkbenchHost.update(
                props,
                WorkbenchHost.RefSelected Build.guilds,
                model
              )

            let model = runEngine(props, model, cmd)

            let registry =
              model.GuildRegistry.IsSome
              && model.LiveGuildRegistry = 1
              && model.LiveHistory = 0

            let model, cmd =
              WorkbenchHost.update(
                props,
                WorkbenchHost.RefSelected(Build.guild "rust"),
                model
              )

            let model = runEngine(props, model, cmd)

            let board =
              model.Guild
              |> Option.map _.GuildHandle
              |> Option.defaultValue ""
              |> (=) "rust"
              && model.LiveGuild = 1
              && model.LiveGuildRegistry = 0

            let model, cmd =
              WorkbenchHost.update(
                props,
                WorkbenchHost.RefSelected following,
                model
              )

            let model = runEngine(props, model, cmd)

            let backToHistory =
              model.History.IsSome
              && model.LiveHistory = 1
              && model.LiveGuild = 0

            release model
            registry, board, backToHistory)

        Expect.isTrue registry "the registry did not take the center"
        Expect.isTrue board "the guild board did not take the center"
        Expect.isTrue backToHistory "the history did not return"
      })

    testCaseAsync
      "a stored guild ref restores its board at start"
      (async {
        let! restored =
          probeUiAsync(fun () ->
            let initial = WorkbenchSettings.parse "guild:rust"

            let env = TestEnv(signedInStore(), guildsWorkbenchApi :> IHttp)
            let props = hostProps(env, initial)
            let model, cmd = WorkbenchHost.init props
            let model = runEngine(props, model, cmd)
            release model

            model.Guild |> Option.map _.GuildHandle |> Option.defaultValue "",
            model.LiveGuild)

        let handle, live = restored

        Expect.equal handle "rust" "the stored board"
        Expect.equal live 1 "the board program count"
      })

    testCase "the staging aim follows the open and the stage button"
    <| fun () ->
      let env = TestEnv(signedInStore(), guildsWorkbenchApi :> IHttp)
      let props = hostProps(env, WorkbenchSettings.initial)
      let model, _ = WorkbenchHost.init props

      // A plain ref opens staging without an aim.
      let plain, _ =
        WorkbenchHost.update(props, WorkbenchHost.StagingToggled, model)

      Expect.isTrue plain.StagingOpen "the staging area did not open"

      Expect.isNone plain.Staging.Aim "a plain ref aimed the composer"

      // A guild ref with its reported row aims the composer.
      let guilded = {
        model with
            Selected = Build.guild "rust"
            GuildRow = Some boardGuild
      }

      let aimed, _ =
        WorkbenchHost.update(props, WorkbenchHost.StagingToggled, guilded)

      Expect.equal aimed.Staging.Aim (Some "rust") "the aim did not land"

      // A paused board never aims: the post would be refused.
      let paused = {
        guilded with
            GuildRow = Some { boardGuild with IsPaused = true }
      }

      let calm, _ =
        WorkbenchHost.update(props, WorkbenchHost.StagingToggled, paused)

      Expect.isNone calm.Staging.Aim "a paused board aimed the composer"

      // The stage button re-aims while staging stands open.
      let staged, _ =
        WorkbenchHost.update(props, WorkbenchHost.StageToGuild "rust", plain)

      Expect.isTrue staged.StagingOpen "the stage button did not open staging"

      Expect.equal
        staged.Staging.Aim
        (Some "rust")
        "the stage button did not aim"

    testCaseAsync
      "the rail reaches the registry and a board with staging aimed"
      (async {
        let api = guildsWorkbenchApi

        let! view, mounted =
          probeUiAsync(fun () ->
            let env = TestEnv(signedInStore(), api :> IHttp)

            let props = hostProps(env, WorkbenchSettings.initial)
            let view, handle = WorkbenchHost.mount(Harness.ui, props)
            view, handle :?> WorkbenchHost.MountedHost)

        let! window = mountAsync view

        // The rail's Guilds row opens the registry index.
        let! railReady =
          waitForAsync(
            8000,
            (fun () -> buttonWithText(window, "Guilds")),
            Option.isSome
          )

        Expect.isTrue railReady "the Guilds row never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Guilds") |> Option.iter click)

        let! listed =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              hasText(root, "GUILD REGISTRY · 74 guilds · page 1/1")
              && hasText(root, "Dev Guild")
              && hasText(root, "#dev")
          )

        Expect.isTrue listed "the registry never listed the directory"

        // A registry row opens the guild's board as the center log.
        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Dev Guild") |> Option.iter click)

        let! boarded =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              hasText(root, "pinned to board")
              && hasText(root, "It was DNS.")
              && hasText(root, "STAGE → #dev")
          )

        Expect.isTrue boarded "the board never landed"

        // The stage button opens the composer aimed at the board.
        do!
          onUiAsync(fun () ->
            controlByName(window, "GuildBoardStage")
            |> Option.iter(function
              | :? Button as button -> click button
              | _ -> ()))

        let! staged =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              hasText(root, "STAGING → #dev: no draft · 0 scheduled")
              && hasText(root, "Posting to #dev")
          )

        Expect.isTrue staged "the aimed composer never opened"

        do! onUiAsync(fun () -> (mounted :> IDisposable).Dispose())
      })
  ]
