module DevNull.Client.Shortcuts

open Avalonia.Controls
open Avalonia.Threading

/// The actions the key table reaches. Every one outlives the pages:
/// the shell supplies its own navigation, overlays, and window state.
[<Struct>]
type Actions = {
  /// Opens the compose dialog.
  Compose: unit -> unit
  /// Opens the search dialog.
  Search: unit -> unit
  /// The escape: closes the top overlay, else leaves fullscreen.
  Escape: unit -> unit
  /// Navigates the router.
  Navigate: string -> unit
  /// Walks the window in and out of fullscreen.
  ToggleFullScreen: unit -> unit
  /// Moves keyboard focus between the commit cards: 1 forward, -1 back.
  WalkCards: int -> unit
}

/// One row of a layout's keyboard cheatsheet: the keys that perform
/// the action, each drawn as its own chip, and the words shown beside
/// them.
type CheatsheetItem = { Keys: string list; Words: string }

/// Everything the one window handler needs.
[<Struct>]
type Props = {
  Root: Window
  Actions: Actions
  /// Opens the window's help layer: the host owns the cheatsheet, so
  /// the same key works under every layout.
  Help: unit -> unit
  /// True while a modal overlay holds the window: the compose dialog,
  /// the search dialog, the bindings list.
  OverlayOpen: unit -> bool
}

/// Installs the one global key handler on the window, tunneling ahead
/// of every focused control: the site's shortcuts — j/k walk commits, c
/// composes, ctrl k searches, g then l/e/p/s navigates, ? shows the
/// bindings, and F11 answers the chrome — with the site's guards.
/// The window acts on Escape only when no focused control handled it.
/// The chrome pairs and the search's pair answer even mid-sentence;
/// plain keys stop at a text caret and at an open overlay, and any
/// other held ctrl, meta, or alt passes through untouched. The handler
/// captures nothing a page owns, so no route outlives its page through
/// it.
val install: props: Props -> unit

/// Moves keyboard focus between the commit cards under the root, in
/// reading order, clamped at the ends. The first forward step enters
/// the first card; a back step from outside enters the last.
val moveCardFocus: dispatcher: IDispatcher * root: Window * step: int -> unit

/// The bindings list the help key opens, built from the active
/// layout's rows: one card over a scrim the host layers above the
/// content slot. The card takes the keyboard when it attaches, so the
/// controls under the scrim answer no keys.
val helpCard: dispatcher: IDispatcher * items: CheatsheetItem list -> Control
