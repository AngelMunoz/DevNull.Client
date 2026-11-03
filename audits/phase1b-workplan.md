# Phase 1b work plan — polish, module shape, and the bug ledger

Written 2026-11-02 from the Phase 1 conformance audit (four read-only
auditors over Views/ and Components/, orchestrator-verified). The Phase 1
plan (`audits/phase1-workplan.md`) is complete; this one covers what the
conformance audit found plus the §10 bug ledger of
`2026-11-phase1-mvuish-audit.md`. Same loop, same gates: one quick-tier
implementer per step, `audits/gates.ps1` on changed files, orchestrator
reads every new file, user verdict commits. Bug steps follow AGENTS
testing rule 2: a regression test that fails before the fix.

## Steps

- [x] **P17 — Shared slots dissolve the forks.** `TabStrip` grows a
  badge slot (the waiting pill); `CommitRows.row` grows an actions-off
  switch; `Feed` grows a riding report (the from-end/extent arithmetic
  `Feed.Tail` already owns). Consumers move over: RemotesInbox drops its
  own `ToggleButton` strip (RemotesInbox.fs:339-421) and its own
  `ItemsControl` + scroll-end probe (:444-470) onto `Feed.view`
  (`HasMore`/`OnNeedMore` already exist); SearchView drops its hand-built
  `RowProps` + private `ActionSlot` (SearchView.fs:438-473) onto
  `CommitRows.row`; ThreadPane drops its `ref` + `ScrollChanged`
  re-derivation of the tail rule (ThreadPane.fs:315-360). Verify: the
  three forks are gone, suites green, gates clean; person rows stay
  separate (intentional).
- [x] **P18 — Ownership and effects discipline** (kills BUG-15, LEAK-1).
  Sub-own or dispose every subscription: Feed.fs:19 (Restore ctor,
  discarded disposable), DialogChrome.fs:56 (naked add on shell
  subjects), Palette.fs:40 (never-opened popup never detaches),
  SearchDialog.fs:415 and RemotesInbox.fs:437 (own-event `Observable.add`
  → `OnTextChangedHandler`). Callbacks out of compute:
  ThreadRecorder's `report` (:221+) → `Cmd.ofAction`; the BUG-15 store
  writes in update (RemotesModel arms, SettingsView `applyPrefs`,
  SearchDialog `RecentsCleared` :340) ride commands. LEAK-1: AsyncImage
  disposes the canceled-decode bitmap (AsyncImage.fs:93-99) — regression
  test proves the native drop. Also the two projection fixes: the
  `"ComposerBody"` name lookup (Shell.fs:437-445) becomes a focus
  callback passed down; a comment states why `chromeHost` sits above
  every route (Shell.fs:1312). Verify: rule-9 grep at zero unexplained,
  `report`/store writes grep inside update at zero, LEAK-1 test green,
  suites green.
- [x] **P19 — Arity, types, and shaping.** `BodyArgs`-style records for
  `CommitCard.body` (5 params, fsi:124), `bodyGen`, `textWithMentions`,
  `segmentView`; `readInbox` (Remotes.fs:171); `ScheduledView.row`
  (:428); the receipt pair `(bool * string)` across
  ThreadMessage.fs:83/:285 and RemotesRows.fs:379 becomes a
  `Pulled of string | Pushed`-style DU. Shaping:
  NotificationsView.toRows (:342-362) via `distinctBy`/`pairwise` (no
  HashSet, no positional index); the five discrete `if/elif` chains
  (Navigation.fs:20-34, ThreadChrome.fs:157-160, ThreadMessage.fs:143-150,
  Completion.fs:212-219, RemotesInbox.fs:501-511) become matches;
  Navigation.go de-inlined (rule 17); the two redundant upcasts
  (AsyncImage.fs:38, ThreadVoice.fs:566); CommitCard's two bare yields
  (:1755, :1777). Verify: 4+-parameter grep at zero unexplained, gates
  clean, suites green.
- [x] **P19b — The arity stragglers the sweep surfaced.** The complete
  4+-parameter census: eleven real hits. The five already owned are
  `CommitCard.iconAction`, `CommitCard.railCell`, `ThreadMessage.ghost`,
  `ThreadMessage.hoverActions`, and `RemotesState.landLinkPreview`. The
  six the audit missed: `src/DevNull.Client.Linux/GStreamer.fs:383`
  `play`, `src/DevNull.Client.Windows/Audio.fs:393` `openPlayback`,
  `src/DevNull.Client.UI/Components/FeedParts.fs:124` `laneElbowAt`,
  `src/DevNull.Client.UI/Views/CommitDetailView.fs:173` `replies`,
  `src/DevNull.Client.UI/Remotes.fs:343` `walk`, and
  `src/DevNull.Client.UI/Remotes.fs:704` `landed`. Same treatment as
  P19: contextual records where a context exists, dead parameters
  deleted, callers named and updated in the same step. Two scan false
  positives stay out: `TranslationModel.walk`/`auto` and
  `RemotesState.withFirstPage` have three real parameters; commas
  inside their `Result<_,_>` and `Map<_,_>` annotations fooled the
  counter. Verify: the 4+-parameter scan over the whole tree names zero
  ownerless hits.
- [x] **P20 — Module shape, the small services.** Migrate to the
  `fsharp-module-shape` (interfaces first, one Helpers block, one module
  per interface, `create` factory): Stores, SettingsStore, UiClock,
  RouteStore, ImageCache, LastVisitStore, RecentSearchStore, SshKeys,
  Timeline, TranslationCache, Unread, UserSettingsStore,
  AttachmentSources, Tray. Construction sites move from `new` to
  `create`; doc comments land in the `.fsi` as part of each move. Update
  the AGENTS.md architecture notes where they name the old shapes.
  Verify: suites green, fakes in tests unchanged in behavior, gates
  clean.
- [ ] **P21 — Module shape, the mixed files + Startup.** Env.fs (five
  workers + AppEnv split per interface), Remotes.fs (~2,100 lines: split
  per interface with honest fsproj order), DeviceAuth.fs. Pair
  Startup.fs with Startup.fsi (or fold into the head projects if the
  audit of its exports says so — its `DesktopApp` export decides).
  Verify: suites green, no capability wall restated on functions, zero
  Components→Views references, gates clean.
- [ ] **P22 — Comments into signatures.** Rule-8 sweep over what P20/P21
  did not already carry: CommitCard (144 vs 64), Shell (61 vs 6),
  CommitDetailView (54 vs 6), and the rest of the debt list. `.fs`
  keeps only implementation-quirk comments. Verify: doc-comment counts
  flip to the `.fsi` side; no behavior change; suites green.

### The bug ledger steps (§10 of the audit report)

- [ ] **P23 — Remotes inbox + thread dialogs** (BUG-5, BUG-9). BUG-5:
  clearing the search box clears the search (:428-439 filter + Text
  refills); settle actions render in Fetched only; the quiet line no
  longer draws over the search header; the org popover gets a placement
  target and light-dismiss; the tab strip stops retaining stripped
  subtrees (dies with P17's shared TabStrip if it lands first). BUG-9:
  placement targets on the six popups; busy guard on pin/confirm; the
  landing flash covers virtualized rows without stacking timers; the
  scrim becomes `Theme.ScrimBack` (kills the `Brushes.Black` finding).
  Verify: regression tests per fix, suites green.
- [ ] **P24 — Remotes read/seen + scroll loop** (BUG-6, BUG-7).
  Per-conversation `MarkedThrough`/`SeenThrough` map (or reset on target
  change); per-target UI state resets on switch; reaction chips in
  first-left order; the scroll→store→model loop settles (sampled or
  view-held until page end). Verify: regression tests; a busy-then-quiet
  thread sequence reports unseen correctly.
- [ ] **P25 — Voice and recorder lifecycle** (BUG-8). Late `Sounded`
  after cancel is stopped/discarded; detaching during `Opening` cannot
  leak the microphone; the page `Recording` flag settles on detach;
  `Solo` releases the last playback; the captured target refreshes on
  same-key rename/avatar. Verify: lifecycle regression tests over the
  recorder's fakes.
- [ ] **P26 — Composer** (BUG-10). Poll cap/floor arms stop destroying
  poll state; `collectStaged` stops the blocking gen-2 GC on the UI
  thread; the attachment gate stops walking staged bytes per keystroke;
  the thread composer's triple `DraftChanged` per keystroke becomes one.
  Verify: regression tests; the staged-collection test stays green.
- [ ] **P27 — Feed pages correctness** (BUG-11). The stale error banner
  clears on successful refresh; pagers gain an in-flight guard (no
  double-append, no duplicate-key throw); a row's reply answers that
  row, not the thread root; `Zone` stops freezing at context build.
  Verify: regression tests per fix across Log/Profile/CommitDetail.
- [ ] **P28 — Staleness suspects, proven** (BUG-16, VERIFY-18b,
  VERIFY-18c). A profile-edit + refresh test decides BUG-16 (same-id
  cells stale until restart) and fixes it if real; a same-key
  replacement test through `BindItemsSource` decides 18b; a
  chrome-hide test decides the completion popup staying open (18c).
  Whatever the tests prove real gets fixed; whatever they prove safe
  gets struck with the test as the evidence.
- [ ] **P29 — Small standalone bugs** (BUG-12, BUG-13, BUG-14). Avatar
  initial-letter fallback behind the URL image (and the `.fsi` claim
  becomes true); UserFlyout: detach close, timer owns its target,
  fetches cancel and order, hover re-fetch caches; SignIn: start
  disabled during `Preparing`, resumed pending sign-in reopens the right
  URL, `awaitSession` tied to the program's lifetime. Verify: regression
  tests per fix.

## Ledger

| Step | State | Notes |
|---|---|---|
| P17 | pending | slots: TabStrip badge, CommitRows actions-off, Feed riding report |
| P17 | committed 2026-11-02 | `Tab.Badge: Control voption`; `Host.Actions: bool` (star stays); `Ride` struct + `OnRide` with report-only Tail; RemotesInbox on TabStrip+Feed, SearchView on CommitRows.row, ThreadPane on OnRide; inbox asks through OnRide (no restore path — OnNeedMore serves only the chase); gates zero hits on added lines |
| P18 | committed 2026-11-03 | refactor: subscriptions get owners, effects ride commands, canceled bitmaps dispose — BUG-15/LEAK-1 dead; OnBodyReady replaces the name lookup; debounce restored on an attach-owned SerialDisposable (architect directive) |
| P19 | committed 2026-11-03 | refactor: arity records, the receipt union, and honest match shapes |
| P20 | committed 2026-11-03 | refactor: Approved: 11 services now contract + same-name module + create with all state (subjects, dictionaries, gate, timer, cancellation) allocated per factory call and zero module-level mutable state; moved bodies verified line-by-line against HEAD with no drift (emit-on-change/forget order, 500 ms settle, thread-pool timer, 64-entry/5 MB caps under lock, 5 s gate, 7-day TTL + startup scan + sweep, gate inversion + cancellation, one-flight Interlocked, raw-byte clipboard order + bitmap disposal); dead surface deleted (Desktop, desktop, unused AppSettings ctor); Tray kept as the no-contract control case; Env/Startup/AGENTS and the five test files changed only at sanctioned lines with constructor-expression-only test diffs; all three prior directives verified fixed ([<Struct>] PollContext, UiClock upcast, RouteStore/Timeline factory docs); all 16 battery-flagged added lines read and classified as moved pre-existing constructs (/> ignore discards, one Seq.toList, one '= Some' record-field false positive); drift greps re-run read-only: 0 constructor calls, 0 old factory names, 0 'new:' in the 13 touched signatures, 0 private/let mutable/:> on added lines; one undirected but harmless loosening noted: emitOnUi dropped its unneeded 'value: equality constraint in Stores.fsi (strictly generalizing, no caller affected). |
| P21 | pending | module shape: Env, Remotes, DeviceAuth + Startup pairing |
| P22 | pending | rule-8 comment migration for the remaining debt |
| P23 | pending | BUG-5 + BUG-9 |
| P24 | pending | BUG-6 + BUG-7 |
| P25 | pending | BUG-8 |
| P26 | pending | BUG-10 |
| P27 | pending | BUG-11 |
| P28 | pending | BUG-16 + VERIFY-18b/c (tests decide) |
| P29 | pending | BUG-12 + BUG-13 + BUG-14 |

Consumed elsewhere: BUG-3, BUG-4, LEAK-2, VERIFY-18a (Phase 1), BUG-15 +
LEAK-1 (P18 here), BUG-17 (Phase 1's sweep).