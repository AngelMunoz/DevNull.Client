module DevNull.Client.Components.SchedulePicker

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Core.ApiTypes

[<Struct>]
type Pick = { Day: int; Hour: int; Minute: int }

[<Struct>]
type Props = {
  Now: DateTimeOffset
  Scheduling: IObservable<bool>
  Picked: IObservable<Pick voption>
  OnDayPicked: int -> unit
  OnHourPicked: int -> unit
  OnMinutePicked: int -> unit
  OnOpenQueue: (unit -> unit) voption
}

// The labels of the day list: today, tomorrow, then the calendar
// dates of the window the server allows.
let dayLabels(now: DateTimeOffset) : string array = [|
  for day in 0 .. ScheduledMoments.maxDays ->
    match day with
    | 0 -> "Today"
    | 1 -> "Tomorrow"
    | _ -> now.DateTime.AddDays(float day).ToString("MMM d")
|]

// The hour labels, two digits, twenty-four hours.
let hourLabels: string array = [| for hour in 0..23 -> hour.ToString("00") |]

// The minute labels, one per five-minute step.
let minuteLabels: string array = [|
  for step in 0..11 -> (step * 5).ToString("00")
|]

// The day select.
let dayCombo(props: Props) : Control =
  ComboBox()
    .Name("ScheduleDay")
    .MinWidth(128.0)
    .MinHeight(32.0)
    .ItemsSource(dayLabels props.Now)
    .SelectedIndex(
      props.Picked
      |> Observable.map(fun picked ->
        picked |> ValueOption.map _.Day |> ValueOption.defaultValue -1)
      |> Observable.distinctUntilChanged
    )
    .OnSelectionChangedHandler(fun picker _ ->
      if picker.SelectedIndex >= 0 then
        props.OnDayPicked picker.SelectedIndex)

// The hour select, off until a day is picked.
let hourCombo(props: Props) : Control =
  ComboBox()
    .Name("ScheduleHour")
    .MinWidth(72.0)
    .MinHeight(32.0)
    .ItemsSource(hourLabels)
    .IsEnabled(props.Picked |> Observable.map(fun picked -> picked.IsSome))
    .SelectedIndex(
      props.Picked
      |> Observable.map(fun picked ->
        picked |> ValueOption.map _.Hour |> ValueOption.defaultValue -1)
      |> Observable.distinctUntilChanged
    )
    .OnSelectionChangedHandler(fun picker _ ->
      if picker.SelectedIndex >= 0 then
        props.OnHourPicked picker.SelectedIndex)

// The minute select, off until a day is picked.
let minuteCombo(props: Props) : Control =
  ComboBox()
    .Name("ScheduleMinute")
    .MinWidth(72.0)
    .MinHeight(32.0)
    .ItemsSource(minuteLabels)
    .IsEnabled(props.Picked |> Observable.map(fun picked -> picked.IsSome))
    .SelectedIndex(
      props.Picked
      |> Observable.map(fun picked ->
        picked
        |> ValueOption.map(fun pick -> pick.Minute / 5)
        |> ValueOption.defaultValue -1)
      |> Observable.distinctUntilChanged
    )
    .OnSelectionChangedHandler(fun picker _ ->
      if picker.SelectedIndex >= 0 then
        props.OnMinutePicked(picker.SelectedIndex * 5))

// The three selects on one row.
let pickerRow(props: Props) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(8.0)
    .Children(
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Text("Submit at")
        .VerticalAlignment(VerticalAlignment.Center),
      dayCombo props,
      hourCombo props,
      TextBlock().Classes(Theme.name Theme.Muted).Text(":"),
      minuteCombo props
    )

// The link into the scheduled queue, absent when the host has none.
let queueLink(props: Props) : Control =
  match props.OnOpenQueue with
  | ValueSome openQueue ->
    Button()
      .Name("ScheduledCommitsLink")
      .Classes(Theme.name Theme.GhostButton, Theme.name Theme.FgAccent)
      .Padding(6.0, 2.0, 6.0, 2.0)
      .HorizontalAlignment(HorizontalAlignment.Left)
      .Content("Scheduled commits →")
      .OnClickHandler(fun _ _ -> openQueue())
  | ValueNone -> Panel()

let view(props: Props) : Control =
  let help =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .FontSize(12.0)
      .TextWrapping(TextWrapping.Wrap)
      .Text(
        $"Within {ScheduledMoments.maxDays} days. Private until submitted; then submitted for review, not published automatically."
      )

  Border()
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(1.0))
    .CornerRadius(8.0)
    .Padding(12.0)
    .Child(
      StackPanel().Spacing(8.0).Children(pickerRow props, help, queueLink props)
    )
    .IsVisible(props.Scheduling)
