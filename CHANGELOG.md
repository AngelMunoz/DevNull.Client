# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Desktop:** the app asks for the terms. When the account's own answer from the server says the terms in force were not accepted, one dialog stands over the app that cannot be clicked away: the terms and the privacy policy open in the browser from it, a ticked box arms Accept, and Accept records the version the app showed. Accepting once satisfies every app on every device. A session saved before this change does not open the ask on its own; the first account refresh decides.
- **Remotes (Linux):** voice notes recorded on the web now play. A note in the web's Opus format rides the desktop's own media player, with the same speed cycle, live volume, position, and seek as a WAV note, and the note's bytes never touch the disk. A desktop without the media framework keeps WAV playback and says so in the log when a web note cannot play.
- **Settings:** the keys approved to sign as the account are listed and can be revoked. Each row shows the key's name, fingerprint, and when it was approved and last used; this app's own row is named and its revoke is worded as the sign-out it is — revoking it signs the app out at once, revoking any other key removes it from the list.
- **Commit actions:** deleting a commit can be undone. A delete leaves one quiet toast for the site's 30-second undo window: Undo restores the commit and the log reloads with it back, the window's end dismisses the toast on its own, and a restore the server refuses answers with the site's own words.
- **Composer:** staged images can be described. Each staged photo carries an optional description for screen readers with a live count against the site's 1000-character bound — an over-long description is refused before anything is sent, and posts that describe nothing are sent exactly as before.

## [0.4.0] - 2026-10-28

### Added

- **Client:** the app now keeps a log that follows its flows. Every use case — sign-in, session restore, the feeds and threads, posting, the chat's polls and pushes, the image cache, the local files, and the platform heads' own workers (the scheme registrars, the audio capture and playback, the system sounds) — records what it starts, what finishes, and what fails, so a misbehaving session can be read instead of guessed at. The log lands in the console and in one file per day under the app's data folder. Secrets and user content stay out: no tokens, no key material, no message bodies, and server answers are trimmed to the error reason. A `DEVNULL_LOG_LEVEL` value (`TRACE`, `DEBUG`, `INFO`, `WARN`, `ERROR`) overrides the build's default level, so a release install can be traced without a rebuild.
- **Core:** the site's direct messages ("Remotes") are now available end to end: the inbox with its tabs, search, and pins; one-to-one and org threads with typing, read receipts, and live reaction changes; messages pushed with images or a voice note; stashing, muting, pinning, and emoji reactions; merging or dropping an incoming request; and finding people to write to, then starting, renaming, and staffing orgs. The Remotes settings on the signed account (who can push, whether pulls are shared) come with it and can be changed.
- **Core:** a voice note is checked before it is sent: Opus WebM or WAV, within the server's upload cap, carrying the 48 loudness bars the player draws.
- **Core:** a draft can be previewed as the commit it would become — the body it is kept as, the segments it renders from, its excerpt, and its mentions — without the draft being kept.
- **Remotes:** messages wear their reactions. A bubble carries a chip per emoji with its count; the chip's tip names who left it, a long-press-free picker offers the three quick picks and the thread's most-used emojis, and a tap reacts (or takes your reaction back) with the chip updating at once before the server settles it. Double-clicking a bubble toggles the heart.
- **Remotes:** every conversation action is at hand. The thread header's menu pins/unpins, stashes/pops, and mutes for an hour, until tomorrow, for a week, forever, or off; direct threads add view-profile and block, org threads add leave — and the destructive picks ask first, with the site's own words on what they do. Org headers show the member stack, and maintainers get the org menu: rename, add members with the share-history choice, promote or demote each member, and remove any one of them. The inbox's "new org" picker starts an org from the people the search found, with an optional name, and opens its thread when the server answers. When the server refuses any of these, the pane says so with the server's own message.
- **Remotes:** conversations are now two-way. Push stages the message at once and swaps in the stored one; a refused push offers a retry that reuses the same message id, so the server stores it once at most. The composer carries a 2,000-character counter (quiet until 80%, warning color from 95%, danger at the limit, thousands grouped the way the site shows them); Enter pushes and Shift+Enter makes the newline, while Esc cancels the reply or discards a live recording. The input fills the whole space between its tool buttons and Push on any window size, a pasted image (Ctrl/Cmd+V) stages the same way a picked one does, and typing `@` offers the thread's handles — the partner's, or the org's members — with a pick completing the handle at the caret. Staged images wear the commit composer's own chip — a thumbnail, the name, and remove — and a voice note rides alone the way the site keeps it: while one is staged, the box closes to typing and images can no longer be added. The link-card picker chooses which link gets the card, and an unsent draft parks itself per conversation and comes back on return, silently the way the site's chat does. When the thread does not take pushes, the composer is replaced by the word on why — merge/drop for a fetched request, or the waiting/restricted/closed line.
- **Remotes:** a conversation now reads like the site's: the checked-out thread shows day breaks, author runs as bubbles (yours on the accent fill, theirs on the quiet surface), quiet org event lines, org author labels, a typing line whose three dots chase while the partner types, and `pushed`/`pulled`/`pulled by N` receipts under the newest message. Every run ends in its clock time — yours right-aligned, theirs left — except the one run whose line is the receipt. Reply ghosts peek above their answers and clicking one walks the thread back to the original and rings it — or says the site's own word when the original is beyond reach. Scrolling up loads older pages, the `HEAD · N new` divider marks the messages that arrived unseen, `back to present` (or the unread count) returns to the bottom, images open from their bubbles, and a pending link card settles once the server has read it. Leaving the page and coming back reopens the same conversation at the same reading place.
- **Remotes:** the direct-message inbox now has a page of its own (`/remotes`): the Open, Fetched, and Stashed tabs (Fetched only while the account takes requests, and carrying the waiting count), a search that lists both matching conversations and people to start a new remote with, rows with unread pills, pins, mute marks, and org previews, and merge/drop actions on fetched requests. The sidebar carries a Remotes entry with the live unread badge, hidden while the account has the feature off. Wide windows show the site's two columns — the inbox takes a fixed share of the strip, floored so its rows stay readable — and below the breakpoint the page shows one pane: the inbox until a conversation opens, then the thread full-width with a back row above it. Scroll to the end of a tab and the next page loads by itself. The chat's polls follow the window's own visibility, the way a visible tab polls on the web: they run while the window stands on screen — focused or not — pause only once it hides (minimized or in the tray), and wake at once when it returns.
- **Remotes:** voice notes record and play. The composer's mic opens a recording lane with a live meter and clock (a half second minimum, a minute ceiling, the warning color through the last ten seconds); a stop stages the note as a chip that pushes alone or with a draft, and a cancel keeps nothing. A stop pressed while the microphone is still opening cancels the take the moment the open lands — one click stops, the lane never resurrects — and an open that beats the lane's first layout still records instead of vanishing. A received note draws its 48-bar waveform: play/pause with the clock, a speed cycle (1x/1.5x/2x) that retimes the note from where it stands rather than restarting it, a volume control whose moves land on the sounding note and are saved for the next one, and the bars filling as it sounds — one note at a time, the way the site plays them. Leaving a conversation stops the sounding note, and a recording opened there releases the microphone. The microphone opens only when the mic is clicked, never when a conversation opens. On Windows the OS's own media player decodes each note's format, so the web's Opus notes sound too, and a take records the room's real sound; on Linux this build plays WAV notes. On a platform that hands the app no audio engine, the mic stays hidden.
- **Remotes:** pings run both ways. The composer carries a ping button — a quiet nudge for the partner or the whole org — and a ping that lands on an open conversation logs the site's own event word (`you got a ping`) and plays the system's notification note: your own ping sounds even while muted, a partner's stays silent through a mute, and pings that were already there when the conversation opened stay still.
- **Audio (Windows):** the microphone and speaker run through Windows' own audio stack (WinRT AudioGraph): capture at the voice-note format with a live level meter, playback with speed and volume, permission refusals named in plain words. The system notification sound plays through the OS media player.
- **Audio (Linux, Windows):** Added the native infra in order to handle sound play.

### Changed

- **Packaging:** the app is now built from a shared client plus one executable per platform (Windows and Linux) instead of a single cross-platform build. Nothing changes for existing installs: the shipped executable keeps its name and the `devnull://` handler keeps working. This is groundwork for platform-native features (audio, notifications) implemented per platform behind shared contracts.

### Fixed

- **Core:** the site renamed the user field `username` to `handle` across its whole API, which broke the client's timelines, notifications, and mentions. Every decoder reads the new field, the client's own vocabulary follows the site's, and a chat message's reply id now reads as absent where the server hides it. Saved sign-in sessions keep working.

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
