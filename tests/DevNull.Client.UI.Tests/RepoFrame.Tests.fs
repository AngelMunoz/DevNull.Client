module DevNull.Client.UI.Tests.RepoFrame

open System
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive
open DevNull.Client.Core.Http
open DevNull.Client.Editor
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.EditorFrame
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let controlByName(root: Control, name: string) =
  root.GetVisualDescendants()
  |> Seq.tryFind(fun visual -> visual.Name = name)
  |> Option.map(fun visual -> visual :?> Control)

let visible(root: Control, name: string) =
  controlByName(root, name)
  |> Option.exists(fun control -> control.IsEffectivelyVisible)

let textOf(root: Control, name: string) : string =
  controlByName(root, name)
  |> Option.bind(fun control ->
    match control with
    | :? TextBlock as block -> Some block.Text
    | :? Button as button ->
      match button.Content with
      | :? string as text -> Some text
      | _ -> None
    | _ -> None)
  |> Option.defaultValue ""

let clickName(root: Control, name: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(root, name)
    |> Option.iter(fun control ->
      match control with
      | :? Button as button -> click button
      | _ -> ()))

let pinnedStatsJson =
  profileWithStatsJson.Replace(
    "\"pinned_commit\":null",
    sprintf "\"pinned_commit\":%s" commitJson
  )

let profileCommits =
  sprintf
    """{"data":[%s],"links":{},"meta":{"per_page":20,"next_cursor":"next","prev_cursor":null}}"""
    commitJson

let followersPage =
  sprintf
    """{"data":[%s],"links":{},"meta":{"per_page":20,"next_cursor":null,"prev_cursor":null}}"""
    apiUserJson

let repoApi =
  FakeApi(fun request ->
    if request.Target = "/api/v1/users/octocat" then
      ok pinnedStatsJson
    elif request.Target.StartsWith "/api/v1/users/octocat/commits" then
      ok profileCommits
    elif request.Target.StartsWith "/api/v1/users/octocat/followers" then
      ok followersPage
    elif request.Target.StartsWith "/api/v1/users/octocat/following" then
      ok followersPage
    elif request.Target.StartsWith "/api/v1/users/octocat/follow" then
      ok "{}"
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif request.Target.StartsWith "/api/v1/scheduled-commits" then
      ok "{}"
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

let mountAt1600() : Async<Window * IDisposable> = async {
  let env = TestEnv(signedInStore(), repoApi :> IHttp)
  let props = EditorFrame.props env

  return!
    probeUiAsync(fun () ->
      let control, handle = EditorRoot.mount(ui, props)

      let window = Window(Width = 1600.0, Height = 900.0, Content = control)

      window.Show()
      window, handle)
}

let close(window: Window, handle: IDisposable) : Async<unit> =
  onUiAsync(fun () ->
    window.Close()
    handle.Dispose())

let tests =
  testList "editor repository tab headless" [
    testCaseAsync
      "E5: the repository tab takes the pinned layout at 1600"
      (async {
        let! window, handle = mountAt1600()

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"

        do! clickName(window, "EditorActivity-Account")

        let! opened =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> visible(root, "RepoTab")
          )

        Expect.isTrue opened "the account icon never opened the repository tab"

        do! clickName(window, "EditorExplorerViewer")

        let! tabs = probeUiAsync(fun () -> tabCount window)
        Expect.equal tabs 2 "the explorer entry made a second repository tab"

        Expect.equal
          (RepoTab.Live.current())
          1
          "the two entries mounted two repository programs"

        let! readme =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> visible(root, "RepoReadme")
          )

        Expect.isTrue readme "the pinned README never rendered"

        let! badge =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> visible(root, "RepoBadge-b1")
          )

        Expect.isTrue badge "the shield row never rendered"

        let! scale =
          waitForAsync(
            8000,
            (fun () -> textOf(window, "RepoActivityScale")),
            fun text -> text = "activity · week"
          )

        Expect.isTrue scale "the server's scale word never rendered"

        Expect.isFalse
          (visible(window, "RepoFollow"))
          "the viewer's own repository offered a follow control"

        do! clickName(window, "RepoFollowers")

        let! list =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "RepoListClose")
          )

        Expect.isTrue list "the followers list never opened inside the tab"

        do! clickName(window, "RepoListClose")

        let! closed =
          waitForAsync(
            3000,
            (fun () -> window),
            fun root -> not(visible(root, "RepoListClose"))
          )

        Expect.isTrue closed "the followers list never closed"

        do! close(window, handle)

        Expect.equal
          (RepoTab.Live.current())
          0
          "the repository program outlived its host"
      })
  ]
