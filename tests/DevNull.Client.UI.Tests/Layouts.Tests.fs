module DevNull.Client.UI.Tests.LayoutHost

// The layout host's switch checks: the stored choice round-trips, the
// default layout maps the window intents to routes, a switch remounts
// the slot and rebinds the key tunnel, and twenty switch rounds retain
// no root the slot dropped.

open System
open System.Reactive.Subjects
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open NXUI.Extensions
open Expecto
open DevNull.Client.Core.Http
open DevNull.Client.Layouts
open DevNull.Client.RouteStore
open DevNull.Client.SettingsStore
open DevNull.Client.Shell
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness
open DevNull.Client.UI.Tests.Leaks

/// Presses one bare key through the routed event.
let pressKey(source: Interactive, key: Key) : unit =
  source.RaiseEvent(
    KeyEventArgs(RoutedEvent = InputElement.KeyDownEvent, Key = key)
  )

/// The test layout: one focusable root with its name on it, every
/// action and intent recorded, and its dispose counted in the same
/// tape.
let testLayout(tape: ResizeArray<string>) : ILayout =
  let root =
    Border()
      .Name("TestRoot")
      .Focusable(true)
      .Child(TextBlock().Text("Test layout"))

  {
    new ILayout with
      member _.Name = "test"
      member _.Root = root

      member _.Actions = {
        Compose = fun () -> tape.Add "compose"
        Search = fun () -> tape.Add "search"
        Help = fun () -> tape.Add "help"
        Escape = fun () -> tape.Add "escape"
        Navigate = fun url -> tape.Add("navigate:" + url)
        ToggleFullScreen = fun () -> tape.Add "fullscreen"
        WalkCards = fun step -> tape.Add("walk:" + string step)
      }

      member _.OverlayOpen() = false
      member _.Navigate(url) = tape.Add("navigate:" + url)
      member _.Narrow = new BehaviorSubject<bool>(false)
      member _.ToggleDrawer() = tape.Add "drawer"

      member _.Handle(intent) = tape.Add("intent:" + string intent)

    interface IDisposable with
      member _.Dispose() = tape.Add "disposed"
  }

/// Registers the test layout on one shell.
let registerTest(window: Window, tape: ResizeArray<string>) : unit =
  (window :?> Shell<TestEnv>)
    .RegisterLayout(
      {
        Name = "test"
        Mount = fun _ -> testLayout tape
      }
    )

let tests =
  testList "layout host" [
    testCase
      "the layout choice round-trips through the settings store"
      (fun () ->
        let saves = ResizeArray<AppSettings>()

        let store = AppSettingsStore.create(None, saves.Add)

        Expect.equal
          store.Current.Layout
          CurrentLayout
          "a fresh store started on another layout"

        store.SetLayout CurrentLayout

        Expect.equal saves.Count 1 "the setter never wrote"

        Expect.equal saves[0].Layout CurrentLayout "the write lost the choice"

        // The restart: a store born from the saved settings keeps the
        // choice the window mounts on open.
        let reopened = AppSettingsStore.create(Some saves[0], ignore)

        Expect.equal
          reopened.Current.Layout
          CurrentLayout
          "a restart lost the choice"

        Expect.equal (layoutString CurrentLayout) "current" "the stored word"

        Expect.isSome (parseLayout "current") "the registry word did not parse"

        Expect.isNone (parseLayout "workbench") "an unknown word parsed")

    testCaseAsync
      "the default layout maps the window intents to routes"
      (async {
        let env = TestEnv(signedInStore(), logApi() :> IHttp)

        let! layout =
          probeUiAsync(fun () ->
            let surface = {
              new IHostSurface with
                member _.Window = Window()

                member _.Active = new BehaviorSubject<bool>(true)

                member _.Switch _ = ()
            }

            DevNull.Client.Layouts.builtIn(env, TimeSpan.FromSeconds 30.0)
            |> List.find(fun entry -> entry.Name = layoutString CurrentLayout)
            |> fun entry -> entry.Mount surface)

        let landed(expected: string) : Async<bool> =
          waitForAsync(
            5000,
            (fun () -> (env :> IProvideRoutes).Routes.Current),
            (fun path -> path = expected)
          )

        do! onUiAsync(fun () -> layout.Handle LayoutIntent.OpenNotifications)

        let! notifications = landed "/notifications"

        Expect.isTrue notifications "the notifications intent never landed"

        do! onUiAsync(fun () -> layout.Handle LayoutIntent.OpenSaved)

        let! saved = landed "/saved"

        Expect.isTrue saved "the saved intent never landed"

        // The home intent returns to the log — a real move after the
        // two pages above, so the row reads the mapping.
        do! onUiAsync(fun () -> layout.Handle LayoutIntent.OpenHome)

        let! home = landed "/log/latest"

        Expect.isTrue home "the home intent never landed"

        do! onUiAsync(fun () -> (layout :> IDisposable).Dispose())
      })

    testCaseAsync
      "a switch mounts the new layout and rebinds the key tunnel"
      (async {
        let tape = ResizeArray<string>()
        let! window = Leaks.startShell(signedInStore(), logApi() :> IHttp)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "The Octocat"))
          )

        Expect.isTrue shown "the log never rendered"

        do! onUiAsync(fun () -> registerTest(window, tape))
        do! onUiAsync(fun () -> (window :?> Shell<TestEnv>).SwitchLayout "test")

        let! mounted =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Test layout"))
          )

        Expect.isTrue mounted "the test layout never mounted"

        // Gap H-G: focus lands on the new root, so no key press lands
        // in a dead control.
        let! focused =
          probeUiAsync(fun () ->
            match window.FocusManager.GetFocusedElement() with
            | :? Control as control -> control.Name
            | _ -> "")

        Expect.equal focused "TestRoot" "the new root never took focus"

        // The tunnel reaches the new layout only: the key records
        // there, and the retired layout's dialog stays gone.
        do! onUiAsync(fun () -> pressKey(window, Key.C))

        let! reached =
          waitForAsync(2000, (fun () -> tape.Contains "compose"), id)

        Expect.isTrue reached "the key never reached the new layout"

        let! dialogGone =
          probeUiAsync(fun () -> not(hasText(window, "Write a commit")))

        Expect.isTrue dialogGone "the retired layout answered the key"

        // Back on the default: the feed renders again and the same key
        // opens its own dialog.
        do!
          onUiAsync(fun () ->
            (window :?> Shell<TestEnv>).SwitchLayout "current")

        let! back =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "The Octocat"))
          )

        Expect.isTrue back "the default layout never remounted"

        do! onUiAsync(fun () -> pressKey(window, Key.C))

        let! compose =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Write a commit"))
          )

        Expect.isTrue compose "the key never reached the default again"
      })

    testCaseAsync
      "twenty layout switches retain nothing"
      (async {
        let tape = ResizeArray<string>()
        let testRoots = ResizeArray<WeakReference>()

        let! window = Leaks.startShell(signedInStore(), soakApi 40)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Card 1."))
          )

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            (window :?> Shell<TestEnv>)
              .RegisterLayout(
                {
                  Name = "test"
                  Mount =
                    fun _ ->
                      let layout = testLayout tape
                      testRoots.Add(WeakReference layout.Root)
                      layout
                }
              ))

        for _ in 1..20 do
          do!
            onUiAsync(fun () -> (window :?> Shell<TestEnv>).SwitchLayout "test")

          let! testMounted =
            waitForAsync(
              5000,
              (fun () -> window),
              (fun root -> hasText(root, "Test layout"))
            )

          Expect.isTrue testMounted "the test layout never mounted"

          do!
            onUiAsync(fun () ->
              (window :?> Shell<TestEnv>).SwitchLayout "current")

          let! defaultMounted =
            waitForAsync(
              5000,
              (fun () -> window),
              (fun root -> hasText(root, "Card 1."))
            )

          Expect.isTrue defaultMounted "the default never remounted"

        do! settleAsync()
        collect()
        do! settleAsync()
        collect()

        let disposed =
          tape |> Seq.filter(fun entry -> entry = "disposed") |> Seq.length

        // Every unmounted layout is asked to let go once, and the
        // test layouts' controls — the ones this switch loop mounts
        // and drops — release. The arrangement's own deep release is
        // tracked as its own fix: its pages ride interval timers that
        // outlive their programs, and Avalonia bindings keep their
        // sources after a detach.
        Expect.equal disposed 20 "an unmounted test layout was not disposed"

        let retained = testRoots |> Seq.filter(fun weak -> weak.IsAlive)

        Expect.equal
          (Seq.length retained)
          0
          "a switched-out test layout root stayed reachable"
      })
  ]
