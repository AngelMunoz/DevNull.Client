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
  | Notifications
  | Activity
  | Terminal

type State = { Open: bool; Tab: Tab }

type ProblemsState = {
  Loading: bool
  Error: AppError option
  Items: Notification list
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
}

let height = 280.0

let closed = { Open = false; Tab = Notifications }

let openOn(tab: Tab, state: State) = { state with Open = true; Tab = tab }

let select(tab: Tab, state: State) = { state with Open = true; Tab = tab }

let close(state: State) = { state with Open = false }

let toggle(tab: Tab, state: State) =
  if state.Open && state.Tab = tab then
    close state
  else
    openOn(tab, state)

let isOpen(state: State) = state.Open

let overlay(band: WidthBand) = band <> Full

let emptyProblems = {
  Loading = false
  Error = None
  Items = []
}

let beginProblems(state: ProblemsState) = {
  state with
      Loading = true
      Error = None
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

let kindOrder(kind: NotificationKind) =
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

let problemGroups(items: Notification list) =
  items
  |> List.groupBy(fun notification -> notification.Type)
  |> List.sortBy(fun (kind, _) -> kindOrder kind, kindLabel kind)

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
  | Notifications -> "NOTIFICATIONS"
  | Activity -> "ACTIVITY"
  | Terminal -> "TERMINAL"

let tabName(tab: Tab) =
  match tab with
  | Notifications -> "Notifications"
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

let tabStrip(props: Props, current: Tab) : Control =
  let strip = Grid()
  strip.ColumnDefinitions <- ColumnDefinitions("Auto,Auto,Auto,*,Auto")

  strip.Children(
    tabButton(props, current, Notifications).Column(0),
    tabButton(props, current, Activity).Column(1),
    tabButton(props, current, Terminal).Column(2),
    Button()
      .Name("EditorPanelClose")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(Thickness(6.0, 2.0))
      .Content(TextBlock().Text("×"))
      .OnClickHandler(fun _ _ -> props.OnClose())
      .Column(4)
  )
  |> ignore

  strip

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

let problemsView(props: Props, state: ProblemsState) : Control =
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
      not state.Loading
      && state.Error |> Option.isNone
      && List.isEmpty state.Items
    then
      yield
        TextBlock()
          .Text("No problems.")
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)

    for kind, rows in problemGroups state.Items do
      yield
        TextBlock()
          .Text(kindLabel kind)
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .Margin(0.0, 8.0, 0.0, 2.0)

      for notification in rows do
        yield problemRow(props, notification)
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
  let tabStripHost =
    let binding =
      rebuildOn(
        props.State,
        (fun (state: State) -> state.Tab),
        (fun (state: State) -> tabStrip(props, state.Tab))
      )
      |> _.ToBinding()

    ContentControl().Content(binding)

  let problemsHost =
    ContentControl()
      .Content(
        rebuildOn(
          props.Problems,
          (fun (state: ProblemsState) -> state),
          (fun (state: ProblemsState) -> problemsView(props, state))
        )
        |> _.ToBinding()
      )
      .IsVisible(
        props.State |> Observable.map(fun state -> state.Tab = Notifications)
      )

  let outputHost =
    ContentControl()
      .Content(
        rebuildOn(props.Output, (fun lines -> lines), outputView)
        |> _.ToBinding()
      )
      .IsVisible(
        props.State |> Observable.map(fun state -> state.Tab = Activity)
      )

  let terminalHost =
    ContentControl()
      .Content(props.Terminal)
      .IsVisible(
        props.State |> Observable.map(fun state -> state.Tab = Terminal)
      )

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
