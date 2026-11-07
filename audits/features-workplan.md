# Feature cycle workplan

One checkbox per issue, ROADMAP order (`ROADMAP.md`). Each entry is one
deliverable: Core, UI, tests, changelog. The loop for these is
`.alta/scripts/devnull-feature.js` — one issue per run, one branch per
issue, a pull request when it passes:

scout (parity census, the server repo) -> derive (the sanitized contract,
behavior spec, visual spec) -> worker (code, tests, snapshot captures,
changelog; its battery — fantomas, build, both suites — is the
verification of record, run by the worker itself, never re-run by the
loop) -> collect (changed files, idiom gates on added lines, captures)
-> review pair (the diff and the renders, both models, vision; round 0
is the full pass, later rounds review the delta on kept seats) -> the
deriver's scope ruling on every finding. Two scope cycles per issue; the
need for a third stops for the user. From the second scope cycle the
derivation is frozen: an in-scope ruling cites a spec line, and a real
defect the spec never promised becomes a recorded task, not another
worker pass. The architect stays with the refactor loop.

The run refuses the peak windows for both providers (weekdays 01:00-04:00
and 06:00-10:00 UTC) unless forced; `action: "window"` reports the status
and the readiness checks.

| Step | Status | Note |
|---|---|---|
| F15 | pr 2026-11-04 | #15 the back button under the drag band |
| F8 | pr 2026-11-05 | feat: the app's name and id at device approval (#8) |
| F13 | pr 2026-11-05 | feat: the emoji picker (#13) |
| F16 | pending | #16 back to the log the reader left |
| F12 | pr 2026-11-05 | feat: share commits into remotes (#12) |
| F10 | pr 2026-11-07 | feat: images on commit reports (#10) |
| F9 | pending | #9 reply participants |
| F17 | pending | #17 the profile header in short windows |
| F4 | pending | #4 follower and following lists |
| F2 | pending | #2 search: organizations and the deprecated people search |

- [x] **F15 — the back button under the drag band** (#15)

  One element role or one margin, plus the sweep of the top strip.

- [x] **F8 — the app's name and id at device approval** (#8)

  Two query parameters and one decode.

- [x] **F13 — the emoji picker** (#13)

  Client-side only, no API.

- [ ] **F16 — back to the log the reader left** (#16)

  Remember the log kind; the scroll place already parks.

- [x] **F12 — share commits into remotes** (#12)

  One candidates mode and one share flow.

- [x] **F10 — images on commit reports** (#10)

  The signed files request already exists.

- [ ] **F9 — reply participants** (#9)

  One decode, one picker, the combined cap.

- [ ] **F17 — the profile header in short windows** (#17)

  The header gives way without touching the feed.

- [ ] **F4 — follower and following lists** (#4)

  Core already carries both calls; the pages and the organizations
  section are the work.

- [ ] **F2 — search: organizations and the deprecated people search** (#2)

  A new Core decode and rows in two surfaces.