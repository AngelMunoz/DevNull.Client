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

## Commands

- `dotnet build DevNull.Client.slnx` - builds everything; the build
  must end with zero errors and zero warnings.
- `dotnet run --project tests/DevNull.Client.Core.Tests` - runs the
  Expecto suite; append `-- --summary` for a summary or `-- --filter <name>`
  for a subset.
- `dotnet run --project tests/DevNull.Client.UI.Tests` - runs the
  headless UI suite (Avalonia.Headless + Expecto over the real shell,
  routes and pages, every request answered by `FakeApi`).
- `dotnet fantomas check .` / `dotnet fantomas .` - format check / format
  (`dotnet tool restore` installs the tool).
- `dotnet run --project src/DevNull.Client` - opens the window and uses
  the real dev-null.sh. Never run it to "test" a change; the two test
  projects are the check.

## Architecture

- `src/DevNull.Client.Core/` - the protocol layer. It carries the CLI
  protocol under `/api/cli/*` with plain-text responses, and the signed
  JSON API under `/api/v1/*`. `Measures.fsi/fs` (`CommitId`,
  `CommitBody`, `ShortHash`, `Nonce` and `KeyId` as `FSharp.UMX`
  measures over `string`; companion modules are plural so the measure
  stays usable in `string<Measure>`), `Types.fs` (parse leaves and
  IWSAM error interfaces), `Pkce.fsi/fs` (PKCE S256 challenge),
  `Http.fsi/fs` (generic request and response values, the `IHttp`
  worker and `IProvideHttp` accessor), `Capabilities.fs` (worker +
  accessor pairs: clock, nonce source, PKCE verifier source,
  ssh-keygen signer), `ApiTypes.fsi/fs` (the full JSON API shape:
  accounts, users, profiles, cursor pages, commits and tombstones,
  segments, mentions, link previews, polls, images, notifications,
  logs, action results, `ApiErrorCode` and decode failures),
  `Api.fsi/fs` (the signed `/api/v1` surface in the `devnull-api`
  namespace: `signedRequest`, device authorization, account and
  preferences, users and follows, logs, commits, replies, stars (the
  starred index doubles as the saved list), pastéis, reports and
  notifications; the create route
  accepts no images or poll), `Payload.fsi/fs` (pure render of the CLI
  signed payload), `Commit.fsi/fs` (CLI findKey / sign / post leaves
  and the `commit` composite, all async). Core never references the
  app. The server contract source is the dev-null repository (read-only
  reference at `E:\dev-null`); verify response shapes there before
  changing `ApiTypes`.
- `src/DevNull.Client/` - the Avalonia consumer, dark theme, NXUI in
  code, no XAML. Layers bottom-up:
  - `Errors.fs` - the `AppError` edge type (every Core IWSAM leaf) and
    its plain-text `describe`.
  - `Theme.fsi/fs` - the dark palette brushes and the closed style
    vocabulary (`StyleClass`); views never style themselves, they add
    classes.
  - `Icons.fsi/fs` - the `Glyph` set as `PathIcon` geometry.
  - `Formatting.fsi/fs` - `timeAgo` and `shortCount`.
  - `Stores.fsi/fs` - `SessionStore` (`BehaviorSubject` of the sign-in
    state; emits on the UI thread; the `forget` hook owns the session
    file) and the `IProvideSession` accessor.
  - `Env.fsi/fs` - `IProvideLogging` (the Serilog-bridged
    `ILoggerFactory`), `AppEnv`: the Core capability workers, the
    session store, and the logging factory; `KeyFiles` discovery in
    `~/.ssh`.
  - `DeviceAuth.fsi/fs` - the app's Ed25519 key under
    `%LOCALAPPDATA%`, device authorization, callback redemption and
    session storage; generic over the capability interfaces, never over
    the concrete env.
  - `SchemeRegistration.fsi/fs` - the `ISchemeRegistrar` for the
    `devnull://` scheme, one per operating system: the Windows registry,
    the Linux `.desktop` file plus `xdg-mime`, and a designed failure on
    macOS, where the handler comes from the app bundle.
  - `Navigation.fsi/fs` - `go`: every fire-and-forget navigation is
    `asyncEx { ... } |> Async.StartImmediate` on the UI thread.
  - `Components/` - `Status` (busy/error/empty panels), `Avatar`
    (initial on a lane-colored disc), `CommitCard` (one commit: header,
    segments, poll, link preview, star/reply/quote actions as
    an `Event` DU), `Composer` (MVUish quick composer: fresh, reply, or
    quote target).
  - `Views/` - one MVUish page per route, each a `Page` `UserControl`
    over a `Program.mkProgram`: `SignInView` (device authorization with
    the pending-file poll), `LogView` (tabs, composer, timeline,
    star toggle, load-more), `ComposeView` (full-height
    composer), `CommitDetailView` (ancestors, card, pastel, delete,
    replies, reply composer), `NotificationsView`, `SavedView`,
    `ProfileView` (header, follow/mute/block, commits),
    `SettingsView` (lane palette, pastéis mode, GitHub sync, sign out),
    and `Routes.fs` (the Navs table, the `requiresSession` /
    `requiresSignedOut` guards, and the boot URL).
  - `Shell.fsi/fs` - the one window: sidebar (`NavEntry` list, active
    section from `router.Changed`), the Navs `RouterOutlet`, the
    extended client area with the Mica chain on Windows, and the
    forwarded sign-in callback.
  - `Program.fs` - Serilog (console plus one file per day under
    `%LOCALAPPDATA%\DevNull\logs`), the Fluent theme first, app styles
    after; single-instance mutex; the second instance completes the
    callback headlessly.
- `tests/DevNull.Client.Core.Tests/` - the Expecto suite over Core, with
  `TestSupport.fs` providing the test edge type, the async fake workers,
  the requirements interface, and `Values` builders.
- `tests/DevNull.Client.UI.Tests/` - the headless UI suite. `Harness.fs`
  holds the session (`PerAssembly` isolation), `onUi`/`probeUi`, the
  `pump`/`settle`/`waitFor` frame helpers, and the finders
  (`buttonWithText` prefers the innermost button);
  `Fixtures.fs` holds `TestEnv` (fixed workers over a `SessionStore`),
  `FakeApi` (records every request, answers canned JSON), and the
  canned payloads; `App.Tests.fs` drives the real shell: boot route per
  session state, timeline through the signed API, composer, star,
  paging, thread, replies, pastel, quote, inbox, profile, saved, and
  sign-out.
- `Directory.Packages.props` - central package management; every package
  version lives there, never in a project file. reference from the
  projects only what each uses. UI stack: Avalonia 12.0.5, NXUI 12.0.0,
  Navs.Avalonia 2.0.1, MVUish.Avalonia 1.0.1 (sources at `E:\Navs` and
  `E:\MVUish.Avalonia`).

## Testing

1. **Assert behavior, not plumbing.** Test through the surface that is meant to survive: parse raw input and assert on the domain value, run the composite with fakes and assert on the outcome. Never assert on internals.
2. **Regression tests prove both directions.** A regression test for a bug must fail before the fix and pass after it.
3. **Fakes control their output.** Each fake worker (`FixedClock`, `FixedNonceSource`, `FixedSigner`, `FakeHttp`, `CapturingHttp`) returns exactly the canned value wrapped in `async { return ... }`; no test reaches a real process, network, or file.
4. **The API is never shaped for tests.** Tests implement the same error interfaces the production edge implements; domain code gains nothing for testability.
5. **Headless UI tests touch controls on the session thread only.** Windows are born inside `probeUi`; every control read or click goes through `onUi`, `probeUi`, or the `waitFor` helpers. A test that touches a control directly off the session thread fails with a cross-thread error at best and hangs at worst.
