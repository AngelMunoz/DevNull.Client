module DevNull.Client.Tray

open System
open Avalonia.Controls
open Microsoft.Extensions.Logging
open Navs

/// Everything the tray needs: the window it raises, the router it
/// navigates, the unread count it badges, and the real quit.
[<Struct>]
type TrayProps = {
  Window: Window
  Router: IRouter<Control>
  Unread: IObservable<int>
  Quit: unit -> unit
  Log: ILogger
}

/// Installs the single tray icon: an icon menu (Open, Notifications,
/// Saved, Quit), a left-click that raises the window, and a tooltip
/// that follows the unread count. The icon attaches to the running
/// Application and is removed at shutdown. Updates through `unread`
/// arrive on the UI thread, the way the session store emits. `Quit`
/// runs instead of a plain window close, so the close-to-tray guard
/// never hides it.
val install: props: TrayProps -> unit
