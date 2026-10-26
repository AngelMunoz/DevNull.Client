# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **Packaging:** the app is now built from a shared client plus one executable per platform (Windows and Linux) instead of a single cross-platform build. Nothing changes for existing installs: the shipped executable keeps its name and the `devnull://` handler keeps working. This is groundwork for platform-native features (audio, notifications) implemented per platform behind shared contracts.

### Added

- **Core:** the site's direct messages ("Remotes") are now available end to end: the inbox with its tabs, search, and pins; one-to-one and org threads with typing, read receipts, and live reaction changes; messages pushed with images or a voice note; stashing, muting, pinning, and emoji reactions; merging or dropping an incoming request; and finding people to write to, then starting, renaming, and staffing orgs. The Remotes settings on the signed account (who can push, whether pulls are shared) come with it and can be changed.
- **Core:** a voice note is checked before it is sent: Opus WebM or WAV, within the server's upload cap, carrying the 48 loudness bars the player draws.
- **Core:** a draft can be previewed as the commit it would become — the body it is kept as, the segments it renders from, its excerpt, and its mentions — without the draft being kept.
- **Audio (Linux, Windows):** Added the native infra in order to handle sound play.

## [0.3.1] - 2026-10-24

### Changed

- **Composer:** attaching a photo sends the file as-is, and the server re-encodes the post. **Breaking (behavioral):** a photo over the 10 MB staging cap is now refused instead of being downscaled on the desktop first.

### Fixed

- **Composer:** removing an image, opening a poll, posting, or leaving the page returns the staged photos' memory to the OS, so an attach-and-remove session no longer stays above the app's resting footprint. Pasting a photo costs its file size in memory, not a full-resolution copy.

## [0.3.0] - 2026-10-24

### Added

- **Sidebar:** the desktop navigation collapses to the site's 80 px icon rail and expands again from a Collapse/Expand row under Commit. The rail centers its icons, unread badge, and avatar; the choice persists between runs, the labels become tooltips, and the reading column grows into the freed width.

### Changed

- **Log:** polls follow the site's design. An open poll shows one choice plus a Vote button, and the results show a fill bar per option, the viewer's check, the share, and the time left or `Final results` under the options. Counts stay hidden until the server publishes them, a poll before approval waits for the commit, and a poll past its close stops taking votes.
- **Composer:** the poll editor is a bordered fieldset with a 50-character option cap, X remove buttons, a `Poll length` select, and an icon-only Add poll button that disables while images are staged.

### Fixed

- **Memory:** attaching large photos costs their posting size, not their camera size. Pasted and picked images re-encode to the service's posting bound before staging, so staging and uploads shrink about eightfold; previews decode at thumbnail size, feed images decode to their displayed height, and the viewer decodes to the window. An attach-and-view session that ratcheted the working set by over a hundred megabytes now stays nearly flat.
- **Memory:** the image memory cache carries a 5 MB budget sized from what the service actually serves (PNG payloads averaging ~60 KB), with the on-disk cache serving every eviction — image browsing no longer parks tens of megabytes of payloads in memory.
- **Composer:** the staged-image strip no longer rebuilds on every keystroke of the reply box; it rebuilds only when the staged list itself changes.
- **Shell:** the window paints the themed background to its edges. The desktop no longer shows through beside the sidebar, and the account avatar is no longer clipped in the rail.
- **Log:** a fenced code block now shows the characters the server escaped in its highlighted HTML. Quotes, angle brackets, and ampersands no longer appear as `&quot;`, `&lt;`, `&gt;`, and `&amp;`.

## [0.2.0] - 2026-10-23

### Added

- **Shortcuts:** the site's keyboard shortcuts work across the window: `j`/`k` walk the commits (paging the feed past what is on screen), `Enter` opens the focused commit, `y`/`r`/`q` copy its link, reply, and quote, `c` composes, `g` then `l`/`e`/`p`/`s` jumps to the log, trending, profile, and settings, and `?` shows the full list. They pause while you type and while a dialog is open. Ctrl/Cmd+Enter posts straight from the composer. The commit under keyboard focus paints the accent wash, so the walk is always visible.

### Fixed

- **Log:** clicking "N new commits" returns the reading place to the top of the fresh page again. The scroll-place memory added in 0.1.1 kept the old offset across the refresh, so the new commits loaded above an unmoved viewport.

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
