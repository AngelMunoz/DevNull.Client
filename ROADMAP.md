# Roadmap

This file is the index: the working rules, the gates, the current state, and
the two install spikes. Every other deliverable lives in a GitHub issue, in
the order of the issue index below. Each phase and each issue is one
deliverable: Core, UI, tests, and changelog, shippable alone and in order.

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

## State as of 2026-10-31

- The burst's client work through org message pins and public organizations
  has landed in main: the terms gate, Linux playback of the web's voice notes,
  devices with self-revoke, the delete undo, image descriptions, scheduled
  commits, the profile pin with related commits, search, composer suggestions,
  who to follow, user settings with timezone and translations, and the org
  work — releases through 0.8.0 are out, and the org work waits on its
  release.
- What remains of the burst is in the issue index: live users and the
  first-commit notification (#3), and the follower and following lists (#4).
- The scoop channel is built and proven locally: URL install from the
  release, no bucket (Phase 1). It goes live with the next release — the
  cut waits until the Linux install path is ready too (Phase 2). The
  self-signed certificate and the version metadata are the step after.
- The two install spikes stay here as phases: scoop on Windows
  (Phase 1), flatpak on Linux (Phase 2). The AOT single-file publish makes
  each manifest a thin wrapper over the release artifacts.
- The server contract was last pulled and audited on 2026-10-29, and the
  server's CLI contract had not changed since 2026-10-19. The live site has
  since shown main-search results the checkout cannot produce —
  organizations in search — so the server repository must be pulled and
  re-audited before feature work; tracked in
  [#2](https://github.com/AngelMunoz/DevNull.Client/issues/2).
- The commit-card actions are re-implemented per feed page (the poll vote
  exists in six views); consolidating them beside `CommitActions.toggleStar`
  is tracked in
  [#1](https://github.com/AngelMunoz/DevNull.Client/issues/1).
- Five of the burst's features are web-only with no JSON API field: the
  profile README and the bio's mention links wait on a server field; the
  guest join link, the organization claim flow, and the developer count stay
  out of scope.
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
| `mvuish-audit` | every View and Component change |

**Testing**: Core tests parse raw JSON fixtures into domain values and drive
composites with the fakes — never network, disk, or processes. Regression tests
prove both directions. UI tests run headless through the real shell, touching
controls only on the session thread, never dispatching inside a dispatch.

---

## Phase 1 — Windows install: scoop from the release URL

*Built and proven locally; ships with the next release. Existing releases
stay untouched — the cut waits until the Linux install path is ready too
(Phase 2).*

**Goal:** the Windows head installs with scoop straight from the release —
no bucket of our own, no manifest-repository pull request.

**Approach:** the AOT publish is one self-contained executable, so the scoop
manifest describes one file. Each release renders `devnull-client.json`
(version, the zip's URL, its SHA-256, the PATH shim, the Start Menu shortcut)
from the template at `.github/scoop/` and attaches it to the release beside
a `.sha256` sidecar over the zip. GitHub serves the newest stable release's
copy at `releases/latest/download/devnull-client.json`, so one URL installs
and one command updates: scoop records the manifest URL at install time and
re-reads it on `scoop update`. Prereleases never ride the channel — a
pre-release installs only by its exact release URL. Winget is out: it wants
a pull request to the community manifest repository for every release (see
*Not in scope*).

**Verify:** a clean machine installs from the URL and upgrades over the
previous version; the hash pins what it installs.

## Phase 2 — Linux install: flatpak spike

**Goal:** the Linux head installs as a flatpak.

**Approach:** the AOT publish carries its own runtime, so the flatpak wraps
the publish output on the freedesktop platform. A flatpak-builder manifest
builds from the release tarball and ships the `.desktop` file, icons, and app
metadata; the sandbox's desktop file carries the scheme so links open the
app. The spike proves the sandbox's seams: the tray, notifications, and the
audio stack through the desktop's portals.

**Investigation source:** hosting the ostree repository on GitHub Pages, the
shape of
[sirredbeard/github-pages-flatpak-repo](https://github.com/sirredbeard/github-pages-flatpak-repo).
A `.flatpakref` at a plain HTTPS address installs the app and registers the
remote, so `flatpak update` pulls each release as it lands on the pages:
automatic updates for Linux with no Flathub and no server of our own. The
spike answers whether that remote fits the release flow, and whether it
makes Flathub unnecessary.

**Verify:** `flatpak-builder` builds and a local install runs end to end:
sign-in, a commit, a voice note, and a scheme link from the browser. Flathub
submission is a separate decision after the spike.

---

## Issue index

The two spikes above come first; then the issues, in this order:

| Issue | Notes |
|---|---|
| [#2 — Re-pull the server contract](https://github.com/AngelMunoz/DevNull.Client/issues/2) | gates every feature issue: their audit lines are written against the checkout |
| [#3 — Live users and the first-commit notification](https://github.com/AngelMunoz/DevNull.Client/issues/3) | the burst's remainder; after the re-pull |
| [#4 — Follower and following lists](https://github.com/AngelMunoz/DevNull.Client/issues/4) | Core already carries both calls; the work is the pages |
| [#1 — Consolidate the commit-card actions](https://github.com/AngelMunoz/DevNull.Client/issues/1) | anytime before the next feed page; each new one pays for it until it lands |
| [#5 — Release hardening](https://github.com/AngelMunoz/DevNull.Client/issues/5) | last: one full sweep over everything that landed, then the version bump |

## Not in scope

- **The Mac update check** — an endpoint for another platform's app updater.
- **winget** — every release wants a pull request to the community manifest
  repository. Scoop installs and upgrades straight from our own release URL,
  so Windows package management rides scoop alone (Phase 1).
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
- **The guest join link, the organization claim flow, and the developer
  count** — web session flows and guest page props with no API surface. The
  organization page may link the site's claim flow.
- **The web's mutuals page** — no JSON API; the follower and following lists
  in #4 are the API-backed part.
