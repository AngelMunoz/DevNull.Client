# Editor layout — phased implementation plan (IDE theme)

Status: plan only. No code in this plan is written yet.
Mounts under the layout host (`PLAN-layout-host.md`): the editor is
content, not a window. `PLAN-workbench-layout.md` is the sibling
layout; neither plan's code imports the other's.

## 0. Intent, stated once

dev-null's words are version-control words. The workbench plan dresses
the network as a source-control workbench; this plan dresses it as the
tool a developer already lives in: an editor. One history as a pinned
tab. Threads as tabs that carry their own dirty dots. Refs as the
explorer tree. Notifications as the problems panel. The composer as
the terminal. A profile as a repository tab, badges as shields.

The metaphor maps to roles, not decoration: tabs navigate, the dirty
dot reports, the explorer selects, the panel hosts writing. Every
themed word names real data (section 2) or real client state, and the
theme degrades all the way down to the app's own window floor
(380 × 640) instead of refusing narrow windows.

## 1. Hard constraints

1. **The website layout stays the default** and byte-identical. The
   editor is opt-in content under the host.
2. **Self-contained under `src/DevNull.Client.UI/Layouts/Editor/`.**
   The editor never opens `DevNull.Client.Views`, never touches
   `Shell.*`, `Tray.*`, `Shortcuts.*`, or `SettingsStore.*`, and never
   imports the workbench. The only shared-file edit is its own
   `Compile` lines in the fsproj files.
3. **The host owns window duties.** The editor draws no band, no
   caption buttons, no tray, no sign-in screen, no callback, no app
   loops. It renders inside the host's content slot and answers
   `LayoutIntent`.
4. **Components come over only as-is** (Tier rules of the workbench
   plan, section 4.2, hold word for word). A component that needs one
   new prop gets an editor-owned copy instead.
5. **Core is read-only.** Every read the editor needs exists today.
6. **The truth table is law** (section 2): a word with no data behind
   it does not ship, however well it fits the theme.
7. **Repo rules apply in full** (`AGENTS.md`): `.fsi` beside every new
   `.fs` (tests excepted), comments in the `.fsi`, no `private` beside
   an `.fsi`, tupled calls, at most 3 parameters, `asyncEx`,
   `dotnet fantomas .` before any commit, one changelog entry per
   shipped phase.

## 2. The metaphor map (truth table)

Use — each word names a field or a read that exists today:

| UI word | Real data |
|---|---|
| Tab | host state (like the workbench's stored state) |
| Pinned history tab | the selected `LogKind`; cannot close |
| Dirty dot `●` on history | `LogPage.NewCommitsCount` from the poll |
| Dirty dot `●` on a thread tab | replies-count delta since the tab was last seen |
| Breadcrumb | `CommitPage.Ancestors`, newest first |
| Peek (breadcrumb hover) | `Commits.show` |
| Problems panel | `Notification` rows grouped by `NotificationKind`; `✓` Approved, `✗` Rejected |
| Output panel | the layout's own fetch events |
| Terminal panel | the `Composer` component; `$ commit` is the site's verb |
| Queue count | the scheduled queue (`Api.ScheduledCommits`) |
| Palette | `SearchProfiles`, `SearchCommits`, plus real host commands |
| Repository tab | `ProfileDetails`, `Users.commits`, follower/following lists |
| README card | the pinned commit (`IsPinned`) |
| Shields | `Badge` (icon, progress, target), level, rank, streak |
| Checks `✓ ⏳ ✗` | `CommitStatus`: Approved, Pending, Rejected |
| Refs / HEAD | `LogKind`; HEAD marks the selected ref |
| Lane | `Lane`, the rail color, as everywhere |

Do not use — no data backs these words (the workbench plan's refuse
list holds, plus the editor's own):

| Word | Why not |
|---|---|
| Branch, merge, pull request, diff, clone, fork | as in the workbench plan, section 2 |
| Breakpoint, run, debug | nothing runs; there is no execution model |
| Minimap | decoration; it names no data |
| Extensions | there is no plugin surface |
| File tree inside a commit | a commit is one document, not a tree |
| "file" for a commit | the tab says `hash @author`, never `file.txt` |

Verbs keep the site's words (Commit, Star, Reply, Quote, Follow). The
editor renames surfaces, not acts — same rule as the workbench.

## 3. Ground facts from the code (checked 2026-11-12)

- The feed control caps rows at 720 px and centers them
  ([Feed.fsi](src/DevNull.Client.UI/Components/Feed.fsi:20)); the
  editor's center is free-width, so gate EG1 checks the fit.
- The commit card has a dense form (28 px node, 48 px rail, 8 px
  padding) and does not navigate: the host receives
  `CommitCard.Event` values and decides
  ([CommitCard.fs](src/DevNull.Client.UI/Components/CommitCard.fs:1238),
  [CommitCard.fsi](src/DevNull.Client.UI/Components/CommitCard.fsi:27)).
  `RowProps` carries the translation lookup and menu slots
  ([CommitCard.fsi](src/DevNull.Client.UI/Components/CommitCard.fsi:175)).
- `CommitCard.chainRows` exists with a `ChainCtx` that needs a `Grow`
  callback ([CommitCard.fsi](src/DevNull.Client.UI/Components/CommitCard.fsi:228))
  — the parents chain's one reuse candidate (gate EG4).
- `CommitPage` carries the commit, ancestors, and reply participants
  in one read ([ApiTypes.fsi](src/DevNull.Client.Core/ApiTypes.fsi:364)).
- `LogPage.NewCommitsCount`
  ([ApiTypes.fsi](src/DevNull.Client.Core/ApiTypes.fsi:498)) is the
  history dirty dot's own number.
- Notification kinds include Approved and Rejected
  ([ApiTypes.fsi](src/DevNull.Client.Core/ApiTypes.fsi:651)); badges
  carry icon, unlocked, progress, target
  ([ApiTypes.fsi](src/DevNull.Client.Core/ApiTypes.fsi:443)).
- `Fonts.mono` is already "the face of hashes, handles, times, and
  counts" ([Theme.fsi](src/DevNull.Client.UI/Theme.fsi)) — Tier A.
- The scheduled queue read exists
  ([Api.fsi](src/DevNull.Client.Core/Api.fsi:974)); follower and
  following lists exist ([Api.fsi](src/DevNull.Client.Core/Api.fsi:465)).
- The app floor is 380 × 640
  ([Shell.fs](src/DevNull.Client.UI/Shell.fs:836)) and the host keeps
  it; cards already render at that width in the default layout.
- Column/tab programs run through `Programs.runPage`; the layout keeps
  the handles (host plan, section 3.2, is the mount contract).

## 4. Anatomy and the width ladder

Five regions; three have their own scroll; none is a copy of another:

- **Activity bar (left, 48 px).** Icons with badges: history, problems
  (unread), remotes (ahead total, shown only above zero), account.
  Every icon opens a flyout or focuses a region.
- **Side bar (240 px, collapsible).** EXPLORER: the three refs with
  HEAD on the selected one, SAVED with count, QUEUE with count, the
  signed-in user's repository entry.
- **Editor area.** Tab strip plus content. One pinned history tab that
  cannot close, one tab per open thread (`ShortHash @author`), repo
  tabs. Dirty dots per the truth table; an overflow `⋯` menu when the
  strip runs out of room.
- **Panel (bottom).** PROBLEMS / OUTPUT / TERMINAL, docked from
  1000 px up, an overlay sheet below that.
- **Status bar (the layout's own strip).** `⎇ ref · lane · key ·
  fetched ago · new count`; the badges live on the activity bar alone.

Width ladder, all the way to the host floor:

| Width | What happens |
|---|---|
| ≥ 1000 | Full anatomy, docked panel. |
| 700–1000 | Side bar folds to a flyout; tab titles drop `@author`. |
| 380–700 | Tab strip: history icon + active hash + `⋯`; panel is the overlay; status bar trims to `⎇ ref · ●n`; code blocks scroll, never wrap. |

No refusal screen at any width: the editor degrades to the floor
instead of asking for a wider window.

## 5. Programs and lifetime

One `EditorHost` program owns: the tab list and order, the selected
tab, the pinned state, panel state, badges, and the width band. One
child program per data-owning tab:

- `HistoryTab` — one ref's commits: fetch, cursor, 1-minute `Since`
  poll under the host's active flag, new-count.
- `ThreadTab` — one commit: `Commits.show`, replies cursor, the seen
  watermark for the dirty dot.
- `RepoTab` — one handle: profile, counts, contributions, history.

Rules, from the repo's MVUish rules and the host plan:

- The host program starts children with `Programs.runPage` and keeps
  every handle. Closing a tab disposes its handle once. The pinned
  history tab's program lives for the layout's lifetime; a ref switch
  within it disposes and restarts, scroll and cursor head held by the
  host program as plain data (the workbench's W-2A ruling, same shape).
- No tab uses `runWithLifetime`: a folded panel or overflowed tab can
  leave the visual tree and must return alive.
- Views bind only their own program's model. Session, clock, unread,
  and the host's `Active` stream enter through `Sub` sets only.
- Cross-tab effects are named messages through the host program. No
  broadcast service, no app-lifetime observable in any view.

## 6. Data flow

```text
ref select ──▶ EditorHost Msg ──▶ HistoryTab starts (Logs.get)
card select ──▶ EditorHost Msg (OpenTab id) ──▶ ThreadTab (Commits.show)
breadcrumb hover ──▶ peek read (one-entry cache)
composer posted ──▶ EditorHost Msg ──▶ HistoryTab Refresh (named, one tab)
1-min tick ──▶ Sub.interval in HistoryTab, gated by Active
problems row click ──▶ OpenTab (commit) / no-op (no commit)
palette pick ──▶ OpenTab (commit) | RepoTab (handle) | host command
```

Worst-case request budget: one history poll a minute plus the
notifications list the problems panel reads on focus, plus first-page
fetches per tab. No search poll, ever (the 15-a-minute cap).

## 7. Wiremocks

E1–E5 and E7–E8 as reviewed, now the record. All chrome below the
drag band is the editor's own; the band and captions are the host's
and do not appear again.

### E1 — Main anatomy, 1600 px, thread open as a tab

```text
├──┬───────────────┬──────────────────────────────────────────────────┤
│  │ EXPLORER    ▾ │ ◉ Following ●3 │ a1b2 @ana │ 55aa @cy ● │  +    │
│  │               │──────────────────────────────────────────────────│
│⌂ │ HISTORY       │ 09fe ← 55aa ← (root)      @ana · lane 2 · 2m    │
│  │ ● Following   │──────────────────────────────────────────────────│
│  │   (HEAD)      │ ◉ a1b2c3d                                       │
│  │ ○ Latest      │ shipped the fix. the parser now reads the whole  │
│  │ ○ Trending    │ file before it answers.                         │
│  │               │ ```rust                                         │
│⚠2│ SAVED      4  │ let whole = reader.take_all()?```               │
│  │ QUEUE      1  │ [img ×2] [poll · voted]                         │
│↑2│               │ ★ 12 · ⏳ pending · 4 replies                   │
│  │ @me (repo)    │──────────────────────────────────────────────────│
│@ │               │ REPLIES 4                      [load more]      │
│  │               │ ├─◉ @cy  tests pass                             │
│  │               │ ├─◉ @bo  lgtm                                   │
├──┴───────────────┴──────────────────────────────────────────────────┤
│ PROBLEMS 2 ▾ │ OUTPUT │ TERMINAL                                    │
│ ✗ Rejected   your commit 55aa…                         1m          │
│ ✓ Approved   ab12 is live · @mod                       3m          │
├─────────────────────────────────────────────────────────────────────┤
│ ⎇ Following · lane 2 · key 9F3A…C1 · fetched 12s ago      ⚠2 · ↑2  │
└─────────────────────────────────────────────────────────────────────┘
```

### E2 — The composer as the terminal panel

```text
│ TERMINAL — draft ●───────────────────────────────────────── [Commit] │
│ $ commit                                                            │
│ release notes for 1.4: the parser now reads the whole file first    │
│ [@ana] [#91]  attach [img ×1]  [poll · 2 options]  [schedule ▾]     │
│ ⎇ Following · lane 2 · ●1 draft · 1 queued             fetched 12s  │
```

### E3 — Tab row, all states

```text
│ ◉ Following ●3 │ a1b2 @ana │ 55aa @cy ● │ @ana (repo) 📌 │ +        │
```

### E4 — Command palette

```text
        ┌───────────────────────────────────────────┐
        │ > an                                      │
        │ @ana     Ana Example · lane 2     open →  │
        │ @andré   André Silva · lane 5     open →  │
        │                                           │
        │ in commits:                               │
        │ a91f @ana  "antlr notes…"        open →   │
        ├───────────────────────────────────────────┤
        │ > switch ref: Latest                      │
        │ > star checked-out commit                 │
        │ > use default layout                      │
        └───────────────────────────────────────────┘
```

### E5 — Profile as a repository tab

```text
│ @ana (repo) 📌 ─────────────────────────────────────────────────────│
│ @ana   Ana Example · lane 2 · Lisbon          [Follow] [★ 214]      │
│ [level 4] [streak 12] [rank: shipwright]     ← shields from Badge   │
│ bio line…  github ↗  joined 2024                                     │
│ ┌ README — pinned ────────────────────────────────────────────────┐ │
│ │ ◉ e5f6  "read this first"  (the pinned commit, full card)       │ │
│ └─────────────────────────────────────────────────────────────────┘ │
│ contributions ▁▃▅▃▂▅▇▅▃▁   (Activity.Buckets, lane-colored)          │
│ HISTORY @ana ─ commits as E1 rows ───────────────────────────────── │
```

### E7 — Minimum, 380 × 640 (the host's floor)

```text
├──┬───────────────────────────────────────────────┤
│  │ ◉ ●3 │ a1b2 │ ⋯2                             │
│  ├───────────────────────────────────────────────┤
│⌂ │ 09fe ← (root)               @ana · 2m        │
│  │───────────────────────────────────────────────│
│⚠2│ ◉ a1b2c3d  shipped the fix. the parser       │
│↑2│ now reads the whole file before it answers.   │
│@ │ ```rust                        ── scroll ──→  │
│  │ let whole = reader.take_all()?```             │
│  │ ★ 12 · ⏳ pending · 4 replies                 │
│  │───────────────────────────────────────────────│
│  │ REPLIES 4                   [load more]      │
│  │ [ reply…                      ] [Post]        │
├──┴───────────────────────────────────────────────┤
│ ⎇ Following · ●3                             ▤   │
└─────────────────────────────────────────────────┘
```

### E8 — Minimum width, panel open as an overlay

```text
│ ⌂│ ⚠2 │ ↑2 │ @ │  (editor dimmed behind)         │
│┌ TERMINAL — draft ●─────────────────── [Commit] ┐│
││ $ commit                                       ││
││ release notes for 1.4: the parser now reads…   ││
││ [@ana] [#91] [img ×1] [schedule ▾]             ││
│└─────────────────────────────────────────────────┘│
│ ⎇ Following · ●3                             [▾] │
```

## 8. Reuse gates (start of Phase 1)

A failed gate moves the item to an editor-owned copy under
`Layouts/Editor/`. No gate is fixed by editing the shared item.

| # | Item | Check | If it fails |
|---|---|---|---|
| EG1 | `Feed.view` | Rows fill a free-width center; virtualization holds | `EditorFeed` |
| EG2 | `CommitCard.feedRow`, dense | Editor row context: `Menu = ValueNone`, lookup answering `ValueNone`, flyout with editor callbacks (open author = repo tab) | `EditorCard` |
| EG3 | `Composer.Page` | Hosted in the panel, `Fresh` and `Quoting`; post, schedule, images, no router | `EditorComposer` |
| EG4 | `CommitCard.chainRows` | `ChainCtx` takes the editor's `Grow` with no edit (Phase 4 parents chain) | dense cards, no chain in v1 |

## 9. Dry run — gaps found before work starts

- **E-1A / E-1B (truth) Lanes are not branches; history is not sorted
  by ancestry.** The workbench rulings W-1A and W-1B hold here word
  for word. The word "branch" appears nowhere; the center list stays
  in the API's order.
- **E-2A (blocks Phase 1) Full-width rows.** Gate EG1. The 720 px cap
  is a max, so a free-width center should bind it from below; the
  gate proves it with a real build.
- **E-2B (tab strip) No virtualization.** A user holds a handful of
  tabs; the strip is an `ItemsControl` with an overflow menu under
  700 px, not a virtualized list. Capping open tabs is decision 1.
- **E-2C (lifetime) Tab close disposes; the pinned tab lives.**
  Closing a thread or repo tab disposes its program handle once. The
  pinned history tab's program runs for the layout's lifetime; a ref
  switch inside it restarts it with scroll and cursor head held by
  the host program. Tests count live programs (the workbench's W-2A
  test shape).
- **E-3A (dirty dots) Session-only, two sources.** History: the
  poll's `NewCommitsCount`. Thread: replies-count delta since the
  tab was last seen — a watermark in the tab model, never stored.
  Focus follows the dot: the click scrolls to the first new reply.
- **E-3B (peek) One-entry cache.** A breadcrumb hover reads
  `Commits.show` once per commit; the cache holds the last entry
  only. A hover storm cannot become a read storm.
- **E-4A (problems) A notification is not a commit.** The workbench's
  3-D ruling holds: own row, grouped by kind, unread as the server
  last reported, never marked read here. A row with a commit opens a
  thread tab; a row without one does nothing.
- **E-4B (queue) The terminal's schedule needs its list.** The queue
  count and panel list read the scheduled index
  ([Api.fsi](src/DevNull.Client.Core/Api.fsi:974)). If the composer
  host cannot offer schedule with no edit (gate EG3), the schedule
  action hides and the QUEUE row reads "The queue opens in the
  default layout." No silent loss.
- **E-5A (repo tab) No invented granularity.** The contribution graph
  draws the profile's own buckets in lane color with the server's
  scale word (the workbench's W-4E ruling holds). Followers and
  following are lists inside the tab, from the existing reads.
- **E-6A (palette) No verb without an action.** The command list
  names real host commands only (switch ref, star the checked-out
  commit, use default layout). A command that does nothing does not
  ship.
- **E-7A (narrow) The ladder is layout behavior, not hardening.**
  E7/E8 land with their regions in Phases 1 and 3; the 380 px
  snapshot lands in Phase 5. Code blocks scroll sideways; they never
  wrap.
- **E-8A (mount) The `ILayout` adapter is the parent's.** This plan
  delivers `EditorRoot` and its `Dispose`; the host plan's section 8
  wires the adapter and the `builtIn` entry.

## 10. Phases

Each phase ships alone: code, tests, changelog entry. Sizes:
S (days), M (one to two weeks), L (more).

### Phase 1 — History tab and the frame (M)

Closes: E-2A, E-2B, E-2C, and the frame of E1 (activity bar, explorer,
tab strip with the pinned tab, status bar).

Work: `EditorTypes` / `EditorSettings` (the parse leaf: stored state
is the selected ref, one string, default Following); `HistoryTab`
(fetch, cursor, poll under the active flag, new-count); `TabStrip`
(pinned tab, close on non-pinned, dirty dot, overflow); `ActivityBar`
and `Explorer` rendering from the host model with counts through
`Sub`; `StatusBar`; `EditorRoot` composing the frame at the width
ladder's three bands. Gates EG1 and EG2 run first and their results
land in the PR.

Acceptance: the three logs read from the explorer with no tab churn;
the pinned tab cannot close; the center uses the full free width at
1600 px; E7's 380 px frame shows the strip and status trim.

Tests: parse tests; history update tests (fetch, append, poll,
new-count) in the Features style; one headless frame test at 1600 and
380 px.

### Phase 2 — Thread tabs (M)

Closes: E-3A, E-3B, and E1's editor area with a thread open.

Work: `ThreadTab` (show, replies cursor, reply composer per gate EG3's
reply shape, quote into the terminal panel, seen watermark); the
breadcrumb line and the peek cache; tab open/close/dispose counting.

Acceptance: open, read, reply, close — the history tab's scroll and
poll never move; the dirty dot appears on a polled reply delta and
clears on visit.

Tests: thread program tests; the tab-churn program count test; a
breadcrumb cache test.

### Phase 3 — Panel: problems, output, terminal (M)

Closes: E-4A, E-4B, and E2/E8.

Work: the panel host with its three tabs; the problems rows; the
output fetch log; the terminal hosting `Composer` (gate EG3); the
queue count; the overlay form under 1000 px.

Acceptance: write, schedule, and post without leaving the frame; the
problems panel opens a thread tab on a commit row; E8 works at 380 px.

Tests: panel host tests; problems row tests; the queue count test.

### Phase 4 — Repository tab and palette (L)

Closes: E-5A, E-6A, gate EG4, and E5.

Work: `RepoTab` (header, shields, README card, contributions,
history below, follower lists); `Palette` over the two search reads
plus the command list; repo tab pinning.

Acceptance: a repository tab opens from a card author, from the
palette, and from the explorer's own entry, with the same content
each time; the palette answers people, commits, and commands.

Tests: repo fetch tests; palette result tests as data; pin state
round trip.

### Phase 5 — Hardening and ship (M)

Closes: E-7A's snapshots and the leak set.

Work: leak tests (build the root, churn tabs 20 rounds, close, count
by the gate's own method); snapshots at 1600, 1100, and 380 px; the
request-count test (one history poll a minute, no search poll);
`dotnet fantomas .`, full build and suites; changelog; the help line
naming what lives in the default layout (remotes pane, more menu,
translations).

## 11. New file manifest (when all phases land)

Under `src/DevNull.Client.UI/Layouts/Editor/`, each `.fs` with its
`.fsi`, compile entries after the `Components/` files and before
`Views/Routes.*`:

| File | Holds |
|---|---|
| `EditorTypes.fsi/.fs` | `EditorTab`, `TabId`, the editor state |
| `EditorSettings.fsi/.fs` | the parse/store leaf, the default state |
| `HistoryTab.fsi/.fs` | the pinned history tab program |
| `ThreadTab.fsi/.fs` | one open thread's program |
| `RepoTab.fsi/.fs` | the repository tab (Phase 4) |
| `TabStrip.fsi/.fs` | the tab strip control |
| `Explorer.fsi/.fs` | the refs tree with counts |
| `ActivityBar.fsi/.fs` | the icon strip, badges, flyouts |
| `EditorPanel.fsi/.fs` | problems / output / terminal host |
| `Palette.fsi/.fs` | the command palette (Phase 4) |
| `StatusBar.fsi/.fs` | the layout's status strip |
| `EditorRoot.fsi/.fs` | the host program composing the regions |

Plus any Tier C copy a failed gate adds, named in that phase's PR.
Tests beside the workbench's, in the projects that hold their kind.

## 12. Decisions still open

1. **Tab cap.** Default: none in v1; the leak test churns 20 tabs.
   Decide at Phase 2 if memory says otherwise.
2. **Pinning threads.** Default: only repo tabs pin; thread tabs are
   ordinary. Decide at Phase 4 review.
3. **Palette command list.** Default: the three commands of E4. Each
   addition must name a real host command (ruling E-6A).

## 13. Relation to the other plans

The host plan mounts this layout: `Layouts/Editor/EditorRoot` behind
the `ILayout` adapter, registered in `builtIn` as `editor`. The
workbench plan is the sibling: same gates thinking, same truths, no
shared code. `PLAN-deck-layout.md` stays unchosen and unedited.
