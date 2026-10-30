module DevNull.Client.Shortcuts

open Avalonia.Controls

/// The actions the key table reaches. Every one outlives the pages:
/// the shell supplies its own navigation, overlays, and window state.
[<Struct>]
type Actions = {
  /// Opens the compose dialog.
  Compose: unit -> unit
  /// Opens the search dialog.
  Search: unit -> unit
  /// Opens the bindings list.
  Help: unit -> unit
  /// The escape: closes the top overlay, else leaves fullscreen.
  Escape: unit -> unit
  /// Navigates the router.
  Navigate: string -> unit
  /// Walks the window in and out of fullscreen.
  ToggleFullScreen: unit -> unit
  /// Moves keyboard focus between the commit cards: 1 forward, -1 back.
  WalkCards: int -> unit
}

/// Everything the one window handler needs.
[<Struct>]
type Props = {
  Root: Window
  Actions: Actions
  /// True while a modal overlay holds the window: the compose dialog,
  /// the search dialog, the bindings list.
  OverlayOpen: unit -> bool
}

/// Installs the one global key handler on the window, tunneling ahead
/// of every focused control: the site's shortcuts — j/k walk commits, c
/// composes, ctrl k searches, g then l/e/p/s navigates, ? shows the
/// bindings, F11 and Escape answer the chrome — with the site's guards.
/// The chrome pairs and the search's pair answer even mid-sentence;
/// plain keys stop at a text caret and at an open overlay, and any
/// other held ctrl, meta, or alt passes through untouched. The handler
/// captures nothing a page owns, so no route outlives its page through
/// it.
val install: props: Props -> unit

/// Moves keyboard focus between the commit cards under the root, in
/// reading order, clamped at the ends. The first forward step enters
/// the first card; a back step from outside enters the last.
val moveCardFocus: root: Window * step: int -> unit

/// The bindings list the help key opens: one card over a scrim the
/// shell layers above the panes. The card takes the keyboard when it
/// attaches, so the cards under the scrim answer no keys.
val helpCard: unit -> Control
