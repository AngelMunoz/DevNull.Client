module DevNull.Client.Layouts.Editor.Explorer

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Core.ApiTypes

type Props = {
  Selected: LogKind
  Saved: int
  Queue: int
  Viewer: string
  OnSelectRef: LogKind -> unit
  OnOpenViewer: unit -> unit
}

let divider() : Control =
  Border()
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
    .Margin(0.0, 4.0, 0.0, 4.0)

let section(title: string) : Control =
  TextBlock()
    .Text(title)
    .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
    .Margin(0.0, 4.0, 0.0, 2.0)

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

  let label = TextBlock().Text(LogLabel.ofKind log)

  let head: Control array =
    if selected then
      [|
        TextBlock()
          .Text("HEAD")
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .Column(2)
      |]
    else
      [||]

  let children: Control array = [|
    marker.Column(0)
    label.Column(1)
    yield! head
  |]

  let content = Grid().Children(children)

  content.ColumnDefinitions <- ColumnDefinitions("Auto,*,Auto")

  let button =
    if selected then
      Button().Classes(Theme.name Theme.NavItem, Theme.name Theme.NavItemActive)
    else
      Button().Classes(Theme.name Theme.NavItem)

  button
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .Content(content)
    .OnClickHandler(fun _ _ -> props.OnSelectRef log)

[<Struct>]
type CountRow = { Title: string; Count: int }

let countRow(row: CountRow) =
  let label = TextBlock().Text(row.Title)

  let count =
    TextBlock()
      .Text(string row.Count)
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .Column(1)

  count.Name <- $"ExplorerCount-{row.Title}"

  let content = Grid().Children(label.Column(0), count)

  content.ColumnDefinitions <- ColumnDefinitions("*,Auto")

  Border().Padding(Thickness(6.0, 4.0)).Child(content)

let view(props: Props) : Control =
  let content: Control array = [|
    section "HISTORY"

    yield!
      [ Following; Latest; Trending ] |> List.map(fun log -> refRow(props, log))

    divider()

    countRow { Title = "SAVED"; Count = props.Saved }

    countRow { Title = "QUEUE"; Count = props.Queue }

    divider()

    Button()
      .Name("EditorExplorerViewer")
      .Classes(Theme.name Theme.NavItem)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Padding(Thickness(6.0, 4.0))
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
    .Padding(Thickness(10.0, 8.0))
    .Child(
      StackPanel()
        .Orientation(Orientation.Vertical)
        .Spacing(2.0)
        .Children(children)
    )
