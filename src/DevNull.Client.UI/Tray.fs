module DevNull.Client.Tray

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Platform
open Microsoft.Extensions.Logging
open DevNull.Client.Layouts

[<Struct>]
type TrayProps = {
  Window: Window
  Intents: LayoutIntent -> unit
  Unread: IObservable<int>
  Quit: unit -> unit
  Log: ILogger
}

module Helpers =

  let raiseWindow(window: Window) : unit =
    window.Show()

    if window.WindowState = WindowState.Minimized then
      window.WindowState <- WindowState.Normal

    window.Activate()

  // The tray asks the active layout: the window stands on screen
  // first, then the layout answers the intent its own way.
  let ask(props: TrayProps, intent: LayoutIntent) : unit =
    raiseWindow props.Window

    props.Log.LogDebug("The tray asked for {Intent}.", intent)

    props.Intents intent

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

  menu.Add(
    Helpers.menuItem(
      "Open /dev/null",
      fun () ->
        props.Log.LogDebug("The tray raised the window.")
        Helpers.raiseWindow window
    )
  )

  menu.Add(
    Helpers.menuItem(
      "Notifications",
      fun () -> Helpers.ask(props, LayoutIntent.OpenNotifications)
    )
  )

  menu.Add(
    Helpers.menuItem(
      "Saved",
      fun () -> Helpers.ask(props, LayoutIntent.OpenSaved)
    )
  )

  menu.Add(NativeMenuItemSeparator())
  menu.Add(Helpers.menuItem("Quit", props.Quit))

  icon.Menu <- menu

  icon.Clicked.Add(fun _ ->
    props.Log.LogDebug("The tray raised the window.")
    Helpers.raiseWindow window)

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
