# Bug cycle workplan

One checkbox per bug, newest last. The loop for these is
`.alta/scripts/devnull-bug.js` — one issue per run, one branch per bug, a
fix: pull request when it passes. The plan is optional: the loop works
from the tracker issue alone, and a step here is the ledger.

triage (expected vs actual, risk, changelog rule) -> repro (the
reproducer writes the failing test, the loop commits it, the command
must fail on the broken tree) -> derive (root cause, boundary, caller
census, fix shape guard) -> worker (the fix, the repro test frozen; its
battery is the verification of record) -> collect -> the repro gate
(hashes unchanged, passes-after) -> review pair (diff + renders, both
models, kept seats — full pass once, deltas after) -> red team when
the risk is high ->
the deriver's scope ruling on every finding. Two scope cycles per bug;
the need for a third stops for the user. The landing is a `fix:` pull
request; the merge stays with the user.

Run it: `workflow devnull-bug` with `step: "B1"` (or `issue: "16"` — the
id defaults to `B<issue>`), plus `action` `window | triage | repro |
derive | battery | land | rollback`.

| Step | Issue | Status | Note |
|---|---|---|---|

<!-- Example row:
| B16 | #16 | pending | back to the log always returns to Latest |
-->
