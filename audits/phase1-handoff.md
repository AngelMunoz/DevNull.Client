# Phase 1 handoff — COMPLETE

Last updated: 2026-11-02, after P16 landed, the branch was merged and
pushed, and the Phase 1 conformance audit ran. Phase 1 (P0-P16 + P2b)
is done: every checkbox in `audits/phase1-workplan.md` is ticked and
the ledger carries each step. The `phase1/mvuish` branch was deleted
after the merge.

**The follow-up lives in `audits/phase1b-workplan.md`** — the polish,
module-shape, and bug-ledger steps (P17-P29) sized from the
conformance audit.

## Where we are

- Local `main` == `origin/main` after the Phase 1 push. The changelog
  carries the user-facing outcomes under [Unreleased].
- The audit report at the repo root and the work plan stay as the
  record of what was done and why.

## If Phase 2 starts

- The loop, the tiers, and the verification bar lived in this file's
  earlier revisions (`git log audits/phase1-handoff.md`); the gates
  battery (`audits/gates.ps1`) and the idiom rules in AGENTS.md are the
  durable part.

## Still open (parked by user decision)

- **§10 bug ledger** (`2026-11-phase1-mvuish-audit.md`): LEAK-1
  (AsyncImage canceled bitmaps) and BUG-5..17 wait for the user to open
  the GitHub-issues gate.
- **Search-minimum UI test**: the wait-for-controls half of the flaky
  test task (the pin half was fixed in P8).
- **Cursor-based timeline + infinite scroll**: future work; today's
  scroll restore lands on the last loaded section, not the exact
  commit (user-accepted limitation).
