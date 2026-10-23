module DevNull.Client.UI.Tests.Leaks

// The retention checks: a themed control releases with its tree, a feed
// row releases when its page ends, and a scroll soak stays inside a
// managed-memory budget. The first three fail while the app roots
// controls from app-lifetime observables; they prove both directions of
// the fix.

open System
open Avalonia
open Avalonia.Controls
open Avalonia.VisualTree

open Expecto

open DevNull.Client.Components
open DevNull.Client.Core.Http
open DevNull.Client.Shell
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

/// The api of the soak: every log read answers a many-card page.
let soakApi(cardCount: int) : IHttp =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok(manyCardsPageJson cardCount)
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))
  :> IHttp

/// Builds and shows the shell over one test env.
let startShell(store: SessionStore, http: IHttp) : Async<Window> =
  let env = TestEnv(store, http)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

/// The first text block under the root carrying the text.
let textBlockWith(root: Control, text: string) : TextBlock option =
  root.GetVisualDescendants()
  |> Seq.tryPick(fun visual ->
    match visual with
    | :? TextBlock as block when block.Text = text -> Some block
    | _ -> None)

/// Clicks one button by its text once it shows up.
let clickWhenShown(window: Window, text: string) : Async<unit> = async {
  let! appeared =
    waitForAsync(5000, (fun () -> buttonWithText(window, text)), Option.isSome)

  Expect.isTrue appeared $"the {text} button never appeared"

  do! onUiAsync(fun () -> buttonWithText(window, text) |> Option.iter click)
}

/// A forced full collection: blocking, compacting, with finalizers
/// drained between the passes.
let collect() : unit =
  for _ in 1..2 do
    GC.Collect()
    GC.WaitForPendingFinalizers()

  GC.Collect()

/// Shrinks the window so a many-card feed overflows the viewport plus
/// its cache band and recycles rows while scrolling.
let shrinkAsync(window: Window) : Async<unit> =
  onUiAsync(fun () ->
    window.Width <- 420.0
    window.Height <- 480.0)

let tests =
  testList "memory leaks" [
    testCaseAsync
      "a themed control releases with its tree"
      (async {
        // The control and its weak reference are built inside one probe,
        // so no test binding keeps the control alive.
        let! pair =
          probeUiAsync(fun () ->
            let control = CommitCard.tombstone()

            Window(Content = control), WeakReference(control))

        let window, weak = pair

        do! settleAsync()

        // The window stays alive and shown; only the themed subtree
        // leaves, so the headless session's window bookkeeping cannot
        // hold the answer.
        do! onUiAsync(fun () -> window.Content <- Panel())

        do! settleAsync()
        collect()

        Expect.isFalse weak.IsAlive "the themed control outlived its tree"
      })

    testCaseAsync
      "a feed row releases when its page ends"
      (async {
        let! window = startShell(signedInStore(), soakApi 40)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Card 1."))
          )

        Expect.isTrue shown "the first card never rendered"

        // The row's body block, held weakly before the row ever recycles.
        let! weak =
          probeUiAsync(fun () ->
            textBlockWith(window, "Card 1.")
            |> Option.map(fun block -> WeakReference(block)))

        Expect.isSome weak "the first card's body never rendered"

        do! shrinkAsync window
        do! scrollFeedToEndAsync window
        do! settleAsync()

        // Leaving the log ends its page program; the recycled row must
        // follow it out of memory.
        do! clickWhenShown(window, "Settings")
        do! settleAsync()
        collect()

        Expect.isFalse weak.Value.IsAlive "the recycled row outlived its page"
      })

    testCaseAsync
      "a scroll soak stays inside its memory budget"
      (async {
        let! window = startShell(signedInStore(), soakApi 40)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Card 1."))
          )

        Expect.isTrue shown "the feed never rendered"

        do! shrinkAsync window

        let! before = probeUiAsync(fun () -> GC.GetTotalMemory true)

        for _ in 1..10 do
          do! scrollFeedToEndAsync window
          do! settleAsync()
          do! scrollFeedToTopAsync window
          do! settleAsync()

        // The realized rows ride the open page's model; leaving the
        // log ends that page, and everything the soak realized must
        // follow it out of memory.
        do! clickWhenShown(window, "Settings")
        do! settleAsync()
        collect()

        // One more drain: CI's runners sit on a higher, noisier
        // baseline than a local run (9.6-14.0 MB observed across runs,
        // against under 8 locally), and Linux lands its finalizers
        // later. The extra beat plus collection settles that floor
        // before the delta is read.
        do! settleAsync()
        collect()

        let! after = probeUiAsync(fun () -> GC.GetTotalMemory true)

        // The budget sits above the noisiest CI baseline observed
        // (14.0 MB) and below the smallest value the leak retained
        // before the fix (16.9 MB), so both directions of the gate
        // hold on every runner.
        Expect.isLessThan
          (after - before)
          15_000_000L
          $"the soak retained {after - before} bytes"
      })
  ]
