---
title: "Make RemotesWatcher stop a thread's loop only when its last reader releases"
kind: problem
status: pending
created: 2026-11-03
summary: "WatchThread hands out one handle per reader, but disposing the first handle cancels the shared loop while the store still counts another reader; WatchedCount then reports one with no polling."
---

## Why

`src/DevNull.Client.UI/RemotesWatcher.fs:135-169` starts one `CancellationTokenSource` per thread and cancels it on every handle's `Dispose`. The store's `WatchCounts` is reference-counted (`Remotes.fs:241-298`). With two readers of one target, disposing the first kills the loop while the second is alive, and `WatchedCount` still reports one. `Remotes.Tests.fs:837` disposes a single reader, so the suite cannot see it. P21 documented the current behavior truthfully instead of changing it in a shape step. `watchInbox` has the same first-dispose behavior.

## What to do

Count handles per thread in the watcher, or keep the loop alive while the store's read count is above zero. Add a regression test with two readers: disposing the first leaves the loop running, disposing the last stops it. Add the equivalent inbox test if two `WatchInbox` handles are supported.

## Where

- `src/DevNull.Client.UI/RemotesWatcher.fs` (`watchThread`, `watchInbox`)
- `src/DevNull.Client.UI/RemotesWatcher.fsi`
- `tests/DevNull.Client.UI.Tests/Remotes.Tests.fs` (near the existing dispose test at `:837`)
