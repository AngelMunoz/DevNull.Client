---
name: mvuish-audit
description: Audit and review F# Avalonia code in DevNull.Client for MVUish usage and the observable-binding patterns that caused memory leaks. Use whenever writing, reviewing, or changing Views/, Components/, Shell.fs, Theme.fs, feeds, images, or anything touching IObservable, Sub, Program runners, bindings, or controls that rebuild — and for any request about memory, leaks, retention, or disposal in this repo.
---

# MVUish usage and leak-pattern audit for DevNull.Client

## Why these rules exist (the mechanism)

Every observable property binding roots its control from the SOURCE:
source → observer list → binding entry → ValueStore → control. Avalonia
holds that edge strongly, and NXUI's fluent setters (`.Text(obs)`,
`.IsVisible(obs)`, `.Background(obs)`, …) discard the disposable that
would end it. A binding ends only when the source completes, the source
drops its observers, or the same property is re-bound.

MVUish's `Program.runWithLifecycle` teardown completes AND disposes the
model `BehaviorSubject`, which releases every model-derived binding.
So page-model bindings are safe. App-lifetime sources (`UiClock`,
`SessionStore`, `Timeline.Requests`, `SettingsStore`, `RouteStore`, and
any Application resource observable) never complete — a view binding on
one retains its control for the life of the process. This exact pattern
retained every scrolled feed row and every themed control before the
0.1.1 fixes; `tests/DevNull.Client.UI.Tests/Leaks.Tests.fs` is the
regression gate that proves it stays dead.

## The rules

1. **Themed brushes are style classes, never observables.** No
   `.Background(Brushes.x)` / `.Foreground` / `.BorderBrush` / `.Fill` /
   `.Stroke` with a themed observable anywhere in Components/ or Views/.
   Use the class vocabulary in `Theme.fs` (`FgAccent`, `CardBack`,
   `Hairline`, …) or the lane helpers (`Theme.laneFill`, `laneStroke`,
   `laneBorder`, `laneBack`, `laneForeground`, `laneText`, `laneClass`).
2. **Shared state lives in env stores.** If a value must cross pages,
   it belongs to a store service (`SessionStore`, `AppSettingsStore`,
   `UiClockService`, `UnreadService`, `TimelineService`, `RouteStore`,
   `LastVisitStore`, `ImageCacheService`). Views never bind a store's
   observable directly.
3. **External observables enter a program only through `Sub`.**
   `Sub.fromObservable` / `fromEvent` / `fromRoutedEvent` / `interval`
   inside `State.subscribe` — the engine disposes them at teardown.
   A `Props` record must not carry an external observable for the view
   to bind. Page-model projections in props (`Now`, `Pasteis` derived
   from the page's own model) are the allowed shape.
4. **View functions bind only their own model stream.** That subject
   dies with the program, so the bindings die with it.
5. **Route pages run `runWithLifecycle` + `props.Context.AddDisposable
   lifetime`.** `runWithLifetime` (first-detach teardown) is only for
   hosts that are replaced and never re-shown: row composers,
   `ProfileCard`, `LogChrome`. A control that can detach and re-attach
   (a cached or stashed page) must not use it.
6. **Never cache routes.** `Route.cache` made page programs immortal.
   State a returning page needs goes into `TimelineService` (scroll
   places, reply drafts) or `LastVisitStore`, and `Feed` restores it
   through its `Restore`/`OnScroll`/`OnNeedMore` props.
7. **Decoded bitmaps are control-owned.** Every `new Bitmap` /
   `DecodeToWidth` site disposes on replace and on detach. Follow
   `AsyncImage.Loader`; the chip pattern in `Composer.Parts.chip`.
8. **Rebuilt shell content carries an identity guard.** Content
   replaced from `session.Observe` rebuilds only when the user id
   changes (see the aside, footer, and chrome in `Shell.fs`).
9. **`Theme.bindClass` only on program-model streams**, and no
   `Observable.add` or `.Subscribe` in Components/ or Views/ outside
   the Sub engine. The one standing exception: `Feed.fs`'s scroll-top
   listener, which lives inside `Sub.ofId … |> Sub.untilDetached`.
10. **Timers stop on every path** (`UserFlyout` is the reference). No
    lifecycle logic may key on detach for controls hosted inside a
    TextBlock inline: `TextBlock.ArrangeOverride` clears and rebuilds
    inline children on every layout pass, so inline-hosted controls
    detach and re-attach continuously.
11. **Feeds bind items through `BindItemsSource`** (weak control edge,
    keyed diff) and pass `supportsRecycling = false` templates; rows
    release when the page program ends.

## The audit procedure

Run these from the repo root over the scope you were asked to audit
(a changeset, a file, or the whole tree):

```
git diff -U0 -- src/DevNull.Client | rg "^\+.*(Observable\.add|\.Subscribe\()"
git diff -U0 -- src/DevNull.Client | rg "^\+.*(Brushes\.(background|surface|card|border|text|muted|accent|onAccent|danger|rail|laneFallback|laneOf|lane ))"
git diff -U0 -- src/DevNull.Client | rg "^\+.*(Route\.cache|runWith\b|runWith[^L])"
rg -n "IObservable" <changed files>            # classify every hit
rg -n "new Bitmap|DecodeToWidth" <changed files> # then check the matching Dispose
```

For a whole-tree audit, drop the `git diff` pipe and search
`src\DevNull.Client\Components` and `src\DevNull.Client\Views`
directly.

Then classify every hit and every new binding in changed view code:

- **Page-model projection** (source is the function's `model`
  argument): allowed.
- **Sub-owned** (inside `Sub.*` or `Sub.untilDetached`): allowed.
- **Shell, Tray, Program.fs, app-lifetime single instance**: allowed —
  one instance for the process, nothing accumulates.
- **Store observable bound in a view, themed brush observable,
  `Observable.add` outside Sub, cached route, `runWithLifetime` on a
  re-showable host, bitmap without a drop path**: a leak — report it.

Also read every changed `view` function for new bindings against
anything that is not the page model, and every new
`ContentControl().Content(obs)` rebuild loop for an identity guard.

## Report format

```
LEAKS (must fix)
- <file>:<line> — <what roots what, from which source>

ALLOWED BY THE RULES
- <file>:<line> — <classification: page-model / Sub-owned / shell>

CHECKS RUN
- <the greps and reads, with hit counts>
```
