# Roadmap

This file is the index: the working rules, the gates, the current state, and
the phases. Every other deliverable lives in a GitHub issue, in the order of
the issue index below. Each phase and each issue is one deliverable: Core,
UI, tests, and changelog, shippable alone and in order.

The server contract is read from the server repository that sits beside this
client in the workspace. Its license is MIT (declared in its `composer.json`),
so its limits, wording, and flows may be mirrored. The client's code stays its
own.

## Principles

1. **The server contract is the design source.** Every phase is written against
   the named server files and the named web pages first — same wording, same
   limits, same interaction flow — and never approximated from memory. Where
   a server constant exists (a window length, a queue cap, a character bound,
   a throttle), the client carries the same rule as a parse leaf, so the user
   hears it before the server does.
2. **Current code does not constrain the new design.** The Windows head was not
   forced through the Linux head's streaming design; its native media stack
   shaped its own path. The same holds for every phase here: design against
   the server contract and the right user flow first, then let the existing modules
   and contracts bend to fit — reshape a `Natives` interface, move a module,
   change a decoder's shape — rather than squeezing the new design into the
   current hole. What stays fixed is the wire contract and the user-facing
   behavior, not our internal seams.
3. **The server is never named by domain or brand in plans or code.** Documents
   and code say "the server" or "the site". Only the user's configured base
   address and true wire constants (an HTTP path, a signature namespace) carry
   real names.
4. **Client-repo rules win inside the client.** The imperatives in `AGENTS.md`
   apply to every phase and every issue; the skill gate below is part of every
   deliverable's definition of done.

## State as of 2026-11-10

- Installation is done on both heads and leaves this roadmap: the Windows app
  installs with scoop straight from the release URL, and the Linux app
  installs as a flatpak from the GitHub Pages channel. Both ship with each
  release; no install work remains.
- Phase 1 is done: the MVUish skills sit in `.agents/skills`, the refreshed
  audit's refactors landed (P1–P29), and the commit-card actions first
  consolidated beside `toggleStar`, then gathered into the card's More
  actions menu with follow, mute, block, report, and delete — plus the
  click-isolation proof and the accessibility pass
  ([#1](https://github.com/AngelMunoz/DevNull.Client/issues/1), PR #33).
- The index's other shipped items — #15, #8, #13, #12, #10 — are merged and
  closed, and #16 and #9 closed after them (PRs #34 and #35). #17 and #4 are
  merged (PRs #36 and #37), and #2 is merged after them (PR #38). With it
  the cycle's index is done. The ordered pool below is the next one.
- The server repository was pulled to `4cb5538` on 2026-11-01 and re-audited
  through `33cf978` on 2026-11-07: no decoder break, no Core change due.
  The sync's findings carry #23–#31, and its one required UI fix — the
  profile stats row wrap in narrow windows — still wants its issue.
- Still web-only with no JSON API field: the profile README and the bio's
  mention links wait on a server field; the guest join link and the developer
  count stay out of scope. The organization claim flow gained an API in the
  2026-11-01 pull and is tracked in
  [#11](https://github.com/AngelMunoz/DevNull.Client/issues/11), the pool's
  first item below.
- Media needs no endpoint work: every image and voice note arrives with a URL
  signed for the user, so the players download bytes directly and Core never
  calls the media routes.

## Standing rules for every phase and issue

**Repo rules** (from `AGENTS.md`): `dotnet fantomas .` before staging; every new
F# file pairs with its `.fsi`; no `private` under a signature; at most three
tupled parameters with contextual request records beyond; `Option` combinators
over match ladders; JDeck for JSON; `asyncEx` lifetimes; changelog entry under
`Unreleased` → `Added` with a bold area prefix, one bullet per user-facing
concern; no push without permission granted for that set of work.

**Skill gate** — the code of every phase and issue is checked against all of
these before it is done:

| Skill | What it gates |
|---|---|
| `parse-dont-validate` | every new wire input gets a parse leaf and a domain type with a private constructor; no raw strings downstream |
| `fsharp-iwsam-errors` | new failure leaves composed into the app's edge type; no ad-hoc error shapes |
| `fsharp-signature-files` | `.fsi` exports only what callers use; helpers stay out |
| `fsharp-module-shape` | new Api modules follow the request-record and signed-call pattern of the existing ones |
| `fsharp-function-parameters` | ≤3 parameters, tupled form, `inline` one-liners |
| `fsharp-async-composition` | correct builders and lifetimes; cancellation through the existing transport |
| `fsharp-failure-paths` | decode, transport, and rejection failures are values; conflict and retry answers surface as state, not crashes |
| `fsharp-env-capabilities` | capability interfaces in Core signatures; never the concrete env |
| `fsharp-idiom-review` | no C# shapes: no `if/elif` ladders on discrete values, no `let mutable`, no out parameters |
| `fsharp-nullness-interop`, `fsharp-verify-api` | decoder boundaries verified against the real APIs in use |
| `fsharp-native-aot` | no `%A` or union `ToString` in new code; AOT publish proof where the phase says so |
| `devnull-mvuish-audit` | every View and Component change |

**Testing**: Core tests parse raw JSON fixtures into domain values and drive
composites with the fakes — never network, disk, or processes. Regression tests
prove both directions. UI tests run headless through the real shell, touching
controls only on the session thread, never dispatching inside a dispatch.

---

## Phase 1 — the MVUish skills, the audit, and the efficient refactors

**Landed:** the skills fold (PR #18), the audit's refactors (P1–P29), and
the card-actions consolidation through the More actions menu (#1, PR #33).

**Goal:** the local MVUish skill set matches the MVUish repository that sits
beside this client, the codebase passes the refreshed audit, and the efficient
refactors land — the codebase in better shape before the feature issues
follow.

**Approach:** the MVUish repository (`../MVUish.Avalonia`) carries four skills
this client's `.agents/skills` does not: `mvuish-controls`,
`mvuish-element-shapes`, `mvuish-performance`, and `mvuish-testing`. Fold them
into the `devnull-mvuish-audit` skill — or set them beside it — and let the gate table
above grow to say so. Then re-audit every View and Component against the
refreshed gate: the observable-binding leaks and rebuilding controls the audit
was written for, plus what the new skills add. The refactors the audit calls
for land here, while the codebase is small enough to move; the first is
[#1](https://github.com/AngelMunoz/DevNull.Client/issues/1), the commit-card
actions consolidated beside `CommitActions.toggleStar`. What the audit finds that
is a gap or a bug becomes an issue, not phase work: this roadmap grows no
issues of its own.

**Verify:** the refreshed skill gates every View and Component change; the
audit's refactors land with both suites green; a feed page renders through
the consolidated card actions.

## Issue index

Phase 1 is done, and the cycle's index (#16, #9, #17, #4, #2) is done
and merged. The next index is the ordered pool below: ten items, triaged
by effort and by how each one builds on the search and organization work
that just landed. Each issue keeps its own detail in the tracker.

1. [#11 — follow and claim organizations](https://github.com/AngelMunoz/DevNull.Client/issues/11).
   The direct follow-on to #2: the search already carries an
   organization's follow state, and this adds the follow, unfollow, and
   claim calls and the organization page's button.
2. [#7 — commit authors can be organizations](https://github.com/AngelMunoz/DevNull.Client/issues/7).
   A correctness fix: an organization author decodes as a person today
   and links wrong. The author shape must branch first, and #23 lands
   on it.
3. [#23 — organization commits tab](https://github.com/AngelMunoz/DevNull.Client/issues/23).
   One Core read and one tab on the organization page, on top of #7.
4. [#24 — organization mentions bell](https://github.com/AngelMunoz/DevNull.Client/issues/24).
   Two decoder fields and the bell, count and jump first: no signed API
   marks mentions read yet, so that part waits.
5. [#30 — code blocks: line ranges and titles out of the language label](https://github.com/AngelMunoz/DevNull.Client/issues/30).
   A check first, then a small split in the renderer if the fault is
   shared. Its own regression test decides.
6. [#28 — log feed catch-up and batching](https://github.com/AngelMunoz/DevNull.Client/issues/28).
   The feed misses late-approved commits today. The log request learns
   the catch-up parameters, and the feed batches what arrives.
7. [#26 — hashtag links in commit text](https://github.com/AngelMunoz/DevNull.Client/issues/26).
   Renderer work only: a tag becomes a link into the search that just
   shipped.
8. [#25 — stargazers on own commits](https://github.com/AngelMunoz/DevNull.Client/issues/25).
   One Core page read and one entry point on the author's own commits.
9. [#31 — profile pictures in the image viewer](https://github.com/AngelMunoz/DevNull.Client/issues/31).
   The viewer exists. Avatars on the card, the flyout, and the profile
   header learn to open it.
10. [#27 — search: a Latest tab](https://github.com/AngelMunoz/DevNull.Client/issues/27).
    Verify first: the API support is not confirmed, and the answer
    decides whether the tab is client work or waits on the server.

The rest stay in the tracker, in no pool order:
[#3](https://github.com/AngelMunoz/DevNull.Client/issues/3) (live users
and the first-commit notification, a wide surface),
[#6](https://github.com/AngelMunoz/DevNull.Client/issues/6) (calls in
remotes, the largest build here), and
[#29](https://github.com/AngelMunoz/DevNull.Client/issues/29) (live
updates, blocked until the server publishes its socket settings).
[#5](https://github.com/AngelMunoz/DevNull.Client/issues/5) (release
hardening) runs whenever a phase ships, not in the pool order.

## Not in scope

- **The Mac update check** — an endpoint for another platform's app updater.
- **winget** — every release wants a pull request to the community manifest
  repository. Scoop installs and upgrades straight from our own release URL,
  so Windows package management rides scoop alone.
- **The media routes** — the players consume their signed URLs directly; Core
  never calls them, and the signing window and day-stability already fit the
  caches.
- **The CLI API** — verified in sync; the server's contract has not moved.
- **Encoding on Linux** — recording stays WAV, which the site accepts; playback
  rides the desktop's media framework, and encoding is a possible follow-up
  through the same framework if upload size ever matters.
- **The profile README and the bio's mention links** — the server ships them
  only as web page props, with no JSON API field; they land when the server
  exposes one.
- **The guest join link and the developer count** — web session flows and
  guest page props with no API surface.
- **The web's mutuals page** — no JSON API; the follower and following lists
  in #4 are the API-backed part.
