---
name: devnull-mvuish-audit
description: Audit and review F# Avalonia code in DevNull.Client for MVUish usage and the observable-binding patterns that caused memory leaks. Use whenever writing, reviewing, or changing Views/, Components/, Shell.fs, Theme.fs, feeds, images, or anything touching IObservable, Sub, Program runners, bindings, or controls that rebuild — and for any request about memory, leaks, retention, or disposal in this repo.
---

# MVUish usage and leak-pattern audit for DevNull.Client

The `mvuish-controls`, `mvuish-element-shapes`, `mvuish-performance`, and
`mvuish-testing` skills carry the library rules. This skill is the DevNull
layer on top of them: where it is stricter, the repo rule wins here, and
nothing below relaxes a library rule.

## Why these rules exist (the mechanism)

Every observable property binding roots its control from the SOURCE:
source → observer list → binding entry → ValueStore → control. Avalonia
holds that edge strongly, and NXUI's fluent setters (`.Text(obs)`,
`.IsVisible(obs)`, `.Background(obs)`, …) discard the disposable that
would end it. A binding ends only when the source completes, the source
drops its observers, or the same property is re-bound.

MVUish's `Program.runWithLifecycle` teardown completes AND disposes the
model `BehaviorSubject`, which releases every model-derived binding.
So page-model bindings are safe. App-lifetime sources — the store
services the env carries, any stream a service keeps, and any
Application resource observable — never complete: a view binding on
one retains its control for the life of the process. This exact
pattern retained every scrolled feed row and every themed control
before the 0.1.1 fixes; the leak regression suite in the UI tests is
the gate that proves it stays dead.

## The rules

1. **Themed brushes are style classes, never observables.** No
   `.Background(Brushes.x)` / `.Foreground` / `.BorderBrush` / `.Fill` /
   `.Stroke` with a themed observable anywhere in Components/ or Views/.
   Use the style classes and the lane helpers `Theme` exports.
2. **Shared state lives in env stores.** If a value must cross pages,
   it belongs to a store service — one the env record carries, alive
   for the process. Views never bind a store's observable directly.
3. **External observables enter a program only through `Sub`.**
   `Sub.fromObservable` / `fromEvent` / `fromRoutedEvent` / `interval`
   inside the program's subscription set — the engine disposes them at
   teardown.
   A `Props` record must not carry an observable that outlives the page
   program. Observables in props are fine only as projections of the
   page's own model (`mvuish-element-shapes` §5).
4. **View functions bind only their own model stream.** That subject
   dies with the program, so the bindings die with it.
5. **Route pages run `runWithLifecycle`, and the page context owns the
   lifetime handle.** `runWithLifetime` (first-detach teardown) is only
   for hosts that are replaced and never re-shown. A control that can
   detach and re-attach (a cached or stashed page) must not use it.
6. **Never cache routes.** Route caching made page programs immortal.
   State a returning page needs (scroll places, reply drafts, the last
   visit) goes into a store service, and a feed page restores it
   through restore, scroll, and need-more props.
7. **Decoded bitmaps are control-owned.** Every `new Bitmap` /
   `DecodeToWidth` site disposes on replace and on detach: one shown
   bitmap at a time, dropped by whoever replaces it and on teardown.
8. **Rebuilt shell content carries an identity guard.** Content
   replaced from a session stream rebuilds only when the user identity
   changes — the shell's aside, footer, and chrome do this.
9. **Theme's class binding only on program-model streams**, and no
   `Observable.add` or `.Subscribe` in Components/ or Views/ outside
   the Sub engine. `mvuish-controls` §3 permits `Observable.add` on a
   control's own event observable; this repo wires events through
   `.OnClickHandler` instead, so the ban stands. The one standing
   exception: a scroll listener bridged inside
   `Sub.ofId … |> Sub.untilDetached`.
10. **Timers stop on every path** — replace, detach, and dispose.
    Prefer `Sub.interval` in the program over an ad-hoc timer in a
    view (`mvuish-controls` §5). No lifecycle logic may key on detach
    for controls hosted inside a TextBlock inline:
    `TextBlock.ArrangeOverride` clears and rebuilds inline children on
    every layout pass, so inline-hosted controls detach and re-attach
    continuously.
11. **Feeds bind items through `BindItemsSource`** (weak control edge,
    keyed diff) and pass `supportsRecycling = false` templates; rows
    release when the page program ends. `mvuish-controls` §6.2 calls
    recycling-off a consumer tradeoff; this repo chose it. The recycle
    path clears a row before the next item lands, so the template must
    stay total against a `null` item.

## The audit procedure

Run these from the repo root over the scope you were asked to audit
(a changeset, a file, or the whole tree):

```
git diff -U0 -- src/DevNull.Client.UI | rg "^\+.*(Observable\.add|\.Subscribe\()"
git diff -U0 -- src/DevNull.Client.UI | rg "^\+.*Brushes\."   # then check each: themed brush or plain static?
git diff -U0 -- src/DevNull.Client.UI | rg "^\+.*(Route\.cache|runWith\b|runWith[^L])"
rg -n "IObservable" <changed files>            # classify every hit
rg -n "new Bitmap|DecodeToWidth" <changed files> # then check the matching Dispose
```

For a whole-tree audit, drop the `git diff` pipe and search
`src/DevNull.Client.UI/Components` and `src/DevNull.Client.UI/Views`
directly.

Then classify every hit and every new binding in changed view code:

- **Page-model projection** (source is the function's `model`
  argument): allowed.
- **Sub-owned** (inside `Sub.*` or `Sub.untilDetached`): allowed.
- **The shell, the tray, and the app entry points**: allowed — one
  instance for the process, nothing accumulates.
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
