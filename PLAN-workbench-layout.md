# Workbench layout — phased implementation plan (source-control layout)

Status: plan only. No code in this plan is written yet.
This plan is the primary direction. `PLAN-deck-layout.md` stays intact
as a separate, unchosen option; this plan does not edit it and does not
depend on it.

## 0. Intent, stated once

dev-null is a Twitter-style social network whose own words are version
control words: a post is a commit, a direct message is a remote, a
saved post rides the star list, and a conversation carries an ahead
count. The website presents these words in a standard social layout:
one feed column, tabs, a sidebar, a right rail.

This layout presents the same network as the thing its words describe:
a source-control workbench. One history in the center. Refs on the
left. The selected commit checked out on the right. Drafts staged at
the bottom. A status bar that names the current ref, the user's lane,
and the signing key.

From `threads-layout.png`, this plan takes one idea only: a desktop
window must use its full width with parallel panes that each keep
their own scroll and state, instead of one 620 px column in the
middle of a wide window. It does not take the deck of equal feeds.
Panes here have ranks: the history is primary, the refs and the
inspector serve it, and no pane is a copy of another pane.

## 1. Hard constraints

The constraints of section 0 of the deck plan hold here, restated for
this plan so it stands alone:

1. **The current layout stays the default.** A user who never opts in
   sees no change, in any phase.
2. **The workbench is completely self-contained.** It reuses no shell,
   no router, no route page, and no shell service. New code lives
   under `src/DevNull.Client.UI/Layouts/Workbench/`.
3. **Components come over only as-is.** A component in `Components/`
   is used only if its current `.fsi` fits with no edit. If it needs
   one changed prop to fit, the workbench writes its own version
   under `Layouts/Workbench/`. The shared component is never edited
   for this layout.
4. **Shared-file edits are a closed list** (section 4). Any other
   shared-file edit stops the phase.
5. **Core is read-only.** Every read and write the workbench needs
   exists in `src/DevNull.Client.Core` today.
6. **Repo rules apply in full** (`AGENTS.md`): an `.fsi` for every
   new `.fs`, comments in the `.fsi`, no `private` beside an `.fsi`,
   tupled calls, at most 3 parameters, `asyncEx`,
   `dotnet fantomas .` before any commit, one changelog entry per
   shipped phase.
7. **The git theme must stay true.** Every version-control word in
   the UI names real data (section 2). A word with no data behind it
   does not ship, however well it fits the theme.

## 2. The metaphor map (truth table)

Use — each word names a field or a read that exists today:

| UI word | Real data |
|---|---|
| History | `Logs.get` for one `LogKind`; cursor pages |
| Ref | Latest, Following, Trending (`LogKind`); one selected at a time |
| HEAD | the last-visit row the feed already draws (`FeedParts.lastVisitRow`) |
| Commit | `Commit`: `ShortHash`, `ParentId`, body, images, poll |
| Parent / children | `ParentId`, `CommitPage.Ancestors`, `Commits.replies` |
| Checks | `CommitStatus`: Approved, Pending, Rejected |
| Lane | `Lane`: palette index 0–5 or a custom hex; the card rail color |
| Staged | local drafts plus the scheduled queue (`ScheduledCommit`) |
| Saved / stash | `Stars.index` (starring is saving in this API) |
| Remote | `Conversation`, with `Ahead`, push access, muted and pinned state |
| Fetch | the `Since` poll and the manual refresh of one history |
| Repository home | `ProfileDetails`: profile, activity buckets, streaks, pinned commit |
| Contribution graph | `Activity.Buckets`, the profile's own buckets |
| Key | `DeviceSession.DeviceFingerprint`, the Ed25519 device key |

Do not use — no data backs these words:

| Word | Why not |
|---|---|
| Branch (for a feed or a user) | a lane is a color, not a line of history; feeds have no fork point |
| Merge | `Commit` has one parent; no commit has two |
| Diff | no read returns a text delta between two commits |
| Pull request | replies and quotes are not review requests; `CommitStatus` is moderation, not review |
| Push (for posting) | the server calls the post itself a commit; a second verb for the same act splits one act into two names |
| Clone, fork | no read copies a profile or a history |

One naming rule follows: verbs the user already knows from the site
(Commit, Star, Follow, Reply, Quote) keep the site's word. The
workbench renames surfaces, not acts.

## 3. Ground facts from the code (checked 2026-11-12)

- The current shell caps the reading column at 620 px, or 796 px with
  a collapsed sidebar, inside a 1264 px strip
  ([Shell.fs](src/DevNull.Client.UI/Shell.fs:1344)). Wide windows add
  empty space. This layout exists to end that.
- Cards already ride a lane rail with a branch elbow per reply, and
  the feed already draws a `HEAD` last-visit row and day separators
  ([FeedParts.fsi](src/DevNull.Client.UI/Components/FeedParts.fsi:10)).
  The history look needs no new drawing in its first form.
- A commit card does not navigate. The host receives
  `CommitCard.Event` values and decides
  ([CommitCard.fsi](src/DevNull.Client.UI/Components/CommitCard.fsi:27)).
  In the workbench, `Opened` means "check out this commit in the
  inspector".
- `CommitPage` carries the commit, its ancestors, and its reply
  participants in one read
  ([ApiTypes.fsi](src/DevNull.Client.Core/ApiTypes.fsi:364)).
  The inspector's first form needs that one read plus
  `Commits.replies` for the children list.
- The device session carries the key fingerprint
  ([ApiTypes.fsi](src/DevNull.Client.Core/ApiTypes.fsi:978)). The
  status bar can name the signing key with no new read.
- The window is built in one place
  ([Startup.fs](src/DevNull.Client.UI/Startup.fs:36)), local settings
  are one record with a JSON round trip
  ([SettingsStore.fsi](src/DevNull.Client.UI/SettingsStore.fsi:20)),
  and route pages own lifetime shapes the workbench must not inherit
  (the deck plan, sections 3 and 8, lists these with rulings that
  hold here word for word: sign-in, callback, tray, app-level loops,
  terms gate, old settings files).

## 4. Isolation contract

### 4.1 Shared files a phase may touch (closed list)

Same list as the deck plan, with the setting renamed for this plan:

| File | Allowed change | Phase |
|---|---|---|
| `SettingsStore.fsi` / `.fs` | Add `LayoutChoice` (`CurrentLayout \| WorkbenchLayout`), `Layout` field, one setter, JSON round trip | 0 |
| `SettingsStore.fsi` / `.fs` | Add `WorkbenchState` field (raw stored string) and one setter | 2 |
| `Views/SettingsView.fs` (+ `.fsi` if its surface changes) | Add one Layout row under Appearance | 0 |
| `Startup.fs` | Pick `Shell` or `WorkbenchWindow` from the stored layout | 0 |
| `DevNull.Client.UI.fsproj` | Add `Compile` entries for new workbench files, in order | 0–4 |
| `CHANGELOG.md` | One `Added` entry per shipped phase | 0–5 |
| `tests/**` | New test files only | 0–5 |

If both this plan and the deck plan ever ship, they share the one
`LayoutChoice` type with three cases. The second plan to land adds
its case to `SettingsStore` as its only extra shared edit, and says
so in its Phase 0. Neither plan's window code touches the other's.

### 4.2 Reuse tiers

**Tier A — bring as-is, no edit.** Each item must pass the gate in
section 6 before use: Core reads and types; `Theme` style classes
and lane helpers (classes only, never a themed brush observable);
`Icons`; `Formatting`; `Components/Feed.fs`;
`Components/CommitCard.fs`; `Components/Avatar.fs` and
`AsyncImage.fs` as the card uses them; `Components/Composer.fs`;
`Programs.runPage`, with the host keeping the returned handle.

**Tier B — do not bring.** `Shell.fs` and all it builds; `Routes`,
`Navigation`, `RouteStore`; every route page; the `Timeline`
broadcast service; `Views/CommitRows.fs`; `Tray` (it needs a
router); `Shortcuts` (it navigates a router). The workbench builds
its own equivalents or does without, per section 7's gaps.

**Tier C — workbench-owned.** Window chrome, refs tree, history
column chrome, inspector, staging area, status bar, thread mode,
repository home, key handler, and any Tier A item that fails its
gate. All under `Layouts/Workbench/`.

## 5. Design

### 5.1 Layout anatomy (the one idea taken from the Threads image)

Four regions, three of them with their own scroll, none of them a
fixed 620 px column:

- **Refs (left, 240 px, own scroll).** A tree, not a tab bar:
  History refs (Latest marked `HEAD`, Following, Trending), Saved,
  Scheduled (the staging count), Notifications with its unread
  count, Remotes with per-remote ahead counts, and the signed-in
  user's own repository entry. Selecting a ref changes the center.
  The center never holds two refs at once in v1.
- **History (center, all free width, own scroll).** One ref's
  commits as a graph list: lane rails, day separators, the `HEAD`
  row at the last visit, a fetch pill when the poll finds new
  commits. This is the feed, uncapped and dense-capable.
- **Inspector (right, 380 px, own scroll).** The checked-out commit:
  full body, checks, parent hash, children, author, actions. Empty
  until the first selection; the selection survives scrolling and
  polling in the center.
- **Staging + status (bottom).** A staging strip that opens into
  the composer and the scheduled queue, over a status bar that
  names the ref, the lane, the key, and the last fetch.

The window has no right aside of suggestions and no tabs. Under
1100 px wide the inspector folds over the history as a sheet; under
760 px the layout shows one line that offers the default layout
(section 8, wiremock W5). The workbench is a wide-screen layout and
says so.

### 5.2 Domain values (parse, don't validate)

```fsharp
// Layouts/Workbench/WorkbenchTypes.fsi
type Ref =
  | HistoryRef of LogKind
  | SavedRef
  | ScheduledRef
  | NotificationsRef
  | RepositoryRef of handle: string   // parsed handle, never raw text

type Checkout = { CommitId: string; Page: CommitPage option }

[<Struct>]
type WorkbenchState = { Ref: Ref; CheckoutId: string option }
```

- The stored form is one string in `AppSettings.WorkbenchState`,
  read by one parse leaf (`WorkbenchSettings.parse`). The grammar:
  one ref token — `latest`, `following`, `trending`, `saved`,
  `scheduled`, `notifications`, `repository:<handle>` — then `|` and
  the checked-out commit id when one is held. The checkout id parses
  through Core's own `CommitIds.parse`; a handle trims its `@` and
  refuses whitespace, `|`, and `:`. A bad stored string loads the
  default state (Following, no checkout). The workbench never loads
  half a state.
- A checkout is live state, restored across restarts only as an id:
  the page re-reads on start. A deleted commit at restore shows the
  inspector's empty state with one line, never an error dialog.
- Drafts are not a new store. The staging area reads the local
  draft and scheduled data through the same settings and API paths
  the composer already uses; the workbench adds no second draft
  store.

### 5.3 Programs and lifetime

One host program (`WorkbenchHost`) owns: the selected ref, the
checkout, the staging open state, and one child program per region
that has its own fetch life:

- `HistoryColumn` — one ref's commits, cursor, `Since` poll.
- `Inspector` — one checkout's `Commits.show` and replies cursor.
- `RefsTree` needs no program: it renders from the host model,
  with counts arriving as host messages through `Sub`.

Rules, from the repo's MVUish rules: the host starts child programs
with `Programs.runPage` and keeps every handle; switching a ref
ends the old history program and starts a new one (state a
returning ref needs — scroll place, cursor head — lives in the
host model as plain data, restored into the new program); views
bind only their own program's model; session, clock, and counts
enter through `Sub` sets only; no view binds a store observable;
no child uses `runWithLifetime`, because a folded inspector can
leave the tree and must return alive.

### 5.4 Data flow

```text
ref select ──▶ host Msg ──▶ HistoryColumn starts (Logs.get / Stars.index)
card select ──▶ host Msg (Checkout id) ──▶ Inspector (Commits.show)
parent click in inspector ──▶ host Msg (Checkout parentId)
composer posted ──▶ host Msg ──▶ HistoryColumn Refresh (named, one column)
1-min tick ──▶ Sub.interval in HistoryColumn, gated by host active flag
status bar ◀── host model only (ref, lane, fingerprint, last fetch time)
```

Cross-region effects are named messages through the host. No
broadcast service exists in this layout.

### 5.5 The history look

The first form reuses the card's own rail (`FeedParts` shapes ride
inside `CommitCard`, Tier A through the card). The workbench adds
three things the feed does not have, all as its own chrome:

- A ref header: `HEAD -> Following` in mono, the fetch time, the
  new-count pill worded as "3 new — fetch".
- Row density as a host choice (full card / dense), stored in the
  workbench state. Dense is the default in this layout.
- A selected row state: lane-colored left edge, held in the host
  model by commit id, so a poll that re-sorts Trending cannot lose
  the checkout.

### 5.6 What each social surface becomes

- **Thread:** opening a commit checks it out in the inspector.
  A full thread mode (Phase 4) takes the center: ancestors as a
  parent chain in the inspector's place, replies in the center,
  the feed kept one key away (`Esc` returns, scroll intact).
- **Profile:** the repository home (Phase 4): header with lane and
  counts, the pinned commit drawn as the readme card, the
  contribution graph from the activity buckets, and the user's
  commits as a history below. Followers and following are lists in
  the inspector's place, not pages.
- **Notifications:** a center list with its own rows (a
  notification is not a commit), grouped by kind, with checks
  words for Approved and Rejected. Selecting one with a commit
  checks that commit out.
- **Remotes:** out of the planned phases, as in the deck plan.
  The refs tree shows the remotes entry with the total ahead
  count and one line: "Remotes open in the default layout." A
  later plan may add a conversation pane; the watcher cost is
  counted there first.
- **Search:** the header search box opens the workbench's own
  palette (Tier C). A picked user becomes a repository ref; a
  picked commit checks out. Free-text search results are a center
  list over `Search.commits`, manual refresh only, as the API has
  no cursor.

## 6. Reuse gates (start of Phase 1)

Same rule as the deck plan: a failed gate moves the item to Tier C.
No gate is fixed by editing the shared item.

| # | Item | Check | If it fails |
|---|---|---|---|
| G1 | `Feed.view` | Rows fill a free-width center and a 380 px inspector-adjacent list; virtualization holds | `WorkbenchFeed` under `Layouts/Workbench/` |
| G2 | `CommitCard.feedRow`, dense and full | Deck-style row context: `Menu = ValueNone`, translation lookup answering `ValueNone`, flyout with workbench callbacks (open user = repository ref) | `WorkbenchCard` under `Layouts/Workbench/` |
| G3 | `Composer.Page` | Hosted in the staging area, `Target = Fresh` and `Quoting`; post and schedule work with no router | `WorkbenchComposer`; schedule naming per gap W-4D |
| G4 | `TermsDialog.Page` | Hosts as the workbench window's gate | Workbench shows the accept-in-default-layout line instead |
| G5 | `CommitCard.chainRows` | Its `ChainCtx` takes a workbench `Grow` callback with no edit, for the Phase 4 thread chain | Thread mode lists replies as dense cards; no chain drawing in v1 |

## 7. Wiremocks

W1 is the layout every later wiremock varies. All regions are the
workbench's own chrome.

### W1 — Main workbench, 1600 px

```text
┌ drag band ─────────────────────────────────────────────── [_] [□] [×] ┐
│ /dev/null · workbench        [search…          ] [@me ▾] [＋ Commit]  │
├────────────┬──────────────────────────────────┬───────────────────────┤
│ REFS       │ HEAD -> Following        ↻ fetch │ CHECKOUT  a1b2c3d     │
│            │ fetched 12s ago · 3 new — fetch  │ @ana · lane 2 · ★ 12  │
│ HISTORY    │──────────────────────────────────│ checks: ✓ approved    │
│  * Latest  │ ◉ a1b2  @ana        2m           │ parent: 09fe…  ▸      │
│  ● Follow. │ ├─◉ reply @cy       4m           │ children: 4 replies   │
│    (HEAD)  │ ◉ 7788  @bo        11m           │───────────────────────│
│  * Trending│ │ body text, two lines…          │ shipped the fix. the  │
│            │ ◉ c3d4  @cy        19m           │ parser now reads the  │
│ SAVED   4  │   [load more]                    │ whole file before…    │
│ STAGED  1  │                                  │ [img ×2] [poll]       │
│ NOTIFS ●3  │        own scroll                │───────────────────────│
│            │                                  │ [Reply] [Quote] [★]   │
│ REMOTES ↑2 │                                  │ replies ▾             │
│ @me (repo) │                                  │ ◉ @cy: tests pass     │
├────────────┴──────────────────────────────────┴───────────────────────┤
│ STAGING: 1 draft · 1 scheduled                    [open staging ▸]    │
│ ⎇ Following · lane 2 · key 9F3A…C1 · @me · fetched 12s ago · ●3      │
└───────────────────────────────────────────────────────────────────────┘
```

### W2 — Staging open

```text
│ STAGING ──────────────────────────────────────────────────── [close] │
│ draft:  "release notes for…"            [Commit] [Schedule] [Clear]  │
│ queued: tomorrow 09:00  "…"             (the queue lives here,       │
│                                          one row per queued commit)  │
│ ⎇ Following · lane 2 · key 9F3A…C1 · @me · fetched 12s ago · ●3      │
```

"Commit" here is the site's own verb: it posts. The staging words
name where the text waits, nothing more (section 2).

### W3 — Thread mode (Phase 4)

```text
│ [Esc back] THREAD a1b2c3d          │ PARENTS                       │
│ ◉ a1b2c3d @ana  (checked out)      │ ◉ 09fe… @bo                   │
│ │ full body, images, poll          │ ◉ 55aa… @ana                  │
│ ├─◉ reply @cy                      │ (root)                        │
│ ├─◉ reply @bo                      │───────────────────────────────│
│ │ └─◉ nested @ana                  │ CHECKOUT (as W1)              │
│ [load more replies]                │                               │
│ [ reply box …            ] [Post]  │                               │
```

### W4 — Repository home (Phase 4)

```text
│ @ana  Ana Example · lane 2 · Lisbon      [Follow] [★ counts…]       │
│ bio line…  github ↗  joined 2024                                    │
│ ┌ README (pinned) ────────────────────────────────────────────────┐ │
│ │ ◉ pinned commit, full card                                      │ │
│ └─────────────────────────────────────────────────────────────────┘ │
│ contributions: ▁▃▅▃▂▅▇▅▃▁  (activity buckets, lane-colored)         │
│ streak 12 · level 4 · rank …                                        │
│ HISTORY @ana ────────────────────────────────────────────────────── │
│ ◉ … commits as W1 rows …                                            │
```

### W5 — Narrow and signed-out

```text
Under 760 px:                        Signed out:
┌──────────────────────────┐         ┌──────────────────────────┐
│ The workbench needs a    │         │ /dev/null                │
│ wider window.            │         │ You are signed out.      │
│ [Use default layout]     │         │ [Sign in] (browser flow, │
└──────────────────────────┘         │  workbench-owned screen) │
                                     └──────────────────────────┘
```

## 8. Dry run — gaps found before work starts

Walked against the code on 2026-11-12, as in the deck plan. The
deck plan's Phase 0 gaps hold here with the window name changed:
0-A (own sign-in screen), 0-B (window finishes the callback),
0-C (no tray in v1, decide at Phase 0), 0-D (window starts the
unread poll, remotes watcher, and settings refresh through the env
service contracts and disposes them on close), 0-F (old settings
files load with defaults). This section lists only the gaps this
layout adds.

- **W-1A (truth) Lanes are not branches.** A lane colors one
  author's rail. Two authors with the same lane index share a
  color and no history. Ruling: the UI never draws two lanes
  joining or splitting as a merge or a fork. Reply elbows branch
  off a parent commit only, because a reply has that parent.
  The word "branch" appears nowhere in this layout.
- **W-1B (truth) The history is not sorted by parent.** A log page
  is time-ordered (or score-ordered, for Trending). A commit's
  parent can sit far above it or on another page. Ruling: the
  center list stays in the API's order. Parent links live in the
  inspector and in reply blocks, never as a re-sorted graph. No
  phase may sort a history by ancestry: the data for a full sort
  is not in one page, and a part-sort teaches a false order.
- **W-2A (blocks Phase 2) Ref switching ends programs.** Each ref
  switch disposes one history program and starts another
  (section 5.3). Ruling: switch cost is one first-page fetch per
  return to a ref, with scroll and head kept in the host. A test
  switches refs 10 times and counts live programs. If the leak
  gate's method shows growth, Phase 2 does not pass.
- **W-2B (poll) Only the shown ref polls.** One `HistoryColumn`
  lives at a time, so the poll budget is one history poll a minute
  plus notifications counts on the host's own tick when the
  notifications ref is not shown. Ruling: the refs tree's counts
  update on ref switches and on the shown column's poll, not on a
  second timer. The tree can show a stale count between polls;
  the count is stamped with the fetch time in the status bar.
- **W-3A (blocks Phase 3) The inspector needs a second read.**
  `Commits.show` gives ancestors and participants; the children
  list needs `Commits.replies` with its own cursor. Ruling: the
  inspector runs both, shows the commit first, and loads children
  under it. A failed replies read shows one line in the children
  block and never fails the checkout.
- **W-3B (selection) A poll can remove the checked-out row.**
  Trending re-sorts; a delete removes. Ruling: the checkout is by
  id in the host, not by row. If the row leaves the list, the
  inspector keeps the commit (it re-reads on its own) and the
  center shows no selected row. No phase may clear a checkout as
  a side effect of a list change.
- **W-4D (composer) Schedule needs its queue surface.** The
  staging area is that surface: it lists the scheduled queue from
  the same read the default layout's scheduled page uses, and a
  scheduled post from the composer lands there. Ruling: if that
  read cannot be called from workbench code without a route page,
  the staging area shows drafts only in Phase 3 and the queue row
  reads "The queue opens in the default layout." No silent loss.
- **W-4E (repository home) The contribution graph has one scale
  field and day buckets.** `Activity.Scale` is a server string.
  Ruling: the graph draws buckets in lane color with the count in
  the tooltip, and takes the scale's word from the server. It does
  not invent week columns the buckets do not carry.
- **W-5A (tests) The leak gate builds the shell.** Workbench leak
  cases join it in Phase 5, beside the deck's if both ship: build
  workbench window, switch refs, check out, close window, count
  retained controls by the gate's own method.

## 9. Phases

Each phase ships alone, in order: code, tests, changelog. Sizes:
S (days), M (one to two weeks), L (more).

### Phase 0 — Setting and empty workbench window (M)

Closes: the shared Phase 0 gaps (section 8, first paragraph).

Work: `LayoutChoice` in `SettingsStore` with `WorkbenchLayout`;
one Layout row in `SettingsView` with the restart text; new
`Layouts/Workbench/WorkbenchWindow.fsi/.fs` with its own band and
caption buttons, the three app-loop starts, the callback finish,
the signed-out and narrow screens (W5), and an empty three-region
frame with the refs tree drawn from a static host model; the
one-line window pick in `Startup.fs`; the layout switch inside
the workbench header ("Use default layout", next start).

Acceptance: default untouched at the default setting; a deck or
workbench choice never builds the other window; the callback
signs in under the workbench; the tray ruling in force is stated
in Settings.

Tests: settings round trip, old-JSON load, headless window build
signed-in and signed-out, callback test.

### Phase 1 — History and refs for the three logs (M)

Closes: W-1A, W-1B (by construction), gates G1, G2, G4.

Work: `WorkbenchTypes` and `WorkbenchSettings` with parse tests;
`HistoryColumn` over `Logs.get` (fetch, cursor, 1-minute `Since`
poll, new-count pill worded as fetch); the refs tree for the three
logs with the selected ref in mono; the status bar (ref, handle,
fetch time); density default dense. Cards carry star, vote,
pastel, and poll. The quote event lands in the host and waits
for Phase 3's staging composer; Phase 1 states this in its PR. No
More menu, no Translate (named losses, as in the deck plan).

Acceptance: a user reads all three logs from the tree with no tab
and no route change; the center uses the full free width at
1600 px; the poll runs once a minute while the window is active.

Tests: history update tests (fetch, append, poll, pill), parse
tests, one headless history test.

### Phase 2 — Checkout inspector and stored state (M)

Closes: W-2A, W-2B, W-3A, W-3B.

Work: `Inspector` program (`Commits.show`, replies cursor,
parent walk, author block, star and pastel on the checked-out
commit); selected-row state in the host; `WorkbenchState` stored
through `AppSettings.WorkbenchState` on ref and checkout change;
restore on start; refs counts (saved count, notifications count
through the host `Sub`, remotes ahead total as one number).

Acceptance: wiremock W1 works whole; a restart returns to the
same ref and checkout; deleting the checked-out commit in the
default layout and restarting shows the one-line empty inspector.

Tests: inspector update tests, restore tests, the ref-switch
program count test (W-2A).

### Phase 3 — Staging area and composer (M)

Closes: W-4D's draft half.

Work: the staging strip and its open state (W2) hosting
`Composer` (gate G3) for fresh posts and quotes; the posted
message refreshes the shown history; drafts read and write
through the composer's own draft path; the scheduled list in
staging per the W-4D ruling; the `Commit` header button opens
staging with the composer focused.

Acceptance: a user writes, schedules, and posts without leaving
the window or opening a dialog route; the queue count in the
refs tree matches the staging list.

Tests: staging host tests (post refreshes history; quote arms
the composer), composer-host smoke test in the headless harness.

### Phase 4 — Thread mode and repository home (L)

Closes: W-4D's queue half if open, W-4E, gate G5.

Work: thread mode (W3) with the parent chain in the right
region, reply composer in the center (participants from
`CommitPage.ReplyParticipants`), `Esc` return with the history
scroll intact; repository home (W4) for any handle: pinned
commit as readme, contribution graph, counts, follow state, the
user's commits as a history under it; followers and following as
right-region lists from the repository header counts.

Acceptance: the full loop — read, check out, open thread, reply,
back — never changes the history's scroll; a repository home
opens from a card author, from search, and from the refs tree's
own entry, with the same content each time.

Tests: thread program tests, repository fetch tests, focus and
scroll return test in the headless harness.

### Phase 5 — Notifications center, search, hardening (M)

Closes: W-5A.

Work: the notifications center list with workbench rows and
checkout on select; the search palette and search center (manual
refresh, no cursor, stated in the header); leak and snapshot
tests at 1600 and 1100 px; the request-count test (one history
poll a minute, no search poll); repo gates (`dotnet fantomas .`,
build, test suites); changelog; the workbench help line that
names what lives in the default layout (Remotes, organizations,
the More menu, translations).

Acceptance: as in Phase 5 of the deck plan, with the workbench
window in place of the deck.

Rollback, any phase: set `Layout` to the current layout and
restart. Stored workbench state stays readable by the phase
before it, or the changelog entry says it does not.

## 10. New file manifest (when all phases land)

Under `src/DevNull.Client.UI/Layouts/Workbench/`, each `.fs` with
its `.fsi`, in the fsproj after `Views/SearchView.*` and before
`Views/Routes.*`, in this order:

| File | Holds |
|---|---|
| `WorkbenchTypes.fsi/.fs` | `Ref`, `Checkout`, `WorkbenchState` |
| `WorkbenchSettings.fsi/.fs` | the parse/store leaf, the default state |
| `HistoryColumn.fsi/.fs` | one ref's commits program |
| `Inspector.fsi/.fs` | the checkout program |
| `Staging.fsi/.fs` | the staging area and composer host |
| `ThreadMode.fsi/.fs` | thread center and parent chain (Phase 4) |
| `RepositoryHome.fsi/.fs` | the profile as repository home (Phase 4) |
| `WorkbenchChrome.fsi/.fs` | the refs tree and status bar renderers |
| `WorkbenchHost.fsi/.fs` | refs tree, regions, focus, keys, status bar |
| `WorkbenchWindow.fsi/.fs` | the window: chrome, app loops, callback, sign-in |

Plus any Tier C file a failed gate adds, named in that phase's
PR. Tests: `WorkbenchSettings.Tests.fs`,
`HistoryColumn.Tests.fs`, `Inspector.Tests.fs`,
`WorkbenchHost.Tests.fs` in the test projects that hold their
kind, plus the Phase 5 leak and snapshot cases.

## 11. Decisions still open (defaults in force)

1. **Tray under the workbench.** Default: none in v1; close
   quits; stated in Settings. Decide at Phase 0.
2. **Split history (two refs side by side).** This is the one
   deck idea that could still enter later, as a Phase 6: two
   `HistoryColumn` programs in the center. Default: not planned.
   The refs tree plus fast switching covers the scan job without
   doubling the poll and program cost.
3. **More menu and translations.** Default: absent, as in the
   deck plan, until a phase prices the workbench's own undo and
   translation book.
4. **Remotes pane.** Default: out. A conversation pane in the
   center with members in the right region fits the anatomy, and
   it waits for its own plan and watcher count.

## 12. Relation to the deck plan

`PLAN-deck-layout.md` is intact and unedited. It is the record of
the deck direction, kept in case the user returns to it. If both
layouts ever ship, the app offers three layouts under one
`LayoutChoice`, each window fully its own, and no window's code
imports the other's folder.

