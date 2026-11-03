---
title: "Delete the two review leftovers: NotificationsView dead open and the RemotesWire duplicate doc line"
kind: problem
status: pending
created: 2026-11-03
summary: "Two architect directives landed unfixed and are still in the tree: NotificationsView.fs:4 `open System.Collections.Generic` (dead since P19's toRows change) and the duplicated doc comment at RemotesWire.fs:499-500 (P21's last directive). The role-eval ground truth used them; they are verified live."
---

## Why

The dev loop recorded both directives but the steps landed without applying them:

1. `src/DevNull.Client.UI/Views/NotificationsView.fs:4` — `open System.Collections.Generic` has no user since P19 dropped the HashSet in `toRows` (grep confirms; also caught by the role-eval architect candidates).
2. `src/DevNull.Client.UI/RemotesWire.fs:499-500` — the doc comment "One tab's next page: a moved pin rebuilds the depth instead." appears twice above `pageAfter` (P21's final directive; `pageAfter` is hidden from the .fsi, so the P22 comment sweep will not reach it).

## What to do

Delete the dead open and one copy of the duplicated line; run fantomas and the UI suite. If P22 lands through the distant-mira worktree first, check item 2 is already collapsed there and only item 1 remains.

## Where

- src/DevNull.Client.UI/Views/NotificationsView.fs:4
- src/DevNull.Client.UI/RemotesWire.fs:499-500
