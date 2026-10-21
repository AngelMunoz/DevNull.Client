# DevNull.Client

An unofficial desktop client for [dev-null.sh](https://dev-null.sh),
built with F# and Avalonia.

**We are not affiliated with dev-null in any way.** This is an
independent project exploring their public API, with the goal of
providing a simple client for desktop platforms. The service and its
API can change at any time; expect breakage.

## What it is

- One window: a sidebar with the unread count, a reading column with
  the commit log, notifications, saved commits, profiles, and
  settings; a tray icon; a dark theme that follows the site's tokens.
- The UI is written in code with NXUI (no XAML). Routing runs on
  Navs.Avalonia. Page state runs on
  [MVUish.Avalonia](https://www.nuget.org/packages/MVUish.Avalonia):
  Model-View-Update over observables, with keyed subscriptions for
  everything that arrives from outside (the clock, the session, the
  settings store).
- `DevNull.Client.Core` carries the protocol layer (the signed JSON
  API and the plain-text CLI routes); the Avalonia app consumes it and
  never speaks HTTP itself.

## Building and running

- `dotnet build DevNull.Client.slnx` — must end with zero errors and
  zero warnings.
- `dotnet run --project src/DevNull.Client` — opens the window
  against the real dev-null.sh.
- `dotnet run --project tests/DevNull.Client.Core.Tests` — protocol
  suite (Expecto; no network, no disk, no processes).
- `dotnet run --project tests/DevNull.Client.UI.Tests` — headless UI
  suite over the real shell, routes, and pages, every request answered
  by a fake API.
- `dotnet fantomas .` — formats every F# file
  (`dotnet tool restore` first).

Close the app before building after a run: a running instance locks
the build output.

## Going deeper

- `AGENTS.md` — the architecture map and the working rules for this
  repository.
- `tests/README.md` — the testing philosophy and the headless-session
  quirks, including every known way a UI test can hang.
