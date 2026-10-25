# Remotes implementation plan

The plan that adds the site's direct messages ("Remotes") to the desktop
client. Written against the production server at `E:\dev-null`
(`6ceabb0`, 2026-10-25) and this codebase at `3bc9fba`. The API layer is
done: `DevNull.Client.Core.Api.Remotes` covers every signed endpoint and
is tested. This document covers the state, the views, and the gates.

The server has no realtime channel. The web client polls HTTP and layers
optimistic updates on top. This client does the same, with the rates the
server throttles for: thread 1.5 s active / 4 s idle, inbox 15 s alone /
6 s while a thread is open, typing ping at most every 3 s.

## Fixed decisions

1. **One global store.** `RemotesStore` lives in the env beside
   `SessionStore`. Pages, the sidebar badge, and the tray read one
   subject. Threads stay loaded across navigation. The server's throttle
   budget assumes client-wide polling, not per-view polling.
2. **State and scheduling are separate.** `RemotesStore` is a state
   machine with plain methods (`pollThread`, `push`, `toggleReaction`).
   `RemotesWatcher` owns the timers that drive them. This is the
   `UnreadService` split and keeps the store logic testable headless
   with a fake `IHttp`, no fake clocks.
3. **One route, master-detail.** `/remotes` shows the inbox and the open
   thread side by side. No docked chat; the tray carries alerting.
4. **Voice records WAV first.** 16 kHz mono PCM is the server's accepted
   fallback and needs no Opus dependency. Opus encode is a later add.
5. **Theming follows the tokens already in `Theme.fs`.** The chat adds
   five keys and one shadow; everything else maps to what exists.

## MVUish and leak rules (from .agents/skills/mvuish-audit — binding plan)

The skill's mechanism: an observable property binding roots the control
from the source, and NXUI's fluent setters discard the disposable that
would end it. A binding ends when the source completes or the program
teardown completes the model subject. App-lifetime sources never
complete, so a view bound to one is retained for the process.

Classifications for everything this plan adds:

| Planned binding or timer | Classification |
| --- | --- |
| `RemotesStore` subject and its `RemotesWatcher` timers | App-lifetime service, owned by `Env`, disposed on window `Closed` beside `unreadPoll` (`Shell.fs`). Allowed. |
| Sidebar badge on the Remotes entry | Shell-lifetime binding through the existing `Badge: IObservable<int> voption` slot (`Shell.fs:87`). Same as the notifications badge. Allowed. |
| Page views binding store state | Never direct. The store enters a page only through `Sub.fromObservable` in `State.subscribe` (the `NotificationsView` shape), which feeds messages into the model. The view binds only its model stream, which dies with the program. |
| Inbox rows, message list | `BindItemsSource` with a key function and `supportsRecycling = false` templates (the `Feed.fs` shape). Rows release when the page program ends. |
| Route page lifecycle | `runWithLifecycle` plus `props.Context.AddDisposable lifetime`, the route-page rule. No `Route.cache`; returning-page state lives in the store (the `TimelineService` precedent: `Remember`/`OffsetOf`/`DraftOf`). |
| Message images | The `AsyncImage.Loader` pattern: control-owned bitmaps, disposed on replace and detach. Signed URLs cached by `ImageCacheService` like commit images. |
| Voice playback position timer, recording meter timer | Control-owned, stopped on every path — stop, detach, discard, page end. Never hosted inside a `TextBlock` inline (the skill's inline-rebuild trap). |
| Rebuilt content | The thread pane's target identity guards rebuilds the way the shell's user-id cell does; poll refreshes must not rebuild rows. |

Gate: `tests/DevNull.Client.UI.Tests/Leaks.Tests.fs` gains one retention
test per phase that adds a view or control (see each phase). The
mvuish-audit greps run over every phase's diff before commit:

```
git diff -U0 -- src/DevNull.Client | rg "^\+.*(Observable\.add|\.Subscribe\()"
git diff -U0 -- src/DevNull.Client | rg "^\+.*(Brushes\.(background|surface|card|border|text|muted|accent|onAccent|danger|rail|laneFallback|laneOf|lane ))"
git diff -U0 -- src/DevNull.Client | rg "^\+.*(Route\.cache|runWith\b|runWith[^L])"
```

Hits must classify as page-model, Sub-owned, or shell before they pass.

## Phase 0 — theme groundwork

**Goal:** the chat's visual vocabulary exists before any view uses it.

**Work:**
- `Theme.fsi/fs`: add `success` (`#2f9e44` light, `#51cf66` dark),
  `popover` (`#ffffff` / `#1f1f22`), `input` (aliases rail for now).
- The one allowed depth: a bubble shadow style (server:
  `0 12px 30px -12px rgb(0 0 0 / 0.45)`), applied by a class on bubbles,
  images-in-messages, and link cards only.
- Named classes for: the "N ahead" pill (mono 11px, accent fill, onAccent
  text, tabular), the day marker (mono 11px accent, trailing hairline at
  50%), the reaction chip (surface fill, 13px), the "N new" pill.

**Verify:** a temporary probe renders each token in both themes against
the server's `app.css` values; `dotnet fantomas .` clean; full build.

**Deliverable:** nothing user-visible. Phases 2+ consume the classes.

## Phase 1 — RemotesStore and RemotesWatcher

**Goal:** the whole chat state machine, headless and tested, exactly the
semantics of the web's `resources/js/lib/chat.ts`.

**Work:**
- `src/DevNull.Client/Remotes.fsi/fs`:
  - `ChatTarget` (partner username or org id), thread keying.
  - `ThreadState`: messages, `HasOlder`, `ReadThrough`, partner read
    fields, `Typing`, `Access`, `Pending` sends, `ReactionsVersion`,
    `Head`/`HeadUntil`, reply draft slot.
  - `InboxState`: the three tab lists with `HasMore`/`LoadingMore` per
    tab, `Unread`, `Requests`.
  - Versioned pin/mute preferences: a slower, older response never undoes
    a newer save.
  - Methods: `PollThread` (after + reactions_after merge, dedupe by
    `client_id`), `PollInbox`, `LoadOlder`, `Push` (staged pending, retry
    reuses the `ClientId`), `ToggleReaction` (in-flight guard so a poll
    cannot clobber a live change), `SetArchived` (rollback on refusal),
    `MarkRead` (only when focused and scrolled to bottom), `PingTyping`
    (3 s throttle), `SetPinned`/`SetMuted` (with the pin-invalidation
    inbox rebuild), `MergeRequest`/`DropRequest`, org mutations, and the
    settings saves that ride `Me`.
  - One `BehaviorSubject<RemotesState>`, emits on the UI thread, the
    `SessionStore` shape.
- `RemotesWatcher`: reference-counted thread ticks (active window 60 s),
  the inbox loop, pause when the window is deactivated or the session is
  signed out. `Start` returns `IDisposable`; `Shell.fs` disposes it on
    `Closed` beside `unreadPoll`.
- `Env.fsi/fs`: `IProvideRemotes`.
- `Errors.fs`: no new cases expected — the store surfaces `AppError` from
  the Core calls as-is.

**Verify (Core-style headless tests, the largest suite in the project):**
- merge and dedupe on overlapping polls; a poll never resurrects a
  reaction mid-flight; a failed push marks `Rejected` and a retry with
  the same `ClientId` sends once; mark-read gating; pin move invalidates
  the cursor and rebuilds depth without duplicates; an older mute save
  loses to a newer one.
- Leaks gate: the watcher's disposables all fire; no timer survives
  `Dispose`.

**Deliverable:** nothing user-visible. Phase 2+ reads the store.

## Phase 2 — the inbox

**Goal:** the Remotes page exists with a live inbox.

**Work:**
- `Views/RemotesView.fsi/fs` (`.fsi` paired, MVU page over
  `Program.mkProgram` + `runWithLifecycle`): tabs Open / Fetched /
  Stashed (Fetched hidden while `Me.Remotes.Requests` is false), search
  (debounced; conversations and candidates in one result list), rows
  (avatar or stacked org avatars, title, excerpt, "N ahead" pill, pin and
  mute marks), scroll-end pagination, merge/drop on fetched rows.
- Rows through `BindItemsSource`; store changes arrive as `Sub`
  messages.
- `Views/Routes.fs`: `/remotes`, session-gated.
- `Shell.fs`: the sidebar entry with `Badge` bound to the store's unread.
- Empty states in git voice ("no conversations yet", mono 13px muted).

**Verify:** headless UI tests over the real shell — tabs render, search
  filters, pagination fires on scroll end, badge follows the store, the
  Fetched tab hides when the flag is off; retention test: leaving the
  page releases its bindings (mirror of the bindings-list leak test).

**Deliverable:** the user can browse, search, and accept or drop
requests; unread shows on the sidebar.

## Phase 3 — the thread, reading

**Goal:** a conversation reads like the site's.

**Work:**
- The detail pane: day separators, author-run grouping, mine-theirs
  bubbles (accent fill vs surface fill — the "blue is you" rule), org
  author labels, event lines (`added`, `renamed`, …) as quiet rows.
- Bodies: the commit segment renderer reused (`CommitCard` module
  helpers); images through `AsyncImage` on the signed URLs; link cards
  that poll `linkPreview` while pending.
- Scroll model: `before`-cursor older pages on scroll-up; the "N new"
  divider between `Head` and `HeadUntil`; "Back to present" when 25+
  newer messages sit below; reply-ghost click loads older until the
  original is found, then flashes it (the dashed-ring flash).
- Typing line; "pulled by N" receipts under my messages when both sides
  share pulls.
- Scroll place and open-target remembered in the store (the `Timeline`
  pattern), so leaving and returning restores position without a cached
  route.

**Verify:** headless UI tests for grouping, dividers, older-page
  loading, restore-after-return; the scroll-soak retention test extended
  to message rows; the audit greps clean.

**Deliverable:** full read-only conversations with live updates.

## Phase 4 — the composer

**Goal:** sending, with the site's optimistic behavior.

**Work:**
- Text: 2,000-char counter (warn at 80%, danger at 0), mention
  highlighting (the commit model), Ctrl+Enter send, Esc cancel.
- Draft stash: per-conversation text kept in the store and persisted
  through the settings hook; restore shows the `stash@{0}` chip with a
  `drop` action.
- Reply bar with cancel; ghost click as in phase 3.
- Images: the existing staged-image components and descriptions; an
  image makes the body optional; the link-card picker (first link,
  chosen, or none).
- `Push`: staged bubble immediately, server message swapped in, rejected
  state with retry on the same `ClientId`.
- The access bar replaces the composer for `request` / `waiting` /
  `restricted` / `closed`, with Merge / Drop on `request`; the server's
  copy verbatim.
- `PingTyping` wired to composer text.

**Verify:** headless UI tests — staged-then-swapped message, rejected
  retry, counter thresholds, stash save/restore/drop, access-bar states;
  store tests from phase 1 already cover the protocol side.

**Deliverable:** two-way conversations. The feature is daily-usable
  here.

## Phase 5 — management and orgs

**Goal:** every conversation action and the org flows.

**Work:** the conversation menu (mute hour/tomorrow/week/forever/off,
  pin, stash/unstash, block partner), the "New remote" candidate picker,
  org create (picker + optional 60-char name), the org header with
  member stack, and the maintainer menu (rename, add with the
  share-history choice, role change, remove/leave).

**Verify:** headless UI tests per menu action against canned store
  responses; refusal paths show the server's message.

**Deliverable:** feature parity with the site's management surfaces.

## Phase 6 — reactions

**Work:** chips row (three quick picks), the picker flyout ranked by
  `reactionUsage`, optimistic toggle through the store's guarded path,
  the who-reacted list, double-click heart.

**Verify:** store tests already cover the guard; UI tests cover chips,
  picker ranking, and optimistic-then-settled rendering.

## Phase 7 — voice

**Work:**
- Playback: the voice bubble — 48-bar waveform drawn from the payload,
  play/pause, 1x / 1.5x / 2x speed, volume persisted in settings; bytes
  through `GetBytes` on the signed URL. The position timer is
  control-owned and stops on every path.
- Recording: `IProvideMicrophone` in `Env` (NAudio WASAPI, mono 16 kHz),
  the 50 ms level meter feeding the live 48 bars, 0.5–60 s bounds, WAV
  encode — all staged through Core's `Voices.parse` and pushed by the
  phase-4 composer's voice lane. Opus encode stays out until asked for.

**Verify:** capability fakes in tests (no device in CI); meter and
  playback timer leak tests (stop on discard, detach, page end).

## Phase 8 — tray alerts

**Work:** the tray tooltip merges notification and remotes unread; a
  toast on a new message ("X sent you a message" — never message text),
  suppressed for muted threads and the open focused thread; optional
  sound behind a setting with the 10 s cooldown; settings rows for the
  remotes switch, who-can-push, and pull receipts.

**Verify:** headless tests with a fake tray; suppression rules per
  state.

## Cross-cutting gates (every phase)

- `dotnet fantomas .` before staging; a `.fsi` beside every new `.fs`.
- The mvuish-audit greps over the diff; every hit classified.
- One new retention test in `Leaks.Tests.fs` for each phase that adds a
  view or control.
- Expecto suites green (`DevNull.Client.Core.Tests`,
  `DevNull.Client.UI.Tests`); the pre-existing flaky UI failures are
  tracked separately, not caused or fixed here.
- A changelog entry per released slice, in the repo's changelog style.

## Open decisions

- `/remotes?with=@user` deep-linking from a profile "Remote" button (the
  web does this) — small, decide in phase 2.
- Whether the sidebar hides the Remotes entry while `Me.Remotes` is
  `None` (off) — decide in phase 2.
- Opus encode (Concentus) — deferred until WAV ships and size matters.
