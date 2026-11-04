module DevNull.Client.Components.ThreadChrome

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Components.ThreadMessage
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Icons
open DevNull.Client.RemotesState
open DevNull.Client.Theme

[<Struct>]
type AccessBarProps = {
  Partner: User
  Access: PushAccess
  OnSettle: User * bool -> unit
}

[<Struct>]
type PinnedBannerProps = {
  Thread: IObservable<ThreadState>
  OnOpen: unit -> unit
}

[<Struct>]
type PinnedPanelProps = {
  Thread: IObservable<ThreadState>
  Shown: IObservable<bool>
  OnClose: unit -> unit
}

[<Struct>]
type PinDialogProps = {
  Open: IObservable<bool>
  Busy: IObservable<bool>
  OnConfirm: bool -> unit
  OnCancel: unit -> unit
}

[<Struct>]
type ConfirmProps = {
  Label: string
  Title: string
  Words: string
  Verb: string
  OnConfirm: unit -> unit
  Busy: IObservable<bool>
}

// One pinned summary's display text: the count, the latest author,
// and the snippet.
[<Struct>]
type BannerInfo = {
  Count: int
  Author: string
  Snippet: string
}

// One quiet word in the access bar's mono voice.
let accessNote(words: string) : Control =
  TextBlock()
    .Classes(Theme.name Theme.Mono)
    .FontSize(13.0)
    .Text(words)
    .TextWrapping(TextWrapping.Wrap)

// One settle pick: the drop or merge the request offers.
let settlePick
  (label: string, klass: StyleClass, onSettle: unit -> unit)
  : Control =
  Button()
    .Classes(Theme.name klass)
    .Content(label)
    .OnClickHandler(fun _ args ->
      args.Handled <- true
      onSettle())

let accessBar(props: AccessBarProps) : Control =
  match props.Access with
  | PushRequest ->
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(8.0)
      .Children(
        TextBlock()
          .Text(
            $"@{props.Partner.Handle} wants to push to you. Merge to reply, or drop it. They won't be told."
          )
          .TextWrapping(TextWrapping.Wrap)
          .Classes(Theme.name Theme.Muted),
        settlePick(
          "Drop",
          Theme.GhostButton,
          fun () -> props.OnSettle(props.Partner, false)
        ),
        settlePick(
          "Merge",
          Theme.AccentButton,
          fun () -> props.OnSettle(props.Partner, true)
        )
      )
  | PushWaiting ->
    accessNote(
      $"pushed. waiting for @{props.Partner.Handle} to merge your request"
    )
  | PushRestricted ->
    accessNote(
      $"@{props.Partner.Handle} doesn't follow you, so you can't push to them yet"
    )
  | PushClosed
  | UnknownAccess _ ->
    accessNote($"@{props.Partner.Handle} isn't taking remotes")
  | PushOpen -> TextBlock()

let notice(notice: IObservable<string option>) : Control =
  TextBlock()
    .Classes(Theme.name Theme.Mono)
    .FontSize(11.0)
    .Text(
      notice |> Observable.map(fun current -> current |> Option.defaultValue "")
    )
    .TextWrapping(TextWrapping.Wrap)
    .IsVisible(notice |> Observable.map Option.isSome)

// One pinned message as a snippet: its words without links, or the
// word for what a bodyless message carries.
let snippetOf(message: Message) : string =
  match summaryOf message with
  | Text words -> words
  | Voice -> "voice note"
  | Images _ -> "image"
  | Empty -> ""

// One pinned message's author as the banner and panel name them:
// the member's own name, the user id only when the member is gone.
let authorOf(current: ThreadState, message: Message) : string =
  current.Members
  |> List.tryFind(fun member' -> member'.User.Id = message.UserId)
  |> Option.map(fun member' -> member'.User.Name)
  |> Option.defaultValue message.UserId

// One pinned summary as the banner draws it: the pin glyph, the
// count, the latest author, and the snippet.
let bannerLine(info: BannerInfo) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(6.0)
    .Children(
      Icons.iconAt(Glyph.Pin, 12.0),
      TextBlock().Classes(Theme.name Theme.Mono).Text($"pinned · {info.Count}"),
      TextBlock().Classes(Theme.name Theme.Mono).Text(info.Author),
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Text(info.Snippet)
        .TextTrimming(TextTrimming.CharacterEllipsis)
        .MaxWidth(320.0)
    )

// The banner's one button: the summary row, and the click that
// opens the panel.
let bannerButton(onOpen: unit -> unit, info: BannerInfo) : Control =
  Button()
    .Name("RemotesPinnedBanner")
    .Classes(Theme.name Theme.GhostButton)
    .Padding(6.0, 2.0, 6.0, 2.0)
    .FontSize(11.0)
    .HorizontalAlignment(HorizontalAlignment.Left)
    .Content(bannerLine info)
    .OnClickHandler(fun _ args ->
      args.Handled <- true
      onOpen())

let pinnedBanner(props: PinnedBannerProps) : Control =
  let info =
    props.Thread
    |> Observable.map(fun current ->
      let pins = current.Pinned |> Option.defaultValue []

      pins
      |> List.tryHead
      |> Option.map(fun latest -> {
        Count = List.length pins
        Author = authorOf(current, latest)
        Snippet = snippetOf latest
      }))
    |> Observable.distinctUntilChanged

  ContentControl()
    .Content(
      info
      |> Observable.map(function
        | Some info -> bannerButton(props.OnOpen, info)
        | None -> null)
      |> _.ToBinding()
    )

// One pinned message in the panel: its author over its snippet.
let pinRow(author: string, snippet: string) : Control =
  StackPanel()
    .Spacing(1.0)
    .Children(
      TextBlock().Classes(Theme.name Theme.Mono).FontSize(11.0).Text(author),
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .FontSize(12.0)
        .Text(snippet)
        .TextWrapping(TextWrapping.Wrap)
    )

// The panel's card: the count under the title, the pinned rows in
// their own scroll, and the close control.
let pinnedCard
  (count: int, rows: (string * string) list, onClose: unit -> unit)
  : Control =
  Border()
    .Name("RemotesPinnedPanel")
    .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
    .BorderThickness(1.0)
    .CornerRadius(10.0)
    .Padding(12.0)
    .Margin(12.0)
    .MaxHeight(360.0)
    .VerticalAlignment(VerticalAlignment.Top)
    .Child(
      StackPanel()
        .Spacing(6.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .FontSize(13.0)
            .Text("Pinned messages"),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .FontSize(11.0)
            .Text($"pinned · {count}"),
          ScrollViewer()
            .MaxHeight(240.0)
            .VerticalScrollBarVisibility(ScrollBarVisibility.Auto)
            .Content(
              StackPanel().Spacing(8.0).Children [|
                for author, snippet in rows -> pinRow(author, snippet)
              |]
            ),
          Button()
            .Name("ClosePinnedMessages")
            .Classes(Theme.name Theme.GhostButton)
            .Padding(6.0, 2.0, 6.0, 2.0)
            .FontSize(12.0)
            .HorizontalAlignment(HorizontalAlignment.Left)
            .Content("Close pinned messages")
            .OnClickHandler(fun _ args ->
              args.Handled <- true
              onClose())
        )
    )

let pinnedPanel(props: PinnedPanelProps) : Control =
  let rows =
    props.Thread
    |> Observable.map(fun current ->
      let pins = current.Pinned |> Option.defaultValue []

      (List.length pins,
       [
         for message in pins -> (authorOf(current, message), snippetOf message)
       ]))
    |> Observable.distinctUntilChanged

  ContentControl()
    .IsVisible(props.Shown)
    .Content(
      rows
      |> Observable.map(fun (count, rows) ->
        pinnedCard(count, rows, props.OnClose))
      |> _.ToBinding()
    )

// The first-pin choice: the site's never-ask-again box.
let pinChoice() : CheckBox =
  CheckBox().Content("Don't show this again").FontSize(12.0)

// The choice's current answer.
let pinChoiceValue(keep: CheckBox) : bool =
  keep.IsChecked |> Option.ofNullable |> Option.defaultValue false

// The dialog's two answers: cancel, and the pin that carries the
// choice.
let pinButtons(props: PinDialogProps, keep: CheckBox) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(8.0)
    .HorizontalAlignment(HorizontalAlignment.Right)
    .Children(
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(8.0, 2.0, 8.0, 2.0)
        .FontSize(12.0)
        .Content("Cancel")
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          props.OnCancel()),
      Button()
        .Name("ConfirmPin")
        .Classes(Theme.name Theme.AccentButton)
        .Padding(8.0, 2.0, 8.0, 2.0)
        .FontSize(12.0)
        .Content("Pin message")
        .IsEnabled(props.Busy |> Observable.map not)
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          props.OnConfirm(pinChoiceValue keep))
    )

// The dialog's card: the site's question, the choice, and the two
// answers.
let pinCard(props: PinDialogProps) : Control =
  let keep = pinChoice()

  Border()
    .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
    .BorderThickness(1.0)
    .CornerRadius(10.0)
    .Padding(14.0)
    .MaxWidth(380.0)
    .HorizontalAlignment(HorizontalAlignment.Center)
    .VerticalAlignment(VerticalAlignment.Center)
    .Child(
      StackPanel()
        .Spacing(8.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .FontSize(13.0)
            .Text("Pin for the whole org?"),
          TextBlock()
            .Classes(Theme.name Theme.Muted)
            .FontSize(12.0)
            .Text(
              "Everyone in the org sees pinned messages, including people added without Share history, who can't read anything pushed before they joined."
            )
            .TextWrapping(TextWrapping.Wrap),
          keep,
          pinButtons(props, keep)
        )
    )

// The scrim under the dialog's card.
let pinScrim(card: Control) : Control =
  Grid()
    .Children(
      Border().Name("PinScrim").Classes(Theme.name Theme.ScrimBack),
      card
    )

let pinDialog(props: PinDialogProps) : Control =
  ContentControl()
    .Content(
      props.Open
      |> Observable.map(function
        | false -> TextBlock() :> Control
        | true -> pinScrim(pinCard props))
      |> _.ToBinding()
    )
    .IsVisible(props.Open)

let menuPick(label: string, onPick: unit -> unit) : Control =
  Button()
    .Classes(Theme.name Theme.GhostButton)
    .Padding(6.0, 2.0, 6.0, 2.0)
    .FontSize(12.0)
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .HorizontalContentAlignment(HorizontalAlignment.Left)
    .Content(label)
    .OnClickHandler(fun _ args ->
      args.Handled <- true
      onPick())

// The destructive card the flyout carries: the site's question,
// the consequences, and the two answers.
let confirmCard(props: ConfirmProps, close: unit -> unit) : Control =
  Border()
    .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
    .BorderThickness(1.0)
    .CornerRadius(6.0)
    .Padding(10.0)
    .MaxWidth(300.0)
    .Child(
      StackPanel()
        .Spacing(6.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .FontSize(13.0)
            .Text(props.Title),
          TextBlock()
            .Classes(Theme.name Theme.Muted)
            .FontSize(12.0)
            .TextWrapping(TextWrapping.Wrap)
            .Text(props.Words),
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .HorizontalAlignment(HorizontalAlignment.Right)
            .Children(
              Button()
                .Classes(Theme.name Theme.GhostButton)
                .Padding(8.0, 2.0, 8.0, 2.0)
                .FontSize(12.0)
                .Content("Cancel")
                .OnClickHandler(fun _ args ->
                  args.Handled <- true
                  close()),
              Button()
                .Classes(Theme.name Theme.DangerButton)
                .Padding(8.0, 2.0, 8.0, 2.0)
                .FontSize(12.0)
                .Content(props.Verb)
                .IsEnabled(props.Busy |> Observable.map not)
                .OnClickHandler(fun _ args ->
                  args.Handled <- true
                  close()
                  props.OnConfirm())
            )
        )
    )

// The pick that opens the destructive card.
let confirmTrigger(label: string, onOpen: unit -> unit) : Control =
  Button()
    .Classes(Theme.name Theme.GhostButton)
    .Padding(6.0, 2.0, 6.0, 2.0)
    .FontSize(12.0)
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .HorizontalContentAlignment(HorizontalAlignment.Left)
    .Content(label)
    .OnClickHandler(fun _ args ->
      args.Handled <- true
      onOpen())

let confirmPick(props: ConfirmProps) : Control =
  let flyout = Popup()

  let trigger =
    confirmTrigger(props.Label, fun () -> flyout.IsOpen <- not flyout.IsOpen)

  let placed =
    flyout
      .Name("ConfirmFlyout")
      .PlacementTarget(trigger)
      .Placement(PlacementMode.BottomEdgeAlignedLeft)
      .Child(confirmCard(props, fun () -> flyout.IsOpen <- false))

  Panel().Children(trigger, placed)
