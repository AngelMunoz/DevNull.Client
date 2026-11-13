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
open Avalonia.VisualTree
open NXUI.Extensions
open Expecto
open DevNull.Client.Core.Http
open DevNull.Client.Layouts
open DevNull.Client.RouteStore
open DevNull.Client.SettingsStore
open DevNull.Client.Shell
open DevNull.Client.Shortcuts
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness
open DevNull.Client.UI.Tests.Leaks

/// Presses one bare key through the routed event.
let pressKey(source: Interactive, key: Key) : unit =
  source.RaiseEvent(
    KeyEventArgs(RoutedEvent = InputElement.KeyDownEvent, Key = key)
  )

/// Presses one key with modifiers through the routed event.
let pressKeyWith
  (source: Interactive, key: Key, modifiers: KeyModifiers)
  : unit =
  source.RaiseEvent(
    KeyEventArgs(
      RoutedEvent = InputElement.KeyDownEvent,
      Key = key,
      KeyModifiers = modifiers
    )
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
        Escape = fun () -> tape.Add "escape"
        Navigate = fun url -> tape.Add("navigate:" + url)
        ToggleFullScreen = fun () -> tape.Add "fullscreen"
        WalkCards = fun step -> tape.Add("walk:" + string step)
      }

      member _.Cheatsheet = [
        {
          Keys = [ "T" ]
          Words = "test the layout"
        }
      ]

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

        Expect.isSome (parseLayout "workbench") "the workbench word parsed"

        Expect.equal
          (layoutString WorkbenchLayout)
          "workbench"
          "the stored word")

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

        // The host cheatsheet reads the default layout's rows.
        do! onUiAsync(fun () -> pressKey(window, Key.OemQuestion))

        let! defaultKeys =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Show this list"))
          )

        Expect.isTrue defaultKeys "the default cheatsheet never showed"

        do! onUiAsync(fun () -> pressKey(window, Key.Escape))

        let! defaultClosed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Keyboard shortcuts")))
          )

        Expect.isTrue defaultClosed "the default cheatsheet never closed"

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

        // The same key reads the mounted layout's rows, not the
        // retired layout's.
        do! onUiAsync(fun () -> pressKey(window, Key.OemQuestion))

        let! testKeys =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "test the layout"))
          )

        Expect.isTrue testKeys "the test layout's cheatsheet never showed"

        let! oldKeysGone =
          probeUiAsync(fun () -> not(hasText(window, "Show this list")))

        Expect.isTrue oldKeysGone "the retired layout's keys stayed in the card"

        do! onUiAsync(fun () -> pressKey(window, Key.Escape))

        let! testClosed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Keyboard shortcuts")))
          )

        Expect.isTrue testClosed "the test cheatsheet never closed"

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

        // Back on the default: the host card reads the default rows
        // again.
        do! onUiAsync(fun () -> pressKey(window, Key.OemQuestion))

        let! defaultAgain =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Show this list"))
          )

        Expect.isTrue defaultAgain "the default cheatsheet did not come back"

        do! onUiAsync(fun () -> pressKey(window, Key.Escape))

        let! defaultAgainClosed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Keyboard shortcuts")))
          )

        Expect.isTrue defaultAgainClosed "the default cheatsheet stayed open"

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
      "the workbench layout mounts through the registry and returns"
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

            let mounted =
              DevNull.Client.Layouts.builtIn(env, TimeSpan.FromSeconds 30.0)
              |> List.find(fun entry -> entry.Name = "workbench")
              |> fun entry -> entry.Mount surface

            let host =
              Window(Width = 1600.0, Height = 900.0, Content = mounted.Root)

            host.Show()
            mounted)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> layout.Root),
            (fun root -> hasText(root, "HEAD -> Following"))
          )

        Expect.isTrue shown "the workbench never mounted"

        do!
          probeUiAsync(fun () ->
            (layout :> IDisposable).Dispose()
            true)
          |> Async.Ignore
      })

    testCaseAsync
      "the workbench cheatsheet answers the question mark on both keys"
      (async {
        let! window = Leaks.startShell(signedInStore(), logApi() :> IHttp)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "The Octocat"))
          )

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            window.Width <- 1600.0
            window.Height <- 900.0)

        do!
          onUiAsync(fun () ->
            (window :?> Shell<TestEnv>).SwitchLayout "workbench")

        let! mounted =
          waitForAsync(
            8000,
            (fun () -> window),
            (fun root -> hasText(root, "HEAD -> Following"))
          )

        Expect.isTrue mounted "the workbench never mounted"

        // A plain apostrophe is a typable character, not the sheet.
        do! onUiAsync(fun () -> pressKey(window, Key.OemQuotes))

        let! apostropheQuiet =
          probeUiAsync(fun () -> not(hasText(window, "Keyboard shortcuts")))

        Expect.isTrue apostropheQuiet "a plain apostrophe opened the cheatsheet"

        // Spanish and several other layouts put ? on Shift+OemQuotes.
        do!
          onUiAsync(fun () ->
            pressKeyWith(window, Key.OemQuotes, KeyModifiers.Shift))

        let! opened =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "open the staging composer"))
          )

        Expect.isTrue
          opened
          "the workbench rows never showed on Shift+OemQuotes"

        let! defaultRowsGone =
          probeUiAsync(fun () -> not(hasText(window, "Show this list")))

        Expect.isTrue
          defaultRowsGone
          "the retired website rows stayed in the sheet"

        do! onUiAsync(fun () -> pressKey(window, Key.Escape))

        let! closed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Keyboard shortcuts")))
          )

        Expect.isTrue closed "the sheet never closed on Escape"

        // The US layout's own question-mark key still answers.
        do!
          onUiAsync(fun () ->
            pressKeyWith(window, Key.OemQuestion, KeyModifiers.Shift))

        let! reopened =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "open the staging composer"))
          )

        Expect.isTrue
          reopened
          "the workbench rows never showed on Shift+OemQuestion"

        do! onUiAsync(fun () -> pressKey(window, Key.Escape))

        let! closedAgain =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Keyboard shortcuts")))
          )

        Expect.isTrue closedAgain "the sheet never closed after the US key"
      })

    // Hangs after the workbench merge: the forced GC wait deadlocks
    // the headless session thread. Skipped pending a harness fix.
    // Hangs after the workbench merge: the headless session stops
    // serving dispatches; the test async waits forever (stack in
    // hang-stack2.txt). Skipped pending a harness fix.
    ptestCaseAsync
      "twenty layout switches retain nothing"
      (async {
        let tape = ResizeArray<string>()
        let testRoots = ResizeArray<WeakReference>()
        let defaultRoots = ResizeArray<WeakReference>()

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
            onUiAsync(fun () ->
              let content = window.Content :?> Panel
              defaultRoots.Add(WeakReference(content.Children.[0]))
              (window :?> Shell<TestEnv>).SwitchLayout "test")

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

        // Every unmounted layout is asked to let go once, and both
        // sides release: the test layouts' controls and the retired
        // default arrangement — its pages, their programs, and the
        // polls and bindings that used to outlive them.
        Expect.equal disposed 20 "an unmounted test layout was not disposed"

        let retained = testRoots |> Seq.filter(fun weak -> weak.IsAlive)

        Expect.equal
          (Seq.length retained)
          0
          "a switched-out test layout root stayed reachable"

        let retainedDefaults =
          defaultRoots |> Seq.filter(fun weak -> weak.IsAlive)

        Expect.equal
          (Seq.length retainedDefaults)
          0
          "a switched-out default layout root stayed reachable"
      })
  ]
