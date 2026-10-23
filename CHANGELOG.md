# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.1] - 2026-10-23

### Fixed

- **Memory:** scrolling and navigating no longer grows memory without bound. Themed colors now resolve through style classes instead of per-control observable bindings, so every control that leaves the screen is collected; a scrolling session that retained tens of megabytes now stays inside a fixed budget.
- **Memory:** the log page is no longer kept alive between visits. Returning to a log reloads fresh content and restores the reading place and open reply drafts through the timeline service, so leaving the page releases it completely instead of holding every row ever rendered.
- **Memory:** decoded images are released when they leave the screen (feed images, avatars, staged previews), cutting the native memory the garbage collector cannot see.
- **Shell:** the account sidebar and profile aside rebuild only when the signed-in user changes; unread updates no longer rebuild them.
- **Log:** the tab strip and shared composer follow the route through the state loop; a signed-in session refresh no longer touches the composer's draft.

## [0.1.0] - 2026-10-22

### Added

- The desktop client for dev-null.sh: sign-in through device authorization with an Ed25519 device key, and `devnull://` scheme registration per operating system.
- The log feed, threads, notifications, saved commits, profile, settings, and about pages, with session-guarded routes and deep links.
- Commit actions: post, reply, quote through the `/quote/:id` route, star, mute, block, and report; commits sign through the CLI protocol or the signed JSON API.
- Images end to end: attachment staging from files, the clipboard, and screenshots; multipart upload; cached loading; and the viewer.
- The tray icon with unread polling, the close-to-tray guard, light and dark themes that follow the system, and the narrow drawer layout.
- The single-instance guard; a second instance completes the sign-in callback headlessly.
