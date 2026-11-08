module DevNull.Client.Components.ReplyPicker

open System
open System.Reactive.Subjects
open Avalonia
open Avalonia.Automation
open Avalonia.Controls
open Avalonia.Controls.Documents
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Icons
open DevNull.Client.Theme

/// One person a reply may reach, as the host renders it: a flat,
/// render-ready row.
type ParticipantRow = {
  Id: string
  Handle: string
  IsAuthor: bool
}

[<Struct>]
type Props = {
  Rows: ParticipantRow list
  Excluded: IObservable<string list>
  OnToggle: string -> unit
}

[<Struct>]
type View = {
  Trigger: Control voption
  Layer: Control
}

module Parts =

  // Whether the reply still reaches the row: every row but the author
  // leaves while its handle sits in the exclusions.
  let included(props: Props, row: ParticipantRow) : IObservable<bool> =
    props.Excluded
    |> Observable.map(fun excluded -> not(List.contains row.Handle excluded))
    |> Observable.distinctUntilChanged

  // The handles the reply still reaches, in the rows' own order: every
  // row but the author that the exclusions leave in.
  let addressed(props: Props, excluded: string list) : ParticipantRow list =
    props.Rows
    |> List.filter(fun row ->
      not row.IsAuthor && not(List.contains row.Handle excluded))

  // The line the trigger carries: "Replying to @a, @b and @c" with the
  // handles in the accent foreground, or the site's quiet sentence
  // when every other row is left out.
  let audienceLine(props: Props, excluded: string list) : Control =
    let line = TextBlock().TextWrapping(TextWrapping.Wrap)

    match addressed(props, excluded) with
    | [] ->
      line.Classes(Theme.name Theme.Muted) |> ignore
      line.Text("Replying to no additional participants") |> ignore
    | people ->
      let inlines = InlineCollection()
      inlines.Add(Run "Replying to ") |> ignore

      let at index =
        let person = people.[index]

        let handle =
          TextBlock()
            .Classes(Theme.name Theme.FgAccent)
            .Text("@" + person.Handle)

        InlineUIContainer(
          Child = handle,
          BaselineAlignment = BaselineAlignment.Center
        )
        :> Inline

      let between index =
        Run(if index = people.Length - 1 then " and " else ", ") :> Inline

      for index in 0 .. people.Length - 1 do
        if index > 0 then
          inlines.Add(between index) |> ignore

        inlines.Add(at index) |> ignore

      line.Inlines <- inlines

    line :> Control

  // One row of the dialog: the handle, the author's caption, and the
  // check while the reply reaches it. A click flips the row in or
  // out; the author's row stays disabled with its check on.
  let participantRow(props: Props, index: int, row: ParticipantRow) : Control =
    let mark = Icons.icon Glyph.Check

    mark.VerticalAlignment(VerticalAlignment.Center) |> ignore

    if not row.IsAuthor then
      mark.IsVisible(included(props, row)) |> ignore

    let columns = Grid()

    columns.ColumnDefinitions <- ColumnDefinitions("*,Auto")

    let identity =
      StackPanel()
        .Spacing(2.0)
        .Children(
          TextBlock().Classes(Theme.name Theme.Subtitle).Text("@" + row.Handle),
          TextBlock()
            .Classes(Theme.name Theme.Muted)
            .FontSize(12.0)
            .Text("Commit author")
            .IsVisible(row.IsAuthor)
        )

    let _ = columns.Children(identity.Column(0), mark.Column(1))

    Button()
      .Name($"ReplyParticipant{index}")
      .Classes(Theme.name Theme.GhostButton)
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .IsEnabled(not row.IsAuthor)
      .Content(columns)
      .OnClickHandler(fun _ args ->
        args.Handled <- true

        if not row.IsAuthor then
          props.OnToggle row.Handle)

  // The dialog's card: the title, the site's description, one row per
  // participant, and the Done button that closes.
  let dialog(props: Props, close: unit -> unit) : Control =
    let rows =
      StackPanel()
        .Spacing(2.0)
        .Children(
          [|
            for index, row in List.indexed props.Rows ->
              participantRow(props, index, row)
          |]
        )

    let footer =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .HorizontalAlignment(HorizontalAlignment.Right)
        .Children(
          Button()
            .Name("ReplyPickerDone")
            .Classes(Theme.name Theme.AccentButton)
            .Content("Done")
            .OnClickHandler(fun _ args ->
              args.Handled <- true
              close())
        )

    Border()
      .Name("ReplyPickerDialog")
      .Classes(Theme.name Theme.Card)
      .MaxWidth(520.0)
      .Child(
        StackPanel()
          .Spacing(12.0)
          .Children(
            TextBlock().Classes(Theme.name Theme.Title).Text("Replying to"),
            TextBlock()
              .Classes(Theme.name Theme.Muted)
              .TextWrapping(TextWrapping.Wrap)
              .Text(
                "Choose who to include. The commit author stays selected. "
                + "Anyone you mention in your reply will also be notified."
              ),
            ScrollViewer()
              .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
              .MaxHeight(320.0)
              .Content(rows),
            footer
          )
      )

let view(props: Props) : View =
  // The trigger stands only while a row besides the author exists; an
  // audience-less reply mounts no named control at all, so a page
  // never mixes two pickers' names.
  if props.Rows |> List.exists(fun row -> not row.IsAuthor) then
    // The dialog stands while the trigger's subject holds true; the
    // subject outlives the controls the layer builds on each open edge.
    let openLine = new BehaviorSubject<bool>(false)

    let trigger =
      Button()
        .Name("ReplyingToLine")
        .Classes(Theme.name Theme.GhostButton)
        .Padding(0.0)
        .HorizontalAlignment(HorizontalAlignment.Left)
        .Content(
          ContentControl()
            .Content(
              props.Excluded
              |> Observable.map(fun excluded ->
                Parts.audienceLine(props, excluded))
              |> _.ToBinding()
            )
        )
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          openLine.OnNext true)

    AutomationProperties.SetName(trigger, "Choose reply participants")

    let layer =
      DialogChrome.layer {
        Open = openLine :> IObservable<bool>
        Content = fun () -> Parts.dialog(props, fun () -> openLine.OnNext false)
        Scrim = true
        OnScrimClick = ValueSome(fun () -> openLine.OnNext false)
        Place = DialogChrome.Center
      }

    {
      Trigger = ValueSome(trigger :> Control)
      Layer = layer
    }
  else
    {
      Trigger = ValueNone
      Layer = Panel() :> Control
    }
