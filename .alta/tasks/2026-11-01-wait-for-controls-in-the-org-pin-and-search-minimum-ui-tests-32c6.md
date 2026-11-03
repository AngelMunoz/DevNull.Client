---
title: "Wait for controls in the org pin and search-minimum UI tests"
kind: problem
status: pending
created: 2026-11-01
summary: "Two UI tests depend on timing: the org pin-refusal test clicks a button that may not exist yet, and the search-minimum test sleeps a fixed 700 ms before it checks the request. Both failed once and passed on rerun."
---

## Why

The sequenced UI suite failed 2 of 262 tests in one run, then passed 262 of 262 on the rerun. Both failures are timing assumptions in the tests:

- `tests/DevNull.Client.UI.Tests/RemotesView.Tests.fs` (refused pin test, around line 3719): after `openOrgAsync`, it looks up `PinMessage41` and clicks it without waiting. The button appears only after the thread state carries the maintainer role. It failed alone with "the refusal path never asked" and passed in the next full run.
- `tests/DevNull.Client.UI.Tests/App.Tests.fs` (commit term under the server's minimum, around line 3666): after typing, it runs `Async.Sleep 700` and then asserts that the people search request went out. Under load, the debounce can exceed 700 ms. It failed in one full run and passed alone and on rerun.

## What to do

Wait for the observable condition, not for a fixed delay:
- Pin test: use `waitForAsync` until `controlByName(window, "PinMessage41")` is `Some`, then click.
- Search test: use `waitForAsync` until `api.Requests` contains `/api/v1/search/users?q=ab`, with the existing 5 s bound, then assert no commits request.

## Where

- `tests/DevNull.Client.UI.Tests/RemotesView.Tests.fs`
- `tests/DevNull.Client.UI.Tests/App.Tests.fs`
