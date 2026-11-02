# Phase 1 MVUish architecture audit — 2026-11

Worktree `437cab8` on `main`. Read-only: no file outside this document was
changed, no build or test ran. Scope: every GUI file in the client — 17 Views
(15,693 lines), 18 Components (7,582), and the 7 scattered GUI files (3,887):
`Shell`, `Theme`, `Shortcuts`, `Icons`, `Tray`, `Startup`, and `Navigation`.
Total 27,162 lines over 42 files.

The audit's goal is architectural: cohesion and coherence. The codebase works,
but the same state layers, chrome, feeds, rows, cards, and dialogs are
re-implemented per file. Bugs found on the way are collected in §10 as issue
bodies, separate from the architecture work. Nothing is fixed in this pass.

---

## 1. Method and coverage

**Gates.** Every file was checked against the six skills (`devnull-mvuish-audit`,
`mvuish-controls`, `mvuish-element-shapes`, `mvuish-performance`,
`fsharp-module-shape`, `fsharp-function-parameters`) and the `AGENTS.md`
imperatives. Where the repo rule is stricter than the library rule, the repo
rule wins. The scattered files were additionally audited against the
shell/tray/app-entry exemption: every use of that exemption is named in §6.

**Auditors.** Eight read-only passes covered the Views and Components, grouped
by size:

| Pass | Files |
|---|---|
| Remotes thread | `RemotesThread.fs/.fsi` |
| Remotes rest | `RemotesModel`, `RemotesInbox`, `RemotesView` (+`.fsi`) |
| Feed pages A | `LogView`, `ProfileView`, `CommitDetailView` |
| Feed pages B | `OrganizationView`, `SearchView`, `SavedView`, `ScheduledView`, `NotificationsView` |
| Views rest | `SettingsView`, `Routes`, `SignInView`, `AboutView`, `CommitActions` |
| Big components | `Composer`, `CommitCard` |
| Mid components | `SearchDialog`, `WhoToFollow`, `Completion`, `Translation`, `LogChrome`, `SearchParts` |
| Small components | `UserFlyout`, `ProfileCard`, `ProfileShell`, `FeedParts`, `Feed`, `PageChrome`, `AsyncImage`, `ImageViewer`, `Avatar`, `Status` |

The synthesis pass (this document) audited the scattered files itself, ran the
cross-file counts below, and checked the issue #1 seam against every view.
Supporting reads: `Remotes.fs` (store), `SettingsStore.fs`, `Timeline.fs`,
the `MVUish.Avalonia` library source and XML, `Core/Commit.fs`, the UI test
suite. Every finding below carries its own `file:line` citation; this
document stands alone.

**Cross-file counts** (verified this pass, `Views/*.fs` + `Components/*.fs`):

| Search | Hits | Meaning |
|---|---|---|
| `Commits.vote` | 6 | one per feed view — the issue #1 count holds |
| `Pasteis.give` | 5 | SearchView swallows it |
| `toggleStar` | 7 | 1 definition + 6 call views |
| `CommitPins.pin/unpin` | 4 | ProfileView + CommitDetailView |
| `replaceNode` definitions | 4 | LogView, ProfileView, OrganizationView, SavedView |
| `commitCtx` definitions | 5 | +1 fake variant in SearchView |
| `CreateLogger` | 26 | 16 files; most resolve the logger per message inside `update` |
| `ToBinding` | 67 | the standard binding edge |
| `ContentControl()` | 47 | the rebuild-host population; §4.1 rule 8 names the ungated ones |
| `runWithLifecycle` | 14 | 11 route pages + Composer's dead branch + 2 nested RemotesThread controls |
| `runWithLifetime` | 5 | all in Components, all replace-only hosts |
| `BindItemsSource` | 8 | all keyed, recycling off, null-total (rule 11 conforms) |
| `Observable.add`/`.Subscribe(` | 8 raw | 2 rule-9 code violations (SearchDialog:468, RemotesInbox:439); the rest sit inside `Sub` wrappers or comments |
| `distinctUntilChanged` | 70 | concentrated in 10 files; 24 files carry none |
| `replayBuffer`/`refCount` | 0 | no shared slice anywhere |
| `ReferenceEqualityComparer` | 0 | no reference gate anywhere |
| real `throttle` | 1 | RemotesInbox:438 |
| `Brushes.` | 4 | all Avalonia static brushes (allowed); 0 themed-observable bindings in Views/Components |
| `let mutable` | 2 | AsyncImage:46,58 |
| `private` code hits | 2 | LogView:259, Theme:350 |

**Verdict scale.** `conforms` = the file matches its target shape (§2) and its
findings are local. `partial` = the right shape for parts of the file, but the
file carries concerns that belong to a shared owner, or repeats a banned shape
at several sites. `wrong shape` = the file is not the kind of thing its folder
promises.

---

## 2. File-level structure

### 2.1 The two target shapes

**The MVU page file** (every route page):

```
Props            contextual record: env, session, nav, route context
module State     Model, Msg, init, update, subscribe   (state only)
module Parts     stateless element functions           (layout only)
view             once-per-page composition
Page class       Program.mkProgram + runWithLifecycle
                 handle -> props.Context.AddDisposable
.fsi             exports Props + Page (plus State surface only when
                 tests drive it — LogView is the one sanctioned case)
```

**The component file** — one of three roles, stated in the file's `.fsi` doc:

- **Chrome host**: stateless where possible; props record whose fields are the
  holes (§3). `PageChrome`, `ProfileShell`, `Feed`, `LogChrome` (after §7).
- **Parts library**: leaf element functions, data in / `Control` out, no holes,
  no state. `FeedParts`, `SearchParts` (minus `followRows`), `Status`, `Icons`.
- **Self-contained component**: a `Page` control with its own program, props
  in, events out through the command path. This set stays small: `Composer`,
  `SearchDialog`, `WhoToFollow`, `ProfileCard`, and the two nested RemotesThread
  controls after the split (§7.4).

Service modules (stores, `Theme`) follow the `fsharp-module-shape` service
template. Both templates carry the standing rules: `.fsi` paired, nothing
`private` under a signature, ≤3 tupled parameters with contextual records
beyond, `inline` one-liners, no `let mutable` / `bool * value` / if-elif
ladders on discrete values.

### 2.2 Verdict table

Views:

| File | Lines | Verdict | Lead deviation |
|---|---:|---|---|
| RemotesThread.fs | 3,836 | wrong shape | Not a route page: one module holds 6+ elements and two private MVU programs (`module Player` :347, `module Recorder` :1958). Split per §7.4. |
| RemotesModel.fs | 1,885 | partial | Page state + pure row shaping + transport wrappers + WAV work in one file; `update` is 744 lines; 16 dead UI opens :3-43. |
| RemotesInbox.fs | 698 | partial | Parts grab bag (header, tabs, feed, popover, empty line); feed hand-built :446-478; `Parts.noThread` belongs to the thread pane :692. |
| RemotesView.fs | 152 | conforms | Split chrome, back row, band offset hand-built (:48-126) — §7.2. |
| LogView.fs | 1,123 | conforms | `let private` :259; `.fsi` constraint wall :163-176; feed `Items` ungated :1029. Best-shaped feed page otherwise. |
| ProfileView.fs | 1,214 | conforms | `headerHost` rebuilds per emission :1019-1045; the pin pair (99 lines) refetches the whole list :380-445. |
| CommitDetailView.fs | 1,215 | partial | `cardHost` retention-grade rebuild :942-951; `replies` 4 params :173; wall on `flyoutOf` :717-732; no chrome host :1163-1180. |
| OrganizationView.fs | 706 | partial | Full copy of the card-action state layer; `Navigation.go` inside view callbacks :561-595. |
| SearchView.fs | 578 | partial | Whole results body rebuilt per emission :494-551; dead vote branch :205; fake ctx streams :454-458; chrome stubs :545-549. |
| SavedView.fs | 523 | partial | Near byte-copy of OrganizationView `State`; feed row duplicates `ProfileShell.commitRow` :455-470. |
| ScheduledView.fs | 705 | partial | `row` 4 params with unused `props` :439-445; `bool * value` pair :446-449; dead `PageRequested` :77. Row gating is the best of the five. |
| NotificationsView.fs | 847 | partial | `groupHost` rebuilds per emission incl. closed groups :672-682; two clocks :348 vs :651; rail cell hand-built :466-534. |
| SettingsView.fs | 1,231 | partial | Four ungated rebuild hosts :546, :613, :713, :1146; store write inside `update` compute :351/:393/:438; scaffold hand-built :1187-1193. |
| SignInView.fs | 324 | conforms | Shape right; three behavior bugs (§10). |
| AboutView.fs | 118 | conforms | Stateless element page; third hand-built scaffold :110-117. |
| Routes.fs | 511 | conforms | Fresh page per navigation; guards correct; no cache. |
| CommitActions.fs | 27 | conforms | The issue #1 seam. Protocol-only module inside Views/ — placement rides with issue #1. |

Components:

| File | Lines | Verdict | Lead deviation |
|---|---:|---|---|
| Composer.fs | 1,823 | partial | Seven concerns in one file; `Register` branch dead at all five hosts :1806-1823; attachment gate walks staged bytes per keystroke :1263-1264; blocking compacting GC on the UI thread :238-251. |
| CommitCard.fs | 1,709 | partial | Four ungated rebuild sites :306, :367, :1117, :1174; two never-raised `Event` cases :43/:50; five functions over the arity limit; the 12-case `Event` union is the anti-slot (issue #1). |
| SearchDialog.fs | 781 | partial | `optionRow` 6 params, `ctx` unused :508-516; `BehaviorSubject` lane side-channel :380-391/:721; `Observable.add` :467-471; rebuilt rows retained until close. |
| WhoToFollow.fs | 729 | conforms | Cleanest component. `BindItemsSource` without a gate :635-639; logger per message :163-166. |
| Completion.fs | 320 | partial | `Shape` type declared mid-file :193; `Surface` documented "pure" but creates a subject, posts, returns Dispose (:225/:304/:319); rebuilt rows retained. |
| Translation.fs | 333 | wrong shape | A pure domain/model module in Components/; no control, no program. Move to `Views/TranslationModel.fs` (or Core with the cache). |
| LogChrome.fs | 279 | partial | Chrome host with zero holes: `Props` = Env/Session/Nav :31-45; three tabs hard-wired :65-81, strip fixed `"*,*,*"` :115; composer hard-wired :137-147; `PathChanged` allocates per route :209-214. |
| SearchParts.fs | 251 | partial | Leaves are right; `followRows` :235-242 is a subscription helper with an unnamed disposal owner; two regexes built per call on the keystroke path :43/:52. |
| UserFlyout.fs | 195 | partial | Stateful control without a program; popup has no detach close :133-168 (comment at :125 claims one); fetch has no cancellation or ordering :155-165. |
| ProfileCard.fs | 217 | partial | `cardHost` rebuilds the whole card per emission :167-186; three exports with no external caller (`statRow`, `identityHead`, `bioLine`). |
| ProfileShell.fs | 210 | conforms | Slot host done right. Pinned header duplicates `PageChrome.page` :160-189; `commitRow` :45-54 is a generic row in a profile-specific owner. |
| FeedParts.fs | 148 | conforms | Hole-free leaves. `laneElbowAt` takes 4 parameters :125-126. |
| Feed.fs | 118 | partial | `ScrollTop` carries the timeline store stream in props :106-116 (rule-3 shape); `Restore.Landed` is a `member val` mutable :19; the `untilDetached` scroll-top subscription never restarts after first detach; `Items` ungated :69. |
| PageChrome.fs | 124 | partial | `bool * string` Busy/Empty slots :69-70 spread to 5 call sites; dead `busy`/`empty` exports; no footer slot; every projection runs twice :81-98. |
| AsyncImage.fs | 126 | partial | Canceled-after-decode bitmaps never disposed :92-103 (native leak); two `let mutable` :46/:58; `Placeholder` slot dead at all four call sites; `DecodeWidth` dead when `DecodeHeight` is set. |
| ImageViewer.fs | 120 | partial | Imperative modal outside MVU; whole stage rebuilt per key press :48-117; no empty/out-of-range guard :29/:52. |
| Avatar.fs | 71 | partial | No initial fallback under `Some url` :42-59 — the comment :44-45 and the `.fsi` claim one. |
| Status.fs | 28 | partial | Namespace break (`DevNull.Client.Status`, not `.Components`); dead `error` export; `busy` doc claims a spinner. |

Scattered GUI (§6 has the detail):

| File | Lines | Verdict | Lead deviation |
|---|---:|---|---|
| Shell.fs | 1,609 | partial | ~960-line `do` block owns fifteen concerns; five hand-built copies of the overlay shape; four hand-rolled identity guards; sidebar/aside/chrome guards all correct. |
| Theme.fs | 1,386 | partial | `let private typingDot` :350; `Brushes` over-exports ~14 observables nothing consumes; `.fsi` doc invites the banned binding; `bindClass` returns `unit`. |
| Shortcuts.fs | 373 | partial | Key table right; `moveCardFocus` is the third keyboard-walk implementation; `helpCard` is a dialog view living in the keyboard module. |
| Icons.fs | 164 | conforms | Geometry parsed once and cached :147-153; property-assignment style :157-160. |
| Tray.fs | 89 | conforms | One `Observable.add` on the unread store :75-81 — the tray exemption, named. |
| Startup.fs | 231 | conforms | App entry; settings→`Theme.apply` :186-187 is the entry exemption; file-hold single instance :120-136; the one sanctioned sync wait :142-145. |
| Navigation.fs | 35 | conforms | `sectionOf` :16-35 re-encodes route knowledge as prefixes — the third copy beside `Routes` and the Shell entries. |

### 2.3 Repeating file-level deviations

- **Redundant constraint walls.** `LogView.fsi:163-176` (also incomplete: 13 of
  15 constraints), `CommitDetailView.fs:717-732` on `flyoutOf` — LogView's
  same function infers them. Walls stay only on type definitions (Shell,
  RemotesView `Page` — allowed).
- **`private` under a signature.** `LogView.fs:259`, `Theme.fs:350`. Both are
  hidden by omission already; delete the marker.
- **Arity breaks (4+ parameters).** Twelve in `RemotesThread` (`ghost` :121,
  `linkCard` :222, `receipts` :859, `reactionChip` :965, `list` :1651,
  `composer` :2270, `confirmPick` :3144, `actionsMenu` :3219, `orgMenu` :3352,
  `footer` :3593, `pane` :3623, `column` :3812 — `solo` is removable there);
  `CommitCard` `textWithMentions` :222, `segmentView` :245, `bodyGen` :280,
  `iconAction` :851, `railCell` :946; `Composer` `chip` :1163, `actionRow`
  :1697; `SearchDialog.optionRow` :508; `FeedParts.laneElbowAt` :125;
  `ScheduledView.row` :439; `CommitDetailView.replies` :173. Every one has a
  natural contextual record over its own fields.
- **Dead exports / dead code.** `Composer.PollDraft` + `Register`; `CommitCard.view`,
  `tombstone`, `Event.PinToggled`, `Event.ThreadRequested`; `PageChrome.busy/empty`;
  `Status.error`; `ProfileCard.statRow/identityHead/bioLine`; `RemotesInbox.RowCtx`;
  `RemotesModel.termOf/mentionOf`; `SettingsView.Props.Nav`; `AsyncImage.Placeholder`;
  `Theme.Brushes` members listed in §6.2; dead messages `SearchView.Typed` :83,
  `ScheduledView.PageRequested` :77, `SettingsView.MeNote` :84.
- **`bool * value` pairs.** `PageChrome.PageProps.Busy/Empty` :69-70 (5 call
  sites), `ScheduledView.busyWith` :446-449, `RemotesThread.receiptWord` :827.
- **One-liners without `inline`.** RemotesModel store wrappers :445/:726/:730/
  :741/:749/:871/:877; WhoToFollow:80; Translation:86; LogChrome:84;
  SearchParts:75/:79; `Status` wrappers; `Icons.icon`.
- **Dead opens.** RemotesModel :3-43 (16 UI opens), RemotesInbox :3-42 (8),
  Routes.fsi:7, PageChrome:8, Shell carries several stale ones.

---

## 3. Content projection and composition map

### 3.1 The slot taxonomy

| Web-component idea | This repo's form | Exists today |
|---|---|---|
| Default slot | `Body: Control` | `PageChrome` |
| Named slot | `Header: Control list` field | `PageChrome` |
| Deferred / repeatable slot | `Header: unit -> Control` factory | `ProfileShell` |
| Scoped slot (slot with props) | `Row: 'item -> Control` render prop | `Feed`, `ProfileShell.Feed` |
| Action slot (host runs the effect) | `{ Command: 'a -> unit; Busy: IObservable<...> }` | `CommitCard.Pin` only |
| Decorator slot | wraps one `child: Control` | `UserFlyout.attach` |

Rules the audit holds every host to:

1. A slot is a `Control` or a factory; it is never `IObservable<Control>`.
   Liveness lives in the projected content's own model slices; the host binds
   nothing on the slot's behalf.
2. Factories defer and repeat. `unit -> Control` builds nothing until the host
   shows the hole and gives a fresh instance per rebuild or per row; a plain
   `Control` slot is single-use (one visual parent).
3. Render-prop rows stay effect-free, so recycled rows release with the page
   program. The host owns the call, the busy stream, and the fold — issue #1's
   shape exactly.
4. An action slot is always `{ Command; Busy }`; `CommitCard.PinToggle` is the
   model.
5. Three or more holes make a props record; the record is the host's whole
   public surface (AGENTS 21 — slots replace parameter threading;
   `Composer.Draft` is the hole done right, `Composer.Register` is the
   symptom: the handle crosses into the `Page` only to be handed back, and no
   host uses it).

### 3.2 Per-component hole map

Holes today (kind) → holes it should have; "absorb" = what a shared control
should take over from callers.

| Component | Holes today | Holes it should have / absorb |
|---|---|---|
| **PageChrome** | Header `Control list`; Body `Control`; TitleOf/ErrorOf/Busy/Empty as `'m ->` projections (Busy/Empty are `bool * string`) | Title as `Control` slot; Empty and Busy as `Control voption`/DU (one `BusyState`); Footer slot; kill the tuple slots; absorb the hand-built scaffolds in SettingsView :1187, SignInView :297, AboutView :110 |
| **ProfileShell** | Header/Meta factories; Tabs (Label/Active/Choose); Feed.Row; Busy/Empty/Note `'m -> string option`; FeedLabel | Meta optional (ProfileView passes `Panel()` :1162); Feed.Footer slot; feed scroll slots or a documented always-top rule; absorb `levelChip`, `metaLine`, `activityView`, `streaksView`, `xpView`, `achievementsChip` (ProfileView :762-915); share the pinned header with PageChrome |
| **Feed** | Row render-prop; Footer `Control voption`; ScrollTop observable; Restore float; OnScroll/OnNeedMore actions | Header and Empty slots; `HasMore` gate for OnNeedMore; ScrollTop as a handle/model token, not a store stream; gate `Items` by reference by default; absorb the four hand-built feeds (RemotesInbox :446-478, RemotesThread `list` :1651-1785, SearchView results :530-536, WhoToFollow :627-647) and the four hand-built footers (ProfileShell :135-145, LogView :1040-1048, Notifications :795-803, Saved :469-478) |
| **LogChrome** | none — `Props` = Env/Session/Nav | `Tabs: Tab list` (ProfileShell's Tab shape), Header/Footer factories, `Composer: unit -> Control`, `RailNode: unit -> Control`, `OnReload`; strip columns from the tab count; the chrome must not know `Composer.Fresh`, `Timeline.CommitLanded`, or `/scheduled` |
| **CommitCard** | `OnEvent: Event -> unit` (12 cases); `Pin: PinToggle voption` (the one true action slot); Flyout bundle; Translation state channel | Issue #1 surface: generalize `PinToggle` into `ActionSlot = { Command: Commit -> unit; Busy: IObservable<string option> }`; add `Star`, `Pastel` (+Visible), `Vote`, `Translate` slots; union shrinks to intent cases; drop dead cases; absorb `Parts.actions` toolbar and the duplicated identity line :891-920/:1035-1055 |
| **Composer** | `OnCommitted`/`OnScheduled`/`OnOpenQueue` bare actions; `Draft: DraftSource` (correct hole); `Register` (dead, wrong direction) | Delete `Register`, expose the handle; split into `CompletionLane`, `AttachmentStrip` (shared with RemotesThread :1813-1865), `PollEditor`, `SchedulePicker`, `ComposerToolbar`; share the key lane with RemotesThread :2447-2503 |
| **SearchDialog** | `optionRow` internal `Content` hole only; `Entry` is closed | Move onto the shared Palette host (§3.3): box, status, scroller, keyboard walk, followRows owned once; dialog card onto the shell's DialogChrome |
| **Completion** | `Row.Content: Control`; `Actions` callbacks; `Surface.Rows: IObservable<Control array>` transport | Same Palette host; `Rows` stays private; `StatusLine`/kbd chip extracted; `.fsi` "pure view" claim fixed |
| **WhoToFollow** | `OnOpen` action | `RenderRow` default `UserRow`; Header/Empty/Busy/Error slots; `Max`; the aside is otherwise right |
| **ProfileCard** | none | none needed; fix `cardHost`; keep only used exports public |
| **UserFlyout** | `child: Control` decorator; `Card` implicit | optional `Card: (User -> ProfileDetails option -> Control) voption`; the card head should call `ProfileCard.identityHead` instead of re-building it :58-77 |
| **AsyncImage** | `Placeholder: Control voption` (dead) | `unit -> Control` factory or documented single-owner rule; `Decode` as one DU; a `Failed` state distinct from loading |
| **Avatar / FeedParts / Status / Icons** | none | keep hole-free; `Avatar` optional `Fallback`; FeedParts `railRow` helper |
| **Shell (host layer)** | none — five hand-built overlay copies :714-741, :746-804, :808-832, :838-884, :890-971 | one `DialogChrome` (scrim + host + open/close), used by compose/search/help and the undo toast's host; the shell keeps ownership of when |

### 3.3 The palette trio (SearchDialog + Completion + followRows)

They are not three copies of one palette: SearchDialog owns box + keyboard +
rows + follow subscription; Completion owns rows + armed chip only; the
keyboard walk for Completion lives in Composer (:1040-1094). Both hosts push
`BehaviorSubject<Control array>` through `Dispatcher.UIThread.Post`
(SearchDialog :553/:630, Completion :225/:304) into `SearchParts.followRows`
— `IObservable<Control>` in exactly the shape §3.1 bans, plus rebuild
retention in both hosts. `Shortcuts.moveCardFocus` (:145-286) is the third
keyboard-walk implementation, the only one that reaches into another
control's internals (`IndexFromContainer`, the `CommitRow` class). One
`Palette`/`RowsHost` component owns the scroller, the row subject, the follow
subscription, and the keyboard walk; all three surfaces supply row models and
actions. Beside it: the kbd chip (SearchDialog :397-405, Completion :86-93,
Shortcuts :307-313), the status line (SearchDialog :409-420, Completion
:68-75, SearchParts :244-251), and the person row (SearchParts :137-171,
Completion :112-139, WhoToFollow :461-592) are three shared parts waiting to
exist.

---

## 4. Component-level composition

### 4.1 The 11 leak rules, verdict per rule across all Components

1. **Themed brushes as classes — pass.** Zero `Brushes.` observable bindings
   in Components/ or Views/ (4 hits are Avalonia statics: SearchDialog:446,
   SearchView:317, RemotesThread:1387, :3116). The only consumers of
   `Theme.Brushes` observables are the shell (§6.1, exempt). RemotesThread:3116
   hand-paints a scrim instead of a token.
2. **Shared state in env stores — pass.** `RemotesModel` copies the store into
   the model on `StoreChanged`; views bind only model slices.
3. **External observables through `Sub` — pass, one shape violation.**
   Every page's `subscribe` bridges clock/session/store/watch correctly. The
   violation: `Feed.Props.ScrollTop` carries `Timeline.Requests` (an
   app-lifetime store stream) in a props record (Feed.fsi:18, LogView:1049-1055).
   It happens to be consumed through the rule-9 standing exception, but the
   prop is the banned shape; a handle or model token fixes it.
4. **Own model only — pass.** Every property binding in Components sources the
   component's own program model (RemotesThread's `PaneWidth` :3642 is
   control-owned, bounded by the pane graph).
5. **Lifecycle — pass.** The 11 route pages run `runWithLifecycle` with the
   handle in `props.Context`. The five `runWithLifetime` components
   (Composer:1808, LogChrome:277, ProfileCard:215, SearchDialog:774,
   WhoToFollow:727) sit on replace-only hosts — correct, but the convention
   (the handle is dropped; the host must never re-show) is written nowhere.
   `Composer.Register`'s `runWithLifecycle` branch is dead at all five hosts.
6. **No cached routes — pass.** `Routes.build` builds fresh pages; no cache
   exists. Remove the route-caching wording from `Composer.fsi:58-62`.
7. **Bitmaps control-owned — one defect.** `AsyncImage` drops the decoded
   bitmap when a load is canceled after decode (:92-103) — native leak under
   scroll/recycle. Composer:1177 and RemotesThread:1818 conform, with a minor
   never-attached caveat at RemotesThread:1818-1828.
8. **Rebuilt content carries an identity guard — the big architecture
   failure.** Twelve-plus ungated `ContentControl.Content(obs)` rebuild sites;
   each rebuilds a subtree per model emission, and each rebuilt subtree's
   bindings stay rooted until the program ends. Retention-grade:
   `CommitDetailView:942-951` (a model-bound card per emission on a page that
   ticks every minute). Waste-grade: `ProfileView:1019-1045` (whole header),
   `SearchView:494-551` (whole results body), `NotificationsView:672-682`
   (event rows per group incl. closed), `SettingsView:546/:613/:713/:1146`
   (palette/theme/picker/devices), `ProfileCard:167-186`, `CommitCard:306/:367/
   :1117/:1174`, `RemotesThread:3708-3713` (avatar stack) + `:3605-3618`
   (composer on Access flip), `RemotesInbox:363-421` (tab strip per waiting
   count) + `:548/:572`, `Completion:313`, `SearchDialog:634-641` (gated after
   the allocation). One `bindContent(keyOf, build)` helper that gates with
   `distinctUntilChanged`/reference compare owns this whole class.
9. **`Theme.bindClass` on program-model streams; no `Observable.add` — 2
   violations.** `SearchDialog.fs:467-471` (`TextChanged`), `RemotesInbox.fs:439`.
   `Feed:111` and `SearchParts:239` sit inside `Sub.ofId … untilDetached`
   wrappers — the standing exception, but the returned disposable's owner is
   documentation-only. `Theme.bindClass` itself is used correctly at every
   call site (program-model slices), but it returns `unit`, so a rebuilt
   subtree cannot release its class observer (RemotesThread:2315-2327
   accumulates one per composer rebuild).
10. **Timers stop on every path — three gaps.** `UserFlyout`'s `openTimer`
    can fire after the trigger detaches and no code closes the popup on detach
    (:133-168). RemotesThread's landing flash timer accumulates one timer +
    handler per re-landing on the same border (:1764-1773). `Solo` never
    releases the last playback (:55-68). `Player`/`Recorder` ticks are
    Sub-owned and stop correctly.
11. **Feeds keyed, recycling off — pass on shape.** All 8 `BindItemsSource`
    sites key correctly, pass `supportsRecycling = false`, and stay null-total.
    The gap is gating: every collection slice is rebuilt and re-diffed per
    emission (§4.3).

### 4.2 Element-shape findings

- Props in / events out holds everywhere except: `RecordLane` calls `report`
  inside `update` compute (:2187, :2210, :2217, :2221) and `Player.update`
  performs outward effects in compute (:459-461, :499, :501-504, :528-529);
  `RemotesModel.update` writes the store in compute (10 arms, :1256-:1753);
  `SettingsView.applyPrefs` (:351, :393, :438) and `SearchDialog.RecentsCleared`
  (:344-346) do the same. All belong on `Cmd.ofAction`.
- Stateless vs stateful is chosen correctly at the surface everywhere except
  `UserFlyout` (state without an owner) and `ImageViewer` (a modal as a
  recursive closure).
- `WhoToFollow`, `ProfileCard`, `SearchDialog` `.fsi` surfaces are tight;
  `ProfileCard`, `PageChrome`, `Status` over-export (§2.3).

### 4.3 Performance shapes (systemic)

- **No shared slices.** 0 `replayBuffer`/`refCount`, 0 `ReferenceEqualityComparer`
  in the tree. Every projection is a cold map rebound per consumer:
  `RemotesView.shape` ×4 (:48-118), `LogChrome.active` ×4, `PageChrome`
  title/busy/empty ×2 each (:81-98), `ProfileShell` six double maps,
  `commitCtx` rebuilt per row in LogView (:913) vs once in `view`
  (ProfileView :1122 — the correct shape), `RemotesThread.thread` bound 9+
  times with no share (:3662-3667).
- **No collection gates.** Every feed re-runs `toRows`/`rowsOf` and the keyed
  diff per emission, including minute ticks: LogView:1029, ProfileView:1179,
  CommitDetailView:1012/:1132, OrganizationView:675, SavedView:456,
  ScheduledView:622, NotificationsView:787, SearchView:530, WhoToFollow:635,
  RemotesInbox:671, RemotesThread:1656-1684.
- **Structural compares over byte arrays.** `Composer.attachmentStrip`
  :1263-1264 and `RemotesThread.staged` :2407-2408 structurally compare
  `ImageAttachment list` (contains `Bytes: byte array`) per emission — the
  O(staged bytes) walk twice per keystroke; `stagedVoice` :2620-2625 defeats
  its own length/name key.
- **Dedup after formatting.** RemotesThread:3753-3760, :2896-2908, :2952-2968,
  :900-916 format strings before `distinctUntilChanged`.
- **Hot inputs unthrottled.** RemotesThread scroll :1694-1742 (three dispatches
  per event, `ThreadScrolled` writes the store each time — a model→store→model
  feedback loop), typing :2436-2538 (three `DraftChanged` per keystroke),
  pointer/volume :707/:735 (volume writes `settings.json` synchronously per
  tick), SettingsView picker :709-710, SearchView box, RemotesInbox name box.
- **No-op arms allocate.** RemotesModel (:1154-:1832, 12 arms), OrganizationView
  :349-361, SavedView:299-311, NotificationsView:317-323, LogChrome:209-214,
  SearchDialog `Typed`/`Moved`/`Hovered`, Composer poll/schedule no-op arms.
- **O(N²) shaping.** `RemotesModel.threadRows` :562-601 (`rows @ […]`,
  `List.removeAt`), `summarizeReactions` :683; per-row `Map.tryFind`/`tryFind`
  scans in RemotesThread (:270, :902, :1006, :1065, :1502) want a
  `Map<int, Message>` index.
- **The fix shape** (`mvuish-performance` §1/§5/§7): gate the identity at the
  boundary (`distinctUntilChanged` / reference compare), share the gated slice
  (`replayBuffer 1 >> refCount`) when it has multiple consumers, gate
  collections by reference before `BindItemsSource`, and never rebuild a
  subtree without a key. A small `Slices` module (`share`, `gate`,
  `gateByRef`) plus the review gate makes this the default instead of 70
  scattered `distinctUntilChanged` calls.

---

## 5. Page-level composition

**Program shape: conforms everywhere.** All 11 route pages follow §2.1:
`runWithLifecycle`, handle to `props.Context.AddDisposable`, no route cache,
external streams only through `Sub`. The failures below are composition and
duplication, not program shape.

**Per-page findings (severity order):**

- **CommitDetailView.** `cardHost` retention (§4.1 rule 8) — the one
  leak-grade page finding. Reply targets the thread root for every row
  (:358-364 with :1074-1084). The page hand-assembles back line, error, note,
  busy, ancestors, card, replies, pager, actions, composers, related into one
  `StackPanel` (:1163-1180) — a `ThreadChrome`-shaped host is missing.
- **SearchView.** The whole results body rebuilds per emission (:551), so the
  minute clock destroys and rebuilds the list; scroll resets (suspected).
  The vote branch is dead (`selectedOption(Map.empty, …)` is always `None`,
  :205) and `StarDone`/`PollVotedDone` drop the returned commit (:256-269) —
  cards go stale until the next search. Fake ctx streams allocate constant
  observables per row (:454-458). This page bypasses the feed chrome entirely.
- **SettingsView.** Four per-emission rebuild hosts; the picker rebuilds up to
  80 buttons per keystroke with no throttle (:713-745); ~450 ICU timezone
  conversions run on the UI thread at first build (:673-687); store writes in
  compute. `picker` (:693-705) is the best hole in the page set and the model
  for the shared SearchList (issues #9 and #13 will need it).
- **NotificationsView.** `groupHost` rebuilds per emission including closed
  groups; two time sources (`DateTimeOffset.UtcNow` in `toRows` :348 vs
  `model.Now` :651); the rail cell (:466-534) is a hand-built
  notification-row element.
- **OrganizationView / SavedView.** Two near-identical `State` modules
  (card-action layer, `SessionChanged` mirror, `replaceNode`, `commitCtx`);
  SavedView's feed row duplicates `ProfileShell.commitRow`. Both call
  `Navigation.go` inside view callbacks (:561-595, :394-429) where SearchView
  shows the conforming route-through-message shape (:195-197). URL escaping
  differs between them (SavedView:427 raw, SearchView:468 escaped).
- **LogView.** Best-shaped page. Stale error banner (success arms never clear
  `Error`, :392-418, :551-558); no in-flight guard on the pager (:538-550) —
  two concurrent same-cursor fetches append the page twice and the keyed feed
  throws on repeated keys; `Zone` frozen at ctx build (:890).
- **ProfileView.** `headerHost` waste; the pin pair refetches the whole commit
  list (:380-445) — the only pin implementation that does; the `Meta` slot is
  a dead `Panel()` (:1162). The header sections (§3.2) must decompose for
  issue #17.
- **ScheduledView.** Right gating patterns (`Parts.body` prebuilds
  folded/full and swaps under a gate :396-421 — the model two-state shape
  other pages should copy); wrong parameter shapes; hand-built row frame and
  offset pager.
- **SignInView / AboutView / Routes.** Shape conforms; §10 carries their
  behavior bugs and the scaffold duplication.

**The Remotes page (four files, 6,571 lines)** is one route wearing four loose
files: the model (1,885), the inbox pane (698), the thread pane (3,836 —
wrong shape per §2.2), and the view (152). It hand-builds its split chrome,
its feed, its scroll/restore machinery, its composer, its popovers, its
confirm dialogs, and its empty states; almost every piece has a shared owner
or a slot target in §3.2 and §7. The `Views/Remotes/` page folder with the
§7.4/§7.5 splits is the structural fix.

**Issue #1 seam check (verified against all six views).** The poll trio
(request + in-flight + settle) is ~50 lines in LogView (:443-492),
ProfileView (:461-510), CommitDetailView (:365-396, :615-631),
OrganizationView (:276-325), SavedView (:226-275), plus a broken copy in
SearchView (:204-269). Pastel pair ~22-27 ×5; star pair 14-22 ×6 (all already
calling the one `toggleStar`); pin pair 68-99 ×2; translate block 74-92 ×6;
`commitCtx` 41-59 ×5; `replaceNode` ×4; `SessionChanged` mirror ×3. Roughly
600 lines of copy-paste. Every view also owns the `Voting`/`PollChoices`
in-flight fields and per-arm error resets. The consolidation target stays as
issue #1 states it: one owner beside `toggleStar` exposing command + busy
streams in the `PinToggle` shape, pages keeping only their fold. The card's
12-case union loses `PinToggled` and `ThreadRequested` (never raised) and the
six effect arms; `CommitCard.Props` gains the `ActionSlot` fields (§3.2).

---

## 6. Scattered GUI outside the two folders

### 6.1 Shell.fs (1,609 lines) — partial

**Shape.** One module: constants (:59-72), `Layout`/`layoutOf`, `NavEntry`,
`NavContext`/`navItem`, `FooterContext`/`footer`, `BandProps`/`captionButton`/
`band`, `composeDialog`, `termsDialog`, `focusInlineComposer`, then the
`Shell` class — a ~960-line `do` block (:634-1591) that owns window chrome,
routing, layout subjects, five overlays, the sidebar, the aside, the log
chrome feed, the main column, the SplitView, the band, the pollers/watchers,
close-to-tray, the tray, the shortcuts, and the sign-in callback. The `.fsi`
exports only the type; parameters conform (every helper ≤3, tupled;
`composeDialog`/`termsDialog` take the lean env-intersection form).

**Exemption uses (named).** The shell exemption covers every store or
app-lifetime observable bound here — all listed:

- `NavEntry.Badge`/`Visible` carry `env.Unread.Count` (:1000),
  `env.Remotes.Observe` slice (:975-977), and a session slice (:979-984),
  bound in `navItem` (:162, :169-171, :202). Seven items built once;
  nothing accumulates. Holds.
- `footer` binds `session.Observe` via `Observable.add` (:233) and two
  `signedIn` maps (:272, :283). One instance. Holds.
- Themed brush observables: `paneBackground` (`Brushes.surface`/`background`
  :1221-1222 → `.Background` :1244), `Brushes.border` (:1243), the main host
  `Brushes.card`/`border` (:1367-1368), the badge `accent`/`onAccent`
  (:154/:166), the drag surface `transparent` (:321), and
  `Brushes.themed Keys.background` (:130). These are the only
  themed-observable property bindings in the app — allowed here, banned
  everywhere else (rule 1). Holds, by design.
- The overlay subjects (`composeOpen` :714, `searchOpen` :746, `helpOpen`
  :808, `termsOpen` :838, `undoToast` :891), the layout subjects (:679-694),
  `router.Changed` (:673, :1304, :1449), `ClientSize` (:686),
  `timeline.Deletions` (:901), and `env.Routes.Path` via `isRemotes`
  (:1349-1363 → `mainWidth` :1370) — all `Observable.add`, all one-instance.
  Holds.
- `env.Session.Observe` feeds the `UserSettings.Refresh` bridge
  (:1495-1503, distinct-gated on the user id). One instance. Holds.
- `windowActive` pulse (:1513-1527) — window events into one subject. Holds.

**Identity guards (rule 8) — conform, but hand-rolled four times.** The
footer (:232-265, guard subject `account`), the aside (:1262-1282,
`asideUser`), the log chrome (:1316-1331, `chromeUser`), and the terms gate
(:856-878, `termsAsked`) all repeat the same shape: a guard
`BehaviorSubject`, a source `Observable.add`, an inequality check, then
`host.Content <- build` or `null`. One helper formalizes the four. Shared
caveat: every guarded cell captures the session snapshot's name/avatar at
build time, so a same-id refresh leaves the built row stale (§10 BUG-16).

**Composition findings.**

- Five hand-built copies of the overlay shape — subject + `ContentControl`
  host + scrim `Border` + layer `Panel` + open/close `Observable.add`:
  compose (:714-741), search (:746-804), help (:808-832), terms (:838-884);
  the undo toast (:890-971) is the non-modal, bottom-anchored fifth. One
  `DialogChrome` owns the shape; the shell keeps the open/close decisions.
  The scrim color `#8A000000` is spelled five times here (:735, :800, :826,
  :881, :1401) and once differently in RemotesThread (:3116) — a missing
  `Keys.scrim` token.
- `termsDialog` (:455-571) is a hand-rolled program outside any `Program`:
  subjects `agreed`/`busy`/`failure` (:465-467), an `asyncEx accept()` that
  calls the API and mutates subjects (:473-493), all inside a view function.
  Shell-exempt, but it is a component by shape (props in, events out) and
  should be one.
- The search overlay builds services in view code (:760-780): the
  `Search.Session` and its logger are constructed inside the shell's
  `Observable.add`, inline with control assembly. The dialog disposes the
  session on detach — correct today, but the ownership lives in a comment,
  not the `Props` contract (SearchDialog.fsi:17-21 vs :780-781).
- Visual-tree scraping by control name, twice: `composeDialog` posts a
  layout-pass-late focus onto `"ComposerBody"` (:440-446), and
  `focusInlineComposer` (:577-597) finds the same name across the window.
  The shell and the composer are coupled through a string; keep it, and keep
  the name as the contract it is.
- The constructor's fifteen concerns extract along existing seams: `Sidebar`
  (wordmark, entries, commit button, collapse, search row, footer
  :1057-1212), the aside (:1254-1289), the chrome feed (:1313-1331), the band
  (:318-403), the overlay set, the undo toast. The shell then composes them;
  AGENTS 20 (lean hosts, module helpers) applies to the host layer too.
- `maximize` rebuilds its icon per window-state emission (:365-367) — three
  states, harmless, but the same ungated `Content(obs.ToBinding())` shape
  §4.1 rule 8 bans elsewhere.

### 6.2 Theme.fs (1,386 lines) — partial

**Shape.** Keys, Fonts, `solid`, `Brushes` (observables), `StyleClass` +
`name` ladder, `bindClass`, the lane system, `styleOf`, the style table,
palettes, `install`, `apply`. The style system is coherent: classes only,
dynamic-resource setters, generated lane blocks, sibling pseudo-class styles
with the stall reason written down (:880-882).

**Findings.**

- `let private typingDot` :350 — the second `private`-under-`.fsi` in the
  tree. Delete the marker.
- **`Brushes` over-exports the banned shape.** Of ~21 exported observables,
  the shell binds nine (`themed`, `background`, `surface`, `card`, `border`,
  `accent`, `onAccent`, `solid`, `transparent`). The rest — `text`, `muted`,
  `accentHover`, `accentPressed`, `danger`, `success`, `popover`, `rail`,
  `input`, `primarySoft`, `laneFallback`, `lane`, `laneOf`, `constant` — have
  no consumer (verified: zero `Brushes.[a-z]` hits outside Theme/Shell).
  Rule 1 says views must never bind them; the `.fsi` doc (:13-15) says
  "views bind it once and never re-style" — the doc invites the leak the rule
  bans, while the implementation's own comment (:1052-1054) says no control
  binds a brush observable. Shrink the module to what the shell binds and fix
  the doc, or move the shell's nine uses into Shell as local helpers.
- `bindClass` returns `unit` (:253-261): the `Observable.add` disposable is
  discarded, so rebuilt subtrees cannot release their class observer. Return
  `IDisposable`; callers that rebuild dispose on replace.
- Two same-name pairs in one module: `Brushes.themed` (:62) vs the
  dynamic-resource `themed` (:344), and `solid` (:52) vs `Brushes.solid`
  (:110). Rename the style-side ones.
- No `Keys.scrim`; the scrim hex is hand-spelled six times (§6.1).
- Parameters and ladders conform (`name`, `laneClass`, and `layoutOf`-style
  matches are discrete or ordered ranges; `typingDot` 2 params; `styleOf` 2).

### 6.3 Shortcuts.fs (373) — partial

One tunnel handler on the window (:84-143), match on discrete keys, guards
documented; `Pending` is a deadline cell, not a timer — one per install,
window-owned. Two placement findings: `moveCardFocus` (:145-286) is the third
keyboard-walk implementation and the only one that reaches into another
control's internals (§3.3); `helpCard` (:335-372) is a dialog view living in
the keyboard module — under a `DialogChrome`, Shortcuts supplies the
`bindings` rows (:289-304) and the chrome supplies the card. Note the sheet
lists Enter/Y/R/Q, which the window table does not handle: `CommitCard`'s
quiet-key block (:1319-1336) owns them, so the sheet and the card must stay
in sync by hand. `chip` (:307-313) is the third copy of the kbd chip (§3.3).

### 6.4 Icons.fs (164) — conforms

`StreamGeometry` parsed once per glyph into a `ConcurrentDictionary`
(:147-153) — the parse cost question is answered: cached. `iconAt` uses
property assignment (:157-160) instead of the fluent chain (AGENTS 18);
`icon` is a one-liner that wants `inline`. No observables, no lifetime.

### 6.5 Tray.fs (89) — conforms

One `Observable.add` on `props.Unread` writing the tooltip (:75-81) — the
tray exemption, named; one icon per process. Menu items are plain callbacks
(`menuItem` 2 params). The `TrayIcon` rides the Application and dies with the
process, as the `.fsi` states.

### 6.6 Startup.fs (231) — conforms

App entry: Fluent first then `Theme.install` (:28-30); the settings store →
`Theme.apply` bridge (:186-187) is the entry exemption (one instance,
process lifetime). Single instance is an exclusive `FileStream` with
`DeleteOnClose` (:120-136) — the operation under `try` with the failure in
hand (AGENTS 25 conforms); the second instance completes the callback
headlessly through the one sanctioned sync wait (:142-145, documented).

### 6.7 Navigation.fs (35) — conforms, one cohesion note

`go` is right (`inline`, tupled). `sectionOf` (:16-35) re-encodes route
knowledge as string prefixes — the third copy of the URL→section mapping
beside the `Routes` table and the Shell's `entries` (:986-1043, which spells
every URL and section again). When §7's chrome work touches routes, `Routes`
should own the section of each route definition.

---

## 7. Target structure

### 7.1 Folder rules

- **`Views/`** — MVU pages only: one route per page, its model file(s), its
  `Parts`. Multi-file routes group in a page folder (`Views/Remotes/`), which
  keeps the fsproj order honest and the unit visible. Views never render for
  another view (today's break: `RemotesInbox.Parts.noThread` :692-698, used
  by the thread pane :3834). Page models that are pure domain state live in
  the page folder (`Views/Remotes/RemotesModel.fs`); `Translation.fs` moves
  out of Components to `Views/TranslationModel.fs` (or Core, with the cache).
- **`Components/`** — the three roles of §2.1, one role per file, stated in
  the `.fsi` doc. Chrome hosts end in `Chrome` or `Shell` (`PageChrome`,
  `ProfileShell`, `ThreadChrome`, `SplitChrome`, `LogChrome`, `DialogChrome`);
  parts libraries end in `Parts` (`FeedParts`, `SearchParts`, and the thread
  bubble set after §7.4); self-contained components keep their domain names
  (`Composer`, `SearchDialog`, `WhoToFollow`, `ProfileCard`). All under
  `DevNull.Client.Components.*` (fixes the `Status` namespace break).
- **Root** — the host layer and its libraries only: `Shell`, `Theme`,
  `Shortcuts`, `Icons`, `Tray`, `Startup`, `Navigation`, plus the stores.
  Shell composes named elements (Sidebar, Band, DialogChrome, aside, chrome
  feed); it stops inlining them.

### 7.2 The chrome-control set

What exists and what each must absorb:

| Control | Covers today | Must absorb |
|---|---|---|
| `PageChrome` | title/error/busy/empty + pinned header + body (5 pages) | `BusyState` slots; footer; title/empty as Control slots; the three hand-built scaffolds (Settings/SignIn/About); `Status.panel` as its static text owner |
| `ProfileShell` | profile header host, tabs, feed column (2 pages) | shared pinned header with PageChrome; optional Meta; feed footer/scroll slots; the profile header sections (for #17) |
| `Feed` | keyed virtualized list + footer + scroll slots (4 hosts) | items gate by default; `HasMore`; header/empty slots; ScrollTop as handle; the four hand-built feeds and four footers |
| `ThreadChrome` (new) | — | back line, error, busy, note, body slot: CommitDetailView's stack (:1163-1180) and the RemotesThread pane (:3623) |
| `SplitChrome` (new) | — | RemotesView's two-pane frame: side/main/back/breakpoint/widths (:48-126) |
| `LogChrome` | log tabs + composer rail | Tabs/Composer/RailNode/Header/Footer slots (§3.2); stops knowing `Composer.Fresh` and `Timeline.CommitLanded` |
| `DialogChrome` (new) | — | the shell's five overlay copies; the scrim token; the card-over-scrim shape `Shortcuts.helpCard` re-builds |
| `Palette` (new) | — | SearchDialog + Completion row hosts, keyboard walk, `followRows`, row subject |
| `FlyoutCard` / `MenuRow` / `ConfirmDialog` (new) | — | the six RemotesThread popup+card blocks, the org popover, the destructive confirms (RemotesThread :3042-3214, ProfileView :734, SettingsView :1139-1176) |
| `UserRow` / `AvatarStack` / `ImageGrid` / `ImageChip` / `IdentityLine` / `KbdChip` / `StatusLine` / `FeedFooter` (new parts) | — | the duplicates named in §3 and §8 |

### 7.3 Slot rules (normative)

The five slot forms of §3.1, plus: props records past three fields; action
slots always `{ Command; Busy }`; a slot never `IObservable<Control>`;
factories where the host may rebuild or repeat; render-prop rows
effect-free. Slots replace deep parameter threading (AGENTS 21).

### 7.4 RemotesThread split (seams verified by the thread pass)

| File | Today's lines | Seam |
|---|---|---|
| `Components/ThreadVoice.fs` | :294-820 | `VoiceCtx` + `Solo`; `Player`, `VoicePlayer`, `voiceChip` |
| `Components/ThreadMessage.fs` | :114-293, :825-1650 | `BubbleAsk`/`RowCtx`; ghost, images, linkCard, receipts, reactions, pin, bubble, run, day/head rows |
| `Components/ThreadFeed.fs` | :1651-1811 | replaced by `Feed.Props` + the present overlay |
| `Components/ThreadComposer.fs` | :1813-1947, :2270-2791 | imageChip (shared with Composer), link picker, mention picker (uses Completion), toolbar |
| `Components/ThreadRecorder.fs` | :1948-2268 | `Recorder` + `RecordLane` (seam: `audio` + `report`) |
| `Components/ThreadChrome.fs` | :2796-3214 | accessBar, notice, pinned banner/panel, pinDialog, menuPick, confirmPick |
| `Components/ThreadMenus.fs` | :3219-3591 | actionsMenu, memberStack, orgMenu |
| `Views/Remotes/ThreadPane.fs` | :3593-3836 | footer, pane, column + slots |

The coupling seams to break: `RowCtx` (:95-112), page `State.Msg` in the
menus/composer, `Solo` (:55-68), `PaneWidth` (:110), and the ungated `thread`
slice (:3662-3667).

### 7.5 RemotesModel split

`RemotesTypes.fs` (~215), `RemotesRows.fs` (pure shaping, ~650),
`RemotesVoice.fs` (WAV work, ~60 — beside Core's `Commit.Voices` or in it),
`RemotesModel.fs` (init/update/subscribe, ~950). The 60-arm `update` stays
one function; splitting it by message family is a later, separate refactor.

---

## 8. Cross-cutting pattern catalog

Entry 1 is issue #1. Every entry: copies today → the shared owner.

**A. State-layer duplication (issue #1 family)**

1. **Commit-card action arms** — poll trio ≈50 ×5 (+22 broken, SearchView
   :204-269); pastel pair ≈22-27 ×5; star pair 14-22 ×6; pin pair 68-99 ×2;
   translate block 74-92 ×6 (LogView :626-712, ProfileView :643-697,
   CommitDetailView :407-435/:679-697, OrganizationView :362-416,
   SavedView :312-366, SearchView :219-289). → `Views/CommitActions` beside
   `toggleStar`, with per-flow commands + busy streams in the `PinToggle`
   shape; `ActionSlot` fields on `CommitCard.Props`; pages keep one fold.
2. **`commitCtx`** — 41-59 lines ×5 (LogView :853, ProfileView :1048,
   CommitDetailView :760, OrganizationView :552, SavedView :386) + fake
   variant (SearchView :452-492). → one `FeedHost` builder: nav/event routing
   + model slices → `RowProps`, slices shared once.
3. **`replaceNode`** — 5 lines ×4 (LogView :275, ProfileView :120,
   OrganizationView :173, SavedView :145) + `withCommit`
   (CommitDetailView :239). → beside `FeedParts.nodeKey`, with a fold for
   extra locations.
4. **`SessionChanged` no-op mirror** — ×3 (OrganizationView :349-361,
   SavedView :299-311, NotificationsView :317-323). → part of 2's host or a
   shared arm helper.
5. **Logger resolved per message** — 26 sites, 16 files. → one logger in the
   page `Props` closure (mechanical).

**B. Chrome duplication**

6. **Pinned header twice** — PageChrome :83-118 vs ProfileShell :160-189
   (blocked only by the `bool * string` vs `string option` slot mismatch). →
   unify on `BusyState`, share one implementation.
7. **Text panel twice** — PageChrome.messagePanel :16-26 vs Status.panel
   :12-20; error line twice (PageChrome :28-38 vs ProfileCard :151-161). →
   one static owner + one observable wrapper.
8. **Hand-built page scaffold** — SettingsView :1187-1193, SignInView
   :297-305, AboutView :110-117. → PageChrome.
9. **Band offset / page frame** — RemotesView :126, LogChrome :120, Shell
   :417/:499/:1086/:1228/:1258, Shortcuts :342. → the chrome hosts own their
   top inset; pages stop hand-margining (`Theme.bandHeight` stays the token).
10. **Feed footer** — ProfileShell :135-145, LogView :1040-1048,
    Notifications :795-803, Saved :469-478. → `FeedFooter` part through the
    Footer slot.
11. **Settings section + dirty save button + status-driven button pair +
    searchable list** — SettingsView :594/:642/:839/:954 (sections),
    :825/:919 (save), :1018-1035 + SignInView :254-268 (pair), :705-750
    (picker). → `SettingsSection`, `SaveAction`, `TogglePair`, `SearchList`
    (the picker's render-prop shape is the model).

**C. Feed and list duplication**

12. **Hand-built feed** — RemotesInbox :446-478, RemotesThread :1651-1785,
    SearchView :530-536, WhoToFollow :627-647 vs Feed :50-118. → `Feed` with
    `OnNearEnd`/`Threshold`.
13. **Ungated collection slice** — the 11 sites of §4.3. → gate in `Feed`
    itself / a `bindKeyedItems` helper.
14. **Ungated `ContentControl` rebuild** — the 12+ sites of §4.1 rule 8. →
    one `bindContent(keyOf, build)` helper.
15. **Double projection per emission** — PageChrome :81-98, ProfileShell
    :70-72/:118-124/:162-174, every cold-slice-rebound site. → the `Slices`
    module (§4.3).
16. **Offset pager** — ScheduledView :572-604. → a pager element over
    `(current, last)` slices.
17. **Row frame** — ScheduledView :564-568, NotificationsView :741-758. → a
    hairline row-frame part.

**D. Palette and row duplication**

18. **Palette host** — §3.3 (SearchDialog, Completion, `followRows`,
    Composer's lane :1091-1140, Shortcuts' walk :145-286). → `Palette`.
19. **Kbd chip + status line** — SearchDialog :397-405/:409-420, Completion
    :86-93/:68-75, SearchParts :244-251, Shortcuts :307-313. → `KbdChip`,
    `StatusLine`.
20. **Person/user row** — SearchParts :137-171, Completion :112-139,
    WhoToFollow :461-592, NotificationsView rail cell :466-534. → `UserRow`
    with action slots.
21. **Identity head/line** — ProfileCard :105-125, UserFlyout :58-77,
    ProfileView :967-1005, SearchParts :135-206, CommitCard :891-920 +
    :1035-1055 (twice in one file). → `IdentityHead`/`identityLine`.
22. **Avatar + optional flyout decorator** — SearchParts :109-122, CommitCard
    :1265-1277/:1519-1529, NotificationsView :490-501, WhoToFollow :521-529. →
    `Avatar.viewWithFlyout(Source voption)`.
23. **Avatar stack** — RemotesThread :3336-3350, RemotesInbox :45-90. →
    `AvatarStack`.

**E. Card, chip, dialog duplication**

24. **Staged image chip** — RemotesThread :1813-1865, Composer :1159-1250. →
    `ImageChip` (one bitmap policy).
25. **Image grid + gallery** — RemotesThread :177-220, CommitCard :374-433. →
    `ImageGrid` over `ImageViewer`.
26. **Flyout card chrome / menu row / destructive confirm** — RemotesThread
    ×6 popups (:726, :1119, :1918, :3153, :3309, :3562) + rows (:1138-1148,
    :1905-1916, :2541-2566, :3127-3139, :3420-3450) + confirms (:3042-3214);
    RemotesInbox popover :594-642; ProfileView :734; SettingsView :1139-1176;
    the `UserFlyout` placement pattern :131. → `FlyoutCard` (placement set),
    `MenuRow`, `ConfirmDialog` with `{ Command; Busy }`.
27. **Scrim** — Shell ×5 + RemotesThread :3116. → `Keys.scrim`.
28. **Nullable-bool match / null-guard text read** — RemotesThread :3059/:3527;
    Composer :769/:811/:1204/:1396/:1729. → `GetValueOrDefault`; an `inline
    textOf(box)`.

**F. Route-knowledge duplication**

29. **URL→section in three places** — Navigation :16-35, Routes table,
  Shell entries :986-1043; plus URL escaping inconsistency (SavedView :427,
  OrganizationView :593-595 raw vs SearchView :468 escaped). → Routes owns
  the section per definition and the escaped-URL builder.

---

## 9. Prioritized Phase 1 work list

Ordered refactors. Each lands behind the Phase 1 verify bar: both suites
green, `dotnet fantomas .`, changelog under Unreleased, and — for feed work —
a feed page rendering through the changed surface. Nothing here fixes §10's
bugs; where a refactor touches one, the issue still gets its own body.

1. **Issue #1 — consolidate the card actions.** Scope: `CommitActions` grows
   the vote/pastel(/star) flows (command + in-flight key + shared log/error
   wording, per the issue body); `CommitCard.Props` gains `ActionSlot` fields
   (Star, Pastel, Vote, Translate) beside the existing `Pin`; the union drops
   `PinToggled`/`ThreadRequested` and the six effect arms; the six views keep
   one fold each; `commitCtx`/`replaceNode` ride along where the touch is
   trivial. Blast radius: CommitCard, CommitActions, six views, their tests.
   Verify: both suites green; a feed page (LogView) and ProfileView render
   through the consolidated actions; ~600 duplicated lines gone.
2. **Chrome unification.** Scope: `BusyState` on PageChrome (kills the
   `bool * string` slots and the 5 call-site tuples); footer/title/empty
   Control slots; PageChrome and ProfileShell share the pinned header;
   `Status` becomes the static text owner; dead-export sweep (§2.3, including
   the `Theme.Brushes` shrink + doc fix, `Composer.Register` removal with
   handle exposure, `bindClass` → `IDisposable`). Blast radius: the five
   PageChrome pages + two ProfileShell pages + Theme; additive slots keep it
   low-risk. Verify: suites green; no page hand-builds a busy/empty/error
   line.
3. **Feed hardening.** Scope: gate `Items` by reference inside `Feed.view`;
   `HasMore` prop; `ScrollTop` becomes a handle or model token (fixes the
   rule-3 shape and the one-shot Sub); Header/Empty slots; feed scroll slots
   for `ProfileShell.FeedProps`. Blast radius: Feed + its four hosts; the
   hand-built feeds stay as they are until step 12 of the catalog lands.
   Verify: suites green; a UI test counts realized containers (closes the
   virtualization verification gap).
4. **Rebuild-guard sweep.** Scope: one `bindContent(keyOf, build)` helper
   beside the chrome controls; apply to the 12+ ungated sites of §4.1 rule 8.
   Blast radius: one file per site, mechanical. Verify: suites green; the
   CommitDetailView card no longer re-subscribes per emission (LEAK-2 dies
   here).
5. **Host extraction: LogChrome slots + SplitChrome + DialogChrome.** Scope:
   LogChrome's Tabs/Composer/RailNode/Header/OnReload slots; `SplitChrome`
   takes RemotesView's side/main/back/breakpoint; `DialogChrome` takes the
   shell's five overlay copies and the scrim token; band-offset sweep
   (issue #15's strip is adjacent). Blast radius: LogChrome, Shell,
   RemotesView, Shortcuts' help card. Verify: suites green; a second surface
   could host each chrome (the slot test).
6. **Palette extraction.** Scope: `Palette` host over SearchDialog +
   Completion; `KbdChip`, `StatusLine`, `UserRow` parts. Blast radius:
   SearchDialog, Completion, Composer's lane, SearchParts, Shell's search
   overlay. Verify: suites green; both palettes walk keys through the one
   host.
7. **Shell decomposition.** Scope: the guard helper for the four
   identity-guarded cells; `termsDialog` becomes a proper component; the
   sidebar/aside/band/chrome feed become named elements the Shell composes.
   Blast radius: Shell only. Verify: suites green; the four guarded cells
   share one helper.
8. **RemotesThread split.** Scope: §7.4's eight files along the verified
   seams, `Feed.Props` replacing `list`/`ScrollState`, slots per §3.2.
   Blast radius: the largest single move — the Remotes page only. Verify:
   suites green (RemotesView.Tests is the deepest UI suite); the thread pane
   renders through `Feed` and the shared dialogs.
9. **RemotesModel split + `Views/Remotes/` folder.** Scope: §7.5's four
   files, the page folder, the WAV move. Blast radius: Remotes page + fsproj
   order. Verify: suites green; the pure row shaping tests run without an
   env.
10. **Composer split.** Scope: CompletionLane, AttachmentStrip (shared with
    the thread), PollEditor, SchedulePicker, ComposerToolbar; handle
    exposure replaces `Register`. Blast radius: Composer + its five hosts
    (additive). Verify: suites green; the thread composer reuses the strip
    and the key lane.
11. **Mechanical sweep.** Scope: `Slices` module + first adopters (the nine
    shared slices of §4.3); per-update logger hoist (26 sites); `inline`
    one-liners; dead opens/messages/props; `private` markers; namespace fix
    (`Status`); `Translation.fs` move. Blast radius: many files, one-line
    each. Verify: suites green; grep counts (§1) show `CreateLogger` inside
    `update` at 0.

Steps 8-10 are the big moves the ROADMAP's "while the codebase is small
enough to move" line points at; they are independent of 1-7 and of each
other, and each can pause after any file lands.

---

## 10. Gaps and bugs found on the way

Per the ROADMAP rule, each becomes an issue, not phase work. Ready bodies,
severity-ordered. Confirmed unless marked suspected.

**LEAK-1 — AsyncImage drops bitmaps on canceled loads.**
`AsyncImage.fs:92-103`: when the token cancels between decode and the UI
post (or before the continuation), the `Bitmap` is never disposed. Scroll and
row recycling make it common; native memory grows. Fix: dispose on the
cancellation paths (the drop-on-replace/detach pattern the file already
uses).

**LEAK-2 — CommitDetailView builds a rooted card per emission.**
`CommitDetailView.fs:942-951`: ungated `Content` rebuild; each replaced card
keeps model bindings until the route ends (leak class 5; dies in work-list
step 4, but the issue tracks the defect).

~~**BUG-3 — Scroll-to-top dies after the first stash.**
`Feed.fs:106-116`: the `untilDetached` subscription cannot restart; LogView
is stashed and returned to (LogView.fs:1086-1093), so the timeline's
scroll-to-top lands once per route instance. Confirmed by the library
contract (MVUish XML 250-258).~~ — died in step P5

~~**BUG-4 — SearchView drops card-action results.**
`SearchView.fs:205` (vote branch dead: `selectedOption(Map.empty, …)`),
`:256-269` (`StarDone`/`PollVotedDone` discard the returned commit). Stars
and votes on search rows stay stale until the next search.~~ — died in step P2

**BUG-5 — Remotes inbox behaviors.**
(a) Clearing the search box cannot clear the search — the whitespace filter
blocks `Searched ""` and the `Text` binding refills the box
(RemotesInbox.fs:431-439, RemotesModel.fs:1196). (b) Settle actions render in
every tab, not only Fetched (:224-247). (c) The quiet line draws over the
search header (:684-687 vs its comment). (d) The org popover has no placement
target and no light-dismiss (:595-:640). (e) The tab strip retains one
stripped subtree per waiting-count change (:363-421).

**BUG-6 — Remotes read/seen model is global, not per conversation.**
`RemotesModel.fs:954-1035`: `MarkedThrough`/`SeenThrough` are single ints;
after a busy thread, a quiet one sends no read and undercounts unseen. Use a
per-conversation map or reset on target change. Related: per-target UI state
survives a target switch (`Landing`, org fields, `Notice` — :1123-1152), and
reaction chips return in key order while the doc claims first-left order
(:662-690).

**BUG-7 — The scroll→store→model feedback loop.**
`RemotesThread.fs:1742` dispatches per `ScrollChanged`; `ThreadScrolled`
writes the store per event (RemotesModel.fs:1296-1301), which republishes and
wakes three thread scans + the row diff. Sample/debounce the scroll place or
keep it in the view until the page ends.

**BUG-8 — Voice/recorder lifecycle.**
(a) A late `Sounded` playback after cancel is neither stopped nor stored
(RemotesThread.fs:495-515) — stale audio can play. (b) `RecordLane` can leak
the microphone when detached during `Opening` (:2227-2265). (c) Detach leaves
the page's `Recording` flag stale (:2223-2255). (d) `Solo` never releases the
last playback; a note can keep playing after the page ends (RemotesView:41,
RemotesThread:55-68). (e) The pane freezes a captured target on same-key
refresh (rename/avatar — :3823-3826).

**BUG-9 — Remotes thread dialogs.**
Six `Popup()` sites with no `PlacementTarget`/`Placement` (:726, :1119,
 :1918, :3153, :3309, :3562); no busy guard on pin/confirm actions
(:1086-1100, :3188-3196 — `PinToggle` is the model); the landing flash
misses virtualized rows and stacks timers (:1753-1775); the modal scrim is
hard-coded `Black` (:3116).

**BUG-10 — Composer.**
(a) The poll editor's cap/floor arms close the poll and drop its state
(Composer.fs:487-516) — latent (buttons hide at the caps) but
state-destroying. (b) `collectStaged` posts a blocking, compacting gen-2 GC
onto the UI thread per staged transition (:238-251). (c) The attachment gate
walks staged bytes per keystroke (:1263-1264). (d) Three `DraftChanged`
dispatches per keystroke on the thread's composer (RemotesThread:2436-2538).

**BUG-11 — LogView/ProfileView/CommitDetailView behaviors.**
(a) The stale error banner survives a successful refresh (LogView:392-418,
:551-558). (b) No in-flight guard on pagers: two concurrent same-cursor
fetches append one page twice and the keyed feed throws on repeated keys
(LogView:538-550, ProfileView:305-316, CommitDetailView:322-327). (c) Every
row's reply answers the thread root, not that row (CommitDetailView:358-364).
(d) `Zone` frozen at ctx build in all three (:890, :1113, :1160).

**BUG-12 — Avatar fallback missing.**
`Avatar.fs:42-59`: with a URL, no initial letter renders behind the image; a
failed load leaves a bare lane disc. The comment (:44-45) and `.fsi` claim
the fallback exists.

**BUG-13 — UserFlyout lifecycle.**
The popup has no detach close; `openTimer` can open for a detached trigger
(UserFlyout.fs:133-168, comment :125 wrong); the fetch has no cancellation or
ordering, so a slow first result can overwrite a later card (:155-165);
repeated hovers re-fetch the same profile (no profile cache) — suspected
visual impact only.

**BUG-14 — SignInView.**
The start button stays enabled during `Preparing` (double flow, :254-260); a
resumed pending sign-in reopens an empty URL (:172, :194-200); `awaitSession`
runs up to five minutes untied to the program's lifetime (:104-148) —
suspected dispatch-into-disposed.

**BUG-15 — Store writes inside `update` compute.**
RemotesModel (10 arms, :1256-:1753), SettingsView `applyPrefs` (:351, :393,
:438), SearchDialog `RecentsCleared` (:344-346). Each should ride
`Cmd.ofAction` so the store round-trip cannot re-enter before the model
settles.

**BUG-16 — Same-id session staleness.** Shell footer/aside/chrome
(§6.1), LogChrome's rail avatar (:175-181 + Shell:1323): a name/avatar
refresh with the same user id leaves the built cells stale until restart.
Suspected until a profile-edit + refresh test exists.

**BUG-17 — Dead code.** Messages `MeNote`/`Typed`/`PageRequested`;
`Props.Nav`; the dead exports of §2.3. Ride with work-list step 11 or a
sweep issue.

**VERIFY-18 — three suspects needing a UI test.**
~~(a) Feed virtualization: outer `ScrollViewer` + `ItemsControl` +
`VirtualizingStackPanel` (Feed.fs:65-87, RemotesInbox:460-466,
RemotesThread:1680-1686) may realize every row — count realized containers.~~
— measured in step P5: 30 of 200 rows realized; virtualization holds.
(b) Same-key item replacement through `BindItemsSource` may show stale row
content. (c) The completion popup may stay open over the next page when the
chrome hides (LogChrome:255 + Composer's `IsOpen` binding) — suspected.

---

*Compiled 2026-11-01 from eight read-only audit passes over the tree and a
synthesis pass that read the scattered GUI files in full. Every finding
carries its own file:line citation; the document stands alone.*
