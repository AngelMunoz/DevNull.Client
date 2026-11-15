# Implementation specification: lifecycle, virtualization, cache, and time-zone loading

Status: ready for implementation  
Repository: DevNull.Client  
Branch: `feat/layout-host-workbench`  
Base commit: `6aab60c`

This document is the complete implementation brief. Follow it literally. Do not add
scope, and do not use the investigation that produced this brief as context for code
comments or commit messages.

## 1. Scope

Implement these work packages:

| ID | Work package | Priority |
|---|---|---|
| WP1 | Workbench handle lifetime | P0 |
| WP2 | Attach-gated component programs | P0 |
| WP3 | Feed header and Editor thread virtualization | P0 |
| WP4 | Remaining long-list virtualization | P0 |
| WP5 | Translation cache file tier only | P0 |
| WP6 | Lazy time-zone loading | P1 |
| WP7 | Verification, changelog, delivery | — |

Out of scope:

- No native, rendering, platform-head, or project-file changes.
- No visual redesign. Preserve the current layout, spacing, and row widths.
- No new public API except the helpers and fields named here.
- No new route cache, page cache, or process-wide collection.
- No change to network payloads or Core API types.
- No change to unrelated tests or formatting.

## 2. Repository constraints

These constraints override convenience.

1. Build must be clean: `dotnet build DevNull.Client.slnx` ends with zero errors and
   zero warnings.
2. Run `dotnet fantomas .` before staging any F# file.
3. Every new production `.fs` file needs its own `.fsi` file. Tests and `Program`
   entry points are exempt. Do not add new production files unless this brief names one.
4. In files that have an `.fsi`, do not mark declarations `private`. Hide with the
   signature file.
5. Comments belong in the `.fsi` file, except an implementation quirk comment that
   states a constraint the code cannot show. Do not put review notes or conversation
   data in code.
6. Keep the existing F# rules from `AGENTS.md`: tupled parameters unless partial
   application is the point, at most three parameters, no `if/elif` chains on
   discrete values, no `let mutable` where a property or `ref` cell works, no
   `Observable.add` or bare `.Subscribe(` in `Components/` or `Views/` outside the
   `Sub` engine or an existing control-owned attachment.
7. UI work runs on the UI thread. Async work uses `asyncEx`. Do not use
   `Async.RunSynchronously`, `.Result`, or `.Wait()`.
8. Tests never reach the network, the real file system, or a real process. Use the
   existing harness and fake `TranslationCache.Files` tier.
9. Do not add production APIs or props only for tests.
10. Do not commit generated diagnostics or build output.
11. Do not push. Ask for permission first. If a pull request is requested, its body
    is a Markdown file, not inline shell-escaped text.

## 3. WP1: Workbench handle lifetime

### Files

- `src/DevNull.Client.UI/Layouts/Workbench/WorkbenchHost.fs`
- `tests/DevNull.Client.UI.Tests/WorkbenchHostView.Tests.fs`
- `tests/DevNull.Client.UI.Tests/Leaks.Tests.fs`

### Required changes

1. In `TrackedHandle`, add an instance property for the wrapped handle:
   `member val Raw = raw with get, set`.
2. In `TrackedHandle.Dispose`, keep the idempotent `Disposed` guard. On the first
   call:
   - call `this.Raw.Dispose()`;
   - then set `this.Raw <- Disposable.Empty`.
3. Do not change the visibility or meaning of `MountedHost.Created`. It MUST keep
   returning every handle ever recorded, including disposed handles, so existing test
   probes still work. Do not clear it.
4. Do not change `MountedHost.DisposeCore` behavior beyond using the new handle
   release. It MUST keep disposing the current child handles, the host handle, and the
   subject.
5. Do not edit other Workbench behavior.

### Tests

Add or extend a test that replaces one Workbench child at least three times and then
asserts:

- every handle recorded by `Created` reports `Disposed = true`;
- `Raw` of every recorded handle is `Disposable.Empty` after disposal;
- a weak reference to the replaced child model or program reports dead after the
  harness's collection step.

Existing tests that assert `Disposed` or `Created` MUST keep passing. Update a test
only if it directly reads `Raw`.

### Acceptance

- `grep` finds no retained wrapped handle after `Dispose`.
- The new lifetime test passes.
- Existing Workbench tests pass unchanged except for `Raw` assertions.

## 4. WP2: Attach-gated component programs

### Files

- `src/DevNull.Client.UI/Programs.fs`
- `src/DevNull.Client.UI/Programs.fsi`
- `src/DevNull.Client.UI/Components/Composer.fs`
- `src/DevNull.Client.UI/Components/GoLiveDialog.fs`
- `src/DevNull.Client.UI/Components/LiveNow.fs`
- `src/DevNull.Client.UI/Components/LogChrome.fs`
- `src/DevNull.Client.UI/Components/ProfileCard.fs`
- `src/DevNull.Client.UI/Components/ReportDialog.fs`
- `src/DevNull.Client.UI/Components/SearchDialog.fs`
- `src/DevNull.Client.UI/Components/ShareDialog.fs`
- `src/DevNull.Client.UI/Components/TermsDialog.fs`
- `src/DevNull.Client.UI/Components/WhoToFollow.fs`
- `tests/DevNull.Client.UI.Tests/Leaks.Tests.fs`
- `tests/DevNull.Client.UI.Tests/EditorChurn.Tests.fs` or the existing layout test file

### New helper

Add to `Programs` and export from `Programs.fsi`:

```fsharp
/// Starts a component program when its scope first attaches to the visual
/// tree. A scope that never attaches starts no program. The returned
/// handle cancels a pending start or stops a started program; disposing it
/// is idempotent.
val runComponentOnAttach:
  scope: Visual *
  dispatcher: IDispatcher *
  program: Program<unit, 'model, 'msg, 'view> *
  setView: ('view -> unit) ->
    IDisposable
```

Required behavior:

1. If `scope` is already attached to a visual tree at call time, start the program
   immediately and call `setView` on the UI thread.
2. Otherwise subscribe once to `scope.AttachedToVisualTree`. On the first attach,
   start the program with the existing `runComponent` semantics and call `setView`
   exactly once.
3. A scope that never attaches MUST start no program and MUST call `setView` zero
   times.
4. Re-attachment MUST NOT start a second program. Guard with a one-shot state object.
5. The returned `IDisposable` MUST:
   - prevent a pending start when disposed before the first attach;
   - stop a started program when disposed;
   - be safe to dispose more than once.
6. Keep the existing `runComponent` function unchanged. It remains the shared
   primitive.
7. Do not use `let mutable` for the one-shot guard. Use a small class instance with
   `member val` fields, or a `ref` cell.
8. Do not use `Observable.add` for the attach subscription. Use the visual's
   `.AttachedToVisualTree.Add(...)` handler as `Feed.fs` already does.

### Call-site conversion

Convert each of the ten components from:

```fsharp
let root, _ = Programs.runComponent(this, props.Env.Dispatcher, program)
this.Content <- root
```

to:

```fsharp
Programs.runComponentOnAttach(
  this,
  props.Env.Dispatcher,
  program,
  fun root -> this.Content <- root
)
|> ignore
```

Rules:

- If a component currently keeps no handle, ignore the returned handle.
- `SearchDialog.fs` currently keeps the handle to dispose it on its close path. It MUST
  keep the returned handle and dispose it in the same close path, replacing the old
  `childHandle`. Do not keep the old `runComponent` call.
- Do not change any component's view, state, subscriptions, or public props.
- Keep `this.Content` assignment inside `setView`. Do not assign `Content` before the
  program starts.

### Tests

1. Helper test: create a detached `Border`; call `runComponentOnAttach` with a test
   program that records init and update calls; assert zero calls while detached.
   Attach the border to a test window; assert one init and one view call. Detach and
   re-attach; assert no second init. Dispose the returned handle; assert idempotence.
2. Helper test: create a detached border, call the helper, dispose the returned handle
   before attaching, then attach; assert no program started.
3. Component lifetime test: mount a Website layout through the existing layout
   registry, switch to the Editor layout, force the harness's collection step, and
   assert weak references to the Website root, router, `WhoToFollow` page, and
   `LogView` model are dead.
4. Keep the existing layout-switch test for test layouts. Add the real Website case;
   do not replace the existing case.

### Acceptance

- Every `let root, _ = Programs.runComponent` call is gone.
- The new helper is the only path used by the ten components.
- The detached-component tests and the Website-switch lifetime test pass.
- No component program starts before attach.

## 5. WP3: Feed header and Editor thread virtualization

### Files

- `src/DevNull.Client.UI/Components/Feed.fs`
- `src/DevNull.Client.UI/Components/Feed.fsi`
- `src/DevNull.Client.UI/Layouts/Editor/ThreadTab.fs`
- Every current `Feed.view` call site:
  - `Components/EmojiPicker.fs`
  - `Components/ProfileShell.fs`
  - `Layouts/Workbench/HistoryColumn.fs`
  - `Views/LogView.fs`
  - `Views/NotificationsView.fs`
  - `Views/SavedView.fs`
  - `Views/Remotes/RemotesInbox.fs`
  - `Views/Remotes/ThreadPane.fs`
  - `Layouts/Editor/HistoryTab.fs`
  - the new `ThreadTab` feed
- `tests/DevNull.Client.UI.Tests/Feed.Tests.fs`
- `tests/DevNull.Client.UI.Tests/Leaks.Tests.fs`

### Feed changes

1. Add this field to `Feed.Props<'T>`:
   ```fsharp
   /// Content above the first row, inside the feed's scroll owner.
   Header: Control voption
   ```
2. Render order inside the feed's rows column: `Header`, the virtualizing
   `ItemsControl`, then `Footer`. If `Header` is `ValueNone`, render nothing.
3. Update the `.fsi` type documentation. Do not change `Feed.view`'s return type.
4. If the current 720 device-independent row cap changes the visuals of a migrated
   list, add an optional row-width field only if necessary. Prefer preserving the
   current cap and adding the width field only for `ThreadTab` and other lists whose
   current width differs. If a width field is added, update every call site.
5. Update every existing `Feed.view` record expression with `Header = ValueNone`.
   Do not change their row, item, footer, scroll, or restore behavior.

### ThreadTab changes

Replace the current replies list and outer scroller with the shared feed:

- Keep `breadcrumbRow`, `error`, `mainCard`, and `repliesHeader` as today.
- Put those four controls in one header `StackPanel` with the current spacing.
- Build a `Feed.view` with:
  - `Header = ValueSome header`;
  - `Items = model |> Observable.map _.Replies`;
  - `KeyOf = keyOf`;
  - `Row = fun node -> feedRow replyProps node`;
  - `Footer = ValueSome loadMoreControl`;
  - `HasMore = hasMore`;
  - `OnNeedMore = ValueSome(fun () -> dispatch State.MoreReplies)`;
  - `ScrollTo = ValueNone`, `Restore = ValueNone`, `OnScroll = ValueNone`,
    `OnRide = ValueNone`, `Ride = false`.
- Remove the old `repliesItems`, `body`, and `ScrollViewer` definitions.
- Keep the `Grid` overlay of `EditorCards.layer(State.cardsProps props, cards, cardDispatch)`.
- Do not use `ItemsControl` with `StackPanel` for replies. The feed's
  `VirtualizingStackPanel` is the only items panel.
- Preserve the current responsive width and margins. If the feed cap changes them,
  use the width field from Feed changes.

### Tests

1. Feed test: a feed with a header renders header text above the first row and the
   footer below the last realized row.
2. Thread test: mount a thread with 100 replies in the headless harness. After
   layout, assert that only a bounded set of reply rows exists (viewport plus
   overscan; use a small fixed budget such as fewer than 30). Assert that the first
   and last replies exist when scrolled to their positions.
3. Thread lifetime test: keep weak references to reply row controls, scroll past
   them, run the harness's collection step, and assert the references are dead.
4. Existing feed tests and scroll-restore tests pass unchanged.

### Acceptance

- Thread replies use one virtualizing list with a header.
- No `StackPanel` items panel remains for thread replies.
- All `Feed.view` call sites compile and preserve behavior.

## 6. WP4: Remaining long-list virtualization

### Rule

A list complies when either:

- it is built with `Feed.view`; or
- it is an `ItemsControl` whose items panel is `VirtualizingStackPanel` and whose
  nearest scrolling ancestor is the list's own viewport owner. A virtualizing panel
  inside a plain `StackPanel` inside a `ScrollViewer` does not comply.

Every list that can display 10 or more rows MUST comply. A list that cannot exceed 10
rows MUST enforce that bound in domain code and say so in a comment in its `.fsi`
file where one exists.

Add to `AGENTS.md` as an imperative:

> Every list that can display 10 or more items MUST virtualize. Use
> `Components/Feed.view` or a virtualizing items panel owned by the list's own
> scroller. Lists capped by domain code MUST state the cap where the cap is enforced.

### Required sites

Convert each site to a compliant list. Preserve the current chrome by moving it to
`Feed.Header` or `Feed.Footer`; do not leave nested scrollers.

| File | Current site | Required action |
|---|---|---|
| `Views/SearchView.fs` | results `ItemsControl` around line 990 | Replace the page's results scroller with `Feed.view`; profile rows, tabs, and heading go in `Header` |
| `Views/ScheduledView.fs` | queue `ItemsControl` around line 619 | Replace queue with `Feed.view`; pager goes in `Footer` |
| `Layouts/Workbench/RepositoryHome.fs` | commits `ItemsControl` around line 953 | Convert to `Feed.view`; repo chrome goes in `Header` |
| `Layouts/Workbench/RepositoryHome.fs` | people `ItemsControl` around line 1094 | Convert to `Feed.view` |
| `Layouts/Workbench/NotificationsCenter.fs` | notification rows around line 307 | Convert to `Feed.view`; head/refresh/notice go in `Header` |
| `Layouts/Workbench/Staging.fs` | queue rows around line 134 | Convert to `Feed.view`; keep the 240 px cap on the feed scroller |
| `Views/CommitDetailView.fs` | related commits around line 1656 | Convert to `Feed.view`; section header goes in `Header` |
| `Views/CommitDetailView.fs` | ancestors around line 1335 | Virtualize with `Feed.view` |
| `Components/WhoToFollow.fs` | suggestion rows around line 645 | Virtualize with `Feed.view`; keep the current section chrome |
| `Layouts/Editor/SearchTab.fs` | prebuilt row array | Convert long results to `Feed.view` |
| `Layouts/Editor/RepoTab.fs` | prebuilt row lists | Audit and convert every list that can exceed 10 |
| `Layouts/Editor/ListTab.fs` | prebuilt row lists | Audit and convert every list that can exceed 10 |
| `Layouts/Editor/RemoteList.fs` | row list | Audit and convert every list that can exceed 10 |
| `Layouts/Editor/RemoteThreadTab.fs` | row lists | Audit and convert every list that can exceed 10 |

Also run this audit and fix every other match:

```
rg -n "BindItemsSource|ItemsControl\(|ItemsPanel\(" src/DevNull.Client.UI
```

If a matched list is inside a `ScrollViewer` but not owned by it, it does not comply.
If it can never exceed 10 rows, enforce the cap in domain code and document it.

### Tests

Add budget tests in the existing leak or feature suites:

- Editor thread replies (WP3 covers this).
- Search results: a result page with 100 rows realizes fewer than 30 row controls.
- Scheduled queue: a queue with 100 rows realizes fewer than 30 row controls.
- Workbench history: a history column with 100 rows realizes fewer than 30 row
  controls.
- Notifications: a notification center with 100 rows realizes fewer than 30 row
  controls.

Use the existing harness and fixed window size. Do not add production test hooks.

### Acceptance

- Every row in the table either uses `Feed.view` or has a documented domain cap of 10.
- No `ItemsControl` with a plain `StackPanel` items panel remains for a list that
  can exceed 10 rows.
- Budget tests pass.

## 7. WP5: Translation cache file tier only

### Files

- `src/DevNull.Client.UI/TranslationCache.fs`
- `src/DevNull.Client.UI/TranslationCache.fsi`
- `src/DevNull.Client.UI/Components/TranslationModel.fs`
- `src/DevNull.Client.UI/Components/TranslationModel.fsi`
- `src/DevNull.Client.UI/Layouts/Editor/EditorCards.fs`
- `src/DevNull.Client.UI/Views/CommitDetailView.fs`
- `src/DevNull.Client.UI/Views/LogView.fs`
- `src/DevNull.Client.UI/Views/OrganizationView.fs`
- `src/DevNull.Client.UI/Views/ProfileView.fs`
- `src/DevNull.Client.UI/Views/SavedView.fs`
- `src/DevNull.Client.UI/Views/SearchView.fs`
- `tests/DevNull.Client.UI.Features.Tests/Translation.Tests.fs`
- `tests/DevNull.Client.UI.Tests/Fixtures.fs`

### Cache changes

1. Remove the process-wide dictionary tier:
   - remove `Held` from `State`;
   - remove `startScan`;
   - remove the memory-hit branch and its debug log from `tryGet`;
   - remove every write and remove from the dictionary;
   - remove the `Snapshot` member from the `TranslationCache` interface and its
     implementation.
2. `TryGet` runs the file read only. A hit returns the decoded value; a miss returns
   `None`.
3. `Set` writes the server JSON with `Files.Keep` only. An answer with no translation
   drops the file. It does not write any collection.
4. `Forget` drops the file and keeps the foreign-file sweep as today.
5. `createWith` MUST NOT call `Files.List` or read any file during construction.
6. Keep `Files` and `create`/`createWith` signatures. Keep the seven-day TTL and the
   corrupt/expired drop.
7. Update the `.fsi` docs: file tier only, reads and writes ride the caller's async,
   no startup scan.

### TranslationModel changes

1. Remove `seed` from `TranslationModel.fsi` and `TranslationModel.fs`.
2. In every page that seeded its book from `Translations.Snapshot()`, initialize the
   book to `TranslationModel.initial` and remove the eager snapshot read.
   The seven call sites are:
   - `Layouts/Editor/EditorCards.fs`
   - `Views/CommitDetailView.fs`
   - `Views/LogView.fs`
   - `Views/OrganizationView.fs`
   - `Views/ProfileView.fs`
   - `Views/SavedView.fs`
   - `Views/SearchView.fs`
3. Keep `recall` as the on-demand cache read. A page keeps a translation in its own
   `Book` after a successful recall, as today.
4. Do not change any page's translate button, retry, or display behavior.

### Test changes

Rewrite the translation cache tests to cover the file tier only:

- `Set` then `TryGet` through the fake tier returns the translation.
- A new `TranslationCache` over the same fake tier returns the stored translation.
- `Forget` removes it.
- An expired file is dropped and returns `None`.
- A corrupt file is dropped and returns `None`.
- `createWith` does not call `Files.List` and does not read files.
- Remove tests for `Snapshot` and `seed`.

Update every fake `TranslationCache` implementation in the test projects to the new
three-member interface.

### Acceptance

- No `Snapshot` in `TranslationCache` or its usages.
- No `TranslationModel.seed` usages.
- No `ConcurrentDictionary` in `TranslationCache.fs`.
- `createWith` performs no file listing or reading.
- All translation tests pass.

## 8. WP6: Lazy time-zone loading

### Files

- `src/DevNull.Client.UI/Views/SettingsView.fs`
- the existing Settings or App test file that mounts `SettingsView`

### Required changes

1. Change the module-level `zones: Zone list` value to a `lazy` value:
   `let zones = lazy ( ... existing body ... )`.
2. Add a small helper that resolves one zone id without forcing the list:
   - try `TimeZoneInfo.FindSystemTimeZoneById id`;
   - return its `{ Id = id; Offset = offsetOf zone }`;
   - on failure return `None`.
3. In `timezoneRow`, replace `zones |> List.tryFind(fun zone -> zone.Id = id)`
   with the single-zone helper. This binding MUST NOT force the lazy list.
4. In the zone picker:
   - when `current.ZoneQuery.Trim() = ""`, return no rows and render a short
     `Status.empty "Type to search zones."` hint;
   - otherwise force `zones.Value` and return the matching rows;
   - the picker `Key` MUST NOT force the list. Use the query text or another value
     that is already available without forcing.
5. Do not change the zone save, unset, device-zone, or show-local behavior.
6. Building the Settings page MUST NOT force `zones.Value`. Opening the picker and
   typing a query is the first point that may force it.

### Tests

Add a UI test that mounts the Settings page and asserts:

- the zone rows do not exist while the query is empty;
- after typing a known zone id, the matching row appears;
- picking the row dispatches the zone pick and the row disappears;
- saving still works.

Use the existing harness. Do not add a production seam for the static time-zone API.

### Acceptance

- No reference to `zones` can run on page build.
- The lazy list is forced only by a non-empty picker query.
- The new test passes.

## 9. WP7: Verification and delivery

### Build and format

Run these in this order and capture the result:

```
dotnet build DevNull.Client.slnx
dotnet fantomas .
dotnet build DevNull.Client.slnx
```

The final build MUST end with zero errors and zero warnings.

### Tests

Run every test project listed in `README.md` under "Building and running". At minimum:

```
dotnet run --project tests/DevNull.Client.Core.Tests
dotnet run --project tests/DevNull.Client.UI.Tests
dotnet run --project tests/DevNull.Client.UI.Features.Tests
```

All suites MUST pass. Run the UI suite with its documented sequenced mode if a run
stalls.

### Source checks

All of these must return no matches:

```
rg -n "Translations\.Snapshot|TranslationModel\.seed" src
rg -n "Snapshot\(\)" src/DevNull.Client.UI/TranslationCache.fs src/DevNull.Client.UI/TranslationCache.fsi
rg -n "let root, _ = Programs\.runComponent" src/DevNull.Client.UI/Components
rg -n "ItemsPanel\(FuncTemplate<Panel>\(fun \(\) -> StackPanel\(\)\)\)" src/DevNull.Client.UI
```

`rg -n "runComponent\(" src/DevNull.Client.UI/Components` may show only the new
helper name if the search text overlaps. No direct `Programs.runComponent` call may
remain in a component.

### Changelog

Add entries under `## [Unreleased]` in `CHANGELOG.md`, following the existing format
and the affected-surface prefixes:

- **Workbench:** Replacing a page releases the previous page's program and model.
- **Layouts:** A retired layout releases the programs of the components it hosted.
- **Editor:** Long threads virtualize their replies and keep only the rows near the
  viewport.
- **Lists:** Long search, history, notification, scheduled, and workbench lists
  virtualize their rows.
- **Settings:** The time-zone list loads when the picker is used.
- **Translations:** Translations are read from the file tier on demand; the process
  keeps no process-wide translation dictionary.

Use one bullet per concern. Do not mention internal module names, line numbers, or
investigation details.

### Delivery

- Keep one commit per work package, or one commit with clearly separated sections if
  the reviewer asks. Do not mix unrelated formatting.
- Do not push. Report the branch, commit ids, build result, and test results. Ask for
  push permission.
- If a pull request is requested, write its body to a Markdown file and pass that file
  to `gh pr create`.

## 10. Definition of done

All of the following are true:

1. WP1 through WP6 are implemented as specified.
2. The build is clean with zero warnings.
3. Fantomas has run.
4. All named test suites pass, including the new lifetime and budget tests.
5. All source checks in WP7 return no matches.
6. The changelog entries exist under `[Unreleased]`.
7. No push has happened.
8. The final report lists: changed files, tests run and their results, build result,
   and any deviation from this specification. Deviations require an explicit reason
   and a proposed alternative. Do not silently change the design.
