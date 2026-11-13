module DevNull.Client.Editor.Explorer

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Core.ApiTypes

type Props = {
  Selected: LogKind
  Saved: int
  Queue: int
  QueueHint: string option
  Viewer: string
  OnSelectRef: LogKind -> unit
  OnOpenSaved: unit -> unit
  OnOpenQueue: unit -> unit
  OnOpenViewer: unit -> unit
}

let section(title: string) : Control =
  TextBlock()
    .Text(title)
    .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
    .Margin(0.0, 8.0, 0.0, 2.0)

let refRow(props: Props, log: LogKind) : Control =
  let selected = log = props.Selected

  let marker =
    TextBlock()
      .Text(if selected then "●" else "○")
      .Classes(
        Theme.name Theme.Mono,
        if selected then
          Theme.name Theme.FgAccent
        else
          Theme.name Theme.FgMuted
      )
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Center)
      .Column(0)

  let label =
    TextBlock()
      .Text(LogLabel.ofKind log)
      .VerticalAlignment(VerticalAlignment.Center)
      .Column(1)

  let head: Control =
    if selected then
      TextBlock()
        .Text("HEAD")
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
        .VerticalAlignment(VerticalAlignment.Center)
        .Padding(4, 0)
        .Column(2)
    else
      Panel().Column(2)

  let content =
    Grid(ColumnDefinitions = ColumnDefinitions("14,*,Auto"))
      .Children(marker, label, head)

  let button =
    if selected then
      Button().Classes(Theme.name Theme.NavItem, Theme.name Theme.NavItemActive)
    else
      Button().Classes(Theme.name Theme.NavItem)

  button
    .Name($"EditorExplorerRef-{LogLabel.ofKind log}")
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .MinHeight(28.0)
    .Margin(Thickness(10.0, 0.0, 0.0, 0.0))
    .Padding(Thickness(8.0, 4.0))
    .Content(content)
    .OnClickHandler(fun _ _ -> props.OnSelectRef log)

[<Struct>]
type CountRow = {
  Title: string
  Name: string
  Count: int
  OnOpen: unit -> unit
}

let countRow(row: CountRow) =
  let label =
    TextBlock()
      .Text(row.Title)
      .VerticalAlignment(VerticalAlignment.Center)
      .Column(0)

  let count =
    TextBlock()
      .Name($"ExplorerCount-{row.Name}")
      .Text(string row.Count)
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .VerticalAlignment(VerticalAlignment.Center)
      .Column(1)

  let content = Grid().Children(label, count)

  content.ColumnDefinitions <- ColumnDefinitions("*,Auto")

  Button()
    .Name($"ExplorerOpen-{row.Name}")
    .Classes(Theme.name Theme.NavItem)
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .MinHeight(28.0)
    .Margin(Thickness(10.0, 0.0, 0.0, 0.0))
    .Padding(Thickness(8.0, 4.0))
    .Content(content)
    .OnClickHandler(fun _ _ -> row.OnOpen())

let queueHint(hint: string) : Control =
  Border()
    .Margin(Thickness(20.0, 0.0, 8.0, 2.0))
    .Child(
      TextBlock()
        .Name("ExplorerQueueNotice")
        .Text(hint)
        .Classes(Theme.name Theme.Muted)
        .TextWrapping(TextWrapping.Wrap)
    )

let view(props: Props) : Control =
  let content: Control array = [|
    yield section "HISTORY"

    yield!
      [ Following; Latest; Trending ] |> List.map(fun log -> refRow(props, log))

    yield section "SAVED"

    yield
      countRow {
        Title = "Stars.index"
        Name = "SAVED"
        Count = props.Saved
        OnOpen = props.OnOpenSaved
      }

    yield section "QUEUE"

    yield
      countRow {
        Title = "Scheduled"
        Name = "QUEUE"
        Count = props.Queue
        OnOpen = props.OnOpenQueue
      }

    match props.QueueHint with
    | Some hint -> yield queueHint hint
    | None -> ()

    yield section "ACCOUNT"

    yield
      Button()
        .Name("EditorExplorerViewer")
        .Classes(Theme.name Theme.NavItem)
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .MinHeight(28.0)
        .Margin(Thickness(10.0, 0.0, 0.0, 0.0))
        .Padding(Thickness(8.0, 4.0))
        .Content(
          TextBlock().Text($"@{props.Viewer}").Classes(Theme.name Theme.Mono)
        )
        .OnClickHandler(fun _ _ -> props.OnOpenViewer())
  |]

  let header =
    TextBlock()
      .Text("EXPLORER")
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .Margin(0.0, 0.0, 0.0, 8.0)

  let children: Control array = [| header; yield! content |]

  Border()
    .Name("EditorExplorer")
    .Classes(Theme.name Theme.SurfaceBack, Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 0.0, 1.0, 0.0))
    .Width(240.0)
    .Padding(Thickness(12.0, 10.0))
    .Child(
      StackPanel()
        .Orientation(Orientation.Vertical)
        .Spacing(2.0)
        .Children(children)
    )
