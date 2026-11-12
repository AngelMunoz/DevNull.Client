module DevNull.Client.Layouts.Workbench.WorkbenchChrome

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Formatting
open DevNull.Client.Theme

[<Struct>]
type RefsModel = { Refs: Ref list; Selected: Ref }

[<Struct>]
type StatusModel = {
  Ref: Ref
  Handle: string
  FetchedAt: DateTimeOffset option
  Now: DateTimeOffset
}

let historyRefs() =
  [ "latest"; "following"; "trending" ]
  |> List.map(fun token ->
    token |> WorkbenchSettings.parse |> WorkbenchState.refOf)

let refButton(selected: Ref, entry: Ref, onSelect: Ref -> unit) : Control =
  let isSelected = entry = selected

  let marker =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .Width(42.0)
      .Text(if isSelected then "HEAD" else "")

  let label = TextBlock().Text(Ref.label entry)

  if isSelected then
    marker.Classes(Theme.name Theme.FgAccent) |> ignore
    label.Classes(Theme.name Theme.Mono, Theme.name Theme.FgAccent) |> ignore

  let button =
    Button()
      .Classes(Theme.name Theme.NavItem)
      .Content(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(marker, label)
      )
      .OnClickHandler(fun _ _ -> onSelect entry)

  if isSelected then
    button.Classes(Theme.name Theme.NavItemActive) |> ignore

  button

let refs(model: RefsModel, onSelect: Ref -> unit) : Control =
  StackPanel()
    .Spacing(2.0)
    .Children(
      [|
        for entry in model.Refs -> refButton(model.Selected, entry, onSelect)
      |]
    )

let status(model: StatusModel) : Control =
  let fetched =
    match model.FetchedAt with
    | Some at -> "fetched " + timeAgo(model.Now, at)
    | None -> "not fetched yet"

  let divider = TextBlock().Classes(Theme.name Theme.Muted).Text("·")

  Border()
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
    .Padding(Thickness(16.0, 8.0, 16.0, 8.0))
    .Child(
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text("HEAD -> " + Ref.label model.Ref),
          divider,
          TextBlock().Classes(Theme.name Theme.Mono).Text("@" + model.Handle),
          divider,
          TextBlock().Classes(Theme.name Theme.Mono).Text(fetched)
        )
    )
