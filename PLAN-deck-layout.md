# Deck layout — phased implementation plan

Status: plan only. No code in this plan is written yet.
Owner: the person who picks up a phase. Each phase ships alone, in order.
Reference image: `threads-layout.png` in the repo root (Threads deck: one
column per feed, each column with its own scroll, columns past the window
edge, a feed list on the left that adds columns).

## 0. Hard constraints (read first)

1. **The current layout stays the default.** A user who never opts in sees
   no change at all, in any phase.
2. **The deck is completely self-contained.** It does not reuse the shell,
   the router, any route page, or any shell service. It lives in new files
   under `src/DevNull.Client.UI/Views/Deck/`.
3. **Components come over only as-is.** A component in `Components/` may be
   used only if its current `.fsi` fits the deck with no edit. If a
   component needs one changed prop, one new prop, or one new behavior to
   fit, the deck writes its own version inside `Views/Deck/`. The shared
   component is never edited for the deck.
4. **The same rule holds for services.** A store or service the env carries
   may be read only through the deck program's `Sub` set. If the service
   shape does not fit (see `TimelineService` below), the deck builds its
   own inside `Views/Deck/` and leaves the service alone.
5. **Shared-file edits are a closed list.** Section 3 names every shared
   file a phase may touch, and the exact change allowed in it. Any other
   shared-file edit stops the phase. The fix goes in a deck file, or the
   phase returns here for a plan change.
6. **Core is read-only for this work.** `src/DevNull.Client.Core` already
   has every read the deck needs. No new API call, no new decoder.
7. **Repo rules apply in full** (`AGENTS.md`): `.fsi` for every new `.fs`
   (tests and `Program.fs` excepted), comments in the `.fsi`, no `private`
   in a file with an `.fsi`, tupled parameters, at most 3 parameters,
   `asyncEx`, `dotnet fantomas .` before any commit, changelog entry per
   shipped phase.

## 1. Goal and success criteria

The deck is a second window layout for wide screens. It shows several
feeds at once, each in its own column with its own scroll, and it opens a
thread as one more column instead of leaving the feed.

The layout is a scan surface. The default layout stays the read surface.

Success means all of these are true:

- The setting `Layout` exists, defaults to the current layout, and the
  current layout's behavior, tests, and snapshots do not change.
- With the deck on, the app opens in a deck window: no sidebar, no router
  outlet, no right aside from the current shell.
- The user can run at least the three log columns (Following, Latest,
  Trending) at once, add and close columns, and the column set survives a
  restart.
- A thread opens as a column at the right and closes without touching the
  feed columns' scroll or items.
- The user can switch back to the default layout from inside the deck.
  The switch takes effect on the next start. No live window swap.
- No deck column program, view, or row binds an app-lifetime observable.
  The leak gate in the UI tests stays green with deck tests added.
- The deck makes no more server requests per minute than the budget in
  section 5.4 states.

## 2. Non-goals

- No change to the default layout's look, routes, or behavior.
- No reuse of `Shell.fs`, `Views/Routes.fs`, `LogChrome`, `LogView`,
  `SavedView`, `NotificationsView`, `SearchView`, `ProfileView`,
  `CommitDetailView`, or the `Timeline` service. Not as a base, not as a
  helper. Their state and lifetime shapes belong to the router.
- No Remotes (direct messages) in the deck in any planned phase. Remotes
  keeps its own page in the default layout. A deck user opens Remotes by
  switching layout, until a later plan says otherwise.
- No topic columns. The client API has no topic read. Custom columns name
  a log, the saved list, one user, one search term, or notifications.
- No graph canvas, no branch names for feeds, no rename of site words
  (Commit, Star, Follow stay as they are).
- No live swap between layouts in one window. Switching needs a restart
  of the app window, not of the machine. The plan states this in the
  settings text so the user hears it before the switch.
- No cross-column live sync in the planned phases (section 5.5).

## 3. Ground facts from the code (checked 2026-11-12)

Each fact names the file that proves it. A phase that disagrees with a
fact must re-check the file first.

- The window is built in one place:
  `desktop.MainWindow <- Shell(env, callback)` in
  [Startup.fs](src/DevNull.Client.UI/Startup.fs:36). This is the one seam
  where a second window type can enter.
- The first-instance sign-in callback is finished inside the shell, at
  [Shell.fs](src/DevNull.Client.UI/Shell.fs:1571): `DeviceAuth.finish`
  runs, then the router goes to `/signin`. A deck window that receives a
  callback must finish it the same way, minus the router step.
- Local settings are one record, `AppSettings`, in
  [SettingsStore.fsi](src/DevNull.Client.UI/SettingsStore.fsi:20): theme,
  close-to-tray, sidebar state, chat drafts, voice volume, pin prompt.
  Every field has a store setter and a JSON round trip. A layout choice
  and a column set are the same kind of local data.
- The settings page builds its theme row from the store, at
  [SettingsView.fs](src/DevNull.Client.UI/Views/SettingsView.fs:571). The
  layout row in the default layout follows that shape.
- The timeline service is a broadcast channel. `Timeline.Requests` sends
  `Reload`, `ScrollToTop`, and `CommitLanded` to every attached log page
  ([Timeline.fsi](src/DevNull.Client.UI/Timeline.fsi:12)), and each log
  page runs a 1-minute since-poll while attached
  ([LogView.fs](src/DevNull.Client.UI/Views/LogView.fs:1154)). Three
  columns on this service means three polls and triple reloads. The deck
  therefore polls on its own, per column, and never touches this service.
- Route pages hand their program lifetime to `RouteContext`
  ([LogView.fs](src/DevNull.Client.UI/Views/LogView.fs:1734)). A column is
  not a route. The deck host owns each column program's handle itself
  (see section 5.3).
- The feed control caps its rows at 720 px and centers them
  ([Feed.fsi](src/DevNull.Client.UI/Components/Feed.fsi:20)). In a 400 px
  column the cap never binds, so the control fits as-is. Section 6 makes
  this a checked gate, not an assumption.
- The commit card has a dense form: 28 px node, 48 px rail, 8 px padding,
  against 40 / 64 / 12 in the full form
  ([CommitCard.fs](src/DevNull.Client.UI/Components/CommitCard.fs:1238)).
  The deck uses dense rows or it wastes the space it exists to use.
- The card does not navigate. The host receives `CommitCard.Event` values
  (`Opened`, `QuoteRequested`, and the rest) and decides
  ([CommitCard.fsi](src/DevNull.Client.UI/Components/CommitCard.fsi:27)).
  In the deck, `Opened` means "open a thread column", with no router.
- Every read the deck needs exists in Core:
  `Logs.get` (three `LogKind` values, cursor, `Since` poll),
  `Stars.index` (the saved list), `Users.commits` (one user, cursor),
  `Search.commits` (one list, no cursor, 15 searches a minute cap),
  `Notifications.list` (filter, cursor), and the commit thread read
  behind `CommitPage` (ancestors, replies, reply cursor). See
  [Api.fsi](src/DevNull.Client.Core/Api.fsi:1258).
- New F# files need a compile-order entry in
  [DevNull.Client.UI.fsproj](src/DevNull.Client.UI/DevNull.Client.UI.fsproj:31).
  Order in that file is dependency order.
- Test projects that exist today: `tests/DevNull.Client.UI.Tests` (shell,
  feed, leaks), `tests/DevNull.Client.UI.Features.Tests` (page features),
  `tests/DevNull.Client.UI.Snapshots.Tests`, `tests/DevNull.Client.Core.Tests`.

## 4. Isolation contract

### 4.1 Shared files a phase may touch (closed list)

| File | Allowed change | Phase |
|---|---|---|
| `SettingsStore.fsi` / `.fs` | Add `LayoutChoice` type, `Layout` field, one setter, JSON round trip | 0 |
| `SettingsStore.fsi` / `.fs` | Add `DeckColumns` field (raw stored strings) and one setter | 2 |
| `Views/SettingsView.fs` (+ `.fsi` if its surface changes) | Add one Layout row in the Appearance block | 0 |
| `Startup.fs` | Pick `Shell` or `DeckWindow` from the stored layout at window build | 0 |
| `DevNull.Client.UI.fsproj` | Add `Compile` entries for new deck files, in order | 0–4 |
| `CHANGELOG.md` | One `Added` entry per shipped phase | 0–5 |
| `tests/**` | New test files for the deck; no edit to an existing test's meaning | 0–5 |

Nothing else. In particular: no edit to `Shell.fs`, `Routes.fs`, any
existing view, any existing component, `Timeline.*`, `Env.*`, or Core to
make the deck fit. If a phase seems to need such an edit, the deck grows
its own copy under `Views/Deck/` and the phase notes the cost here.

`Env` needs no change at all: deck code takes the capability interfaces
it uses (`#IProvideHttp`, `#IProvideSession`, and so on), never `AppEnv`.

### 4.2 Reuse tiers

**Tier A — bring as-is, no edit allowed.** Each item passes only if its
current `.fsi` fits. Phase 1 starts with the check in section 6.

- Core: `Api` reads, `ApiTypes`, `Commit` posting, capability interfaces.
- `Theme`: style classes, lane helpers, `Fonts`. Deck views use style
  classes only. No themed brush observable in any deck view (repo rule).
- `Icons`: `Glyph` values and `icon` / `iconAt`.
- `Formatting`: `timeAgo`, `shortCount`.
- `Components/Feed.fs`: the virtualized feed, if the gate in section 6
  passes.
- `Components/CommitCard.fs`: with `Dense = true`, if the gate passes.
- `Components/Avatar.fs`, `Components/AsyncImage.fs`: as the card uses
  them today.
- `Components/Composer.fs`: one instance, in the compose dialog or the
  thread column, if the gate passes.
- `Programs.fs`: `runPage` for column programs. The deck host keeps the
  returned `IDisposable`.

**Tier B — do not bring, at any price.**

- `Shell.fs` and everything only it builds (sidebar, band, aside,
  `LogChrome` host, compose/search dialog layers).
- `Views/Routes.fs`, `Navigation`, `RouteStore`: the deck has no routes.
- Every route page (`LogView`, `SavedView`, and the rest of section 2).
- `Timeline` service: broadcast shape, wrong for columns (section 3).
- `Views/CommitRows.fs`: its `Host` record is page-shaped and carries a
  page-model translation lookup. The deck builds its row context from
  `CommitCard.RowProps` in its own file.
- `Unread` service as a view source: `DeckWindow` may start it (gap
  0-D, a service contract call), and the host program may receive its
  count through the host's `Sub` set. No deck view binds `Count`, and
  no column reads it at all in v1; columns show their own new-count
  pills from their own polls.

**Tier C — deck-owned copies.** Anything in Tier A that fails its gate,
plus everything the deck needs that has no component today: column
chrome, thread column, notification rows, deck dialogs, deck window
chrome. Copies live in `Views/Deck/` and name their origin in the `.fsi`
("the deck's own …"), with no reference to this plan in code comments.

## 5. Design

### 5.1 Domain values (parse, don't validate)

Raw stored text and raw UI input become domain values at one parse leaf
each. Downstream deck code never re-checks a string.

```fsharp
// Views/Deck/DeckTypes.fsi — the deck's own vocabulary.
type ColumnSource =
  | LogColumn of LogKind                 // Following | Latest | Trending
  | SavedColumn
  | UserColumn of handle: string         // a parsed handle, never raw text
  | SearchColumn of term: string         // a parsed, non-empty term
  | NotificationsColumn

type ColumnId = private ColumnId of string   // built once, at column birth

[<Struct>]
type ColumnSpec = { Id: ColumnId; Source: ColumnSource; Width: float }

type DeckLayout = private { Head: ColumnSpec; Tail: ColumnSpec list } // 1 to 6
```

Rules the parse leaf enforces, and the types then carry:

- `DeckLayout` holds at least 1 and at most 6 columns. The non-empty
  shape is a head plus a tail, so an empty deck cannot be built.
- No two columns share one `ColumnSource`. One log kind appears once.
  A user handle appears once, case-insensitive.
- One thread column at most, and it is not a `ColumnSpec`: it is host
  state (`ThreadOpen: commitId option`), never stored.
- Width parses into 320.0–560.0. A stored width outside the range fails
  the whole stored layout, and the default deck loads instead. The deck
  never loads half a stored layout.
- A stored column string that names no source fails the whole stored
  layout the same way. Partial decks hide user data; the default deck
  does not.
- The default deck is Following, Latest, Notifications, in that order,
  each 400 px.

`DeckSettings.parse: string list -> DeckLayout` is the one leaf. It
takes the raw stored strings from `AppSettings.DeckColumns`, one string
per column in the stored form `kind:payload:width`, and answers the
domain value or the default deck. `DeckSettings.store: DeckLayout ->
string list` writes it back. No other deck code reads the stored form.

### 5.2 Programs and lifetime

Three program levels, each a stateful component in the repo's shape
(props in, events out, parent notices ride `Cmd.ofAction`):

1. **Deck host program** (`DeckHost`): owns the column list, the thread
   state, the focused column, and the compose dialog state. Its model is
   the only model the deck window's view binds.
2. **One program per column** (`DeckColumn` for commit columns,
   `DeckNotifications` for the notifications column, `DeckThread` for
   the thread column): owns its items, cursor, poll, and scroll place.
3. **Composer program**: the existing `Composer` component, hosted by
   the host in a dialog, or by `DeckThread` for a reply. Tier A gate
   applies.

Lifetime rules, from the repo's MVUish rules:

- The host starts each column program with `Programs.runPage` and keeps
  every `IDisposable`. Closing a column disposes its handle, once.
  Hiding, scrolling away, or covering a column disposes nothing.
- No column uses `runWithLifetime`. A column can leave the visual tree
  in the sideways scroll and must come back with its program alive.
- Column views bind only their own program's model stream. Session,
  clock, and settings enter through the column program's `Sub` set
  (`Sub.fromObservable`, `Sub.interval`), never through props and never
  through a view binding.
- A column program that ends completes its model stream, so its rows
  release. The host never caches a dead column control to re-show it:
  a re-added column is a new program that restores from its own first
  fetch, with its scroll place held by the host as plain data.
- The deck window follows the shell's identity rule in its own code:
  session-driven rebuilds of window chrome happen only when the
  signed-in user id changes.

### 5.3 Data flow

```text
Core Api ──signed request──▶ DeckColumn program ──Msg──▶ Model ──▶ view
                                  ▲                        (own stream)
session store ──Sub.fromObservable┘
UiClock ────────Sub.fromObservable┘
1-min tick ─────Sub.interval (only while window active, see 5.4)

card event ──▶ column Msg ──▶ Cmd.ofAction ──▶ host Msg (OpenThread id)
host ──▶ starts DeckThread program, appends thread column, scrolls to it
composer post ──▶ host Msg (Committed) ──▶ host sends Refresh to the
                 Following and Latest column programs, by direct Msg
                 dispatch the host holds — no broadcast service
```

The host holds one `Dispatch<ColumnMsg>` per live column, in a map by
`ColumnId`. Every cross-column effect is a named message to a named
column. Nothing in the deck subscribes to a stream that outlives the
deck window, except through a program's `Sub` set.

### 5.4 Poll and request budget

- A log column polls `Logs.get` with `Since` once a minute, only while
  the deck window is active and the column is in the sideways scroll's
  loaded range. Off-range columns pause, the way the log page pauses
  while detached today.
- Saved and user columns fetch on open and on manual refresh only.
- A search column fetches on open and on manual refresh only. The
  server cap is 15 searches a minute; the deck never polls a search.
- The notifications column polls `Notifications.list` once a minute
  under the same active-and-in-range rule. It never marks rows
  read: marking read stays with the default layout's inbox. The
  deck shows the unread state as the server last reported it.
- Worst case with 6 columns: 4 polls a minute (three logs, one
  notifications), plus first-page fetches on open. The host enforces
  the active-and-in-range rule in one place, so the count cannot grow
  by accident when a phase adds a source.

### 5.5 State sync policy (accepted limits, stated to the user nowhere)

- One commit can stand in two columns with two star counts. The count
  in a column is true as of that column's last fetch or poll.
- A star, vote, or pastel in one column updates that column at once.
  Other columns learn on their next poll or refresh.
- A delete in one column removes the row there. Other columns drop the
  commit on their next fetch, as the log does today after a delete.
- A posted commit refreshes Following and Latest at once (section 5.3).
  Trending learns on its next poll: it is ranked, and a fresh post does
  not belong at its top.

These limits match what the site itself shows across two open tabs.
Closing the gap needs a shared commit store, which is a separate plan.

### 5.6 Keyboard and focus

- `j` / `k` walk cards in the focused column. `Tab` moves between
  columns; the focused column header shows the focus ring.
- `Enter` on a card opens the thread column. `Esc` closes the thread
  column first, then the compose dialog.
- `c` opens the compose dialog. `r` refreshes the focused column.
- The deck installs its own key handler on its own window. It does not
  reuse `Shortcuts.fs` (Tier B: it navigates a router). The key table
  is deck data, tested as data.

## 6. Reuse gates (run at the start of Phase 1, before any deck view)

Each gate is one small check in the Phase 1 spike. A failed gate moves
the item from Tier A to Tier C. No gate may be "fixed" by editing the
shared item.

| # | Item | Check | If it fails |
|---|---|---|---|
| G1 | `Feed.view` | Build one column with `Feed.view`, dense rows, a footer load-more button. Rows must fill the column width and virtualize. | Deck writes `DeckFeed` in `Views/Deck/`, from the same props shape. |
| G2 | `CommitCard.feedRow` with `Dense = true` | Build it with a deck row context: `Menu = ValueNone`, translation lookup that answers `ValueNone` for every commit, flyout source with deck callbacks (open user = add user column). Star, vote, pastel, poll must work from column dispatch. | Deck writes `DeckCard` in `Views/Deck/`. Do not add a prop to `CommitCard`. |
| G3 | `Composer.Page` | Host it in a deck dialog with `Target = Fresh`, `OnOpenQueue = ValueNone`. Post, schedule, and image attach must work with no router present. | Deck writes `DeckComposer`. Note: schedule with no queue surface means the deck hides the schedule action until Phase 4 adds a queue column. |
| G4 | `Composer.Page` as reply | Host it in the thread column with `Target = ReplyTo id` and participants built from `CommitPage.ReplyParticipants` plus the parent author, in the `ReplyPicker.ParticipantRow` shape. | The thread column ships without inline reply in Phase 4; reply opens the dialog in reply mode instead. |
| G5 | `TermsDialog.Page` | Host it in the deck window as the terms gate (section 8, gap 0-E). | The deck window falls back to the default layout when terms are unaccepted. This fallback must be stated in the Phase 0 work, not found in test. |
| G6 | `UserFlyout.Source` | Supply deck callbacks: open user adds or focuses a user column; open org is inert in v1 (no org surface in the deck); open tag adds a search column. | `Flyout = ValueNone` on every deck card. Mentions stay accent text. |

## 7. Wiremocks

All wiremocks use the deck's own chrome. The band at the top is the
deck window's own drag band with its own caption buttons. No sidebar,
no aside, no tabs from the current shell appear in any wiremock.

### 7.1 Deck, wide window (1600 px and up)

```text
┌ drag band ──────────────────────────────────────────── [_] [□] [×] ┐
│ /dev/null · deck        [@me ▾] [＋ Commit] [search…] [layout ▾]   │
├──────────────┬──────────────┬──────────────┬──────────────┬───────▶ │
│ FOLLOWING ▾ ↻│ LATEST    ▾ ↻│ NOTIFS    ▾ ↻│ SAVED     ▾ ↻│ (more  │
│ ● 3 new      │              │ ● 1 new      │              │ columns│
│──────────────│──────────────│──────────────│──────────────│ scroll │
│ ◉ a1b2 @ana  │ ◉ c3d4 @cy   │ ◉ @bo        │ ◉ e5f6 @ana  │  in)   │
│ │ shipped it │ │ notes…     │ │ starred    │ │ pinned…    │        │
│ ├─◉ reply    │ ◉ 7788 @bo   │   your post  │ ◉ 9900 @cy   │        │
│ ◉ 4455 @cy   │ ◉ ab12 @ana  │ ◉ @cy        │              │        │
│   own scroll │   own scroll │ │ mentioned  │   own scroll │        │
│ [load more]  │ [load more]  │   you        │ [load more]  │        │
├──────────────┴──────────────┴──────────────┴──────────────┴────────┤
│ ＋ Add column   Following · Latest · Trending · Saved · @user ·   │
│                 Search · Notifications        focused: FOLLOWING  │
└────────────────────────────────────────────────────────────────────┘
```

Column header, left to right: source name, source menu (▾: refresh,
close, move left, move right), refresh button (↻), new-count pill
under the header when the poll found new posts. The pill click loads
the new posts and scrolls that column to its top. No other column
moves.

### 7.2 Thread column open

```text
│ FOLLOWING ▾ ↻│ LATEST    ▾ ↻│ THREAD a1b2c3d            [×] │
│ ◉ a1b2 @ana  │ ◉ c3d4 @cy   │ parent 09fe…  @bo             │
│ │ shipped it │ │ notes…     │ ◉ a1b2c3d  @ana  ★ 12  ✓     │
│ ├─◉ reply ◀──┼─ selected    │ │ shipped the fix, full body, │
│ ◉ 4455 @cy   │ ◉ 7788 @bo   │ │ images, poll, link card     │
│              │              │ ├─◉ reply  @cy                │
│              │              │ ├─◉ reply  @bo                │
│              │              │ [load more replies]           │
│              │              │ [ reply box …        ] [Post] │
```

The selected card keeps a lane-colored left edge in its feed column.
The thread column is 440 px, always last, never stored. `Esc` or [×]
closes it. The feed columns keep scroll, items, and focus.

### 7.3 Add column

```text
              ┌ Add column ──────────────┐
              │ Log:  (•) Following      │
              │       ( ) Latest         │   a source that already
              │       (•) Trending       │   has a column is shown
              │ Saved                    │   ticked and cannot be
              │ Notifications            │   picked again
              │ @user:  [ handle…  ]     │
              │ Search: [ term…    ]     │
              │              [Add]       │
              └──────────────────────────┘
```

A bad handle or an empty term keeps the dialog open and shows one
error line under the field. No column is added. The parse leaf from
section 5.1 makes this call; the dialog does not check strings itself.

### 7.4 Layout switch (in the deck)

The deck header's `layout ▾` menu holds one item: "Use default layout".
It writes the setting and shows one line: "The default layout opens
next time the app starts." The deck stays open. The same row in the
default layout's Settings says the mirror text for the deck.

### 7.5 Narrow window and signed-out states

```text
Window under 900 px wide:          Signed out:
┌────────────────────────────┐     ┌────────────────────────────┐
│ /dev/null · deck           │     │ /dev/null                  │
│ The deck needs a wider     │     │ You are signed out.        │
│ window. Widen it, or use   │     │ [Sign in]  opens the       │
│ the default layout.        │     │ browser approval, as in    │
│ [Use default layout]       │     │ the default layout.        │
└────────────────────────────┘     └────────────────────────────┘
```

The signed-out screen is deck-owned (Phase 0, gap A). It starts the
same device flow the sign-in page starts (`ensureKey`,
`startAuthorization`, browser), and the deck window finishes the
callback itself (section 3). When the session store reports a
signed-in user, the deck builds its columns. No route page is hosted.

### 7.6 One column, all states

```text
loading (first fetch)   error            empty            offline*
┌──────────────┐       ┌──────────────┐  ┌──────────────┐ ┌──────────────┐
│ LATEST    ↻  │       │ LATEST    ↻  │  │ SAVED     ↻  │ │ LATEST    ↻  │
│ Loading…     │       │ The server   │  │ Nothing here │ │ Last shown   │
│ (rows ride   │       │ did not      │  │ yet. Star a  │ │ 4 min ago.   │
│  the rail    │       │ answer.      │  │ commit and   │ │ [Retry]      │
│  as ghosts)  │       │ [Retry]      │  │ it lands     │ │ (rows stay,  │
└──────────────┘       └──────────────┘  │ here.        │ │  read-only)  │
                                         └──────────────┘ └──────────────┘
* offline = a fetch failed after rows already loaded. Rows stay.
  The header shows the age line. Star and vote stay live in the
  model but the server answer decides, as in the default layout.
```

Every state is one column model state. The host never paints a
deck-wide error over healthy columns.

## 8. Dry run — gaps found before work starts

Each phase below was walked against the current code on 2026-11-12.
A gap is a place where the phase, as first sketched, would not build,
would not run, or would silently lose a behavior. Every gap has a
ruling. A ruling marked "decide" needs the user's answer at the phase
gate; the plan gives the default that the phase uses if no answer
comes. No gap may be found again in code review: the phase work lists
the gap it closes.

### Phase 0 gaps — setting, window pick, entry and exit

- **0-A (blocks Phase 0) Signed-out users have no deck sign-in.**
  `SignInView.Page` needs a router (`Nav: INavigable`) and a
  `RouteContext` ([SignInView.fsi](src/DevNull.Client.UI/Views/SignInView.fsi:23)).
  The deck has neither, by design. Ruling: the deck window builds its
  own signed-out screen (wiremock 7.5) from `DeviceAuth`:
  `ensureKey`, `startAuthorization`, `startBrowser`, then the session
  store's `Observe` through the `DeckHost` program's `Sub` set. The
  host exists signed out too; its model carries the signed-out state
  and builds no columns until a session lands. The flow is about
  300 lines in `SignInView.fs`; the deck writes its own smaller one
  (start, wait, error line) and does not copy the page.
- **0-B (blocks Phase 0) The first-instance callback lives in the
  shell.** At [Shell.fs:1571](src/DevNull.Client.UI/Shell.fs:1571) the
  shell runs `DeviceAuth.finish(env, uri, log)` and then navigates.
  Ruling: `DeckWindow` takes the same `callback: string option`
  parameter, runs the same `finish` call on open, and then does
  nothing: the session store change builds the columns. One test must
  prove the callback signs in under the deck (Phase 0 tests).
- **0-C (behavior loss) The tray cannot come over.** `Tray.install`
  needs a `Router` ([Tray.fsi](src/DevNull.Client.UI/Tray.fsi:11)),
  for its Notifications and Saved menu items. The deck has no router.
  Ruling (decide; default: accept and state it): the deck window has
  no tray icon in v1. Close-to-tray has no effect while the deck is
  the layout: a deck window close quits the app. The settings row for
  close-to-tray gains one line under the deck switch, and the deck
  header gains a Quit item. If the user rules otherwise, Phase 0
  grows a deck-owned tray with two items (Open, Quit) and no router:
  about one extra file, priced in Phase 0 as an option.
- **0-D (behavior loss) App-level loops live in the shell.** The shell
  starts the unread poll, the remotes watcher, the user-settings
  refresh, and the window-active gate
  ([Shell.fs:1472](src/DevNull.Client.UI/Shell.fs:1472) to line 1522).
  A deck window that skips them loses unread counts, remote message
  notices, and fresh translation settings. Ruling: `DeckWindow`
  starts the same three services through the env contracts
  (`env.Unread.Start`, `env.Remotes.Watcher.Start`,
  `env.UserSettings.Refresh`) with its own window-active subject.
  These are service contracts, not shell code; calling them is not
  reuse of the shell. Each start is disposed on window `Closed`.
- **0-E (terms gate) Unaccepted terms block the shell, not the deck.**
  The shell layers `TermsDialog` over everything while
  `TermsAccepted` is false. Ruling: gate G5 in Phase 1 decides. If
  `TermsDialog.Page` hosts cleanly in the deck window, the deck shows
  it the same way. If not, the deck window shows one line ("Accept
  the terms in the default layout") and the layout switch. The deck
  never lets an unaccepted user read a feed.
- **0-F (small) Old settings files must load.** `load()` in
  [SettingsStore.fs](src/DevNull.Client.UI/SettingsStore.fs:219) reads
  each key with a default when the key is absent. Ruling: `layout`
  and `deck_columns` follow the same pattern (absent = current
  layout, absent = default deck). One load test with an old-shape
  JSON proves it. Note: `SettingsView.fs` builds an `AppSettings`
  literal for its first model (line 101), so adding a field touches
  that literal: it is inside the allowed edit for the settings page.
- **0-G (small) The window pick must not read settings twice.**
  `Startup.start` builds the env before any window exists, and
  `DesktopApp` receives `(env, callback)`. Ruling: `DesktopApp` reads
  `(env :> IProvideSettings).Settings.Current.Layout` once, in
  `OnFrameworkInitializationCompleted`, and builds `Shell` or
  `DeckWindow`. No new startup parameter, no env change.

### Phase 1 gaps — one column end to end

- **1-A (blocks Phase 1) `Feed` rows may not fill a column.** The
  720 px cap in [Feed.fsi](src/DevNull.Client.UI/Components/Feed.fsi:20)
  is a max, so a 400 px column should bind it from below. Gate G1
  proves it with a real build. If the rows center or leave a gutter,
  `DeckFeed` is written (Tier C) and Phase 1 grows by that file.
- **1-B (blocks Phase 1) The card wants a translation lookup and a
  menu.** `CommitCard.RowProps` requires a translation observable, a
  flyout source (or none), and action slots
  ([CommitCard.fsi](src/DevNull.Client.UI/Components/CommitCard.fsi:175)).
  Ruling: Phase 1 supplies a lookup that answers `ValueNone` for
  every commit and `Menu = ValueNone`. Cards in Phase 1 have star,
  vote, pastel, poll, and quote. They have no More menu and
  no Translate button. Both are named losses, restored in Phase 4
  only if the user asks. Gate G2 proves the rest builds as-is.
- **1-C (poll cost) One column, one poll, no sharing.** The deck may
  not use `Timeline.Requests` (section 3). Ruling: the column
  program's own `Sub.interval` at 1 minute. The host sends a
  `SetPolling` message; the column model holds `Polling: bool` and
  the subscription set reads only that field. No observable from
  the host or from a store enters the column's props (repo rule 3).
- **1-D (scroll place) The feed restores from a parked offset.** The
  log page parks scroll in the `Timeline` service by log kind. The
  deck may not. Ruling: the host holds `Map<ColumnId, float>` as
  plain model data, hands `Restore` to the column view, and takes
  `OnScroll` reports back. No service is built for this.
- **1-E (images) Rows decode bitmaps.** Every deck row that shows an
  image goes through `AsyncImage` inside the card, as today. Ruling:
  the leak tests in Phase 1 include one column close with images on
  screen. If bitmaps survive the close, the fault is in the deck's
  disposal of the column program, and Phase 1 does not pass.

### Phase 2 gaps — many columns, add and close, persistence

- **2-A (blocks Phase 2) A closed column's program must end.** The
  host keeps the `IDisposable` from `Programs.runPage` per column
  (section 5.2). Ruling: close = dispose handle, drop the control,
  drop the scroll entry. Re-adding the same source builds a fresh
  program. A test opens and closes a column 5 times and counts live
  programs through the leak gate's own method.
- **2-B (duplicate sources) The parse leaf, not the dialog, refuses
  duplicates.** Section 5.1 puts the rule in `DeckLayout`. Ruling:
  the add dialog shows used sources ticked (wiremock 7.3) as a
  courtesy, and `DeckLayout.add` returns the layout unchanged for a
  duplicate. The type keeps the rule if the dialog is ever replaced.
- **2-C (column cap) Six columns is a stored-layout rule, not a UI
  rule.** At 6 columns the sideways scroll holds 2400 px of columns.
  Ruling: `DeckLayout` refuses a seventh. The add dialog shows
  "Six columns is the most the deck holds." No other phase may raise
  the cap without a poll-budget change in section 5.4.
- **2-D (focus) Sideways scroll hides columns; their programs must
  not poll.** Ruling: the host computes in-range from the scroll
  offset and column widths on each scroll settle, and sends
  `SetPolling` messages. A column that never enters range never
  polls. Its first fetch still runs at build, so a user who scrolls
  to it sees rows, then polls start.
- **2-E (persistence) Column order and widths ride `AppSettings`.**
  `DeckColumns: string list` joins the record (Phase 2 allowed
  edit). Ruling: writes go through the store's writer stream like
  every other field. A drag-reorder writes once, on drop, never per
  pixel. Width changes write on the same settle rule as chat drafts
  only if a resize control ships; v1 has fixed 400 px columns, so
  widths are stored but not user-changed until a later phase.

### Phase 3 gaps — saved, user, search, notifications columns

- **3-A (saved means starred) `SavedColumn` reads `Stars.index`.**
  Starring is saving in this API
  ([Api.fsi](src/DevNull.Client.Core/Api.fsi:1056)). Ruling: the
  column title is "Saved", the read is `Stars.index`, and a star
  toggle inside the saved column removes the row at once in that
  column's model (the server has unstared it; the row's own state
  says so). No other column changes (section 5.5).
- **3-B (user column replies) `Users.commits` takes a `Replies`
  flag.** Ruling: `false`. A user column shows top-level commits,
  like a profile's first view. The handle parse accepts the same
  handles the site accepts; a handle with no profile shows the
  column error state with the server's words.
- **3-C (search is a snapshot) `Search.commits` has no cursor.**
  Ruling: a search column shows one page (the API's own limit),
  has no load-more, and refreshes only by hand or on column re-add.
  The header shows "Search · manual refresh". The 15-a-minute cap
  is stated in the column error state when the server refuses.
- **3-D (notifications are not commits) The notifications column
  needs its own rows.** A `Notification` has an actor, a kind, an
  optional commit, and a group count
  ([ApiTypes.fsi](src/DevNull.Client.Core/ApiTypes.fsi:676)). Ruling:
  `DeckNotifications` renders its own row (avatar, kind line in the
  site's words, time, unread dot). A row click with a commit opens
  the thread column. A row click with no commit does nothing. The
  column never marks rows read (section 5.4).
- **3-E (no org surface) Organizations have no column in v1.**
  `Users.followedOrganizations` exists, but an org column needs an
  org header and member facts the deck does not plan to draw.
  Ruling: org handles in the add dialog answer "Organizations open
  in the default layout." A later plan may add the column.

### Phase 4 gaps — thread column and composer

- **4-A (blocks Phase 4) Thread rendering has no reusable page.**
  `CommitDetailView` is a route page (Tier B). Ruling: `DeckThread`
  reads `Commits.show` (ancestors) and `Commits.replies` (cursor)
  itself and renders ancestors as dense cards, the commit as a full
  card, replies as dense cards. It does not draw the branch-elbow
  chain in v1; the lane rail on each card carries the thread read.
  If the user wants the chain, it is a deck-owned `DeckChain` file
  and a priced addition, not a reuse of `CommitCard.chainRows`
  unless that function's current shape takes the deck's context
  with no edit (check it in the Phase 4 spike; its `ChainCtx` needs
  a `Grow` callback the deck can supply).
- **4-B (reply participants) The reply composer needs the thread's
  participants.** `Composer` takes `Participants` as
  `ReplyPicker.ParticipantRow list voption`. Ruling: gate G4.
  `DeckThread` builds the rows from `CommitPage.ReplyParticipants`
  plus the parent author after `Commits.show` lands. Before it
  lands, the reply box stays shut with one line: "Loading the
  thread…".
- **4-C (quote) A quote opens the composer, not a route.** The
  default layout routes quotes to `/quote/:id`. Ruling: in the
  deck, `QuoteRequested` opens the deck compose dialog with
  `Target = Quoting id` and the quoted card shown over the dialog,
  as the site shows it. Gate G3 covers the dialog; the quoted card
  is one `CommitCard` in the dialog, dense.
- **4-D (schedule has no home) The composer can schedule, and the
  deck has no queue surface.** Ruling: in the deck the schedule
  action is hidden until the user asks for a Scheduled column
  (a `ScheduledColumn` source over the scheduled index read, same
  shape as Saved). Hiding is a `Composer` host choice today: the
  dialog the shell builds already passes `OnScheduled` as an
  option. If the current `Composer` cannot hide schedule without
  an edit, the deck shows schedule and, on success, shows one line:
  "Queued. The queue lives in the default layout." No silent loss.
- **4-E (delete and undo) The shell's undo toast is shell code.**
  Ruling: Phase 4 cards gain the More menu only with delete, and
  delete in the deck shows the deck's own confirm and no undo in
  v1, or the phase does not ship the menu. The plan's default: no
  More menu in the deck until a phase prices the deck's own undo
  (30-second restore through `Commits.restore`, which Core has).
  This is a named loss, stated in the changelog entry.

### Phase 5 gaps — hardening

- **5-A (leak gate) New programs need new leak tests.** The gate in
  `tests/DevNull.Client.UI.Tests/Leaks.Tests.fs` builds the shell.
  Ruling: Phase 5 adds the deck cases beside it (build deck window,
  open and close columns, close window, count retained controls by
  the gate's own method). The deck does not pass without them.
- **5-B (headless tests) Deck tests need a test env.** The existing
  harness builds a shell over a fake env. Ruling: deck tests build
  `DeckWindow` over the same fake env through a new harness entry,
  in the deck test files only. No edit to the shell harness's
  meaning.
- **5-C (snapshots) The snapshot suite renders pages at set sizes.**
  Ruling: Phase 5 adds deck snapshots at 1600 px and 1100 px (the
  two wiremock widths that matter) in
  `tests/DevNull.Client.UI.Snapshots.Tests`. Snapshot names follow
  the suite's own pattern.
- **5-D (request count) The poll budget needs a test, not a hope.**
  Ruling: one test runs a 3-log-column deck against the fake HTTP
  layer for 3 fake minutes and counts `Logs.get` calls: at most one
  per column per minute, and zero for off-range columns.
- **5-E (fantomas and build) Every phase ends with the repo gates:**
  `dotnet fantomas .`, `dotnet build DevNull.Client.slnx`, and the
  phase's test project. A phase that cannot run the full suite says
  so in its PR and names the suite it ran.

## 9. Phases

Each phase is one deliverable: code, tests, changelog, shippable
alone. A phase starts only when the phase before it has shipped.
Effort is a rough size for one person: S (days), M (one to two
weeks), L (more).

### Phase 0 — Layout setting and empty deck window (M)

Closes gaps: 0-A to 0-G.

Work:

1. `SettingsStore`: add `LayoutChoice` (`CurrentLayout | DeckLayout`),
   `Layout` field, `SetLayout`, JSON key `layout`. Old files load
   with the current layout. The `deck_columns` key joins in Phase 2,
   with the field it stores.
2. `SettingsView`: one Layout row under Appearance, with the restart
   text. The deck side of the switch is item 5.
3. `Views/Deck/DeckWindow.fsi/.fs`: a `Window` that takes
   `(env, callback)`, draws its own band and caption buttons, starts
   the three app loops (gap 0-D), finishes the callback (gap 0-B),
   and shows: the signed-out screen (gap 0-A), the narrow screen
   (wiremock 7.5), or a placeholder deck area with the header
   (wordmark, account handle, Commit button inert, layout menu with
   "Use default layout").
4. `Startup.fs`: the one-line window pick (gap 0-G).
5. The layout menu writes `SetLayout` and shows the restart line.
   No live swap.
6. Tray: follow the gap 0-C ruling in force. State the choice in
   the settings text.

Files: new `Views/Deck/DeckWindow.fsi/.fs` (+ `.fsi` first). Allowed
shared edits: `SettingsStore.*`, `SettingsView.*`, `Startup.fs`,
fsproj entries, changelog.

Acceptance: a signed-in user who picks Deck in Settings and restarts
lands in the deck window; the sign-in callback still signs in; a
signed-out start shows the deck sign-in screen and signs in; the
default layout is byte-for-byte the old behavior when the setting
is untouched. Close-to-tray behavior matches the gap 0-C ruling.

Tests: settings round trip (new and old JSON shapes) in
`tests/DevNull.Client.UI.Tests`; one headless test that the deck
window builds signed-in and signed-out; one callback test.

Gate: the full UI test suite passes with the setting at its default.

### Phase 1 — One log column (M)

Closes gaps: 1-A to 1-E. Runs gates G1, G2, G5, G6 first and records
each result in the phase's PR. A failed gate adds the Tier C file
and the phase says so.

Work:

1. `Views/Deck/DeckTypes.fsi/.fs`, `DeckSettings.fsi/.fs`: the
   section 5.1 values and the parse/store leaf, with unit tests for
   every parse rule.
2. `Views/Deck/DeckColumn.fsi/.fs`: the column program for one
   `LogKind`. First fetch, cursor load-more, 1-minute `Since` poll
   under the host's polling flag, new-count pill, star, vote,
   pastel, and poll choice. Quote opens the compose dialog in quote
   mode (gate G3). The card's reply event is received and does
   nothing in Phase 1: reply lives in the thread column, Phase 4.
   The Phase 1 PR states this in its first paragraph.
3. `Views/Deck/DeckHost.fsi/.fs`: the host program with one column
   (Following), the header, the compose dialog (gate G3), and the
   terms gate per gap 0-E.
4. `DeckWindow` hosts `DeckHost` in place of the placeholder.

Acceptance: Following reads, scrolls, loads more, polls once a
minute while the window is active, and posts through the dialog.
The posted commit refreshes the column. Closing the window ends
every program (leak check by hand in this phase; the test lands in
Phase 5, the check does not wait).

Tests: column state tests (fetch, append, poll, pill) as pure
update tests in the Features test style; parse tests; one headless
column test.

### Phase 2 — Deck of log columns, persistence (M)

Closes gaps: 2-A to 2-E.

Work:

1. Host: add, close, move left/right for the three log kinds; the
   add dialog (wiremock 7.3) with log sources only; sideways
   scroll with in-range polling (gap 2-D); focus model and the
   section 5.6 keys for column focus.
2. Store the layout through `AppSettings.DeckColumns` on every
   add, close, and move. Restore on start. A bad stored layout
   loads the default deck and logs one line.
3. Scroll places held by the host (gap 1-D) for every column.

Acceptance: the deck survives a restart with its columns and order;
six columns is the cap; off-range columns make no poll requests
(a test counts them against the fake HTTP layer).

Tests: host update tests for add/close/move and the cap; restore
tests; the open-close program count test (gap 2-A).

### Phase 3 — Saved, user, search, notifications columns (M)

Closes gaps: 3-A to 3-E.

Work: one source at a time, in this order: Saved, User,
Notifications, Search. Each adds: the `ColumnSource` case (already
in the type from Phase 1), its fetch in `DeckColumn` or its own
program (`DeckNotifications`), its add-dialog row, its header
words, and its tests. Search ships last and carries the manual
refresh rule in its header.

Acceptance: each column matches its wiremock 7.6 states; the
search column never polls (one test with a fake clock proves no
second call); notifications rows open thread columns for rows
with commits (the thread column itself lands in Phase 4, so in
Phase 3 the click selects the row and does nothing else — stated
in the PR, finished in Phase 4).

Tests: per-source fetch and state tests; search no-poll test.

### Phase 4 — Thread column and full composer (L)

Closes gaps: 4-A to 4-E, and finishes the Phase 3 notification
click.

Work:

1. `Views/Deck/DeckThread.fsi/.fs`: show, ancestors, replies with
   cursor, reply composer (gate G4), quote into the dialog
   (gap 4-C), selected-card mark in the source column.
2. Host: `OpenThread` from every column kind, thread close, `Esc`
   order (thread, then dialog), thread is never stored.
3. More menu decision (gap 4-E): ship no menu, or price the deck
   undo and ship delete with it. The phase does not ship delete
   without undo unless the user rules so at the gate.

Acceptance: the full loop of wiremock 7.2 works from each column
kind; the feed column does not move when the thread opens, loads
more, or closes; a reply posts and lands in the thread and in the
source column's reply count on its next poll.

Tests: thread program tests (show, replies cursor, reply post);
host tests for open/close and focus return.

### Phase 5 — Hardening and ship (M)

Closes gaps: 5-A to 5-E.

Work: leak tests for the deck window and column close; the poll
budget test (5-D); snapshots at 1600 and 1100 px; the deck key
table tests; a full run of the repo gates; changelog; one
paragraph in the deck header's help (the `?` item in the deck's
own key table) that names what the deck does not do (Remotes,
organizations, scheduled queue), each with "in the default
layout".

Acceptance: all gates green; a fresh user who never opens
Settings cannot tell the deck exists; a deck user's request count
matches section 5.4.

Rollback, any phase: set `Layout` back to the current layout and
restart. Every phase keeps the stored deck data readable by the
phase before it, or says in its changelog entry that it does not.

## 10. New file manifest (when all phases land)

All under `src/DevNull.Client.UI/Views/Deck/`, each `.fs` with its
`.fsi`, compile order in the fsproj after `Views/SearchView.*` and
before `Views/Routes.*`:

| File | Holds |
|---|---|
| `DeckTypes.fsi/.fs` | `ColumnSource`, `ColumnId`, `ColumnSpec`, `DeckLayout` |
| `DeckSettings.fsi/.fs` | the parse/store leaf, the default deck |
| `DeckColumn.fsi/.fs` | the commit column program (logs, saved, user, search) |
| `DeckNotifications.fsi/.fs` | the notifications column program and its rows |
| `DeckThread.fsi/.fs` | the thread column program |
| `DeckHost.fsi/.fs` | the host program: columns, focus, dialogs, keys |
| `DeckWindow.fsi/.fs` | the window: chrome, app loops, callback, sign-in screen |

Plus any Tier C file a failed gate adds (`DeckFeed`, `DeckCard`,
`DeckComposer`, `DeckChain`), named in that phase's PR. Test files:
`tests/DevNull.Client.UI.Tests/DeckSettings.Tests.fs`,
`tests/DevNull.Client.UI.Features.Tests/DeckColumn.Tests.fs`,
`DeckHost.Tests.fs`, `DeckThread.Tests.fs`, and the leak and
snapshot cases of Phase 5, in the projects that hold their kind.

## 11. Decisions still open (with the default each phase uses)

1. **Tray under the deck** (gap 0-C). Default: no tray in v1,
   close quits, stated in Settings. Decide at the Phase 0 gate.
2. **More menu in the deck** (gaps 1-B, 4-E). Default: no menu
   until a phase prices the deck's own undo. Decide at Phase 4.
3. **Translations in deck cards** (gap 1-B). Default: off in v1.
   The deck's own translation book is a later phase if asked.
4. **Scheduled column** (gap 4-D). Default: schedule hidden or
   labeled as living in the default layout. Decide at Phase 4.
5. **Remotes in the deck.** Out of every planned phase. A new
   plan decides it, with the remotes watcher cost counted first.

## 12. What "done" means for the plan itself

This plan is ready to hand over when a reader can start Phase 0
with no question that sections 3 to 8 do not answer, and can name,
for every shared file they touch, the section that allows it.
Anything found later that this plan got wrong goes in section 8 as
a new gap with a ruling, in the same change that works around it.

