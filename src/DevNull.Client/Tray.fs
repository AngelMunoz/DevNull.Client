module DevNull.Client.Tray

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Platform
open Navs
open DevNull.Client.Navigation

/// Brings the window back: restore first, then activate.
let private raiseWindow(window: Window) : unit =
  if window.WindowState = WindowState.Minimized then
    window.WindowState <- WindowState.Normal

  window.Activate()

let private navigate
  (window: Window, router: IRouter<Control>, url: string)
  : unit =
  raiseWindow window
  go(router, url, ignore)

let private menuItem(label: string, onClick: unit -> unit) : NativeMenuItem =
  let entry = NativeMenuItem(Header = label)

  entry.Click.Add(fun _ -> onClick())

  entry

let install
  (window: Window, router: IRouter<Control>, unread: IObservable<int>)
  : unit =
  let icon =
    new TrayIcon(
      Icon =
        WindowIcon(
          AssetLoader.Open(Uri "avares://DevNull.Client/Assets/app.ico")
        )
    )

  let menu = NativeMenu()

  menu.Add(
    menuItem(
      "Open /dev/null",
      fun () -> navigate(window, router, "/log/latest")
    )
  )

  menu.Add(
    menuItem(
      "Notifications",
      fun () -> navigate(window, router, "/notifications")
    )
  )

  menu.Add(menuItem("Saved", fun () -> navigate(window, router, "/saved")))
  menu.Add(NativeMenuItemSeparator())
  menu.Add(menuItem("Quit", fun () -> window.Close()))

  icon.Menu <- menu

  icon.Clicked.Add(fun _ -> raiseWindow window)

  unread
  |> Observable.subscribe(fun count ->
    icon.ToolTipText <-
      if count = 0 then
        "/dev/null"
      else
        $"/dev/null · {count} unread")
  |> ignore

  match Application.Current with
  | null -> ()
  | app ->
    let icons = TrayIcons()
    icons.Add icon
    TrayIcon.SetIcons(app, icons)
