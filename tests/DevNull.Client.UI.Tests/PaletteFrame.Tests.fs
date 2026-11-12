module DevNull.Client.UI.Tests.PaletteFrame

open System
open Avalonia.Controls
open Avalonia.Input
open Avalonia.VisualTree
open Expecto
open DevNull.Client.Core.Http
open DevNull.Client.Layouts.Editor
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

let clickName(root: Control, name: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(root, name)
    |> Option.iter(fun control ->
      match control with
      | :? Button as button -> click button
      | _ -> ()))

let typeQuery(window: Window, text: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, "PaletteQuery")
    |> Option.iter(fun control ->
      match control with
      | :? TextBox as box -> box.Text <- text
      | _ -> ()))

let escapePalette(window: Window) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, "PaletteQuery")
    |> Option.iter(fun control ->
      control.RaiseEvent(
        KeyEventArgs(
          RoutedEvent = InputElement.KeyDownEvent,
          Key = Key.Escape
        )
      )))

let mountAt1600() : Async<Window * IDisposable> = async {
  let env = TestEnv(signedInStore(), searchApi(false, false) :> IHttp)
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
      "E4: the palette lands people and commits at 1600 and opens picked tabs"
      (async {
        let! window, handle = mountAt1600()

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
            fun root -> visible(root, "PaletteQuery")
          )

        Expect.isTrue opened "the header search never opened the palette"

        do! typeQuery(window, "kai kou")

        let! people =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "PalettePerson-kai-kou")
          )

        Expect.isTrue people "the people result never landed"

        let! commits =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              visible(
                root,
                "PaletteCommit-018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
              )
          )

        Expect.isTrue commits "the commit result never landed"

        let! commands =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "PaletteCommand-0")
          )

        Expect.isTrue commands "the command list never rendered"

        let! tabsBefore = probeUiAsync(fun () -> tabCount window)

        do! escapePalette(window)

        let! closed =
          waitForAsync(
            3000,
            (fun () -> window),
            fun root -> not(visible(root, "PaletteQuery"))
          )

        Expect.isTrue closed "Escape never closed the palette"

        let! tabsAfter = probeUiAsync(fun () -> tabCount window)

        Expect.equal tabsAfter tabsBefore "Escape changed the tab strip"

        do! clickName(window, "EditorSearch")

        let! reopened =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "PaletteQuery")
          )

        Expect.isTrue reopened "the palette never reopened"

        do! typeQuery(window, "kai kou")

        let! picked =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "PalettePerson-kai-kou")
          )

        Expect.isTrue picked "the people result never returned"

        do! clickName(window, "PalettePerson-kai-kou")

        let! repo =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> visible(root, "RepoTab")
          )

        Expect.isTrue repo "the person pick never opened the repository tab"

        do! clickName(window, "EditorSearch")

        let! reopenedForCommit =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "PaletteQuery")
          )

        Expect.isTrue
          reopenedForCommit
          "the palette never reopened for a commit"

        do! typeQuery(window, "kai kou")

        let! commit =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              visible(
                root,
                "PaletteCommit-018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
              )
          )

        Expect.isTrue commit "the commit result never returned"

        do!
          clickName(
            window,
            "PaletteCommit-018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
          )

        let! thread =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "REPLIES 3")
          )

        Expect.isTrue thread "the commit pick never opened the thread tab"

        do! close(window, handle)
      })
  ]
