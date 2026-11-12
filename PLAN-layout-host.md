# Layout host — one window, many layouts, live switch

Status: plan only. No code in this plan is written yet.
Owner: the person who picks up a phase. Each phase ships alone, in order.
This plan is infrastructure. It builds no second layout itself: it builds
the place layouts plug into, so `PLAN-workbench-layout.md` and a later
editor-layout plan stop paying the window tax. `PLAN-deck-layout.md`
stays intact and unchosen.

## 0. Intent, stated once

Today `Shell` is two things fused: the app's only window (chrome, tray,
close-to-tray, sign-in callback, the three app loops, the key tunnel)
and the dev-null website layout (width bands, sidebar, feed strip,
aside, routes). Every alternative layout plan so far pays for that
fusion. Each one re-builds a window, a sign-in screen, a callback
finish, three app loops, and its own tray ruling — and every one of
them pushes the layout switch behind a restart.

This plan splits the two.

- A **host** owns what a window must own no matter how the content is
  arranged: chrome, gates, loops, tray, callback.
- A **layout** owns the arrangement only, and mounts inside the host's
  content slot as a plain control tree with its own programs.

The website layout is the default layout, unchanged. Switching layouts
happens at runtime, in the open window, with no restart. The choice
still persists: the stored layout is the one the next start mounts.

## 1. Hard constraints

1. **Default behavior is byte-identical.** With the setting untouched,
   the app builds the host with the default layout mounted, and every
   existing test, snapshot, and behavior passes unchanged.
2. **The `Shell` constructor survives Phase 0.** Tests and `Startup`
   build `Shell(env, callback)` today
   ([Startup.fs](src/DevNull.Client.UI/Startup.fs:36),
   [Harness.fs](tests/DevNull.Client.UI.Tests/Harness.fs)). The split
   keeps that signature: `Shell` becomes the host with the default
   layout mounted. No test churn in Phase 0.
3. **A layout is content, not a window.** Layouts draw no band, no
   caption buttons, no tray, no sign-in screen, no callback finish,
   no app loops. A layout file that grows any of these is wrong.
   Each layout is self-contained under `Layouts/<name>/`; the
   website layout lives in `Layouts/Website/`.
4. **The switch disposes everything the old layout owned.** Every
   program handle, every control, every subscription that is not the
   host's. The leak gate proves it with a switch loop, not hope.
5. **Layout isolation stays.** One layout's files never import
   another's (the rule `PLAN-workbench-layout.md` section 12 already
   states). The route pages stay in `Views/` as website content: only
   `Layouts/Website/**` may open `DevNull.Client.Views`, and a check
   test enforces the path rule. The host imports no layout; it knows
   factories by name.
6. **Core stays read-only.** Everything the host does is UI-layer.
7. **Repo rules apply in full** (`AGENTS.md`): `.fsi` beside every new
   `.fs` (tests and entry points excepted), comments in the `.fsi`, no
   `private` beside an `.fsi`, tupled calls, at most 3 parameters,
   `asyncEx`, `dotnet fantomas .` before any commit, one changelog
   entry per shipped phase.

## 2. Ground facts from the code (checked 2026-11-12)

- `Shell<'env>` is a `Window` taking `(env, callback, ?undoWindow)`
  ([Shell.fs](src/DevNull.Client.UI/Shell.fs:787), `inherit Window()`
  at line 814). It opens 1080×720 with a floor of 380×640 (lines
  834-837).
- The window duties, all inside `Shell`'s constructor:
  the drag band and caption buttons (line 1444), the unread poll with
  its inbox-pause gate (line 1470), the user-settings refresh (line
  1484), the session-driven re-read (line 1497), the window-active
  subject (line 1512), the remotes watcher (line 1527), close-to-tray
  (line 1536), `Tray.install` (line 1548), `Shortcuts.install` with the
  one key tunnel (line 1562), and the first-instance callback finish
  (line 1575).
- The website arrangement, also inside `Shell`: the four width bands
  with their breakpoints 768 / 1024 / 1280 / 1264 (lines 57-82), the
  sidebar `SplitView` with its modes (line 1385), the aside, and the
  overlay stack — compose, help, search, terms, undo — laid over the
  content panel (lines 1456-1462).
- The router is built in the shell, not env-level: `IProvideRoutes`
  carries the route *store* (the live path), and `Routes.build` ran
  inside `Shell` ([Shell.fs](src/DevNull.Client.UI/Shell.fs:852);
  [Routes.fsi](src/DevNull.Client.UI/Views/Routes.fsi)). The route
  store is env-level and survives any switch by construction; the
  router's home is the registry (gap H-0a).
- `Tray.install` needs `Router: IRouter<Control>`
  ([Tray.fsi](src/DevNull.Client.UI/Tray.fsi:11)) — the one place a
  window duty reaches into the default layout's navigation.
- `Shortcuts.install` takes an `Actions` record with `Navigate` and
  overlay opens ([Shell.fs](src/DevNull.Client.UI/Shell.fs:1562)) —
  the tunnel is a window duty, the actions are the default layout's.
- Route pages hand their program lifetimes to `RouteContext`, so an
  unmounted default layout disposes its pages through the mechanism
  the pages already use.
- Settings are one record with a JSON round trip
  ([SettingsStore.fsi](src/DevNull.Client.UI/SettingsStore.fsi:20));
  `LayoutChoice` was sketched in the other plans and is owned by this
  plan from here on.
- The leak gate and the headless harness build the shell
  ([Leaks.Tests.fs](tests/DevNull.Client.UI.Tests/Leaks.Tests.fs),
  [Harness.fs](tests/DevNull.Client.UI.Tests/Harness.fs)); a switch
  test joins them beside the existing cases.

## 3. Target architecture

### 3.1 Ownership after the split

| Today in `Shell` | Goes to | Why |
|---|---|---|
| Window, band, captions, min size, fullscreen | host | window duty |
| Width bands, sidebar `SplitView`, aside | default layout | website arrangement |
| Compose / search / help / undo layers | default layout | default layout chrome (gap H-A) |
| Terms layer, signed-out sign-in screen | host | gates every layout the same way |
| Callback finish (`DeviceAuth.finish`) | host | once, not per layout |
| Unread poll, settings refresh, remotes watcher, window-active | host | app loops, layout-free |
| Close-to-tray, tray | host | window duty |
| Key tunnel install | host | window duty |
| Key actions (Compose, Search, Navigate, WalkCards) | active layout | arrangement verbs |
| Router, route store, boot URL | env (already) | survives switches |
| Route pages, `RouteContext`, `Timeline` | default layout | default layout content |

### 3.2 The layout contract

Every layout is one object behind a small interface, created by a
factory that takes `(env, host)` — two parameters, tupled, the env
intersection the layout needs:

```fsharp
// Layouts.fsi — as landed in Phase 0 (gap H-0b).
/// What the active layout can be asked to do from window-level
/// surfaces: the tray menu, the key tunnel, the host itself.
type LayoutIntent =
  | OpenNotifications
  | OpenSaved
  | ComposeNow
  | SearchNow
  | OpenHome

/// One mounted layout: its control tree, its programs, its intents.
type ILayout =
  inherit IDisposable
  /// The wire name, the same word `AppSettings.Layout` stores.
  abstract Name: string
  /// The root control. The host sets it as its content.
  abstract Root: Control
  /// The key actions the host's tunnel reaches.
  abstract Actions: Actions
  /// True while one of the layout's modal overlays holds the window.
  abstract OverlayOpen: unit -> bool
  /// Navigates the layout's own way, on the router it mounted.
  abstract Navigate: url: string -> unit
  /// True while the layout stands in its narrow band (drawer button).
  abstract Narrow: IObservable<bool>
  /// Opens or closes the layout's drawer; no drawer, no-op.
  abstract ToggleDrawer: unit -> unit
  /// Answers a window-level intent, or ignores it.
  abstract Handle: intent: LayoutIntent -> unit
```

`IDisposable` is the unmount: the layout disposes every program handle
it created and drops nothing else. The host calls it exactly once per
mount, on the UI thread.

The host surface layouts may read (no layout subscribes to a host
observable directly; values enter through the layout's own `Sub` set,
per the repo's MVUish rules):

```fsharp
/// What a layout may ask of its window.
type IHostSurface =
  /// The window the layout is mounted in.
  abstract Window: Window
  /// True while the window stands visible on screen.
  abstract Active: IObservable<bool>
  /// Ask the host to switch layouts by wire name.
  abstract Switch: name: string -> unit
```

### 3.3 The host

`Shell` stays the type name and the file
(`Shell.fsi` / `Shell.fs`), refactored to the ownership table. Its
content slot holds exactly one mounted layout's `Root`. On start it
reads the stored layout name, asks the registry, and mounts. The
signed-out screen and the terms layer sit above the content slot, so
every layout inherits both gates with zero work — gaps 0-A and 0-E of
the layout plans dissolve for every layout at once.

### 3.4 The registry

```fsharp
// Layouts.fsi — as landed in Phase 1 (gap H-0a reversed).
/// One registered layout: its wire name and the factory that mounts
/// it. A factory closes over the env it was registered with, so the
/// registry carries no generic parameter.
type RegisteredLayout = { Name: string; Mount: IHostSurface -> ILayout }

/// The built-in layouts, in settings order. Each mount builds its own
/// router and restores the path the route store kept.
val builtIn: env: 'env * undoWindow: TimeSpan -> RegisteredLayout list
```

The registry is data: wire name to factory. Registration happens where
the env is known — `Startup` for the built-ins, the test harness for
test layouts. Unknown stored name parses to the default layout, and
one log line says so — the same fail-whole-or-default rule the layout
plans use for their own state. The default layout registers under
`current`. Test projects may register their own layouts beside the
built-ins; that is how Phase 1 proves the switch without shipping a
second layout.

## 4. The live switch

1. The user picks a layout in Settings, or a layout calls
   `host.Switch(name)` (the workbench header's "Use default layout"
   item becomes this call).
2. The setter writes `AppSettings.Layout` at once — the choice
   persists even if the app dies mid-switch.
3. The host, on the UI thread: `old.Dispose()`, drop the old `Root`,
   `new <- factory(env, host)`, set `Content` to `new.Root`, rebind
   the key tunnel to `new`'s actions, tell the tray the intents
   changed. One frame, no window rebuild, no flicker beyond it.
4. What survives a switch: the session, every env store (settings,
   drafts, last visit, recent searches, profiles cache), the router
   and its path, and each layout's own persisted state
   (`WorkbenchState` when the workbench adopts this).
5. What resets: scroll places, open dialogs, focus. v1 carries no
   handoff record across layouts; each layout restores its own state
   on mount. Named limit, stated in the changelog entry.
6. Switching is allowed while signed out or under the terms gate: the
   gates cover the content slot, so what mounts behind them cannot
   tell the difference.

## 5. Wiremocks

### H1 — The switch, from Settings

```text
┌ drag band ───────────────────────────────────────────── [_] [□] [×] ┐
│ Appearance                                                           │
│ Layout      (•) Default (site)     ← the website layout, unchanged  │
│             ( ) Workbench          ← from PLAN-workbench-layout.md  │
│             ( ) Editor             ← a later plan, when picked      │
│             The layout changes now. No restart.                     │
└─────────────────────────────────────────────────────────────────────┘
   pick Workbench:  the same window keeps its band, tray, and sign-in;
                    the content slot swaps website → workbench in one
                    frame; the tray menu and keys answer the workbench.
```

### H2 — The gates, above every layout

```text
┌ drag band ───────────────────────────────────────────── [_] [□] [×] ┐
│  ┌ Accept the terms to read ──────────────────────────────────────┐ │
│  │ (the host's terms layer — the same one over every layout)      │ │
│  └────────────────────────────────────────────────────────────────┘ │
│  ▓▓▓ whatever layout is mounted — default, workbench, editor ▓▓▓▓▓ │
└─────────────────────────────────────────────────────────────────────┘
   Signed out: the host's sign-in screen covers the slot the same way.
```

### H3 — Tray, one icon, any layout

```text
 Open            → raise the window, then OpenHome (host)
                   default: /log/latest — byte-identical with today
 Notifications   → LayoutIntent.OpenNotifications
                   default: /notifications · workbench: the ref
                   · editor: the problems panel · unanswered: no-op
 Saved           → LayoutIntent.OpenSaved, same mapping
 Quit            → the real quit (host)
```

## 6. Dry run — gaps found before work starts

- **H-A (blocks Phase 0) The overlay layers are default chrome.** The
  compose, search, help, and undo layers are built in `Shell` today.
  Ruling: they move with the default layout. No other layout inherits
  them: each layout plan already builds its own compose surface
  (workbench staging, editor terminal). The tray's Compose intent is
  the only window-level reach, and it goes through `LayoutIntent`.
- **H-B (behavior) The undo toast stays with the default layout.**
  `undoLayer` serves the route pages' delete flow. Ruling: it moves to
  the default layout; other layouts keep their own rulings
  (workbench gap 4-E already prices its undo).
- **H-C (lifetime) Unmounting the default layout disposes its pages.**
  Ruling: the default layout's `Dispose` disposes the `RouteContext`
  page lifetimes; the router object itself is env-level and survives
  (section 2). Remounting restores the last path: the route store
  still holds it.
- **H-D (keyboard) The tunnel stays, the actions rebind.** The host
  owns `Shortcuts.install`'s tunneling; the `Actions` record is asked
  from the active layout at mount and switch. `Escape` order becomes
  a host contract: layout overlays first (asked through the intent
   surface), then the layout's own escape, then fullscreen exit.
- **H-E (truth in settings) One row, no restart text.** The other
  plans' "opens next time the app starts" copy is deleted by this
  plan; their adoption phases update their own wiremocks (7.4, W-…).
- **H-F (leak) The switch itself is the new risk.** Ruling: the leak
  gate grows a switch loop — default ↔ test layout, 20 rounds,
  retained controls counted by the gate's own method; plus one switch
  with images on screen and one with a thread open. Phase 1 does not
  pass without it.
- **H-G (focus) Focus returns to the layout root on switch.** Ruling:
  the host sets focus to the new `Root` after mount, so no key press
  lands in a dead control. Tested as data in the key table tests.
- **H-H (window floor) One floor for all: 380 × 640.** The host keeps
  today's minima. Layouts degrade inside them (the editor rides to
  380 with its E7 form; the workbench folds its inspector below
  1100). No layout may raise the window floor; a layout that cannot
  degrade shows its own one-line fallback *inside* the slot.

- **H-0a (reversed in Phase 1) Each mount builds its own router.**
  `IProvideRoutes` carries the route store; `Routes.build` ran inside
  `Shell`. Phase 0 shared one router across mounts. Phase 1's switch
  test killed that shape: a fresh outlet re-attached the previous
  route's view, whose MVUish cells had ended at the first detach
  (process crash), and the outlet never removed its
  `Router.Changed` handler, keeping retired outlets reachable.
  Ruling, landed: `builtIn` returns `RegisteredLayout list` again;
  every mount builds its own route table and restores the path the
  env route store kept; the tray reaches pages through intents, the
  callback navigates through the active layout.
- **H-0b (closed) `ILayout` carries five members beyond the sketch**
  (`Actions`, `OverlayOpen`, `Navigate`, `Narrow`, `ToggleDrawer`):
  the host owns the band and the key tunnel and must reach the active
  arrangement. `Narrow`/`ToggleDrawer` serve the website drawer; a
  layout without one answers false and no-op. This surface does not
  grow again without a plan change.
- **H-0c (integration) Module names do not follow folders.** A module
  `DevNull.Client.Layouts` collides with any `Layouts.*` namespace, so
  the website module is `DevNull.Client.WebsiteLayout` in folder
  `Layouts/Website/`. The workbench and editor files carry
  `DevNull.Client.Layouts.Workbench` / `.Editor` namespaces today;
  the integrator normalizes them to `DevNull.Client.Workbench` /
  `DevNull.Client.Editor` at merge.
- **H-0d (open, Phase 1) Signed-out is a route page, not a host
  screen.** No host sign-in screen existed to keep; the website layout
  keeps `/signin` in its outlet. The host cover for non-website
  layouts is decided when the first one ships (decision 1).
- **H-0e (closed) Terms sit above the layout root, so the undo toast
  now draws under the terms layer.** A user the terms gate holds
  cannot act, so no undo toast can exist under it. Accepted.
- **H-1b (open, task `vj9b`) Retired default-layout trees stay
  reachable after a switch away.** The 20-round switch test proves
  the contract for a test layout (disposed once, roots released by
  the leak gate's own method). The default layout's own tree does
  not fully release: MVUish interval timers (`LiveNow`, the log
  page's `since-poll`) outlive their program's dispose, and
  component-internal bindings pin their source after detach. Ruling:
  bounded by user-triggered switch frequency, accepted for the
  switch's first ship; the component audit and MVUish timer-lifetime
  fix are filed as task `vj9b` and land before any layout ships
  beyond the default and the first adopter.

## 7. Phases

Each phase ships alone: code, tests, changelog. Sizes: S (days),
M (one to two weeks), L (more).

### Phase 0 — Split `Shell` into host + default layout (L)

Closes: H-A, H-B, H-C groundwork, H-D groundwork.

Work:

1. Extract the website arrangement from `Shell` into
   `Views/DefaultLayout.fsi/.fs`: width bands, sidebar `SplitView`,
   aside, the router outlet, the compose / search / help / undo
   layers, and the key `Actions` record. The extraction moves code;
   it does not rewrite it.
2. `Shell.fsi/.fs` keeps: window, band, captions, gates (terms,
   signed-out screen), callback finish, the three loops plus
   window-active, close-to-tray, tray, the key tunnel, and the
   content slot with the default layout mounted.
3. `Layouts.fsi/.fs` lands with `builtIn = [ "current", defaultFactory ]`
   — one entry. The host mounts through the registry already, so
   Phase 1 adds entries, not seams.
4. `LayoutIntent` lands now (tray still routes through the default
   layout's mapping, which is the identity mapping to routes today).

Acceptance: `Shell(env, callback, ?undoWindow)` signature unchanged;
the full UI, features, and snapshot suites pass unchanged; the leak
gate passes; a fresh user sees no difference at all.

Tests: none new beyond the suites above — this phase is behavior-
preserving by contract, and the existing suites are its proof.

### Phase 1 — Registry, live switch, tray intents (M)

Closes: H-D, H-E, H-F, H-G.

Work:

1. `SettingsStore`: `LayoutChoice` (`CurrentLayout` plus one case per
   adopted layout), the `Layout` field, one setter, the JSON round
   trip, old-file default. This plan owns the field from here on.
2. `SettingsView`: the Layout row (wiremock H1), instant-switch copy.
3. The switch itself (section 4): dispose, mount, rebind keys,
   refresh tray intents, focus the new root.
4. `Tray.fsi/.fs`: `Router: IRouter<Control>` becomes the intent
   mapping — `TrayProps` loses the router and gains
   `Intents: LayoutIntent -> unit`. The default layout maps intents
   to routes, so tray behavior is unchanged for default users.
5. The switch leak test (gap H-F) with a test-registered layout.

Acceptance: switching in the open window works in both directions;
the stored choice survives a restart as the mounted layout; the tray
answers under any layout; the key tunnel answers the active layout;
20 switch rounds retain nothing.

Tests: settings round trip and old-JSON load; switch leak test;
intent-mapping test as data; one headless switch test.

### Phase 2 — Hardening (S)

Work: snapshots of the slot under two layouts; the perf line (the
switch completes inside one UI frame on the headless harness); the
help text that names where each unadopted surface lives; full repo
gates; changelog.

Acceptance: all gates green; a user who never opens Settings sees
nothing new anywhere.

## 8. Adoption — what this plan changes in the others

No adoption happens inside this plan's phases. Each is one phase in
its own plan, edited there:

- **Workbench** (`PLAN-workbench-layout.md`): its Phase 0 collapses.
  Gaps 0-A, 0-B, 0-D, 0-E, 0-F, 0-G dissolve into the host; 0-C
  reverses (tray and close-to-tray simply work); its window file
  becomes `WorkbenchLayout` — content, no chrome, no loops, living
  under `Layouts/Workbench/`, registered in `builtIn`. Its restart
  text (7.4 of the deck plan, the workbench switch menu) becomes a
  `host.Switch` call. Its `LayoutChoice` case joins here.
- **Editor** (to be written as `PLAN-editor-layout.md` when picked):
  the E1-E8 wiremocks drop their band and caption rows; E7/E8's
  minimum-size ruling is already the host's floor; the theme work
  (tabs, problems, palette) lands as layout files under
  `Layouts/Editor/`.
- **Deck** (`PLAN-deck-layout.md`): same collapse applies if it is
  ever chosen. It stays unchosen and unedited.

## 9. New file manifest (when all phases land)

| File | Holds |
|---|---|
| `Shell.fsi/.fs` (rewritten thin) | the host: window, chrome, gates, loops, tray, callback, slot |
| `Layouts.fsi/.fs` | `LayoutIntent`, `ILayout`, `IHostSurface`, `builtIn` |
| `Layouts/Website/WebsiteLayout.fsi/.fs` | today's website arrangement, extracted |

Shared files touched: `SettingsStore.*`, `Views/SettingsView.*`,
`Tray.*`, `Shortcuts.*` (tunnel/actions split, if its `.fsi` must say
so), `DevNull.Client.UI.fsproj`, `CHANGELOG.md`, `tests/**`. Nothing
else; Core is untouched.

## 10. Decisions still open

1. **Where the sign-in screen lives.** Phase 0 finding (gap H-0d):
   there is no host sign-in screen today — sign-out shows the
   `/signin` route inside the website layout. The website layout keeps
   it. A host-owned cover for non-website layouts (the workbench
   plan's 0-A ruling, hoisted) is decided when the first such layout
   registers.
2. **A handoff record across layouts.** Default: none in this plan's
   phases; each layout persists its own state. Decide after the
   workbench adopts, if real use asks for it.
3. **Switch animation.** Default: none — one frame, like a route
   change. Decide at Phase 2 review only if the headless switch test
   shows a visible flash.

## 11. What "done" means for the plan itself

A reader can start Phase 0 with no question sections 2 to 6 do not
answer, can name for every `Shell` line today whether it moves to the
host, the default layout, or stays on env, and can add a layout to
`builtIn` without reading another layout's code. Anything found wrong
later goes in section 6 as a new gap with a ruling, in the same change
that works around it.
