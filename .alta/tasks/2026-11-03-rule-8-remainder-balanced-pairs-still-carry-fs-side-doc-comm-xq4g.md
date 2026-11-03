---
title: "Rule-8 remainder: balanced pairs still carry .fs-side doc comments"
kind: gap
status: pending
created: 2026-11-03
summary: "P22 swept every pair whose .fs doc count exceeded the .fsi side, but pairs the signature side already dominates keep their .fs-side docs: those are duplicates of fsi docs or docs on hidden members, which rule 7 sends to the signature file or turns into plain comments."
---

## Why

A census after the P22 sweep (2026-11-03) shows the remaining `///` lines all sit in pairs where the `.fsi` count already meets or beats the `.fs` count: RemotesState (112/108), Remotes (72/80), Natives (44/45), RemotesRows (33/33), RemotesWatcher (12/12), Theme (32/71), LogChrome (15/16), PageChrome (14/26), FeedParts (14/15), CommitActions (14/15), Formatting (6/15), Status, ImageChip, WhoToFollow, Profiles, SessionFiles, SshKeys, and Core's Api (65/240), ApiTypes (59/216), Commit (19/33), Completion (17/27), Http (5/15), Measures (2/9), RemotesText (8/10). The `.fs` copies are either duplicate fsi docs or docs on hidden members; rule 7 gives them the signature file or a plain `//`.

## What to do

Same procedure as P22: delete `.fs` docs the fsi already carries, merge richer sentences into the fsi, convert hidden-member docs to `//`. Core files with dominant fsi docs need only the duplicate deletes.

## Where

`src/DevNull.Client.Core/*`, `src/DevNull.Client.UI/*`, `src/DevNull.Client.UI/Views/Remotes/RemotesRows.fs`. Types.fs and Errors.fs stay exempt (no signature file, AGENTS rule 6).
