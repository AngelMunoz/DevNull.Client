# AGENTS.md

DevNull.Client is an Avalonia desktop client for https://dev-null.sh: F# on
.NET 10, split into a Core library and the Avalonia consumer.

## Imperatives

1. **NEVER PUSH WITHOUT PERMISSION.** Always ask before pushing to the remote. A previous permission push for one set of work DOES NOT MEAN ALWAYS PUSH AFTER. Permissions are granted per set of work, not for the session.
2. **NEVER FORCE PUSH.** Tell the user they have to force push instead of you.
3. **USE YOUR EDIT TOOL FOR FILE EDITS**: Do not use sed, python, perl or any other scripting language or cli command to write/update files and its contents.
4. **Always run `dotnet fantomas .` before committing code.** Format all F# files before staging.
5. **Prefer `Option.*` (or `ValueOption`) combinators over nested matches.** When threading optional values, chain `Option.map` / `bind` / `filter` / `iter` / `defaultValue` / `orElse` instead of hand-rolling match ladders. Match only when the two branches carry substantially different logic, not to unwrap and re-wrap.
6. Pull requests made with the `gh` command use a markdown file as the PR body, not inline escaped markdown.
7. New F# files **MUST** be paired with their own signature file, with two exceptions: files whose types carry `private` constructors or implement IWSAM error interfaces inline (`Types.fs`, `Errors.fs`), and tests and Program entry points.
8. Comments go to the signature files. Implementation quirk comments may stay in implementation files.
9. **No `private` in files with an `.fsi`.** The signature already hides anything it does not export; `private` there is noise. Hiding is done by omitting from the `.fsi`, never by marking `private`.
10. Report to me only in ASD-STE100 Simplified Technical English, or plain Spanish when I write in Spanish.
11. For non-reports, do not use LLM jargon, phrases that are textual noise without actual value. Speak simple plain, concise and direct english.
12. **No more than 3 parameters.** A function with 4 or more takes a contextual object: a `[<Struct>]` record for short-lived values, a class (or a record over a ref) when the value outlives the call.
13. **Tupled parameter form.** Write `sign(env, payload)`, not curried chains, where partial application is not the point.
14. **No `let mutable`, out parameters, `bool * value` pairs, or `if/elif` chains on discrete values.** Match on discrete values; use `List.sumBy`/`tryFind`/`fold` for accumulation and search; return records for multi-value results.
15. **Parse, don't validate.** Raw input becomes a domain value at the boundary, through a `parse` leaf. Downstream functions take the domain value and never re-check. Domain types keep `private` constructors; a `string` in an error payload is only the raw input or an external library's message.
16. **NEVER LEAK INTERNAL CONVERSATIONS OR PLAN DATA INTO SOURCE CODE.** Do not put chat transcripts, agent plans, reasoning, review notes, or meta-commentary in code, comments, tests, or documentation. A code comment states a constraint the code cannot show. It never explains the conversation that produced the code.
17. **Inline small helpers.** Mark one-line projections, accessors, and trivial predicates `inline`. This keeps the body local and removes the call cost. Do not inline parsers, async flows, or multi-line protocol functions.
18. **UI code is fluent-heavy (NXUI).** Compose controls as chained fluent expressions; extract helpers when a chain stops reading, not to avoid the style.
19. **Threading: `asyncEx` always.** Work that must run where it is triggered uses `asyncEx { ... } |> Async.StartImmediate` (UI event handlers, navigation); background work uses `asyncEx { ... } |> Async.Start`. Controls are built on the UI thread. Never `Async.RunSynchronously`, `.Result`, or `Post` where `StartImmediate` is the correct lifetime. The one exception is the headless second instance in `Program.fs`: it must finish the sign-in callback before the process exits.
20. **Lean pages, module helpers.** View classes wire state and events only; layout and pure logic live in module functions (marked `inline` when trivially small). Not everything goes inside a class.
21. **No deep parameter threading.** When a value or control must cross several layers to reach one leaf, use content projection: pass a `unit -> Control` (or `unit -> value`) callback that works as the hole where the leaf places it.
22. **Use the tools that exist. JSON payloads go through JDeck.** Pick the collection the consumer needs from the start and build it directly: a `[| ... |]` comprehension for a `Control array`, no `Array.ofList` / `List.toArray` on a list the same function just built. F# `list` stays only where it earns its keep. No `yield` where a comprehension arrow suffices.
23. **Pass the command the function, not a wrapper.** Write `Cmd.OfAsync.perform fetch (env, session, log, None) Fetched`, never `Cmd.OfAsync.perform (fun () -> fetch (env, session, log, None)) () Fetched`. No `let more = fetch (...)` plus `(fun () -> more) ()`: put the cursor in the argument tuple. The same holds for `Cmd.OfFunc.attempt`.
24. **No redundant upcasts.** With a declared return type (`: Control`) or a known argument type (`.Children(...)`, `.Content(...)`), the compiler inserts the upcast: write the concrete control where a `Control` is expected. `:> X` stays only where the compiler cannot see the target, such as `IObservable<IBrush>` or `IObservable<obj>` element conversions, or a match arm inside a lambda whose result type is still being inferred.
25. **Operate on files, handle the failure.** Do the read, write, or delete under `try` with the failure case in hand. No `File.Exists` guard before an operation: the check races, and the operation repeats its answer. A probe that only picks between alternatives (which `ssh-keygen` to run, whether to generate a key) is the one allowed use.
26. **Lean env constraints.** Function parameters take the intersection form `env: #IProvideClock & #IProvideNonceSource & ...`. A `when` wall stays only where the compiler requires it — type and class definitions (`type Props<'env when ...>`, the `Shell` class) — or where a named constraint factors a secondary mechanism (Core's env-and-error composition). Never restate on `init`/`update`/`view` a constraint `Props<'env>` already carries.

## Architecture

- `src/DevNull.Client.Core/` - the protocol layer
- `src/DevNull.Client.UI/` - the shared Avalonia client, dark theme,
  NXUI in code, no XAML. Layers bottom-up:
  - `Errors.fs` - the `AppError` edge type (every Core IWSAM leaf) and
    its plain-text `describe`.
  - `Theme.fsi/fs` - the themed resource dictionaries
  - `SettingsStore.fsi/fs` - the local settings (`closeToTray`, the
    theme choice) in a JSON file under `%LOCALAPPDATA%\DevNull`; the
    store persists through an injected hook, so tests inject their
    own.
  - `Icons.fsi/fs` - the `Glyph` set as `PathIcon` geometry.
  - `Formatting.fsi/fs` - `timeAgo` and `shortCount`.
  - `Stores.fsi/fs` - `SessionStore` (`BehaviorSubject` of the sign-in
    state; emits on the UI thread; the `forget` hook owns the session
    file) and the `IProvideSession` accessor.
  - `Env.fsi/fs` - `IProvideLogging` (the Serilog-bridged
    `ILoggerFactory`), `AppEnv`: the Core capability workers, the
    session store, the native services the platform head passed, and
    the logging factory; `KeyFiles` discovery in `~/.ssh`.
  - `DeviceAuth.fsi/fs` - the app's Ed25519 key under
    `%LOCALAPPDATA%`, device authorization, callback redemption and
    session storage; generic over the capability interfaces, never over
    the concrete env.
  - `Natives.fsi/fs` - the native contracts a platform head implements:
    `ISchemeRegistrar`, `IAudioEngine` (the microphone and the speaker;
    unimplemented yet, `ValueNone` in both heads), and the
    `NativeServices` record `AppEnv` carries.
  - `Navigation.fsi/fs` - `go`: fire-and-forget navigation on the UI
    thread.
  - `UnreadPoller.fsi/fs` - polls `Me.get` on an interval into the
    session store; pauses while the inbox is open; survives failures.
  - `Tray.fsi/fs` - the tray icon: menu, raise on click, unread
    tooltip.
  - `Components/`
  - `Views/` - one MVUish page per route over `Program.mkProgram`
  - `Shell.fsi/fs`
  - `Startup.fs` - `App.start(natives, argv)`: Serilog (console plus
    one file per day under `%LOCALAPPDATA%\DevNull\logs`), the Fluent
    theme first, app styles after; the scheme registration through the
    head's registrar; single-instance mutex; the second instance
    completes the callback headlessly.
- `src/DevNull.Client.Windows/` - the Windows head (`net10.0-windows`,
  Windows 11 APIs as the floor): the registry scheme registrar, the
  native services record, and the entry point that calls `App.start`.
  Windows-only implementations (audio through WASAPI or WinRT, toasts,
  camera prompts) grow here.
- `src/DevNull.Client.Linux/` - the Linux head: the `.desktop` plus
  `xdg-mime` scheme registrar, the native services record, and the
  entry point. Linux-only implementations (ALSA audio, portals) grow
  here.
- Both heads keep the `DevNull.Client` assembly name: the shipped
  executable is still `DevNull.Client`, so the scheme handlers of
  existing installs keep working.
- `tests/DevNull.Client.Core.Tests/` - Expecto over Core; `TestSupport`
  carries the fakes and builders. Tests never reach network, disk, or
  real processes.
- `tests/DevNull.Client.UI.Tests/` - the headless suite over the real shell.
- `Directory.Packages.props` - central package management; every package
  version lives there, never in a project file.

## Testing

1. **Assert behavior, not plumbing.** Test through the surface that is meant to survive: parse raw input and assert on the domain value, run the composite with fakes and assert on the outcome. Never assert on internals.
2. **Regression tests prove both directions.** A regression test for a bug must fail before the fix and pass after it.
3. **Fakes control their output.** Each fake worker (`FixedClock`, `FixedNonceSource`, `FixedSigner`, `FakeHttp`, `CapturingHttp`) returns exactly the canned value wrapped in `async { return ... }`; no test reaches a real process, network, or file.
4. **The API is never shaped for tests.** Tests implement the same error interfaces the production edge implements; domain code gains nothing for testability.
5. **Headless UI tests touch controls on the session thread only.** Windows are born inside `probeUiAsync`; every control read or click goes through `onUiAsync`, `probeUiAsync`, or the `waitForAsync` helpers. A test that touches a control directly off the session thread fails with a cross-thread error at best and hangs at worst.
6. **Never dispatch from inside a dispatch.** A `probeUiAsync`/`onUiAsync` call inside an `onUiAsync` block queues a job the nested wait cannot serve, and the run freezes. Finders run plain inside `onUiAsync`; only the outermost call dispatches. When a run stalls, rerun with `-- --sequenced --debug`: it names the test that never finished.

## Changelog Management

We follow https://github.com/ionide/KeepAChangelog guidelines

Changelog Format:

```markdown
# Changelog

## [Unreleased]

### Added

- Instanced draws inside a `beginEffect`/`endEffect` scope are now shaded by the user shader when it opts into instancing (raylib: `in mat4 instanceTransform;`; MonoGame: an `Instanced` technique reading `TEXCOORD1..4`). Effects that don't opt in keep the previous PBR-instanced fallback. Skinned + instanced remains unsupported. See `docs/shader-uniforms.md` → "Instancing (opt-in)".

## [1.0.0] - 2026.01.13

### Added

- Initial release
```

Each section may contain the following categories:

- Added
- Changed
- Deprecated
- Removed
- Fixed
- Security

When adding entries to the changelog, make sure to follow format and categories.

### Writing style

The changelog is written for **developers upgrading their version**, not as a development
journal. Keep these rules in mind:

1. **Concise and reader-focused.** Each entry is one bullet that says what changed and why a
   user cares — not how it's implemented internally. No internal module/file paths, no build/
   milestone/phase numbers (e.g. "B12", "Phase 3"), no section references (e.g. "§6.2"), and no
   "mirrors the canonical X" narration. A reader should understand the entry without reading the
   code.

2. **Group by user-facing concern, not by task.** One bullet per feature/fix area. If multiple
   commits touch the same subsystem (e.g. several shadow-pass fixes), collapse them into one
   bullet that names each fix briefly, rather than one bullet per commit.

3. **Only released code can be Changed or Fixed.** Features that have never shipped belong in
   `Added` — there is no prior version to change from or fix against. Design choices and
   implementation details of a new feature are part of its `Added` description, not separate
   `Fixed`/`Changed` entries. Use `Changed`/`Fixed` only for modifications to already-released
   behavior (and mark breakage with **Breaking:** or **Breaking (behavioral):**).

4. **Lead with the affected surface.** Bold-prefix each bullet with the area:
   `**MonoGame 3D:**`, `**Core:**`, `**Raylib:**`, `**MonoGame 2D:**`, etc. — so a reader can
   scan for their backend. Keep breaking changes at the top of their category.

5. **Plain language.** Describe the user-visible effect ("shadows render correctly on scaled
   objects"), not the code diff ("BoundingSphere.Transform now scales center and radius"). The
   reader wants to know what they'll observe, not what line changed.
