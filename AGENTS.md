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

## Commands

- `dotnet build DevNull.Client.slnx` - builds everything; the build
  must end with zero errors and zero warnings.
- `dotnet run --project tests/DevNull.Client.Core.Tests` - runs the
  Expecto suite; append `-- --summary` for a summary or `-- --filter <name>`
  for a subset.
- `dotnet fantomas check .` / `dotnet fantomas .` - format check / format
  (`dotnet tool restore` installs the tool).
- `dotnet run --project src/DevNull.Client` - opens the window; clicking
  Commit posts a real commit under the user's key. Never run it to "test"
  a change, the unit tests with fakes are the check.

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
  `Api.fsi/fs`
  (the signed `/api/v1` surface in the `devnull-api` namespace:
  `signedRequest`, device authorization, account and preferences,
  users and follows, logs, commits, replies, stars, bookmarks,
  pastéis, reports and notifications; the create route accepts no
  images or poll), `Payload.fs` (pure
  render of the CLI signed payload), `Commit.fs` (CLI findKey / sign /
  post leaves and the `commit` composite, all async). Core never
  references the app. The server contract source is the dev-null
  repository; verify response shapes there before changing
  `ApiTypes`.
- `src/DevNull.Client/` - the Avalonia consumer: `Errors.fs` (the edge
  type and its plain-text descriptions), `Env.fsi/fs` (production
  workers: system clock, `RandomNumberGenerator`, PKCE verifier source,
  `ssh-keygen` through `Process` off the UI thread, one `HttpClient`
  behind `IHttp`; plus `KeyFiles` discovery in `~/.ssh`),
  `DeviceAuth.fsi/fs` (the app's Ed25519 key under `%LOCALAPPDATA%`,
  the per-user `devnull://` scheme registration, device authorization,
  callback redemption and session storage), `CommitView.fsi/fs` (the
  one window: sign-in status, message, reply hash, commit button,
  status line, NXUI in code), `Program.fs` (Fluent theme, desktop
  lifetime, single-instance callback forwarding).
- `tests/DevNull.Client.Core.Tests/` - the Expecto suite over Core, with
  `TestSupport.fs` providing the test edge type, the async fake workers,
  the requirements interface, and `Values` builders.
- `Directory.Packages.props` - central package management; every package
  version lives there, never in a project file. reference from the projects only what each uses.

## Testing

1. **Assert behavior, not plumbing.** Test through the surface that is meant to survive: parse raw input and assert on the domain value, run the composite with fakes and assert on the outcome. Never assert on internals.
2. **Regression tests prove both directions.** A regression test for a bug must fail before the fix and pass after it.
3. **Fakes control their output.** Each fake worker (`FixedClock`, `FixedNonceSource`, `FixedSigner`, `FakeHttp`, `CapturingHttp`) returns exactly the canned value wrapped in `async { return ... }`; no test reaches a real process, network, or file.
4. **The API is never shaped for tests.** Tests implement the same error interfaces the production edge implements; domain code gains nothing for testability.
