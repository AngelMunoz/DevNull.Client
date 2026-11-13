module DevNull.Client.UI.Tests.WorkbenchSnapshots

open System
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive

open DevNull.Client.Core.Http
open DevNull.Client.ImageCache
open DevNull.Client.Workbench
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let private now = DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)

let private checkoutId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

let private following =
  WorkbenchSettings.parse "following" |> WorkbenchState.refOf

let private images = {
  new ImageCacheService with
    member _.Get(_url: string) = async { return None }
}

let private hasTextFragment(root: Control, fragment: string) : bool =
  root.GetVisualDescendants()
  |> Seq.exists(function
    | :? TextBlock as block ->
      block.Text <> null && block.Text.Contains fragment
    | _ -> false)

let private hostProps(env: TestEnv) : WorkbenchHost.Props<TestEnv> = {
  Env = env
  Initial = Build.state(following, Some checkoutId)
  Images = images
  Now = Observable.single now
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  OnOpenAuthor = ignore
  OnOpenExternal = ignore
  OnStateChanged = ignore
  OnUseDefaultLayout = ignore
  OnReportCommit = ignore
  OnShare = ignore
}

let private captureWorkbenchAsync(name: string, width: float) : Async<string> =
  async {
    let! window, handle =
      probeUiAsync(fun () ->
        let env = TestEnv(signedInStore(), workbenchApi() :> IHttp)
        let view, handle = WorkbenchHost.mount(ui, hostProps env)

        let window = Window(Width = width, Height = 900.0, Content = view)

        window.Show()
        window, handle)

    let! shown =
      waitForAsync(
        8000,
        (fun () -> window),
        fun root ->
          hasText(root, "It was DNS.")
          && hasTextFragment(root, "CHECKOUT")
          && hasTextFragment(root, "HEAD -> Following")
      )

    Expect.isTrue shown "the workbench frame never rendered its regions"

    let! path = captureAsync(name, window)
    let bytes = IO.File.ReadAllBytes path
    Expect.isGreaterThan bytes.Length 1000 "the frame saved real pixels"

    do!
      onUiAsync(fun () ->
        window.Close()
        handle.Dispose())

    return path
  }

let tests =
  testList "workbench snapshots" [
    testAsync "the workbench frame at 1600 px" {
      let! path = captureWorkbenchAsync("W1/w1-workbench-1600", 1600.0)
      Expect.stringContains path "W1" "the 1600 capture left its suite folder"
    }

    testAsync "the workbench frame at 1100 px" {
      let! path = captureWorkbenchAsync("W1/w1-workbench-1100", 1100.0)
      Expect.stringContains path "W1" "the 1100 capture left its suite folder"
    }
  ]
