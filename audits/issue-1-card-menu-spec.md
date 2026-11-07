# Issue #1 finish: the commit card's More actions menu

This spec is the whole deliverable for the open part of
[#1](https://github.com/AngelMunoz/DevNull.Client/issues/1): the web moved
the commit card's secondary actions into one "More actions" menu (upstream
`e1fb7d2`, site #1371), and the client mirrors it, plus the two checks the
sync named: card buttons must not navigate the card, and the card gets an
accessibility pass.

## Ground rules

- Repo rules from `AGENTS.md` apply in full: `dotnet fantomas .` before
  staging; every new F# file pairs with its `.fsi`; no `private` under a
  signature; at most three tupled parameters, contextual records beyond;
  `Option`/`voption` combinators over match ladders; `asyncEx` lifetimes;
  no push — commit locally on branch `feature/1` and stop.
- The skill gate from `ROADMAP.md` applies; in particular
  `devnull-mvuish-audit` over every View and Component change, and
  `parse-dont-validate` (no new wire input exists in this spec, so no new
  parse leaf is due).
- No Core changes: every call this menu needs already exists (verified
  below). Do not reshape Core.

## The site contract (the design source)

Menu order and wording, from `commit-secondary-actions.tsx` at `e1fb7d2`:

1. `Copy link` — always present.
2. `Send in Remotes` — signed in, remotes on, commit approved.
3. Author items — only when the author is another person (not the viewer):
   `Follow @{handle}` / `Unfollow @{handle}`, `Mute @{handle}` /
   `Unmute @{handle}`, `Block @{handle}` / `Unblock @{handle}`. The site
   hides Follow while blocking. Block is the destructive item unless the
   viewer already blocks.
4. `Report commit` — when the viewer may report (another author's approved
   commit).
5. `Delete commit` — the viewer's own commit. Destructive.

Flow facts from the site:

- The trigger is an icon-only ellipsis button labeled `More actions`,
  tooltip `More`.
- The author items wait disabled until the viewer's relation to the author
  loads; commits do not carry follow/mute/block state, so the menu asks
  once it opens.
- Block asks first, with these words: title `Block @{handle}?`, body
  `You'll stop following each other. Neither of you will be able to
  message, follow, reply to, or star the other, or see each other's
  commits. They won't be told, but they'll see your profile as
  unavailable. You can unblock them any time from your settings.`,
  buttons `Cancel` and `Block`.
- Delete asks first, with these words: `Delete this commit? You can undo
  for 30 seconds.`, buttons `Cancel` and `Delete`.

Client deviations, both deliberate:

- Items are text-only. The client's menus (`ThreadMenus`) carry no icons;
  wording matches the site exactly.
- The client draws no separator lines between groups; its menus space the
  rows instead.

## The code today (verified seams)

- `src/DevNull.Client.UI/Components/CommitCard.fs`
  - The whole card is one `Button` that raises `Opened` on click
    (`CommitCard.fs:1440-1443`). Quiet keys at `:1444-1461`: Enter opens,
    R replies, Q quotes, Y copies the link.
  - `iconAction` (`:865-896`) builds every quiet toolbar button and sets
    `args.Handled <- true` so clicks never open the thread. The avatar
    does the same (`UserFlyout.fs:159-169`).
  - The toolbar `Parts.actions` (`:1085-1338`) docks reply, star, pastel,
    quote, translate, pin left; `link`, `share`, `report` right
    (`:1327-1338`). Share builds only when the slot is present and the
    commit is `Approved` (`:1223-1239`); Report adds `Author.Id <>
    ViewerId` (`:1241-1259`). `copyLink(source, url)` is card-local
    (`:899`).
  - `Event` carries `ShareRequested`/`ReportRequested` (`CommitCard.fsi:23-24`);
    every view ignores both.
- `src/DevNull.Client.UI/Views/CommitActions.fsi` — the shared owners:
  `toggleStar`, `vote`, `pastel`, `failure` (a logger bind). The new
  owners join them.
- Core, all verified present:
  - `Users.Request = { KeyFile; Username }` (`Api.fsi:348`);
    `get -> ProfileDetails` (`:387`, carries `IsFollowing`, `FollowsYou`,
    `IsMuting` — no block flag);
    `follow :439`, `unfollow :452`, `mute :506 -> MuteResult`,
    `unmute :519`, `block :545 -> BlockResult`, `unblock :558`.
  - `MuteResult = { User; IsMuting }` (`ApiTypes.fsi:524`);
    `BlockResult = { User; IsBlocking }` (`:527`).
  - `Commits.ActionRequest = { KeyFile; CommitId }` (`Api.fsi:642`);
    `delete :746`, `restore :762`.
  - `CommitNode = LiveCommit of Commit | Tombstone of Tombstone`
    (`ApiTypes.fsi:237-239`).
- `src/DevNull.Client.UI/Profiles.fsi:8-13` — `IProfiles.Fetch: string ->
  Async<ProfileDetails option>` and `IProvideProfiles`; the env implements
  it (`Env.fs:386`); `UserFlyout` already consumes it per card row.
- Menu idiom: `Components/ThreadMenus.fs:159-190` `actionsMenu` — a
  `GhostButton` with content `"···"` toggles a `Popup` named
  `ActionsMenuFlyout`, `Placement BottomEdgeAlignedRight`, child a
  `CardBack`+`Hairline` border, `CornerRadius 6.0`, `Padding 4.0`, over a
  `StackPanel` of rows. Rows come from `Components/ThreadChrome.fsi:81`
  `menuPick(label, onPick)`; destructive asks go through
  `ThreadChrome.fsi:48+86` `ConfirmProps`/`confirmPick` (Label, Title,
  Words, Verb, OnConfirm, Busy), as `ThreadMenus.fs:104-112` does for
  block. A `Popup` inside a recycled feed row is proven: every card's
  avatar carries one (`UserFlyout.fs:116-181`).
- Delete today: only `CommitDetailView.fs:970-982` (an under-card
  `DangerButton`, no confirm) whose update calls `Commits.delete`
  (`:614-633`), then `Timeline.AnnounceDeletion` (`:679`) and navigates to
  `/log/latest` (`:682`).
- Undo is shell-owned: `Timeline.fsi:35` `AnnounceDeletion`, the shell
  answers with the `UndoToast` control (`Shell.fs:1095`), whose Undo
  button runs `Commits.restore` and reloads the open log
  (`Shell.fs:1024-1114`). A page never wires the undo itself.
- `Views/CommitRows.fsi:30-42` — `Host` carries `OnShare`/`OnReport`
  voptions into the card slots; `LogView.fs:1108-1113` and
  `CommitDetailView.fs:1150-1156` fill them (Share only while the session
  has remotes).
- Accessibility: `AutomationProperties.SetName` exists only in
  `ComposerToolbar.fs:140` and `EmojiPicker.fs:275,476,478`. On the card,
  the icon-only controls with no name and no tooltip are: ReplyButton,
  StarButton, StarCount, PastelButton, QuoteButton, LinkButton, the poll
  choice toggles, and CardImageButton.

## Design

### The card's menu slot

`CommitCard` gains one slot; `Share`, `Report`, and the `LinkButton` leave
the toolbar, replaced by the `···` trigger at the right dock's end.

In `CommitCard.fsi`:

```
/// The viewer's relation to one commit's author, as the More menu
/// asks for it: none until the page's read lands.
[<Struct>]
type AuthorRelation = {
  IsFollowing: bool
  IsMuting: bool
  IsBlocking: bool
}

/// The More actions one host offers a card: the share, report, and
/// delete commands, the follow, mute, and block commands for another
/// author, the relations the page has read (a function of the commit,
/// the shape the translation slot uses), and the commit id whose menu
/// action is in flight.
[<Struct>]
type MenuSlot = {
  Share: (Commit -> unit) voption
  Report: (Commit -> unit) voption
  Delete: (Commit -> unit) voption
  ToggleFollow: Commit -> unit
  ToggleMute: Commit -> unit
  Block: Commit -> unit
  Relations: IObservable<Commit -> AuthorRelation voption>
  Busy: IObservable<string option>
}
```

- `Props` and `RowProps` replace `Share` and `Report` with
  `Menu: MenuSlot voption`. An absent slot draws nothing.
- The `Event` union drops `ShareRequested` and `ReportRequested` (dead:
  every view ignores them) and gains `MenuOpened of Commit`, raised when
  the popup opens — the page's cue to load the author relation if absent.
- The card keeps the Y key exactly as is (`Parts.copyLink`), and the menu's
  `Copy link` item calls the same helper.

### The menu component

New `src/DevNull.Client.UI/Components/CommitMenu.fsi/fs`, one exported
builder over the `ThreadMenus` idiom — nothing else leaves the signature:

- Trigger: `Button`, class `GhostButton`, content `"···"`,
  `Name "CommitMenuButton"`, `ToolTip "More"`,
  `AutomationProperties.SetName "More actions"`; click sets `Handled` and
  toggles the popup.
- Popup: `Name "CommitMenuFlyout"`,
  `Placement BottomEdgeAlignedRight` on the trigger, `CardBack`+`Hairline`
  border, `CornerRadius 6.0`, `Padding 4.0`.
- Items, `menuPick` rows in the site's order, every click handled:
  1. `Copy link` — always.
  2. `Send in Remotes` — only while `Share` is `ValueSome` and the commit
     is `Approved`; click runs the command.
  3. Author items, only while `commit.Author.Id <> ViewerId`, disabled
     until `Relations` yields `Some` for the commit:
     - `Follow @{handle}` / `Unfollow @{handle}` — hidden while
       `IsBlocking`; click runs `ToggleFollow`.
     - `Mute @{handle}` / `Unmute @{handle}` — click runs `ToggleMute`.
     - `Block @{handle}` (destructive ask) / `Unblock @{handle}` (runs
       directly) — through `confirmPick` with the site's block words.
  4. `Report commit` — only while `Report` is `ValueSome`, the commit is
     `Approved`, and the author is not the viewer; click runs the command.
  5. `Delete commit` — only while `Delete` is `ValueSome` and
     `commit.Author.Id = ViewerId` (the gate mirrors Report's, so a host
     may fill the slot once); destructive ask through `confirmPick` with
     the site's delete words; confirm runs the command.
- Escape with the popup open closes it and returns focus to the trigger.
- While `Busy` matches the commit id, the action items disable.

Organization authors do not exist in the client model yet (issue #7); the
`Author.Id <> ViewerId` gate is the whole author gate, and #7 will add the
type gate when it lands.

### The shared owners

`Views/CommitActions` grows three owners beside `toggleStar`, same shape:
request records, `Result` answers, the env capability intersection:

- `toggleFollow(env, { Session; Author; IsFollowing })` —
  `Users.follow`/`unfollow` per the flag, `Result<unit, AppError>`.
- `toggleMute(env, { Session; Author })` — `Users.mute`/`unmute` per the
  held flag, `Result<MuteResult, AppError>`.
- `blockAuthor(env, { Session; Author; IsBlocking })` —
  `Users.block`/`unblock`, `Result<BlockResult, AppError>`.

### The page wiring

`CommitRows.Host` replaces `OnShare`/`OnReport` with `Menu: MenuSlot
voption` (or the slot pieces it needs) and feeds the card. Each of the six
views — `LogView`, `ProfileView`, `SavedView`, `SearchView`,
`CommitDetailView`, `OrganizationView` — builds the slot:

- Share/Report commands reuse the existing `ShareOpened`/`ReportOpened`
  dispatch and the existing dialogs; nothing about the dialogs changes.
- `MenuOpened` loads the author relation through the env's
  `IProfiles.Fetch` when the handle is absent from the page state; the
  state holds `Map<string, AuthorRelation>`; `IsBlocking` starts `false`
  (the read carries none) and updates from the block results.
- Follow and mute flip optimistically and roll back on error, logging
  through `CommitActions.failure`, the way the site rolls back; the mute
  answer's `MuteResult.IsMuting` is the truth the state keeps.
- Delete: the menu's confirmed command dispatches; the update calls
  `Commits.delete`; on `Ok` the page folds that node from `LiveCommit` to
  its `Tombstone` and calls `Timeline.AnnounceDeletion`; on `Error` it
  logs and shows the page's failure line. The undo, the reload, and the
  toast stay shell-owned.
- `CommitDetailView` drops its under-card delete button and its
  navigate-to-`/log/latest`; the detail page folds to the tombstone in
  place, the way the site keeps the deleted card inert.

### Buttons never navigate

Every new control sets `args.Handled <- true` — the trigger, the
`menuPick` rows (already handled), the confirm cards (already handled).
No existing path changes: `iconAction` and the avatar already handle.

### The accessibility pass

- `AutomationProperties.SetName` on every icon-only card control, with a
  tooltip where none exists: Reply (`Reply`), Star (`Star`), StarCount
  (`Stars`), Pastel (`Pastel`), Quote (`Quote`), the poll choice toggles
  (the option's label), CardImageButton (`Commit image`), the menu
  trigger (`More actions`). The card row button itself gets
  `Commit {hash} by @{handle}`.
- Tab order stays the visual order; no `KeyboardNavigation` overrides.
  The card's quiet keys stay.

## Work items

1. `Components/CommitMenu.fsi/fs` (new, paired).
2. `Components/CommitCard.fsi/fs` — the slot, the Event change, the
   toolbar change, the a11y names.
3. `Views/CommitActions.fsi/fs` — the three owners.
4. `Views/CommitRows.fsi/fs` — the Host change.
5. The six views — slot wiring, relation state, delete fold; the detail
   view's delete button removal.
6. Tests (below) and `CHANGELOG.md`.

## Tests

Headless, the `Report.Tests`/`Share.Tests` style: `controlByName`, `click`,
FakeApi request asserts, windows born inside `probeUiAsync`.

1. Gating: a stranger's approved commit shows Copy link, Send in Remotes
   (remotes on), the three author items, Report; the viewer's own commit
   shows Delete and no author items, no Report; a pending commit shows
   neither Send nor Report; remotes off hides Send.
2. The relation load: opening the menu GETs the author
   (`users/{handle}`); items disable before the answer and enable after;
   the words flip per `IsFollowing`/`IsMuting`.
3. Follow: click POSTs `users/{handle}/follow`, the item reads
   `Unfollow`; unfollow DELETEs; a failure rolls the word back.
4. Mute: click POSTs `users/{handle}/mute`; the `MuteResult` answer sets
   the word.
5. Block: the ask shows the site's words; confirm POSTs
   `users/{handle}/block`; the item reads `Unblock` from the result.
6. Delete: the ask shows `Delete this commit? You can undo for 30
   seconds.`; confirm DELETEs `commits/{id}`; the row renders the
   tombstone; the shell's `UndoToast` appears; its Undo runs restore and
   the log reloads (assert the tombstone leaves after the reload's GET).
7. Click isolation: with the page's open-counter armed, click every card
   action control (reply, star, pastel where shown, pin where shown, the
   menu trigger, each menu item, the confirm buttons) and assert the
   counter never moves.
8. Accessibility: `AutomationProperties.GetName` is non-empty on every
   control named above.

## Snapshot scenes

Add to `Snapshots.Tests.fs` under the prefix `F1/`, the `captureAsync`
mechanism, existing capture names untouched:

- `F1/f1-card-own-menu-closed` and `F1/f1-card-stranger-menu-closed` —
  the card's toolbar with the `···` trigger (the link/share/report trio
  is gone).
- `F1/f1-menu-open-stranger` and `F1/f1-menu-open-own` — the popup open,
  relation loaded; if the popup layer does not reach the capture, mount
  the popup's content control directly and say so in a comment.
- `F1/f1-menu-block-confirm` and `F1/f1-menu-delete-confirm` — the
  confirm cards.
- `F1/f1-card-pending` — a pending commit's card.

## Changelog

One bullet under `Unreleased` → `Added`, area prefix `UI:`: the commit
card's secondary actions gather into one More menu — copy link, send in
Remotes, follow, mute, block, report, and delete for your own commits —
with the site's confirm asks for block and delete and the undo toast
after a delete. No internal paths.

## Definition of done

- Both suites green: `dotnet run --project
  tests/DevNull.Client.Core.Tests` and `dotnet run --project
  tests/DevNull.Client.UI.Tests -- --summary`.
- `dotnet fantomas .` clean; the branch `feature/1` holds one or more
  well-worded commits; nothing pushed.
- The new `F1` PNGs exist under `artifacts/snapshots` for the review
  round; no PNG is committed.
