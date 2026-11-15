module DevNull.Client.UI.Tests.PaletteFrame

open System
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.VisualTree
open Expecto
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

let rowHasText(root: Control, name: string, text: string) =
  controlByName(root, name)
  |> Option.exists(fun row ->
    row.GetVisualDescendants()
    |> Seq.choose(fun visual ->
      match visual with
      | :? TextBlock as block -> block.Text |> Option.ofObj
      | _ -> None)
    |> Seq.exists(fun value -> value.Contains text))

let clickName(root: Control, name: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(root, name)
    |> Option.iter(fun control ->
      match control with
      | :? Button as button -> click button
      | _ -> ()))

let typeQuery(window: Window, text: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, "SearchInput")
    |> Option.iter(fun control ->
      match control with
      | :? TextBox as box -> box.Text <- text
      | _ -> ()))

let pressEnter(window: Window) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, "SearchInput")
    |> Option.iter(fun control ->
      control.RaiseEvent(
        KeyEventArgs(RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter)
      )))



let mountAt1600(api: FakeApi) : Async<Window * IDisposable> = async {
  let env = TestEnv(signedInStore(), api :> IHttp)
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
  testList "editor palette headless" [
    testCaseAsync
      "E4: the dialog renders and maps enter to a repo or thread"
      (async {
        let api = searchApi(false, false)
        let! window, handle = mountAt1600(api)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"

        do! clickName(window, "EditorSearch")

        let! opened =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "SearchInput")
          )

        Expect.isTrue opened "the header search never opened the web dialog"

        let! commands =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "PaletteCommand-0")
          )

        Expect.isTrue commands "the command strip never rendered"

        do! typeQuery(window, "kai")

        let! peopleSearched =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            fun requests ->
              requests
              |> List.exists(fun request ->
                request.Target.StartsWith "/api/v1/search/profiles")
          )

        Expect.isTrue peopleSearched "the people search never ran"

        let! person =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "SearchRow0")
          )

        Expect.isTrue person "the people result never landed"

        do! pressEnter(window)

        let! repo =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> visible(root, "RepoTab")
          )

        Expect.isTrue repo "enter on a person never opened the repository tab"

        do! clickName(window, "EditorSearch")

        let! reopened =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "SearchInput")
          )

        Expect.isTrue reopened "the search dialog never reopened"

        do! typeQuery(window, "kai kou")

        let! commitsSearched =
          waitForAsync(
            5000,
            (fun () -> api.Requests),
            fun requests ->
              requests
              |> List.exists(fun request ->
                request.Target.StartsWith "/api/v1/search/commits")
          )

        Expect.isTrue commitsSearched "the commit search never ran"

        Expect.isTrue
          (api.Requests
           |> List.exists(fun request ->
             request.Target.StartsWith "/api/v1/search/commits"
             && request.Target.Contains "limit=5"))
          "the hosted dialog did not use its web limit"

        let! commit =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> rowHasText(root, "SearchRow0", "It was DNS.")
          )

        Expect.isTrue commit "the commit result never landed"

        do! pressEnter(window)

        let! thread =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "REPLIES 3")
          )

        Expect.isTrue thread "enter on a commit never opened the thread tab"

        do! close(window, handle)
      })

    testCaseAsync
      "see all opens a search results tab and closes the dialog"
      (async {
        let api = searchApi(false, false)
        let! window, handle = mountAt1600(api)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"

        do! clickName(window, "EditorSearch")

        let! opened =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "SearchInput")
          )

        Expect.isTrue opened "the header search never opened the dialog"

        do! typeQuery(window, "kai kou")

        let! seeAll =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> visible(root, "SearchRow0")
          )

        Expect.isTrue seeAll "the search rows never rendered"

        do! pressEnter(window)

        let! tab =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              visible(root, "EditorTab-search:kai kou")
              && visible(root, "EditorSearchTab-kai kou")
              && rowHasText(root, "EditorTab-search:kai kou", "search: kai kou")
          )

        Expect.isTrue tab "see all never opened the search results tab"

        let! dialogClosed =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(visible(root, "SearchInput"))
          )

        Expect.isTrue dialogClosed "the dialog stayed over the search tab"

        let! person =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> visible(root, "SearchPerson-kai-kou")
          )

        Expect.isTrue person "the people result never reached the search tab"

        let! commit =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue commit "the commit result never reached the search tab"

        Expect.isTrue
          (api.Requests
           |> List.exists(fun request ->
             request.Target.StartsWith "/api/v1/search/commits"
             && request.Target.Contains "limit=20"))
          "the search tab did not ask for its own page"

        do! clickName(window, "EditorTabClose-search:kai kou")

        let! closed =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(visible(root, "EditorSearchTab-kai kou"))
          )

        Expect.isTrue closed "the search results tab did not close"

        do! close(window, handle)
      })
  ]
