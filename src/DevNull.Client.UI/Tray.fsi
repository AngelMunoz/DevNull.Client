module DevNull.Client.Tray

open System
open Avalonia.Controls
open Microsoft.Extensions.Logging
open DevNull.Client.Layouts

/// Everything the tray needs: the window it raises, the layout intents
/// it asks, the unread count it badges, and the real quit.
[<Struct>]
type TrayProps = {
  Window: Window
  /// Hands one window-level intent to the active layout; the tray
  /// raises the window first, the layout answers from there.
  Intents: LayoutIntent -> unit
  Unread: IObservable<int>
  Quit: unit -> unit
  Log: ILogger
}

/// Installs the single tray icon: an icon menu (Open, Notifications,
/// Saved, Quit), a left-click that raises the window, and a tooltip
/// that follows the unread count. The icon attaches to the running
/// Application and is removed at shutdown. Updates through `unread`
/// arrive on the UI thread, the way the session store emits. The
/// Notifications and Saved items raise the window and hand their
/// intent to the active layout; Open raises the window alone. `Quit`
/// runs instead of a plain window close, so the close-to-tray guard
/// never hides it.
val install: props: TrayProps -> unit
