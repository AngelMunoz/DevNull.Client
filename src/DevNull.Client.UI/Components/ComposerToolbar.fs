module DevNull.Client.Components.ComposerToolbar

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Core.Commit
open DevNull.Client.Errors
open DevNull.Client.Icons

[<Struct>]
type Props = {
  Attachments: IObservable<ImageAttachment list>
  Poll: IObservable<DevNull.Client.Components.PollEditor.Draft option>
  Busy: IObservable<bool>
  Scheduling: IObservable<bool>
  Picked: IObservable<DevNull.Client.Components.SchedulePicker.Pick voption>
  Error: IObservable<AppError option>
  OfferAttach: bool
  OfferExtras: bool
  OnAttach: unit -> unit
  OnPoll: unit -> unit
  OnSchedule: unit -> unit
  OnSubmit: unit -> unit
}

// Sets one control's tooltip and returns it, so the composition stays
// one expression.
let withTip(text: string, control: Control) : Control =
  ToolTip.SetTip(control, text)
  control

// The attach button: the image glyph, a live count, disabled past
// three staged files and while a poll is open — the server takes a
// poll or images, never both. A quote target hides it.
let attachButton(props: Props) : Control =
  match props.OfferAttach with
  | false -> Panel()
  | true ->
    let count =
      props.Attachments
      |> Observable.map(fun staged -> $"{List.length staged}/3")

    Button()
      .Name("AttachButton")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(6.0, 2.0, 6.0, 2.0)
      .Content(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(4.0)
          .Children(
            Icons.iconAt(Glyph.Image, 16.0),
            TextBlock().Classes(Theme.name Theme.Mono).Text(count)
          )
      )
      .IsEnabled(
        Observable.combineLatest props.Attachments props.Poll
        |> Observable.map(fun (staged, poll) ->
          List.length staged < 3 && poll.IsNone)
      )
      .OnClickHandler(fun _ _ -> props.OnAttach())
    :> Control

// The poll button: the site's icon-only entry that opens the builder.
// A poll rides a fresh commit with no images, so images disable it
// and a reply or quote hides it.
let pollButton(props: Props) : Control =
  let button =
    Button()
      .Name("PollButton")
      .Classes(Theme.name Theme.GhostButton)
      .Width(32.0)
      .Height(32.0)
      .Padding(Thickness(0.0))
      .Content(Icons.iconAt(Glyph.Poll, 20.0))
      .IsVisible(props.OfferExtras)
      .IsEnabled(
        Observable.combineLatest props.Attachments props.Poll
        |> Observable.map(fun (staged, poll) -> staged.IsEmpty && poll.IsNone)
      )
      .OnClickHandler(fun _ _ -> props.OnPoll())

  withTip("Add poll", button)

// The schedule button: the clock entry beside the poll button. A
// reply or quote hides it — the server takes a scheduled commit as
// neither.
let scheduleButton(props: Props) : Control =
  let button =
    Button()
      .Name("ScheduleButton")
      .Classes(Theme.name Theme.GhostButton)
      .Width(32.0)
      .Height(32.0)
      .Padding(Thickness(0.0))
      .Content(Icons.iconAt(Glyph.Clock, 20.0))
      .IsVisible(props.OfferExtras)
      .IsEnabled(props.Busy |> Observable.map not)
      .OnClickHandler(fun _ _ -> props.OnSchedule())

  withTip("Schedule for later", button)

// The commit button: its word follows the schedule mode, and it
// stays off while a request is out or no moment is picked.
let commitButton(props: Props) : Control =
  let label =
    props.Scheduling
    |> Observable.map(fun scheduling ->
      if scheduling then "Schedule commit" else "Commit")
    |> Observable.distinctUntilChanged

  let enabled =
    Observable.combineLatest
      (Observable.combineLatest props.Busy props.Scheduling)
      props.Picked
    |> Observable.map(fun ((busy, scheduling), picked) ->
      not busy && (not scheduling || picked.IsSome))

  Button()
    .Classes(Theme.name Theme.AccentButton)
    .Content(TextBlock().Text(label))
    .IsEnabled(enabled)
    .OnClickHandler(fun _ _ -> props.OnSubmit())
    .DockRight()

// The error line under the input.
let errorLine(props: Props) : Control =
  TextBlock()
    .TextWrapping(TextWrapping.Wrap)
    .Classes(Theme.name Theme.FgDanger)
    .Text(
      props.Error
      |> Observable.map(fun error ->
        error |> Option.map describe |> Option.defaultValue "")
    )
    .IsVisible(props.Error |> Observable.map Option.isSome)

let view(props: Props) : Control =
  DockPanel()
    .LastChildFill(true)
    .Children(
      commitButton props,
      attachButton props,
      pollButton props,
      scheduleButton props,
      errorLine props
    )
