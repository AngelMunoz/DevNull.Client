# Testing DevNull.Client

Two suites, one philosophy: assert the behavior a user can see, through
the surface meant to survive.

- `DevNull.Client.Core.Tests` - Expecto over the protocol layer. No
  network, no disk, no processes: every worker is a fake that returns a
  canned value (`FakeApi`, `FixedClock`, `FixedSigner`, ...).
- `DevNull.Client.UI.Tests` - Expecto over the real shell in an
  Avalonia headless session: real routes, real pages, every request
  answered by `FakeApi`.

## Commands

- `dotnet run --project tests/DevNull.Client.Core.Tests`
- `dotnet run --project tests/DevNull.Client.UI.Tests`
- Append `-- --summary` for a summary, `-- --sequenced --debug` to
  run one test at a time with names printed, `-- --filter-test-case
  "a test name"` to run one test.
- Close the app before building/running the UI suite: a running
  `DevNull.Client` locks the output DLLs and the build fails on the
  copy step.

## Philosophy

1. **Assert behavior, not plumbing.** Drive the shell like a user
   (clicks, text, visibility) and assert what renders. Never assert on
   internal state, spies, or call counts of internals.
2. **Fakes control their output.** Each fake returns exactly the canned
   value; `FakeApi` records every request so a test can assert on the
   requests the app actually made.
3. **The API is never shaped for tests.** Tests implement the same
   interfaces production implements. A `Unchecked.defaultof` for a
   field a pure function never touches (the router in `LogView.Props`)
   is a test-side choice, not an API change.
4. **Everything async.** No `GetAwaiter().GetResult()`, no `.Result`,
   no `Thread.Sleep` in waits. The one exception is the harness
   bootstrap below.
5. **Windows and controls live on the session thread.** Every control
   read or click goes through `onUiAsync`/`probeUiAsync`; only the
   outermost call dispatches.

## The headless session and its hang quirks

Learned the hard way, twice. Read before touching `Harness.fs` or
adding anything that touches the dispatcher.

### One session, no parallel collections

`Dispatcher.UIThread` is process-global. Two headless sessions (or
PerTest isolation, which resets the global dispatcher) make the MVUish
update loop's posts land on a ghost dispatcher. The harness therefore
uses ONE `HeadlessUnitTestSession` with
`AvaloniaTestIsolationLevel.PerAssembly` for the whole assembly, and
the first session dispatch captures the dispatcher (`Harness.ui`)
before any test thread can touch it.

### Never create dispatcher-bound objects off the session thread

A `DispatcherTimer` (or anything else that reads
`Dispatcher.UIThread`) constructed on a test/pool thread BEFORE the
session's first dispatch claims the global dispatcher: the object's
thread becomes `Dispatcher.UIThread`, the headless session can never
run its operations, and `Harness`'s static constructor deadlocks
blocking on its first dispatch. This library's clock service uses a
`System.Timers.Timer` (thread-pool, the same kind MVUish's
`Sub.interval` uses) for exactly this reason. Rule: services built
outside the UI thread must not touch the dispatcher in their
constructor; hand ticks to `dispatch` and let the program loop marshal.

### Waits are many small session dispatches, not one long one

The program loop posts its message processing to the dispatcher, and
layout posts to it too. In a headless session BOTH are served by the
session's own dispatch loop: `waitForAsync` therefore probes and pumps
as separate `session.Dispatch` calls from the test thread, again and
again. One single dispatch that holds a nested `PushFrame` pump runs
neither: layout stays stale (a resize never applies, feeds never
realize) and loop posts starve. Keep the probe, sleep, pump rhythm;
never collapse it into one dispatched task.

### No nested dispatch

An `onUiAsync`/`probeUiAsync` call made from the UI thread would
queue session work the current dispatch cannot serve. The helpers run
the work inline when `ui.CheckAccess()`; keep it that way. A test
that starts a wait inside a wait freezes.

### When a run stalls

1. Do not wait more than a couple of minutes. Kill the job.
2. Sweep zombies: a killed run leaves `DevNull.Client.UI.Tests` /
   `testhost` processes holding the output DLLs; the next build fails
   with MSB3027 until they die.
3. Rerun with `-- --sequenced --debug`: the last printed name is the
   test that never finished. Wrap the run in a shell-level timeout if
   you must (Expecto has no CLI timeout).
4. For the real stacks: `dotnet-stack report -p <pid>` prints every
   managed thread's stack of the hung process. The stuck frame names
   the culprit (this is how the DispatcherTimer deadlock was found).

### The bootstrap exception

`Harness.ui` blocks once (`GetAwaiter().GetResult()`) inside the
module's static initializer to capture the dispatcher before anything
else can. It is the only blocking wait in the suite, it runs once, and
removing it re-opens the ghost-dispatcher race above.
