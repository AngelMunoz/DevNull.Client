module DevNull.Client.Components.ComposerKeys

open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Threading
open IcedTasks
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Commit

[<Struct>]
type Commit =
  | ControlOrMeta
  | PlainOrControl

[<Struct>]
type Props = {
  Body: TextBox
  Attachments: IAttachmentSources
  Commit: Commit
  OnCommit: unit -> unit
  OnStaged: ImageAttachment -> unit
  OnEscape: (unit -> unit) voption
}

/// The platform's paste gesture.
let pasteMatches(args: KeyEventArgs) : bool =
  match Application.Current with
  | null -> false
  | app ->
    match app.PlatformSettings with
    | null -> false
    | settings ->
      settings.HotkeyConfiguration.Paste
      |> Seq.exists(fun gesture -> gesture.Matches args)

/// The Enter keys the mode commits on.
let commits(props: Props, args: KeyEventArgs) : bool =
  if args.Key <> Key.Enter then
    false
  else
    match props.Commit with
    | ControlOrMeta ->
      args.KeyModifiers &&& (KeyModifiers.Control ||| KeyModifiers.Meta)
      <> KeyModifiers.None
    | PlainOrControl ->
      args.KeyModifiers = KeyModifiers.None
      || args.KeyModifiers.HasFlag KeyModifiers.Control

/// Wire the paste gesture, the commit keys, and the optional escape.
let attach(props: Props) : unit =
  props.Body.AddHandler(
    InputElement.KeyDownEvent,
    (fun _ args ->
      if commits(props, args) then
        args.Handled <- true
        props.OnCommit()
      elif args.Key = Key.Escape then
        props.OnEscape
        |> ValueOption.iter(fun escape ->
          args.Handled <- true
          escape())
      elif pasteMatches args then
        args.Handled <- true

        asyncEx {
          let! image = props.Attachments.ReadClipboardImage()

          match image with
          | Some staged ->
            Dispatcher.UIThread.Post(fun () -> props.OnStaged staged)
          | None ->
            let! text = props.Attachments.ReadClipboardText()

            match text with
            | Some pasted ->
              Dispatcher.UIThread.Post(fun () ->
                props.Body.SelectedText <- pasted)
            | None -> ()
        }
        |> Async.StartImmediate
      else
        ()),
    RoutingStrategies.Tunnel
  )
