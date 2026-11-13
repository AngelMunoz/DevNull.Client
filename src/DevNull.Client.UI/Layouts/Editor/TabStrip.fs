module DevNull.Client.Editor.TabStrip

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Icons

type Props = {
  Tabs: EditorTab list
  Active: TabId
  Band: WidthBand
  OnSelect: TabId -> unit
  OnClose: TabId -> unit
}

let shortTitle(title: string) =
  match title.IndexOf(" @", StringComparison.Ordinal) with
  | -1 -> title
  | index -> title.Substring(0, index)

let titleAt(title: string, band: WidthBand) =
  match band with
  | Full -> title
  | Folded
  | Trimmed -> shortTitle title

let dirtyDot(count: int) : Control voption =
  if count > 0 then
    ValueSome(
      TextBlock()
        .Text($"●{count}")
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgAccent)
    )
  else
    ValueNone

let closeButton(props: Props, tab: EditorTab) : Control =
  let button =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(Thickness(2.0))
      .Content(iconAt(Close, 10.0))
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        props.OnClose(EditorTab.id tab))

  button.Name <- $"EditorTabClose-{TabId.key(EditorTab.id tab)}"
  button

let tabContent(props: Props, tab: EditorTab) : Control =
  let mark: Control array =
    if EditorTab.isPinned tab then
      [|
        if TabId.isRepo(EditorTab.id tab) then
          iconAt(Pin, 14.0)
        else
          iconAt(Log, 14.0)
      |]
    else
      [||]

  let title: Control =
    TextBlock().Text(titleAt(EditorTab.title tab, props.Band))

  let dirty = dirtyDot(EditorTab.dirty tab) |> ValueOption.toArray

  let close: Control array =
    if EditorTab.isPinned tab then
      [||]
    else
      [| closeButton(props, tab) |]

  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(6.0)
    .Children([| yield! mark; title; yield! dirty; yield! close |])

let tabButton(props: Props, tab: EditorTab) : Control =
  let button =
    if EditorTab.id tab = props.Active then
      Button()
        .Classes(Theme.name Theme.TabButton, Theme.name Theme.TabButtonActive)
    else
      Button().Classes(Theme.name Theme.TabButton)

  let button =
    button
      .Content(tabContent(props, tab))
      .OnClickHandler(fun _ _ -> props.OnSelect(EditorTab.id tab))

  button.PointerPressed.Add(fun args ->
    if
      not(EditorTab.isPinned tab)
      && args.GetCurrentPoint(button).Properties.IsMiddleButtonPressed
    then
      args.Handled <- true
      props.OnClose(EditorTab.id tab))

  button.Name <- $"EditorTab-{TabId.key(EditorTab.id tab)}"
  button

let overflow(props: Props, hidden: EditorTab list) : Control =
  let menu = MenuFlyout()

  for tab in hidden do
    let item = MenuItem()
    item.Header <- titleAt(EditorTab.title tab, props.Band)
    item.Click.Add(fun _ -> props.OnSelect(EditorTab.id tab))
    menu.Items.Add item |> ignore

  let button =
    Button().Classes(Theme.name Theme.GhostButton).Content($"⋯{hidden.Length}")

  button.Flyout <- menu
  button

let view(props: Props) : Control =
  let visible =
    match props.Band with
    | Full
    | Folded -> props.Tabs
    | Trimmed ->
      let pinned =
        props.Tabs
        |> List.filter(fun tab -> EditorTab.isPinned tab)
        |> List.truncate 1

      let active =
        props.Tabs
        |> List.filter(fun tab ->
          EditorTab.id tab = props.Active && not(EditorTab.isPinned tab))

      pinned @ active

  let hidden =
    props.Tabs
    |> List.filter(fun tab ->
      visible
      |> List.exists(fun shown -> EditorTab.id shown = EditorTab.id tab)
      |> not)

  let children: Control array = [|
    for tab in visible do
      tabButton(props, tab)

    if not hidden.IsEmpty then
      overflow(props, hidden)
  |]

  let strip =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(4.0)
      .Children(children)

  Border()
    .Name("EditorTabStrip")
    .Classes(Theme.name Theme.SurfaceBack, Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
    .Child(strip)
