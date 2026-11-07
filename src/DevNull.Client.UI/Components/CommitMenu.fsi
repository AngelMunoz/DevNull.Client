module DevNull.Client.Components.CommitMenu

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes

/// The viewer's relation to one commit's author, as the More menu
/// asks for it: none until the page's read lands. The profile read
/// carries no block flag, so `IsBlocking` starts false and moves only
/// when a block or unblock answers.
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
/// action is in flight. An absent command leaves its row out of the
/// menu; the card's own gate decides which rows show at all.
[<Struct>]
type MenuSlot = {
  /// The share command the page offers on an approved commit, when the
  /// account carries the direct messages.
  Share: (Commit -> unit) voption
  /// The report command the page offers on another author's approved
  /// commit.
  Report: (Commit -> unit) voption
  /// The delete command the page offers on the viewer's own commit.
  Delete: (Commit -> unit) voption
  /// The follow toggle the page runs optimistically.
  ToggleFollow: Commit -> unit
  /// The mute toggle the page runs optimistically.
  ToggleMute: Commit -> unit
  /// The block command the page runs once the ask is confirmed.
  Block: Commit -> unit
  /// The relation the page has read per commit, none until it lands.
  Relations: IObservable<Commit -> AuthorRelation voption>
  /// The commit id whose menu action is in flight.
  Busy: IObservable<string option>
}

/// The menu's input: the commit it rides, the signed-in viewer, the
/// host's slot, the open cue the page loads the relation on, and the
/// card's own link copier.
[<Struct>]
type MenuProps = {
  Commit: Commit
  ViewerId: string
  Slot: MenuSlot
  /// The cue the page takes to load the author relation when absent,
  /// raised once per open.
  OnOpened: Commit -> unit
  /// The card's link copier, handed the trigger to read the clipboard
  /// from.
  CopyLink: Control -> unit
}

/// The commit card's More actions menu over the ThreadMenus idiom: the
/// ghost `···` trigger, the popup card under it, and the site's rows in
/// its order — copy link, send in Remotes, the author items that wait
/// disabled until the relation lands, report, and delete — with the
/// site's confirm asks for block and delete. Every click stays
/// handled, so no row ever opens the card behind the menu.
val view: props: MenuProps -> Control
