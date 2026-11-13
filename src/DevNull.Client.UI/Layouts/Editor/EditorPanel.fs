module DevNull.Client.Editor.EditorPanel

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Components.PageChrome
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.Formatting

type Tab =
  | Kind of NotificationKind
  | Activity
  | Terminal

type State = { Open: bool; Tab: Tab }

type ProblemsState = {
  Loading: bool
  Error: AppError option
  Items: Notification list
  NextCursor: string option
}

[<Struct>]
type OutputLine = {
  Ref: string
  Result: string
  At: DateTimeOffset
}

type Props = {
  State: IObservable<State>
  Problems: IObservable<ProblemsState>
  Output: IObservable<OutputLine list>
  Terminal: Control
  Now: IObservable<DateTimeOffset>
  OnSelect: Tab -> unit
  OnClose: unit -> unit
  OnOpenCommit: Commit -> unit
  OnLoadMore: unit -> unit
}

let height = 280.0

let closed = { Open = false; Tab = Activity }

let openOn(tab: Tab, state: State) = { state with Open = true; Tab = tab }

let select(tab: Tab, state: State) = { state with Open = true; Tab = tab }

let isKindTab(tab: Tab) =
  match tab with
  | Kind _ -> true
  | Activity
  | Terminal -> false

let close(state: State) = { state with Open = false }

let toggle(tab: Tab, state: State) =
  if state.Open && state.Tab = tab then
    close state
  else
    openOn(tab, state)

let kindRank(kind: NotificationKind) =
  match kind with
  | NotificationKind.Followed -> 0
  | NotificationKind.Replied -> 1
  | NotificationKind.Quoted -> 2
  | NotificationKind.Starred -> 3
  | NotificationKind.Mentioned -> 4
  | NotificationKind.LeveledUp -> 5
  | NotificationKind.Approved -> 6
  | NotificationKind.Rejected -> 7
  | NotificationKind.FirstCommit -> 8
  | NotificationKind.UnknownKind _ -> 9

let isOpen(state: State) = state.Open

let overlay(band: WidthBand) = band <> Full

let emptyProblems = {
  Loading = false
  Error = None
  Items = []
  NextCursor = None
}

let beginProblems(state: ProblemsState) = {
  state with
      Loading = true
      Error = None
      NextCursor = None
}

let kindLabel(kind: NotificationKind) =
  match kind with
  | NotificationKind.Followed -> "FOLLOWS"
  | NotificationKind.Replied -> "REPLIES"
  | NotificationKind.Quoted -> "QUOTES"
  | NotificationKind.Starred -> "STARS"
  | NotificationKind.Mentioned -> "MENTIONS"
  | NotificationKind.LeveledUp -> "LEVELS"
  | NotificationKind.Approved -> "APPROVED"
  | NotificationKind.Rejected -> "REJECTED"
  | NotificationKind.FirstCommit -> "FIRST COMMITS"
  | NotificationKind.UnknownKind raw -> raw.ToUpperInvariant()

let kindsOf(items: Notification list) : NotificationKind list =
  items
  |> List.map(fun notification -> notification.Type)
  |> List.distinctBy id
  |> List.sortBy(fun kind -> kindRank kind, kindLabel kind)

let notificationsOf(items: Notification list, kind: NotificationKind) =
  items |> List.filter(fun notification -> notification.Type = kind)

let hasKind(items: Notification list, kind: NotificationKind) =
  items |> List.exists(fun notification -> notification.Type = kind)

let firstKind(items: Notification list) =
  match kindsOf items with
  | kind :: _ -> Some kind
  | [] -> None

let effectiveTab(items: Notification list, tab: Tab) =
  match tab with
  | Kind kind when hasKind(items, kind) -> tab
  | Kind _ -> firstKind items |> Option.map Kind |> Option.defaultValue Activity
  | Activity
  | Terminal -> tab

let reconcile(items: Notification list, state: State) = {
  state with
      Tab = effectiveTab(items, state.Tab)
}

let openFirst(items: Notification list, state: State) =
  let tab =
    match effectiveTab(items, state.Tab) with
    | Kind _ as held -> held
    | _ ->
      firstKind items |> Option.defaultValue NotificationKind.Followed |> Kind

  { state with Open = true; Tab = tab }

let toggleNotifications(items: Notification list, state: State) =
  match state.Open, state.Tab with
  | true, Kind _ -> close state
  | _ -> openFirst(items, state)

let severityOf(kind: NotificationKind) =
  match kind with
  | NotificationKind.Approved -> Some "✓"
  | NotificationKind.Rejected -> Some "✗"
  | _ -> None

let isUnread(notification: Notification) = notification.UnreadCount > 0

let commitOf(notification: Notification) =
  match notification.Commit with
  | Some(LiveCommit commit) -> Some commit
  | _ -> None

let outputCap = 50

let appendOutput(line: OutputLine, lines: OutputLine list) =
  (lines @ [ line ]) |> List.rev |> List.truncate outputCap |> List.rev

let actionOf(kind: NotificationKind) =
  match kind with
  | NotificationKind.Followed -> "followed you"
  | NotificationKind.Replied -> "replied to your commit"
  | NotificationKind.Quoted -> "quoted your commit"
  | NotificationKind.Starred -> "starred your commit"
  | NotificationKind.Mentioned -> "mentioned you"
  | NotificationKind.LeveledUp -> "sent an update"
  | NotificationKind.Approved -> "your commit was approved"
  | NotificationKind.Rejected -> "your commit was rejected"
  | NotificationKind.FirstCommit -> "made their first commit"
  | NotificationKind.UnknownKind _ -> "sent an update"

let tabLabel(tab: Tab) =
  match tab with
  | Kind kind -> kindLabel kind
  | Activity -> "ACTIVITY"
  | Terminal -> "TERMINAL"

let tabName(tab: Tab) =
  match tab with
  | Kind kind -> kindLabel kind
  | Activity -> "Activity"
  | Terminal -> "Terminal"

let tabButton(props: Props, current: Tab, tab: Tab) : Control =
  let classes = [|
    Theme.name Theme.TabButton
    if current = tab then
      Theme.name Theme.TabButtonActive
  |]

  let button =
    Button()
      .Classes(classes)
      .Padding(Thickness(10.0, 6.0))
      .Content(TextBlock().Text(tabLabel tab).Classes(Theme.name Theme.Mono))
      .OnClickHandler(fun _ _ -> props.OnSelect tab)

  button.Name <- $"EditorPanelTab-{tabName tab}"
  button

let tabStrip(props: Props, current: Tab, items: Notification list) : Control =
  let tabs = [ Activity; Terminal ] @ (kindsOf items |> List.map Kind)

  let strip =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(2.0)
      .Children([| for tab in tabs -> tabButton(props, current, tab) |])

  let scroll =
    ScrollViewer()
      .Name("EditorPanelTabs")
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Auto)
      .VerticalScrollBarVisibility(ScrollBarVisibility.Disabled)
      .Content(strip)

  let frame = Grid()
  frame.ColumnDefinitions <- ColumnDefinitions("*,Auto")

  frame.Children(
    scroll.Column(0),
    Button()
      .Name("EditorPanelClose")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(Thickness(6.0, 2.0))
      .Content(TextBlock().Text("×"))
      .OnClickHandler(fun _ _ -> props.OnClose())
      .Column(1)
  )
  |> ignore

  frame

let problemRow(props: Props, notification: Notification) : Control =
  let mark =
    severityOf notification.Type
    |> Option.map(fun text ->
      TextBlock().Text(text).Classes(Theme.name Theme.Mono))
    |> Option.defaultValue(TextBlock().Text(" "))

  let count =
    if notification.UnreadCount > 1 then
      string notification.UnreadCount
    else
      ""

  let unread = if isUnread notification then "●" + count else " "

  let title =
    TextBlock()
      .Text($"{Author.name notification.Actor} {actionOf notification.Type}")
      .TextTrimming(TextTrimming.CharacterEllipsis)

  let time =
    props.Now
    |> Observable.map(fun now -> timeAgo(now, notification.CreatedAt))
    |> fun text ->
        TextBlock()
          .Text(text)
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)

  let content = Grid()
  content.ColumnDefinitions <- ColumnDefinitions("Auto,Auto,*,Auto")

  content.Children(
    mark.Column(0),
    TextBlock()
      .Text(unread)
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgAccent)
      .Column(1),
    title.Column(2),
    time.Column(3)
  )
  |> ignore

  Button()
    .Name($"ProblemRow-{notification.Id}")
    .Classes(Theme.name Theme.CommitRow)
    .Content(content)
    .OnClickHandler(fun _ _ ->
      commitOf notification |> Option.iter props.OnOpenCommit)

let problemsView
  (props: Props, state: ProblemsState, kind: NotificationKind)
  : Control =
  let rows = notificationsOf(state.Items, kind)

  let children: Control list = [
    if state.Loading && List.isEmpty state.Items then
      yield
        TextBlock()
          .Text("Reading notifications…")
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)

    match state.Error with
    | Some _ -> yield errorPanel(Observable.single state.Error)
    | None -> ()

    if
      not state.Loading && state.Error |> Option.isNone && List.isEmpty rows
    then
      yield
        TextBlock()
          .Text("No notifications of this kind.")
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)

    for notification in rows do
      yield problemRow(props, notification)

    if state.NextCursor.IsSome && not state.Loading then
      yield
        Button()
          .Name("EditorPanelLoadMore")
          .Classes(Theme.name Theme.GhostButton)
          .Content("Load more")
          .OnClickHandler(fun _ _ -> props.OnLoadMore())
  ]

  ScrollViewer()
    .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
    .Content(
      StackPanel()
        .Spacing(2.0)
        .Margin(Thickness(10.0, 6.0))
        .Children(Array.ofList children)
    )

let outputView(lines: OutputLine list) : Control =
  if List.isEmpty lines then
    TextBlock()
      .Text("No fetches yet.")
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .Margin(10.0, 6.0)
  else
    let rows: Control array = [|
      for line in lines do
        let failed = line.Result.StartsWith("failed:")

        let classes = [|
          Theme.name Theme.Mono
          if failed then
            Theme.name Theme.FgDanger
          else
            Theme.name Theme.FgMuted
        |]

        let moment = line.At.ToUniversalTime().ToString("HH:mm:ss")

        TextBlock()
          .Text($"{moment}  {line.Ref}  {line.Result}")
          .Classes(classes)
          .TextWrapping(TextWrapping.NoWrap)
    |]

    ScrollViewer()
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Auto)
      .Content(
        StackPanel().Spacing(2.0).Margin(Thickness(10.0, 6.0)).Children(rows)
      )

let view(props: Props) : Control =
  let current =
    Observable.combineLatest props.State props.Problems
    |> Observable.map(fun (state, problems) ->
      effectiveTab(problems.Items, state.Tab), problems)

  let tabStripHost =
    let binding =
      rebuildOn(
        current,
        (fun (tab, problems) -> tab, problems.Items),
        (fun (tab, problems) -> tabStrip(props, tab, problems.Items))
      )
      |> _.ToBinding()

    ContentControl().Content(binding)

  let problemsHost =
    ContentControl()
      .Content(
        rebuildOn(
          current,
          (fun (tab, problems) -> tab, problems),
          fun (tab, problems) ->
            match tab with
            | Kind kind -> problemsView(props, problems, kind)
            | Activity
            | Terminal -> Panel()
        )
        |> _.ToBinding()
      )
      .IsVisible(
        current
        |> Observable.map(fun (tab, _) ->
          match tab with
          | Kind _ -> true
          | Activity
          | Terminal -> false)
      )

  let outputHost =
    ContentControl()
      .Content(
        rebuildOn(props.Output, (fun lines -> lines), outputView)
        |> _.ToBinding()
      )
      .IsVisible(current |> Observable.map(fun (tab, _) -> tab = Activity))

  let terminalHost =
    ContentControl()
      .Content(props.Terminal)
      .IsVisible(current |> Observable.map(fun (tab, _) -> tab = Terminal))

  let body = Grid()
  body.RowDefinitions <- RowDefinitions("Auto,*")

  body.Children(
    Border()
      .Classes(Theme.name Theme.SurfaceBack, Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Child(tabStripHost)
      .Row(0),
    Grid().Children(problemsHost, outputHost, terminalHost).Row(1)
  )
  |> ignore

  Border()
    .Name("EditorPanel")
    .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
    .BorderThickness(Thickness(1.0))
    .Height(height)
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .VerticalAlignment(VerticalAlignment.Bottom)
    .IsVisible(props.State |> Observable.map(fun state -> state.Open))
    .Child(body)
