module DevNull.Client.Tray

open System
open Avalonia.Controls
open Navs

/// Installs the single tray icon: an icon menu (Open, Notifications,
/// Saved, Quit), a left-click that raises the window, and a tooltip
/// that follows the unread count. The icon attaches to the running
/// Application and is removed at shutdown. Updates through `unread`
/// arrive on the UI thread, the way the session store emits.
val install:
  window: Window * router: IRouter<Control> * unread: IObservable<int> -> unit
