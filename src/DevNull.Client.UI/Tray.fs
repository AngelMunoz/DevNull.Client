module DevNull.Client.Tray

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Platform
open Microsoft.Extensions.Logging
open Navs
open DevNull.Client.Navigation

/// Brings the window back. Close-to-tray hides it, and `Activate`
/// alone never shows a hidden window: `Show` runs first, always.
let raiseWindow(window: Window) : unit =
  window.Show()

  if window.WindowState = WindowState.Minimized then
    window.WindowState <- WindowState.Normal

  window.Activate()

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

let navigate(props: TrayProps, url: string) : unit =
  raiseWindow props.Window

  props.Log.LogDebug("The tray navigates to {Url}.", url)
  go(props.Router, url, fun error -> props.Log.LogWarning("{Error}", error))

let menuItem(label: string, onClick: unit -> unit) : NativeMenuItem =
  let entry = NativeMenuItem(Header = label)

  entry.Click.Add(fun _ -> onClick())

  entry

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

  menu.Add(menuItem("Open /dev/null", fun () -> navigate(props, "/log/latest")))

  menu.Add(
    menuItem("Notifications", fun () -> navigate(props, "/notifications"))
  )

  menu.Add(menuItem("Saved", fun () -> navigate(props, "/saved")))

  menu.Add(NativeMenuItemSeparator())
  menu.Add(menuItem("Quit", props.Quit))

  icon.Menu <- menu

  icon.Clicked.Add(fun _ ->
    props.Log.LogDebug("The tray raised the window.")
    raiseWindow window)

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
