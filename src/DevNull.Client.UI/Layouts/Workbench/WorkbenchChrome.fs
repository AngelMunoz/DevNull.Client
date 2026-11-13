module DevNull.Client.Workbench.WorkbenchChrome

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Formatting
open DevNull.Client.Theme

[<Struct>]
type RefRow = { Ref: Ref; Count: int option }

[<Struct>]
type RefsModel = { Rows: RefRow list; Selected: Ref }

[<Struct>]
type StatusModel = {
  Ref: Ref
  Handle: string
  FetchedAt: DateTimeOffset option
  Now: DateTimeOffset
}

let rowOf(token: string, count: int option) : RefRow = {
  Ref = token |> WorkbenchSettings.parse |> WorkbenchState.refOf
  Count = count
}

let rows(saved: int, scheduled: int, unread: int) = [
  rowOf("latest", None)
  rowOf("following", None)
  rowOf("trending", None)
  rowOf("saved", Some saved)
  rowOf("scheduled", Some scheduled)
  rowOf("notifications", Some unread)
]

let refButton(selected: Ref, row: RefRow, onSelect: Ref -> unit) : Control =
  let isSelected = row.Ref = selected

  let marker =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .Text(if isSelected then "HEAD" else "")

  let label = TextBlock().Text(Ref.label row.Ref)

  if isSelected then
    marker.Classes(Theme.name Theme.FgAccent) |> ignore
    label.Classes(Theme.name Theme.Mono, Theme.name Theme.FgAccent) |> ignore

  let count =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .HorizontalAlignment(HorizontalAlignment.Right)
      .Margin(Thickness(8.0, 0.0, 0.0, 0.0))
      .Text(row.Count |> Option.map string |> Option.defaultValue "")

  let grid = Grid()

  grid.ColumnDefinitions <- ColumnDefinitions("42,*,Auto")

  grid.Children(
    marker.Column(0),
    label.Column(1),
    count.Column(2).VerticalAlignment(VerticalAlignment.Center)
  )
  |> ignore

  let button =
    Button()
      .Classes(Theme.name Theme.NavItem)
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .Content(grid)
      .OnClickHandler(fun _ _ -> onSelect row.Ref)

  if isSelected then
    button.Classes(Theme.name Theme.NavItemActive) |> ignore

  button

let defaultLayoutRow(onUseDefaultLayout: unit -> unit) : Control =
  Button()
    .Name("UseDefaultLayout")
    .Classes(Theme.name Theme.NavItem)
    .HorizontalContentAlignment(HorizontalAlignment.Stretch)
    .Content(TextBlock().Text("Default layout"))
    .OnClickHandler(fun _ _ -> onUseDefaultLayout())

let refs
  (model: RefsModel, onSelect: Ref -> unit, onUseDefaultLayout: unit -> unit)
  : Control =
  StackPanel()
    .Spacing(2.0)
    .Children(
      [|
        for row in model.Rows -> refButton(model.Selected, row, onSelect)
        yield defaultLayoutRow onUseDefaultLayout
        yield
          TextBlock()
            .Name("DefaultLayoutNotice")
            .Classes(Theme.name Theme.Muted)
            .Margin(Thickness(16.0, 2.0, 12.0, 8.0))
            .TextWrapping(TextWrapping.Wrap)
            .Text("Settings open in the default layout.")
      |]
    )

let statusText(model: StatusModel) =
  let fetched =
    match model.FetchedAt with
    | Some at -> "fetched " + timeAgo(model.Now, at)
    | None -> "not fetched yet"

  $"HEAD -> {Ref.label model.Ref} · @{model.Handle} · {fetched}"
