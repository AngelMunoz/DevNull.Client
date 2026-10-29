# Roadmap

This roadmap syncs the client with the server's public JSON and CLI API as the
server completed it on 2026-10-28, and adds Linux playback for the web's voice
codec. Each phase is one deliverable: Core, UI, tests, and changelog, shippable
alone and in order.

The server contract is read from the server repository that sits beside this
client in the workspace. Its license is MIT (declared in its `composer.json`),
so its limits, wording, and flows may be mirrored. The client's code stays its
own.

## Principles

1. **The server contract is the design source.** Every phase is written against
   the named server files and the named web pages first — same wording, same
   limits, same interaction flow — and never approximated from memory. Where a
   server constant exists (a window length, a queue cap, a character bound,
   a throttle), the client carries the same rule as a parse leaf, so the user
   hears it before the server does.
2. **Current code does not constrain the new design.** The Windows head was not
   forced through the Linux head's streaming design; its native media stack
   shaped its own path. The same holds for every phase here: design against the
   server contract and the right user flow first, then let the existing modules
   and contracts bend to fit — reshape a `Natives` interface, move a module,
   change a decoder's shape — rather than squeezing the new design into the
   current hole. What stays fixed is the wire contract and the user-facing
   behavior, not our internal seams.
3. **The server is never named by domain or brand in plans or code.** Documents
   and code say "the server" or "the site". Only the user's configured base
   address and true wire constants (an HTTP path, a signature namespace) carry
   real names.
4. **Client-repo rules win inside the client.** The imperatives in `AGENTS.md`
   apply to every phase; the skill gate below is part of every phase's
   definition of done.

## State as of 2026-10-28

- The CLI API (signed commits, key lookups) and every endpoint the client calls
  today still match the server exactly; the server's CLI contract has not
  changed since 2026-10-19.
- The burst of server work from 2026-10-26 to 2026-10-28 (terms, devices,
  search, suggestions, scheduled commits, translations, pins, restorations,
  related commits, organization pages, user settings) is absent from Core and
  lands phase by phase below.
- Media needs no endpoint work: every image and voice note arrives with a URL
  signed for the user, day-stable and good until midnight UTC the day after
  tomorrow, so the players download bytes directly. Core never calls the media
  routes.
- One real defect: the site records voice notes as Opus in WebM, and the Linux
  head plays PCM WAV only, so voice notes recorded on the web do not play on
  Linux. Windows plays them through its native stack. Phase 2 closes this.

## Standing rules for every phase

**Repo rules** (from `AGENTS.md`): `dotnet fantomas .` before staging; every new
F# file pairs with its `.fsi`; no `private` under a signature; at most three
tupled parameters with contextual request records beyond; `Option` combinators
over match ladders; JDeck for JSON; `asyncEx` lifetimes; changelog entry under
`Unreleased` → `Added` with a bold area prefix, one bullet per user-facing
concern; no push without permission granted for that set of work.

**Skill gate** — each phase's code is checked against all of these before it is
done:

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

## Phase 1 — Terms acceptance

**Goal:** the app knows when the person has not accepted the current terms, asks
them, and records the answer — at first sign-in or any time after.

**Server reference:** the current-user resource (the `terms_version`,
`terms_accepted_at`, `terms_accepted` fields), the terms update controller and
request, the device-redeem controller's optional `terms_version` field, and the
web's terms page.

**Client work:** Core extends the current-user decode with the three terms
fields and adds `Terms.accept` (an empty-body update that answers as the
current-user endpoint does). The redeem call sends `terms_version` when the
person accepts in the sign-in flow, so first sign-in accepts in one step. The
UI shows the prompt whenever a refreshed session says the terms are unaccepted:
the web page's wording, a tick box, links to the site's terms and privacy pages.

**Verify:** fixture tests for the new fields and the accept round-trip; a
headless test drives the prompt; changelog bullet.

## Phase 2 — Linux voice decode: Opus in WebM to PCM

**Goal:** a voice note recorded on the web plays on Linux with the same speed,
volume, seek, and position behavior as a WAV note.

**Server and web reference:** the web's voice-note library (Opus in WebM at
24 kbps, WAV only as the browser fallback), Core's voice staging (the byte cap,
the WebM/WAV sniffing), the shared natives contract (the head owns decoding),
and the Linux head's playback pipeline (16 kHz mono S16LE over the desktop's
audio client, with speed by resample and position from samples served).

**Design first, per Principle 2:** the playback path is the platform's own
media stack, exactly as the Windows head hands its notes to the OS player. The
Linux counterpart is the desktop's media framework: probe its library at head
start; when present, a note's bytes play through its ready-made player element
— decode, demux, resample, and output all native — steered by a thin
platform-invocation layer. When the probe finds nothing, the head keeps the
current PCM path: WAV notes play as today, a WebM note names why it cannot
play, and one log line says the desktop's media framework is missing so the
word is traceable. No managed decoder is written; the fallback is the
documented cut, not a second implementation. Recording on Linux stays WAV —
the site accepts it, and the cap holds for the minute-long notes the web
allows — so this phase only plays.

**Approach:** one probe for the framework's library, one `gst_init` on first
use, and a player element per note: the bytes ride a memory file (a
file-descriptor URI), the element's state machine runs the note, the bus is
polled from an async loop for the end and for failures, the position is
queried, seeking with a rate covers the three speeds, and the volume property
covers loudness. Every playback-contract member maps to one native feature;
no managed binding package is added, and the system library links dynamically
under its own license. The probe decides at startup which path each note
takes, and the content-type sniff the Core already does picks WAV versus the
player.

**Verify:** a headless test drives a WebM note through the voice player where
it failed before, with the player faked behind the head's own seam, and a
missing-framework probe keeps the WAV path and the named WebM failure (both
directions). A manual pass on a clean Fedora KDE machine plays a WebM note
from the web and a WAV note end to end — start, speed cycle, volume, seek, and
the position bar — with the framework present and, after hiding the library,
the named failure in its place. The skill gate runs over the new module pair,
with an `fsharp-native-aot` publish proof of the Linux head in this phase, not
only at the end.

## Phase 3 — Devices

**Server reference:** the device index and destroy controllers, the device
resource (id, name, fingerprint, approved at, last used at), the web's devices
page.

**Client work:** Core adds the list (a plain collection, newest approval first)
and the revoke (a no-content answer). The settings page lists each device with
its facts and a revoke action. Revoking the device that signed the request
signs the app out — the server's own rule — so the client signs out locally
right after the answer.

**Verify:** fixture tests for the list decode and the revoke; a headless test
lists and revokes; a test proves the self-revoke path signs out.

## Phase 4 — Commit delete undo

**Server reference:** the restoration controller and request (author-only,
others see not-found; a 30-second window; the no-longer-deleted and
window-expired answers), the web's undo toast.

**Client work:** Core adds the restore call, answered by the commit as its page
shows it. Deleting a commit shows the undo affordance for the window's length;
undo restores the commit into the feed. Expired and already-restored failures
show the server's messages verbatim.

**Verify:** fixture test for the restore answer; a headless delete → undo →
return test; timer expiry dismisses the affordance.

## Phase 5 — Image descriptions on commits

**Server reference:** the commit-create request's description rules (a nullable
list beside the images, each entry bounded at the server's own length), the
web's image picker.

**Client work:** the commit-create request carries optional per-image
descriptions in its payload, exactly as the remotes push already carries them;
a parse leaf enforces the per-entry bound and the count. The composer's image
flow gains the optional description field with the counter the web shows.

**Verify:** bound tests at the parse leaf; a headless composer test posts a
description; an over-long entry surfaces the bound before sending.

## Phase 6 — Scheduled commits

**Server reference:** the scheduled-commit controller (an offset-paginated
queue, twenty a page — unlike every cursor list the client has), the create
request (body; a scheduled moment after now and within thirty days; no replies
or quotes; a link-card choice among the body's links; images, descriptions, and
a poll as a commit carries them; at most ten outstanding), the submit-now
controller and its conflict answers, the scheduled-commit resource (the status
story: scheduled, the commit's moderation status, or deleted; the body hidden
once submitted), the web's scheduled page.

**Client work:** Core gains a scheduled module — list by page number, create
(multipart when images ride), cancel, submit now — with the queue's own page
decoder and a parsed status. The composer gains the schedule action (a
date-time picker inside the server's bounds, with reply and quote controls
off), and a queue surface lists the entries with status, cancel, and commit
now, mirroring the web page's rows and conflict messages.

**Verify:** fixture tests for the offset page and the status transitions;
create-with-poll-and-images through the fakes; both conflict answers surface
verbatim; a headless schedule → cancel → submit-now pass.

## Phase 7 — Profile pin and related commits

**Server reference:** the commit pin controllers (both answer the commit as its
page shows it, so the pinned flag updates; author-only), the related-commits
controller (a plain collection, optionally signed), the web's secondary actions
and related-commits list.

**Client work:** Core adds pin, unpin, and related. The author's own commit
actions gain the pin toggle; the profile shows the pinned commit at the top;
the thread shows related commits under it, laid out as the web's list.

**Verify:** fixture tests for both answers; a headless pin from a thread
reflects on the profile; the related list renders.

## Phase 8 — Search

**Server reference:** the people search controller (a term bounded at the
server's length, no pagination, follow relations loaded) and the commit search
controller (a required term with a minimum length, a bounded limit with the
web's default, nearest-meaning-first, possibly fewer than asked for, reply
previews included), the web's search page.

**Client work:** Core adds both searches with a parse leaf for the term's
minimum length, keeping the server's reasoning in the UI hint. The UI gains a
search route (keyboard-opened) with the web's two tabs, debounce, empty state,
and fewer-results note; people rows reuse the profile card and flyout.

**Verify:** fixture tests for both decoders; the minimum-length guard; a
headless type-see-open pass across both tabs.

## Phase 9 — Composer suggestions

**Server reference:** the mention-suggestion controller (a term plus the
composer's context parameters; the answer is people beside an organizations
array, since mentions link to organizations in commits) and the
issue-suggestion controller (a digits-only term, answered with the issue or
nothing), the web composer's suggestion components.

**Client work:** Core adds the mention search (context-aware) and the issue
lookup. The composer shows @-mention completions (people and organizations) and
fetches the issue card for a number reference, with the web's triggers and
latency behavior.

**Verify:** fixture tests for both answers; the digits-only parse; a headless
drive of both completions from the composer.

## Phase 10 — Who to follow

**Server reference:** the suggested-users controller (rounds with spares that
fill dismissed slots; a profile or commit context; nobody suggested is followed
yet), the dismissal controller, the user resource's known-followers count, the
web's who-to-follow rail and its people-you-know line.

**Client work:** Core decodes the known-followers count and adds the suggestion
list and the dismissal. The UI gains the rail: the web's slots, reason lines,
refresh-to-next-round, and not-interested with a spare filling the slot, placed
beside profiles and commits with the same context calls.

**Verify:** a fixture test for the spare contract; a headless dismiss swaps in
a spare; refresh advances the round.

## Phase 11 — User settings, timezone, and translations

**Server reference:** the user-settings controller (timezone, the follow-the-
device language, automatic translation, the pickable languages with their
names, the admin flag), the three preference update requests, the commit
translation controller (a language field; the answer's display shape; a
retryable failure with the web's exact messages), the web's language setting
and translate components.

**Client work:** Core adds the settings read and the three preference writes,
plus the commit translate call with the retryable failure leaf. The settings
page gains the language, automatic-translation, and timezone rows as the web
shows them; commits gain the translate action that swaps the body and back,
with the web's pending and failed states.

**Verify:** fixture tests for the settings and translation answers including
the retryable failure; a headless language change and a translate-retry pass.

## Phase 12 — Org message pins and public organizations

**Server reference:** the message resource's pinned-at field (with the reply
reference hidden on pinned messages), the message pin controllers (org
maintainers, under the posting-timeout guard), the organization resource and
its mentions controller, the web's organization pages.

**Client work:** Core decodes the message pin state and adds the message pin
and unpin calls and the organization read with its mentions. Org threads show
pinned messages with the maintainer's pin action; an organization route shows
the public profile and its mentions.

**Verify:** fixture tests including the hidden-reply case; a headless pin
visible to members with the action limited to maintainers; the organization
page renders from fixtures.

## Phase 13 — Release hardening

`dotnet fantomas .`; full build; both test suites; Native AOT publish of both
heads over everything the roadmap added; one full skill-gate sweep of the whole
diff; changelog review (one bullet per user-facing concern, all under
`Added`); the version bump per the changelog-driven scheme. Publish output
stays out of the tree.

---

## Not in scope

- **The Mac update check** — an endpoint for another platform's app updater.
- **The media routes** — the players consume their signed URLs directly; Core
  never calls them, and the signing window and day-stability already fit the
  caches.
- **The CLI API** — verified in sync; the server's contract has not moved.
- **Encoding on Linux** — recording stays WAV, which the site accepts; only
  playback decode is needed.
