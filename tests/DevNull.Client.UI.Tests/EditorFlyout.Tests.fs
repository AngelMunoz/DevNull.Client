module DevNull.Client.UI.Tests.EditorFlyout

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Headless
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

let clickName(root: Control, name: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(root, name)
    |> Option.iter(fun control ->
      match control with
      | :? Button as button -> click button
      | _ -> ()))

let mountAt1600() : Async<Window * IDisposable> = async {
  let env = TestEnv(signedInStore(), threadApi() :> IHttp)
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

let hoverAuthor(window: Window) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, "AuthorAvatar")
    |> Option.iter(fun control ->
      match control with
      | :? Button as button ->
        let center =
          Point(button.Bounds.Width / 2.0, button.Bounds.Height / 2.0)

        let moved = button.TranslatePoint(center, window)

        if moved.HasValue then
          window.MouseMove moved.Value
      | _ -> ()))

let clickFlyout(window: Window) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, "UserFlyout")
    |> Option.iter(fun control ->
      match control with
      | :? ContentControl as host ->
        match host.Content with
        | :? Button as card -> click card
        | _ -> ()
      | _ -> ()))

let tests =
  testList "editor author flyout" [
    testCaseAsync
      "the thread card author opens the repository tab"
      (async {
        let! window, handle = mountAt1600()

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history card never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! thread =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "A reply.")
          )

        Expect.isTrue thread "the thread tab never opened"

        do! hoverAuthor(window)

        let! flyout =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              visible(root, "UserFlyout")
              && (hasText(root, "Builds things.")
                  || hasText(root, "Reading the profile…"))
          )

        Expect.isTrue flyout "the author flyout never opened"

        do! clickFlyout(window)

        let! repo =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> visible(root, "RepoTab")
          )

        Expect.isTrue repo "the author flyout never opened the repository tab"

        do! close(window, handle)
      })
  ]
