module DevNull.Client.UI.Tests.EditorChurn

open System
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open DevNull.Client
open DevNull.Client.Core.Http
open DevNull.Client.Layouts.Editor
open DevNull.Client.UI.Tests.EditorFrame
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let commitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

let controlByName(root: Control, name: string) =
  root.GetVisualDescendants()
  |> Seq.tryFind(fun visual -> visual.Name = name)
  |> Option.map(fun visual -> visual :?> Control)

let tabCount(window: Window) =
  window.GetVisualDescendants()
  |> Seq.filter(fun visual ->
    match visual with
    | :? Button as button ->
      button.Classes.Contains(Theme.name Theme.TabButton)
    | _ -> false)
  |> Seq.length

let liveCount() = ThreadTab.Live.current()

let tests =
  testList "editor tab churn" [
    testCaseAsync
      "open and close 20 thread tabs keeps one live program per open tab"
      (async {
        let api = threadApi()
        let env = TestEnv(signedInStore(), api :> IHttp)

        let! window, handle =
          probeUiAsync(fun () ->
            let control, handle = EditorRoot.mount(ui, props env)

            let window =
              Window(Width = 1600.0, Height = 900.0, Content = control)

            window.Show()
            window, handle)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history card never rendered"

        let before = liveCount()

        for _ in 1..20 do
          do!
            onUiAsync(fun () ->
              buttonWithText(window, "It was DNS.") |> Option.iter click)

          let! opened =
            waitForAsync(
              5000,
              (fun () -> liveCount()),
              fun count -> count = before + 1
            )

          Expect.isTrue opened "the thread program never started"

          let! close =
            waitForAsync(
              5000,
              (fun () -> controlByName(window, $"EditorTabClose-{commitId}")),
              Option.isSome
            )

          Expect.isTrue close "the thread tab carried no close control"

          do!
            onUiAsync(fun () ->
              controlByName(window, $"EditorTabClose-{commitId}")
              |> Option.iter(fun control -> click(control :?> Button)))

          let! closed =
            waitForAsync(
              5000,
              (fun () -> liveCount()),
              fun count -> count = before
            )

          Expect.isTrue closed "the thread program outlived its tab"

          do! settleAsync()

        let! tabs = probeUiAsync(fun () -> tabCount window)

        Expect.equal tabs 1 "the pinned tab alone remained"

        do!
          onUiAsync(fun () ->
            window.Close()
            handle.Dispose())
      })

    testCaseAsync
      "opening the same commit twice keeps one tab and one program"
      (async {
        let api = threadApi()
        let env = TestEnv(signedInStore(), api :> IHttp)

        let! window, handle =
          probeUiAsync(fun () ->
            let control, handle = EditorRoot.mount(ui, props env)

            let window =
              Window(Width = 1600.0, Height = 900.0, Content = control)

            window.Show()
            window, handle)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history card never rendered"

        let before = liveCount()

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        let! opened =
          waitForAsync(
            5000,
            (fun () -> liveCount()),
            fun count -> count = before + 1
          )

        Expect.isTrue opened "the thread program never started"

        do!
          onUiAsync(fun () ->
            controlByName(window, "EditorTab-history")
            |> Option.iter(fun control -> click(control :?> Button)))

        do! settleAsync()

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.") |> Option.iter click)

        do! settleAsync()

        let! live, tabs = probeUiAsync(fun () -> liveCount(), tabCount window)

        Expect.equal live (before + 1) "the second open started a program"
        Expect.equal tabs 2 "the second open doubled the tab"

        do!
          onUiAsync(fun () ->
            controlByName(window, $"EditorTabClose-{commitId}")
            |> Option.iter(fun control -> click(control :?> Button)))

        let! closed =
          waitForAsync(
            5000,
            (fun () -> liveCount()),
            fun count -> count = before
          )

        Expect.isTrue closed "the thread program outlived its tab"

        do!
          onUiAsync(fun () ->
            window.Close()
            handle.Dispose())
      })
  ]
