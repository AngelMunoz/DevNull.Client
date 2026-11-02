module DevNull.Client.Components.PollEditor

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Icons

[<Struct>]
type Draft = {
  Options: string list
  Duration: PollDuration
}

[<Struct>]
type Props = {
  Draft: IObservable<Draft option>
  OnOptionChanged: int * string -> unit
  OnOptionAdded: unit -> unit
  OnOptionRemoved: int -> unit
  OnDurationPicked: PollDuration -> unit
  OnClosed: unit -> unit
}

/// The option as the draft holds it, empty past the draft's end.
let optionText(draft: Draft option, index: int) : string =
  draft
  |> Option.bind(fun poll -> List.tryItem index poll.Options)
  |> Option.defaultValue ""

/// One option box: the text rides the draft the way the body rides
/// the model, so the structure rebuilds only when the option count
/// moves. The site caps an option at 50 characters.
let optionBox(props: Props, index: int) : Control =
  TextBox()
    .Name($"PollOption{index}")
    .PlaceholderText($"Option {index + 1}")
    .MinHeight(36.0)
    .FontSize(15.0)
    .MaxLength(50)
    .Text(
      props.Draft
      |> Observable.map(fun draft -> optionText(draft, index))
      |> Observable.distinctUntilChanged
      |> _.ToBinding()
    )
    .OnTextChangedHandler(fun box _ ->
      let text = box.Text |> Option.ofObj |> Option.defaultValue ""

      props.OnOptionChanged(index, text))

/// The row of one option: its box and, past the first two, the remove
/// button the site draws as an X.
let optionRow(props: Props, index: int) : Control =
  let remove =
    Button()
      .Name($"PollOptionRemove{index}")
      .Classes(Theme.name Theme.GhostButton)
      .Width(28.0)
      .Height(28.0)
      .Padding(Thickness(0.0))
      .Margin(8.0, 0.0, 0.0, 0.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Content(Icons.iconAt(Glyph.Close, 16.0))
      .IsVisible(
        props.Draft
        |> Observable.map(fun draft ->
          draft
          |> Option.map(fun poll -> List.length poll.Options > 2)
          |> Option.defaultValue false)
      )
      .DockRight()
      .OnClickHandler(fun _ _ -> props.OnOptionRemoved index)

  ToolTip.SetTip(remove, $"Remove option {index + 1}")

  DockPanel().LastChildFill(true).Children(remove, optionBox(props, index))

/// The poll length picker: the site's select, with its four
/// durations, and the draft's pick as the selected item.
let lengthPicker(props: Props) : Control =
  let labels = [| "1 hour"; "1 day"; "3 days"; "7 days" |]

  let indexOf(duration: PollDuration) : int =
    match duration with
    | OneHour -> 0
    | OneDay -> 1
    | ThreeDays -> 2
    | SevenDays -> 3

  let durationAt(index: int) : PollDuration =
    match index with
    | 0 -> OneHour
    | 1 -> OneDay
    | 2 -> ThreeDays
    | _ -> SevenDays

  ComboBox()
    .Name("PollLength")
    .Width(128.0)
    .MinHeight(32.0)
    .ItemsSource(labels)
    .SelectedIndex(
      props.Draft
      |> Observable.map(fun draft ->
        draft
        |> Option.map(fun poll -> indexOf poll.Duration)
        |> Option.defaultValue 1)
      |> Observable.distinctUntilChanged
    )
    .OnSelectionChangedHandler(fun picker _ ->
      if picker.SelectedIndex >= 0 then
        props.OnDurationPicked(durationAt picker.SelectedIndex))

/// The builder for the live option count; the boxes keep their carets
/// while typing because the structure does not rebuild for text.
let built(props: Props, count: int) : Control =
  match count with
  | 0 -> Panel() :> Control
  | _ ->
    let optionRows: Control array = [|
      for index in 0 .. count - 1 do
        optionRow(props, index)
    |]

    let add =
      Button()
        .Name("PollOptionAdd")
        .Classes(Theme.name Theme.GhostButton, Theme.name Theme.FgAccent)
        .Padding(6.0, 2.0, 6.0, 2.0)
        .HorizontalAlignment(HorizontalAlignment.Left)
        .Content(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(6.0)
            .Children(
              Icons.iconAt(Glyph.Plus, 16.0),
              TextBlock().Text("Add option")
            )
        )
        .IsVisible(
          props.Draft
          |> Observable.map(fun draft ->
            draft
            |> Option.map(fun poll -> List.length poll.Options < 4)
            |> Option.defaultValue false)
        )
        .OnClickHandler(fun _ _ -> props.OnOptionAdded())

    let length =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Muted)
            .Text("Poll length")
            .VerticalAlignment(VerticalAlignment.Center),
          lengthPicker props
        )

    let remove =
      Button()
        .Name("PollClose")
        .Classes(Theme.name Theme.GhostButton, Theme.name Theme.PollRemove)
        .Padding(6.0, 2.0, 6.0, 2.0)
        .Content("Remove poll")
        .DockRight()
        .OnClickHandler(fun _ _ -> props.OnClosed())

    let footer =
      Border()
        .Classes(Theme.name Theme.Hairline)
        .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
        .Padding(Thickness(0.0, 12.0, 0.0, 0.0))
        .Child(DockPanel().LastChildFill(true).Children(remove, length))

    let children: Control array = [| yield! optionRows; add; footer |]

    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .Padding(12.0)
      .Child(StackPanel().Spacing(8.0).Children(children))
    :> Control

/// The poll builder under the body.
let view(props: Props) : Control =
  ContentControl()
    .Content(
      props.Draft
      |> Observable.map(fun draft ->
        draft
        |> Option.map(fun poll -> List.length poll.Options)
        |> Option.defaultValue 0)
      |> Observable.distinctUntilChanged
      |> Observable.map(fun count -> built(props, count))
      |> _.ToBinding()
    )
