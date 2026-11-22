module DevNull.Client.UI.Tests.WebsiteRetirementTests

// The shell mounts the website arrangement through the layout registry,
// and retiring it for the editor releases the website root, its router,
// its aside page, and its log page. The log page stands in for the log
// model: the model's stream lives inside the page program, and no handle
// outside that program reaches it, so the page control releasing with
// its program ended is the observable leg.

open System
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open Navs.Avalonia
open DevNull.Client.Core.Http
open DevNull.Client.Shell
open DevNull.Client.Layout.Website
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness
open DevNull.Client.UI.Tests.Leaks

let tests =
  testList "website retirement" [
    testCaseAsync
      "retiring the website layout releases its root, router, and pages"
      (async {
        let! window = startShell(signedInStore(), logApi() :> IHttp)

        do!
          onUiAsync(fun () ->
            window.Width <- 1600.0
            window.Height <- 900.0)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "The Octocat")
          )

        Expect.isTrue shown "the log never rendered"

        let! asideShown =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> controlByName(root, "WhoToFollow") |> Option.isSome
          )

        Expect.isTrue asideShown "the aside never rendered"

        // The probe returns presence flags with the weak references, so
        // no strong reference to a retired control survives in test
        // scope past the collection step.
        let! foundRouter, foundWho, foundLog, weaks =
          probeUiAsync(fun () ->
            let root = (window.Content :?> Panel).Children.[0]

            let router =
              window.GetVisualDescendants()
              |> Seq.tryPick(function
                | :? RouterOutlet as outlet -> outlet.Router
                | _ -> None)
              |> Option.map(fun router -> WeakReference(router :> obj))

            let who =
              controlByName(window, "WhoToFollow") |> Option.map WeakReference

            let log =
              window.GetVisualDescendants()
              |> Seq.tryPick(function
                | :? LogView.Page<TestEnv> as page -> Some(WeakReference page)
                | _ -> None)

            let weaks =
              [ Some(WeakReference root); router; who; log ] |> List.choose id

            Option.isSome router, Option.isSome who, Option.isSome log, weaks)

        Expect.isTrue foundRouter "the website never mounted its router outlet"
        Expect.isTrue foundWho "the aside never mounted who to follow"
        Expect.isTrue foundLog "the outlet never mounted the log page"

        do!
          onUiAsync(fun () -> (window :?> Shell<TestEnv>).SwitchLayout "editor")

        let! editorShown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> controlByName(root, "EditorStatusBar") |> Option.isSome
          )

        Expect.isTrue editorShown "the editor never mounted"

        do! settleAsync()
        collect()
        do! settleAsync()
        collect()

        let alive = weaks |> List.filter _.IsAlive |> List.length

        Expect.equal alive 0 "a retired website root, router, or page survived"

        do! onUiAsync(fun () -> window.Close())
      })
  ]
