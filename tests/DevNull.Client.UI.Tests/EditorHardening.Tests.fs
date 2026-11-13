module DevNull.Client.UI.Tests.EditorHardening

open System
open System.Reactive.Subjects
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive
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

let collect() : unit =
  for _ in 1..2 do
    GC.Collect()
    GC.WaitForPendingFinalizers()

  GC.Collect()

let logCount(api: FakeApi) =
  api.Requests
  |> List.filter(fun request ->
    request.Method = "GET" && request.Target.StartsWith "/api/v1/log/")
  |> List.length

let searchCount(api: FakeApi) =
  api.Requests
  |> List.filter(fun request -> request.Target.StartsWith "/api/v1/search/")
  |> List.length

let userJson(handle: string) =
  apiUserJson
    .Replace("\"handle\":\"octocat\"", $"\"handle\":\"{handle}\"")
    .Replace("\"name\":\"The Octocat\"", $"\"name\":\"{handle}\"")

let churnApi() =
  let users = [ "ana"; "bo"; "cy" ] |> List.map userJson |> String.concat ","

  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/search/profiles" then
      ok(sprintf "{\"users\":[%s],\"organizations\":[]}" users)
    elif request.Target.StartsWith "/api/v1/search/commits" then
      ok """{"data":[]}"""
    elif
      request.Target.StartsWith "/api/v1/users/"
      && request.Target.Contains "/commits"
    then
      ok logPageJson
    elif request.Target.StartsWith "/api/v1/users/" then
      ok profileJson
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif request.Target.Contains "/replies" then
      ok
        "{\"data\":[],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
    elif request.Target.StartsWith "/api/v1/commits/" then
      ok threadPageJson
    elif request.Target.StartsWith "/api/v1/scheduled-commits" then
      ok "{}"
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

let tests =
  testList "editor hardening" [
    testCaseAsync
      "three fake minutes poll once a minute while active and never search"
      (async {
        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/log/" then
              ok logPageJson
            elif request.Target.StartsWith "/api/v1/scheduled-commits" then
              ok "{}"
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let env = TestEnv(signedInStore(), api :> IHttp)
        let active = new BehaviorSubject<bool>(true)

        let props = {
          EditorFrame.props env with
              Active = active
        }

        let! window, handle =
          probeUiAsync(fun () ->
            let control, handle = EditorRoot.mount(ui, props)

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

        Expect.isTrue shown "the history never rendered"

        do! settleAsync()

        let before = logCount api
        Expect.isGreaterThan before 0 "the first page never read"

        let! polled =
          waitForAsync(
            7000,
            (fun () -> logCount api),
            fun count -> count >= before + 3
          )

        Expect.isTrue polled "three fake minutes never polled"

        Expect.equal (searchCount api) 0 "a search ran before a query"

        do! onUiAsync(fun () -> active.OnNext false)
        do! Async.Sleep 2200
        do! settleAsync()

        let idle = logCount api
        Expect.equal idle (before + 3) "an inactive stream kept polling"

        do! onUiAsync(fun () -> active.OnNext true)

        let! resumed =
          waitForAsync(
            4000,
            (fun () -> logCount api),
            fun count -> count = idle + 1
          )

        Expect.isTrue resumed "an active stream did not resume polling"
        Expect.equal (searchCount api) 0 "a search polled without a query"

        do!
          onUiAsync(fun () ->
            window.Close()
            handle.Dispose())
      })

    testCaseAsync
      "mountFor sends the terminal open and Escape closes it"
      (async {
        let env = TestEnv(signedInStore(), threadApi() :> IHttp)

        let! window, mounted =
          probeUiAsync(fun () ->
            let mounted = EditorRoot.mountFor(ui, EditorFrame.props env)

            let window =
              Window(Width = 1600.0, Height = 900.0, Content = mounted.Root)

            window.Show()
            window, mounted)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"
        Expect.isFalse (mounted.OverlayOpen()) "an overlay stood at rest"

        do! onUiAsync(fun () -> mounted.Send EditorRoot.OpenTerminal)

        let! panel =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "EditorPanel")
          )

        Expect.isTrue panel "the terminal panel never opened"

        Expect.isFalse
          (mounted.OverlayOpen())
          "the docked panel masked the host keys"

        let hostHits = ResizeArray<string>()

        do!
          onUiAsync(fun () ->
            DevNull.Client.Shortcuts.install {
              Root = window
              Actions = {
                Compose = fun () -> hostHits.Add "compose"
                Search = fun () -> ()
                Help = fun () -> hostHits.Add "help"
                Escape = fun () -> ()
                Navigate = fun _ -> ()
                ToggleFullScreen = fun () -> ()
                WalkCards = fun _ -> ()
              }
              OverlayOpen = mounted.OverlayOpen
            }

            window.RaiseEvent(
              KeyEventArgs(
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.OemQuestion
              )
            )

            window.RaiseEvent(
              KeyEventArgs(
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.C
              )
            ))

        Expect.sequenceEqual
          hostHits
          [ "help"; "compose" ]
          "the docked panel swallowed host keys"

        do! onUiAsync(fun () -> mounted.Send EditorRoot.OpenPalette)

        let! palette =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(root, "PaletteQuery")
          )

        Expect.isTrue palette "the palette never opened over the panel"

        do! onUiAsync(fun () -> mounted.Send EditorRoot.Escape)

        let! paletteClosed =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(visible(root, "PaletteQuery"))
          )

        Expect.isTrue paletteClosed "Escape did not close the palette first"

        Expect.isFalse
          (mounted.OverlayOpen())
          "the panel still counted as an overlay"

        do! onUiAsync(fun () -> mounted.Send EditorRoot.Escape)

        let! closed =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(visible(root, "EditorPanel"))
          )

        Expect.isTrue closed "Escape did not close the panel"
        Expect.isFalse (mounted.OverlayOpen()) "the overlay probe stayed true"

        do!
          onUiAsync(fun () ->
            window.Close()
            (mounted :> IDisposable).Dispose())
      })

    testCaseAsync
      "the default layout affordance is the status strip's way back"
      (async {
        let api = threadApi()
        let env = TestEnv(signedInStore(), api :> IHttp)
        let asked = ref false

        let props = {
          EditorFrame.props env with
              OnUseDefaultLayout = fun () -> asked.Value <- true
        }

        let! window, handle =
          probeUiAsync(fun () ->
            let control, handle = EditorRoot.mount(ui, props)

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

        Expect.isTrue shown "the history never rendered"

        let! queueRow =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> visible(window, "ExplorerOpen-QUEUE")
          )

        Expect.isTrue queueRow "the queue row never appeared"

        do! clickName(window, "EditorDefaultLayout")
        Expect.isTrue asked.Value "the default layout affordance did nothing"

        let! privateHelp =
          probeUiAsync(fun () ->
            visible(window, "EditorHelp")
            || visible(window, "EditorHelpOverlay"))

        Expect.isFalse privateHelp "the editor kept its own help overlay"

        do!
          onUiAsync(fun () ->
            window.Close()
            handle.Dispose())
      })

    testCaseAsync
      "the frame churn drops its tab programs and root"
      (async {
        let api = churnApi()
        let env = TestEnv(signedInStore(), api :> IHttp)

        let! window, handleRef, weak =
          probeUiAsync(fun () ->
            let control, handle = EditorRoot.mount(ui, EditorFrame.props env)

            let window =
              Window(Width = 1600.0, Height = 900.0, Content = control)

            window.Show()
            window, ref(Some handle), WeakReference(control))

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the history never rendered"

        for _ in 1..20 do
          do!
            onUiAsync(fun () ->
              buttonWithText(window, "It was DNS.") |> Option.iter click)

          let! opened =
            waitForAsync(
              5000,
              (fun () -> ThreadTab.Live.current()),
              fun count -> count = 1
            )

          Expect.isTrue opened "the thread program never started"

          let commitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

          do!
            onUiAsync(fun () ->
              controlByName(window, $"EditorTabClose-{commitId}")
              |> Option.iter(fun control -> click(control :?> Button)))

          let! closed =
            waitForAsync(
              5000,
              (fun () -> ThreadTab.Live.current()),
              fun count -> count = 0
            )

          Expect.isTrue closed "the thread program outlived its tab"
          do! settleAsync()

        let handles = [ "ana"; "bo"; "cy" ]

        for handle in handles do
          do! clickName(window, "EditorSearch")

          let! opened =
            waitForAsync(5000, (fun () -> visible(window, "PaletteQuery")), id)

          Expect.isTrue opened "the palette never opened"

          do! typeQuery(window, "three")

          let! row =
            waitForAsync(
              5000,
              (fun () -> visible(window, $"PalettePerson-{handle}")),
              id
            )

          Expect.isTrue row "the repository result never landed"

          do! clickName(window, $"PalettePerson-{handle}")

          let! repo =
            waitForAsync(
              8000,
              (fun () -> RepoTab.Live.current()),
              fun count -> count = List.findIndex ((=) handle) handles + 1
            )

          Expect.isTrue repo "the repository program never started"

        Expect.equal (ThreadTab.Live.current()) 0 "a thread program remained"
        Expect.equal (RepoTab.Live.current()) 3 "a repository program remained"

        do! onUiAsync(fun () -> window.Content <- Panel())
        do! settleAsync()

        do!
          onUiAsync(fun () ->
            window.Close()

            handleRef.Value |> Option.iter(fun handle -> handle.Dispose())

            handleRef.Value <- None)

        collect()

        Expect.equal (ThreadTab.Live.current()) 0 "the thread programs survived"

        Expect.equal
          (RepoTab.Live.current())
          0
          "the repository programs survived"

        Expect.isFalse weak.IsAlive "the frame outlived its mount"
      })
  ]
