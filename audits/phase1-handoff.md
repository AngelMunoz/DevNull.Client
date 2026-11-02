# Phase 1 handoff — resume here in a fresh session

Last updated: 2026-11-02, after P15 landed. Owner of this file: whoever
continues the dev loop. Read `AGENTS.md` and `audits/phase1-workplan.md`
first; this file is the state snapshot, the plan is the authority.

## Where we are

- Branch `phase1/mvuish`, fast-forward-merged into local `main` after every
  approved step. **`origin/main` is behind and unpushed — pushing needs the
  user's explicit go.**
- Committed: P0-P4, P2b, P5-P15 (see `git log --oneline -20`). The audit
  report at the repo root and the work plan under `audits/` are committed
  with the work.
- Uncommitted: nothing (each step lands only after the user's verdict).

## The loop (unchanged)

1. Pick the next unchecked step in `audits/phase1-workplan.md`.
2. ONE implementer agent, tier `quick` (deepseek-flash@xhigh) — never
   glm-5.3@xhigh. One writer at a time. Briefs are written at dispatch time
   from the plan + audit; they carry the idiom rules verbatim.
3. Orchestrator verifies: `dotnet fantomas .` stable; build 0 errors;
   Core suite (218) + UI suite (263, `--sequenced` only — ~8 unsequenced
   failures are pre-existing); **`pwsh audits/gates.ps1 <new-or-changed
   files>` at zero unexplained hits; AND a read of every new file** — the
   suites have never caught a single thing the user reviews for.
4. Report to the user. NO commit until their verdict — a go for the next
   step is never commit approval. Micro-fixes from their feedback are done
   directly by the orchestrator, not re-dispatched.
5. On verdict: tick the plan (checkbox + ledger row), commit on the branch,
   `git switch main; git merge --ff-only phase1/mvuish; git switch phase1/mvuish`.
6. Known trap: a leftover `DevNull.Client.exe` from the repo bin locks the
   DLL (MSB3021) — stop only processes whose path is under the repo.

## The user's gates (every one learned from a rejection)

- Structured log fields never merge into one string; per-action debug logs
  stay (success logs included).
- Option comparisons compare inner values (explicit match or combinators);
  no identity `match!` wrappers; `Option.filter/map/bind` over re-wrap arms.
- Fluent chains are uninterrupted — no `let host = Control()` then
  `host.X(...) |> ignore` then return; decompose over-nesting instead.
- `yield` only as `yield!` splicing a collection; build the collection the
  consumer needs directly.
- Composition over god objects: no speculative slots/optional fields
  waiting for consumers; scaffolding dissolves in the step that removes
  its reason.
- Views/Components never bind app-lifetime streams (Sub or bust);
  `.Add` on a control's own event is fine.
- Bugs go to the issues gate — **which stays closed until the user opens
  it.** §10 of the audit is the ledger; steps strike entries they kill.

## State of the audit ledger (§10)

Dead: BUG-3, BUG-4 (P2), LEAK-2, VERIFY-18a (P5/P6). Live: LEAK-1
(AsyncImage canceled bitmaps — the one the user can feel), BUG-5..BUG-17,
VERIFY-18b/c. The flaky-test task is half done (pin test fixed in P8);
search-minimum's fixed sleep remains.

## User-accepted limitation (recorded, not a bug)

Scroll restore lands on the last loaded section, not the exact commit the
reader left — proper cursor-based timeline + infinite scroll is future
work outside Phase 1.

## Next steps

- **P14** — landed. `Views/Remotes/` holds `RemotesTypes`, the env-free
  `RemotesRows`, and the slim `RemotesModel` (1,176 from 1,885); the WAV
  `stageVoice` lives in `Core.Commit.Voices`, the draft helpers in
  `Core.RemotesText`; `typingNames` reaches `ThreadComposer` as a
  `Typing: IObservable<string list>` prop. No Components file references
  Views — the fsproj order proves it.
- **P15** — landed. Composer 1,803 → 969 plus seven elements
  (`CompletionLane`, `ImageChip` shared with the thread composer,
  `AttachmentStrip`, `ComposerKeys`, `PollEditor`, `SchedulePicker`,
  `ComposerToolbar`); the lane mailbox and snapshot read are gone
  (one element-local `BehaviorSubject<Snapshot>` survives, justified:
  synchronous key decisions); ThreadComposer 708 → 612; chip text
  unified at 12px.
- **P16** — the mechanical sweep, the LAST step: the `Slices` module and
  its nine adopters (§4.3), the per-update logger hoist (grep gate:
  `CreateLogger` inside `update` at 0, `replayBuffer`/`refCount` at the
  nine boundaries), `inline` one-liners, dead opens/messages, the two
  `private` markers, the `Status` namespace fix, `Translation.fs` →
  `Views/TranslationModel.fs`, and the option-compare fix list
  (RemotesState:735, Shell:843, LogChrome:102/218, WhoToFollow:193/229/263,
  NotificationsView:406, RemotesInbox:507, RemotesModel:991). Justified
  survivors stay: ThreadPane's timer-callback class flips, Feed's
  view-scoped ref cells, LogChrome's per-user tab bindings, ThreadPane's
  two construction-time `CreateLogger` calls.
- **P16** — mechanical sweep; its parked lists (option compares, per-update
  loggers, `let private` markers, dead opens) are itemized in the plan.

## Standing follow-ups (alta tasks, pending)

- File §10 as GitHub issues — blocked on the user's gate.
- Wait-for-controls in the search-minimum UI test.
