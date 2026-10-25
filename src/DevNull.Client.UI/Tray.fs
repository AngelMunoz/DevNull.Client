module DevNull.Client.Tray

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Platform
open Navs
open DevNull.Client.Navigation

/// Brings the window back. Close-to-tray hides it, and `Activate`
/// alone never shows a hidden window: `Show` runs first, always.
let raiseWindow(window: Window) : unit =
  window.Show()

  if window.WindowState = WindowState.Minimized then
    window.WindowState <- WindowState.Normal

  window.Activate()

let navigate(window: Window, router: IRouter<Control>, url: string) : unit =
  raiseWindow window
  go(router, url, ignore)

let menuItem(label: string, onClick: unit -> unit) : NativeMenuItem =
  let entry = NativeMenuItem(Header = label)

  entry.Click.Add(fun _ -> onClick())

  entry

/// Everything the tray needs: the window it raises, the router it
/// navigates, the unread count it badges, and the real quit.
[<Struct>]
type TrayProps = {
  Window: Window
  Router: IRouter<Control>
  Unread: IObservable<int>
  Quit: unit -> unit
}

let install(props: TrayProps) : unit =
  let window = props.Window

  let icon =
    new TrayIcon(
      Icon =
        WindowIcon(
          AssetLoader.Open(Uri "avares://DevNull.Client.UI/Assets/app.ico")
        )
    )

  let menu = NativeMenu()

  menu.Add(
    menuItem(
      "Open /dev/null",
      fun () -> navigate(window, props.Router, "/log/latest")
    )
  )

  menu.Add(
    menuItem(
      "Notifications",
      fun () -> navigate(window, props.Router, "/notifications")
    )
  )

  menu.Add(
    menuItem("Saved", fun () -> navigate(window, props.Router, "/saved"))
  )

  menu.Add(NativeMenuItemSeparator())
  menu.Add(menuItem("Quit", props.Quit))

  icon.Menu <- menu

  icon.Clicked.Add(fun _ -> raiseWindow window)

  props.Unread
  |> Observable.add(fun count ->
    icon.ToolTipText <-
      if count = 0 then
        "/dev/null"
      else
        $"/dev/null · {count} unread")

  match Application.Current with
  | null -> ()
  | app ->
    let icons = TrayIcons()
    icons.Add icon
    TrayIcon.SetIcons(app, icons)
