module DevNull.Client.UI.Tests.Snapshots

// The camera tests: a feature's visual states saved as PNGs a review
// pass — or the user — can open. The harness session renders through
// Skia, so a capture is the frame a user sees. artifacts/snapshots is
// gitignored: captures are evidence for a review round, never fixtures.

open System
open Avalonia.Controls
open Expecto

open DevNull.Client.Core.Http
open DevNull.Client.UI.Tests.App
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

/// The proof of the pipeline itself: one signed-in shell over the canned
/// timeline page, rendered and saved. Every feature's states ride the
/// same captureAsync.
[<Tests>]
let captureTests =
  testList "snapshots" [
    testAsync "the shell renders a frame the camera can save" {
      let api =
        FakeApi(fun request ->
          if request.Target.StartsWith "/api/v1/log/" then
            ok logPageJson
          else
            (match accountAnswer request with
             | ValueSome answer -> answer
             | ValueNone -> ok "{}"))

      let! window = startShell(signedInStore(), api :> IHttp)

      let! shown = showsText(window, "The Octocat")
      Expect.isTrue shown "the shell never rendered its content"

      let! path = captureAsync("spike-shell", window)
      let bytes = IO.File.ReadAllBytes(path)
      Expect.isTrue (bytes.Length > 1000) "the frame saved real pixels"
    }
  ]
