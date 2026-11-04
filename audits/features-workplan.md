# Feature cycle workplan

One checkbox per issue, ROADMAP order (`ROADMAP.md`). Each entry is one
deliverable: Core, UI, tests, changelog. The loop for these is
`.alta/scripts/devnull-feature.js` — one issue per run, one branch per
issue, a pull request when it passes:

scout (parity census, the server repo) -> derive (the sanitized contract,
behavior spec, visual spec) -> worker (code, tests, snapshot captures,
changelog) -> battery (fantomas, build, both suites, gates, captures) ->
review pair (the diff and the renders, both models, vision) -> the
deriver's scope ruling on every finding. Two scope cycles per issue; the
need for a third stops for the user. The architect stays with the
refactor loop.

The run refuses the peak windows for both providers (weekdays 01:00-04:00
and 06:00-10:00 UTC) unless forced; `action: "window"` reports the status
and the readiness checks.

| Step | Status | Note |
|---|---|---|
| F15 | pending | #15 the back button under the drag band |
| F8 | pending | #8 the app's name and id at device approval |
| F13 | pending | #13 the emoji picker |
| F16 | pending | #16 back to the log the reader left |
| F12 | pending | #12 share commits into remotes |
| F10 | pending | #10 images on commit reports |
| F9 | pending | #9 reply participants |
| F17 | pending | #17 the profile header in short windows |
| F4 | pending | #4 follower and following lists |
| F2 | pending | #2 search: organizations and the deprecated people search |

- [ ] **F15 — the back button under the drag band** (#15)

  One element role or one margin, plus the sweep of the top strip.

- [ ] **F8 — the app's name and id at device approval** (#8)

  Two query parameters and one decode.

- [ ] **F13 — the emoji picker** (#13)

  Client-side only, no API.

- [ ] **F16 — back to the log the reader left** (#16)

  Remember the log kind; the scroll place already parks.

- [ ] **F12 — share commits into remotes** (#12)

  One candidates mode and one share flow.

- [ ] **F10 — images on commit reports** (#10)

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
