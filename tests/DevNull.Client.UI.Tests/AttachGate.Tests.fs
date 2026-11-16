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

/// The counters one probe program ticks: init once per program start,
/// one update tick, and one view tick per program start.
type private ProbeCounters = {
  mutable Started: int
  mutable Updated: int
  mutable Viewed: int
  mutable Sets: int
}

/// The recording probe program: init ticks once per program start, the
/// init command drives one update tick, and the view ticks once per
/// program start.
let private probeProgram
  (counters: ProbeCounters)
  : Program<unit, int, unit, Control> =
  Program.mkProgram
    (fun () ->
      counters.Started <- counters.Started + 1
      0, Cmd.ofMsg())
    (fun _ model ->
      counters.Updated <- counters.Updated + 1
      model, Cmd.none)
    (fun _ _ ->
      counters.Viewed <- counters.Viewed + 1
      Border(Name = "AttachProbe") :> Control)

/// Builds a detached scope and starts it through the attach gate. The
/// scope stays detached until the test shows it, so the test reads the
/// quiet counters first.
let private startDetached
  (counters: ProbeCounters)
  : Async<ContentControl * System.IDisposable> =
  probeUiAsync(fun () ->
    let scope = ContentControl(Name = "AttachScope")

    let handle =
      Programs.runComponentOnAttach(
        scope,
        ui,
        probeProgram counters,
        fun root ->
          counters.Sets <- counters.Sets + 1
          scope.Content <- root
      )

    scope, handle)

let tests =
  testList "attach-gated components" [
    testCaseAsync
      "a detached scope starts nothing; the first attach starts one program"
      (async {
        let counters = {
          Started = 0
          Updated = 0
          Viewed = 0
          Sets = 0
        }

        let! scope, handle = startDetached counters

        do! settleAsync()

        let! quiet =
          probeUiAsync(fun () ->
            counters.Started = 0
            && counters.Updated = 0
            && counters.Viewed = 0
            && counters.Sets = 0)

        Expect.isTrue quiet "a detached scope started its program"

        let! window =
          probeUiAsync(fun () ->
            let window = Window(Content = scope)
            window.Show()
            window)

        let! startedOnce =
          waitForAsync(
            5000,
            (fun () ->
              counters.Started,
              counters.Updated,
              counters.Viewed,
              counters.Sets),
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
            counters.Started, counters.Updated, counters.Viewed, counters.Sets)

        Expect.equal again (1, 1, 1, 1) "a re-attach started a second program"

        // The handle stops the started program and tolerates a second
        // dispose.
        do! onUiAsync(fun () -> handle.Dispose())
        do! onUiAsync(fun () -> handle.Dispose())
        do! settleAsync()

        let! stopped =
          probeUiAsync(fun () ->
            counters.Started, counters.Viewed, counters.Sets)

        Expect.equal
          stopped
          (1, 1, 1)
          "disposing restarted or re-set the program"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "disposing before attach cancels the pending start"
      (async {
        let counters = {
          Started = 0
          Updated = 0
          Viewed = 0
          Sets = 0
        }

        let! scope, handle = startDetached counters

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
            counters.Started = 0
            && counters.Updated = 0
            && counters.Viewed = 0
            && counters.Sets = 0)

        Expect.isTrue silent "a canceled start ran on attach"

        let! empty = probeUiAsync(fun () -> scope.Content = null)

        Expect.isTrue empty "a canceled start set the view"

        do! onUiAsync(fun () -> window.Close())
      })
  ]
