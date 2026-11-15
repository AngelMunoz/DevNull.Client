module DevNull.Client.UI.Tests.AttachGateTests

// The attach gate: a detached scope starts nothing; the first attach
// starts one program and sets its view once; a re-attach starts nothing
// more; and the returned handle cancels a pending start or stops a
// started program.

open Avalonia.Controls
open Expecto
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.UI.Tests.Harness

/// The recording probe program: init ticks once per program start, the
/// init command drives one update tick, and the view ticks once per
/// program start.
let private probeProgram
  (started: int ref, updated: int ref, viewed: int ref)
  : Program<unit, int, unit, Control> =
  Program.mkProgram
    (fun () ->
      incr started
      0, Cmd.ofMsg())
    (fun _ model ->
      incr updated
      model, Cmd.none)
    (fun _ _ ->
      incr viewed
      Border(Name = "AttachProbe") :> Control)

/// Builds a detached scope and starts it through the attach gate. The
/// scope stays detached until the test shows it, so the test reads the
/// quiet counters first.
let private startDetached
  (started: int ref, updated: int ref, viewed: int ref, sets: int ref)
  : Async<ContentControl * System.IDisposable> =
  probeUiAsync(fun () ->
    let scope = ContentControl(Name = "AttachScope")

    let handle =
      Programs.runComponentOnAttach(
        scope,
        ui,
        probeProgram(started, updated, viewed),
        fun root ->
          incr sets
          scope.Content <- root
      )

    scope, handle)

let tests =
  testList "attach-gated components" [
    testCaseAsync
      "a detached scope starts nothing; the first attach starts one program"
      (async {
        let started = ref 0
        let updated = ref 0
        let viewed = ref 0
        let sets = ref 0

        let! scope, handle = startDetached(started, updated, viewed, sets)

        do! settleAsync()

        let! quiet =
          probeUiAsync(fun () ->
            started.Value = 0
            && updated.Value = 0
            && viewed.Value = 0
            && sets.Value = 0)

        Expect.isTrue quiet "a detached scope started its program"

        let! window =
          probeUiAsync(fun () ->
            let window = Window(Content = scope)
            window.Show()
            window)

        let! startedOnce =
          waitForAsync(
            5000,
            (fun () -> started.Value, updated.Value, viewed.Value, sets.Value),
            fun counts -> counts = (1, 1, 1, 1)
          )

        Expect.isTrue startedOnce "the first attach never started one program"

        let! hasContent = probeUiAsync(fun () -> scope.Content <> null)

        Expect.isTrue hasContent "the attach never landed the view in its scope"

        // Detach and re-attach: the one-shot guard starts no second
        // program and sets the view no second time.
        do! onUiAsync(fun () -> window.Content <- Panel())
        do! settleAsync()
        do! onUiAsync(fun () -> window.Content <- scope)
        do! settleAsync()

        let! again =
          probeUiAsync(fun () ->
            started.Value, updated.Value, viewed.Value, sets.Value)

        Expect.equal again (1, 1, 1, 1) "a re-attach started a second program"

        // The handle stops the started program and tolerates a second
        // dispose.
        do! onUiAsync(fun () -> handle.Dispose())
        do! onUiAsync(fun () -> handle.Dispose())
        do! settleAsync()

        let! stopped =
          probeUiAsync(fun () -> started.Value, viewed.Value, sets.Value)

        Expect.equal
          stopped
          (1, 1, 1)
          "disposing restarted or re-set the program"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "disposing before attach cancels the pending start"
      (async {
        let started = ref 0
        let updated = ref 0
        let viewed = ref 0
        let sets = ref 0

        let! scope, handle = startDetached(started, updated, viewed, sets)

        do! onUiAsync(fun () -> handle.Dispose())
        do! onUiAsync(fun () -> handle.Dispose())

        let! window =
          probeUiAsync(fun () ->
            let window = Window(Content = scope)
            window.Show()
            window)

        do! settleAsync()

        let! silent =
          probeUiAsync(fun () ->
            started.Value = 0
            && updated.Value = 0
            && viewed.Value = 0
            && sets.Value = 0)

        Expect.isTrue silent "a canceled start ran on attach"

        let! empty = probeUiAsync(fun () -> scope.Content = null)

        Expect.isTrue empty "a canceled start set the view"

        do! onUiAsync(fun () -> window.Close())
      })
  ]
