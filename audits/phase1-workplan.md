# Phase 1 work plan — the dev loop

Status: active · Started 2026-11-01 · Source of truth for the work:
[`2026-11-phase1-mvuish-audit.md`](../2026-11-phase1-mvuish-audit.md) at the
repo root (the audit; its §9 is the step material and its §1 grep counts are
the gates).

This file is the durable record of how Phase 1 gets built. A session that
has never seen this work resumes from here (see "Resume protocol").

## Scope guard

- **Architecture only.** The steps refactor structure, composition, and
  shared ownership. They do not chase bugs.
- **No GitHub issues are opened until the user says so.** The audit's §10 is
  the bug ledger. Where a refactor happens to kill a §10 entry, the step
  marks that entry struck-through in the audit file (same commit) and says
  so in its changelog line. Nothing else tracks bugs.
- **Composition over god objects.** Surfaces stay small and composable:
  a record that only exists to carry several optional things past one
  consumer is scaffolding, and scaffolding gets dissolved in the step that
  removes its reason (P1's `ActionSlots`/`feedRowWith` dissolve into
  per-view `RowProps` fields in P2). Extraction steps split files and
  surfaces; none of them grows a bundle.
- The eight batch reports that fed the audit are session artifacts; they are
  not part of the repo. The audit stands alone.

## The loop

Roles: **orchestrator** (the session that holds this plan), **implementer**
(one child agent), **user** (verification gate and commit gate).

1. **Pick the step.** The first unchecked step below whose dependencies are
   done. Check the tree is clean (`git status`); a step never starts on a
   dirty tree.
2. **Brief the implementer.** One child agent on tier `quick`
   (deepseek-flash@xhigh — the only implementer tier; never glm-5.3@xhigh).
   One writing agent at a time; no parallel writers. The brief carries: the
   goal in one sentence, the exact file scope, the audit section that
   defines the change, the constraints, and the done-when. Briefs are
   written at dispatch time from the audit, because earlier steps may have
   moved the code.
3. **Implementer works.** It edits only the step's files plus the changelog
   plus (when a step completes) this checklist. It runs
   `dotnet fantomas .` before finishing.
4. **Orchestrator verifies.** Format clean; the solution builds; both test
   suites green (Core and UI); the step's grep gates hold; the full diff is
   read; no file outside the step's scope changed. If verification fails,
   the loop restarts at 2 with the failure named. Implementer output is
   always re-verified by the orchestrator, never trusted.
5. **User verifies.** The orchestrator reports the outcome, the diff scope,
   and the verification evidence. The user reviews.
6. **Commit.** Only after the user approves. The commit carries the step's
   files, the changelog entry, and this file's updated checkbox. **No
   commits exist between step start and user approval.** The user's
   go-ahead to run the next step is never commit approval for the
   current one — only an explicit review verdict commits.

Standing constraints on every step (from `AGENTS.md` and the skill gate):
`dotnet fantomas .` before staging; every new F# file pairs with its `.fsi`;
no `private` under a signature; at most three tupled parameters with
contextual records beyond; changelog under `Unreleased` with a bold area
prefix (docs-only steps carry no changelog entry); every View/Component
change is checked against the `devnull-mvuish-audit` gate.

## Resume protocol

A new session, in order: read `AGENTS.md` → read the audit report at the
root → read this file → `git log --oneline -8` and `git status` → continue
at the first unchecked step whose dependencies are done. The state lives in
this checklist and the git log, not in any session's memory.

## Steps

Default order follows the audit §9's rationale: settle the high-touch
surfaces first (card actions, chrome slots, Feed), keep mechanical systemic
wins early, put the three big splits last so each file is touched once.
Steps may swap when their dependencies allow.

- [x] **P0 — Land the docs.** Scope: the audit report (self-contained; no
  `.alta` references) and this workplan, under `audits/`. Verify: the user
  reads both. No changelog (docs only).
- [x] **P1 — Issue #1, part A.** The shared flows beside
  `CommitActions.toggleStar` (vote, pastel: call + in-flight key + shared
  log/error wording, `Result<Commit, AppError>`), the generalized
  `ActionSlot` on `CommitCard.Props` beside `Pin` (audit §3.2, §5), and
  LogView as the pilot migration. Depends on: P0. Verify: suites green;
  LogView renders through the consolidated actions; its duplicated arms are
  gone.
- [x] **P2 — Issue #1, part B.** Migrate ProfileView, CommitDetailView,
  OrganizationView, SavedView, SearchView onto the action slots; the card
  union drops the never-raised cases and the six effect arms; the
  transitional `ActionSlots`/`feedRowWith` dissolve into individual
  `RowProps` fields each view composes for itself. Depends on:
  P1. Verify: suites green; §1 greps drop (`Commits.vote` inside Views 6 → 0 — the one flow lives in CommitActions; `Pasteis.give` inside Views 5 → 0); BUG-4 struck in the audit.
- [ ] **P2b — The shared row builder.** One builder turns a page's props,
  routing, and model into `RowProps` with shared, multicasted slices
  (`replayBuffer 1 >> refCount` at the boundary); the five `commitCtx`
  copies and LogView's per-row ctx collapse onto it (audit §8 pattern 2;
  the ProfileView build-once shape is the model). Depends on: P2.
  Verify: suites green; `commitCtx` definitions 5 → 1.
- [x] **P3 — Chrome unification A.** One `Line` state (`Off | Words of
  string`) replaces the `bool * string` slots and ProfileShell's
  `string option` busy/empty; PageChrome and ProfileShell render one
  shared pinned header; `Status` becomes the static text owner (its dead
  exports die); the hand-built scaffolds (Settings/About, and SignIn if it
  fits without new slots) project into PageChrome. No speculative slots:
  title-as-Control and a PageChrome footer wait for a consumer (feed
  footers ride Feed in P5). Verify: suites green; no page hand-builds a
  busy/empty/error line; no `bool * string` projection remains.
- [x] **P4 — Chrome unification B (surface cleanup).** Dead exports and
  dead code (audit §2.3); `Theme.Brushes` shrinks to what the shell binds
  and its doc stops inviting the banned binding; `Keys.scrim`;
  `bindClass` returns `IDisposable`; `Composer.Register` is deleted and the
  handle exposed. Verify: suites green; §1 grep gates (`CreateLogger` 26 →
  lower only if trivially safe, dead exports 0).
- [ ] **P5 — Feed hardening.** `Feed.view` gates `Items` by reference and
  gains `HasMore`; `ScrollTop` becomes a handle or model token (fixes the
  rule-3 shape and the one-shot Sub); Header/Empty slots; feed scroll slots
  on `ProfileShell.FeedProps`; a UI test counts realized containers (closes
  VERIFY-18a either way). Verify: suites green; the new test asserts its
  number.
- [ ] **P6 — Rebuild-guard sweep.** One `bindContent(keyOf, build)` helper
  beside the chrome controls, applied to the 12+ ungated sites of §4.1
  rule 8. LEAK-2 dies here (strike it in §10). Verify: suites green; the
  CommitDetailView card no longer re-subscribes per emission.
- [ ] **P7 — DialogChrome.** One host for the shell's five overlay copies
  plus the scrim token; `Shortcuts.helpCard` becomes dialog content (the
  bindings rows stay in Shortcuts). Depends on: P4 (scrim token). Verify:
  suites green; the five overlays open and close through one host.
- [ ] **P8 — LogChrome slots + SplitChrome.** LogChrome takes
  Tabs/Composer/RailNode/Header/OnReload and stops knowing
  `Composer.Fresh`/`Timeline.CommitLanded`/`/scheduled`; `SplitChrome`
  owns RemotesView's side/main/back/breakpoint; the band-offset sweep runs
  (issue #15's strip is adjacent, not in scope). The strip becomes one
  shared stateless `TabStrip` part used by LogChrome and ProfileShell
  (user review finding): prefer a property/pseudo-class active look over
  `bindClass` — no class subscription to own — and this rewrite consumes
  LogChrome's two parked option-compare sites (:102/:218). Verify: suites
  green; the log chrome and the remotes two-pane render through the new
  hosts.
- [ ] **P9 — Palette extraction.** One `Palette` host (scroller, row
  subject, `followRows`, keyboard walk) with SearchDialog and Completion
  supplying row models and actions; `KbdChip`, `StatusLine`, `UserRow`
  parts beside it. Verify: suites green; a UI test walks keys through the
  one host from both surfaces.
- [ ] **P10 — Shell decomposition.** One helper for the four
  identity-guarded cells; `termsDialog` becomes a proper component; the
  sidebar, aside, band, and chrome feed become named elements the Shell
  composes. Depends on: P7. Verify: suites green; the four guards share one
  helper.
- [ ] **P11 — RemotesThread split A.** Extract `Components/ThreadVoice.fs`
  and `Components/ThreadRecorder.fs` along the audit §7.4 seams
  (`VoiceCtx`/`Solo`; `audio` + `report`). Verify: suites green after each
  extraction; the file count of `RemotesThread.fs` drops by the seam sizes.
- [ ] **P12 — RemotesThread split B.** Extract `ThreadMessage.fs`,
  `ThreadChrome.fs`, `ThreadMenus.fs` (§7.4). Verify: suites green.
- [ ] **P13 — RemotesThread split C.** The thread list moves onto
  `Feed.Props` (deletes `list`/`ScrollState`), `ThreadComposer.fs` and
  `Views/Remotes/ThreadPane.fs` land, `column` keeps only slots. Depends
  on: P5, P9. Verify: RemotesView.Tests green end to end; the thread pane
  renders through `Feed` and the shared dialogs.
- [ ] **P14 — RemotesModel split + page folder.** `RemotesTypes.fs`,
  `RemotesRows.fs`, `RemotesVoice.fs` (WAV work beside Core's
  `Commit.Voices`), the model file shrinks to init/update/subscribe;
  the Remotes files group under `Views/Remotes/`; fsproj order stays
  honest. Verify: suites green; the pure row shaping is callable without an
  env.
- [ ] **P15 — Composer split.** `CompletionLane`, `AttachmentStrip`
  (with `ImageChip`, now shared with the thread composer), `PollEditor`,
  `SchedulePicker`, `ComposerToolbar`. Depends on: P4, P9. Verify: suites
  green; the thread composer reuses the strip and the key lane.
- [ ] **P16 — Mechanical sweep.** The `Slices` module (`share`, `gate`,
  `gateByRef`) and its first adopters (the nine shared slices of §4.3); the
  per-update logger hoist (26 sites); `inline` one-liners; dead opens and
  messages; the two `private` markers; the `Status` namespace fix;
  `Translation.fs` moves to `Views/TranslationModel.fs`; the direct option
  comparisons outside P2's scope compare inner values instead (P2 review
  list: RemotesState.fs:735, Shell.fs:843, LogChrome.fs:102/:218,
  WhoToFollow.fs:193/:229/:263, NotificationsView.fs:406, RemotesInbox.fs:507,
  RemotesModel.fs:991). Verify: suites
  green; grep gates — `CreateLogger` inside `update` at 0,
  `replayBuffer`/`refCount` present at the nine boundaries.

## Ledger

| Step | State | Notes |
|---|---|---|
| P0 | committed 2026-11-01 | report self-contained; workplan landed; branch `phase1/mvuish` |
| P1 | committed 2026-11-01 | failure keeps two structured log fields (user feedback); merged to main |
| P2 | committed 2026-11-01 | user review: inner-value option compares, no redundant match, debug logs restored; `PastelResult` payload; BUG-4 struck |
| P3 | committed 2026-11-01 | `Line` + first `share` multicaster; one pinned header; SignIn fit with no new slots |
| P4 | committed 2026-11-01 | net −149 lines; dead surface gone; scrim token; `bindClass` disposes at both accumulation sites |
