module DevNull.Client.Views.LogView

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open NXUI.Extensions
open IcedTasks
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open Navs
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.Icons
open DevNull.Client.Navigation
open DevNull.Client.Stores
open DevNull.Client.Theme

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
  Log: LogKind
  Session: SignedInSession
  Nav: INavigable
}

type Model = {
  Loading: bool
  Error: AppError option
  Items: CommitNode list
  NextCursor: string option
  Head: string option
  New: CommitNode list option
}

type Msg =
  | Fetched of Result<LogPage, AppError>
  | ReloadRequested
  | Appended of Result<LogPage, AppError>
  | MoreRequested
  | StarToggled of Commit
  | StarDone of Result<Commit, AppError>
  | CheckNew
  | NewChecked of Result<LogPage, AppError>
  | NewRequested

let initial = {
  Loading = true
  Error = None
  Items = []
  NextCursor = None
  Head = None
  New = None
}

/// The id of the newest commit in a page: the head the since-poll
/// checks against.
let headOf(items: CommitNode list) : string option =
  items
  |> List.tryPick(function
    | LiveCommit commit -> Some commit.Id
    | Tombstone _ -> None)

let fetch(props: Props<'env>, cursor: string option) =
  Logs.get(
    props.Env,
    {
      KeyFile = props.Session.KeyFile
      Log = props.Log
      Since = None
      Cursor = cursor
    }
  )

let checkNew(props: Props<'env>, head: string) =
  Logs.get(
    props.Env,
    {
      KeyFile = props.Session.KeyFile
      Log = props.Log
      Since = Some head
      Cursor = None
    }
  )

let init (props: Props<'env>) () =
  initial, Cmd.OfAsync.perform fetch (props, None) Fetched

let replaceNode(items: CommitNode list, updated: Commit) : CommitNode list =
  items
  |> List.map(function
    | LiveCommit commit when commit.Id = updated.Id -> LiveCommit updated
    | node -> node)

let actionRequest
  (session: SignedInSession, commit: Commit)
  : Stars.ActionRequest = {
  KeyFile = session.KeyFile
  CommitId = commit.Id
}

let update (props: Props<'env>) msg model =
  match msg with
  | Fetched result ->
    match result with
    | Ok page ->
      {
        model with
            Loading = false
            Error = None
            Items = page.Page.Items
            NextCursor = page.Page.NextCursor
            Head = headOf page.Page.Items
            New = None
      },
      Cmd.none
    | Error error ->
      {
        model with
            Loading = false
            Error = Some error
      },
      Cmd.none
  | CheckNew ->
    match model.Head with
    | Some head -> model, Cmd.OfAsync.perform checkNew (props, head) NewChecked
    | None -> model, Cmd.none
  | NewChecked result ->
    match result with
    | Ok page when not page.Page.Items.IsEmpty ->
      {
        model with
            New = Some page.Page.Items
      },
      Cmd.none
    | _ -> model, Cmd.none
  | NewRequested ->
    match model.New with
    | Some newItems ->
      {
        model with
            Items = newItems @ model.Items
            Head = headOf newItems
            New = None
      },
      Cmd.none
    | None -> model, Cmd.none
  | ReloadRequested ->
    {
      model with
          Loading = true
          Error = None
    },
    Cmd.OfAsync.perform fetch (props, None) Fetched
  | MoreRequested ->
    model, Cmd.OfAsync.perform fetch (props, model.NextCursor) Appended
  | Appended result ->
    match result with
    | Ok page ->
      {
        model with
            Items = model.Items @ page.Page.Items
            NextCursor = page.Page.NextCursor
      },
      Cmd.none
    | Error error -> { model with Error = Some error }, Cmd.none
  | StarToggled commit ->
    let toggle() =
      let request = actionRequest(props.Session, commit)

      if commit.Starred |> Option.contains true then
        Stars.unstar(props.Env, request)
      else
        Stars.star(props.Env, request)

    { model with Error = None }, Cmd.OfAsync.perform toggle () StarDone
  | StarDone result ->
    match result with
    | Ok updated ->
      {
        model with
            Items = replaceNode(model.Items, updated)
      },
      Cmd.none
    | Error error -> { model with Error = Some error }, Cmd.none

/// One row of the log feed: a day separator, a commit on the rail, or
/// a deletion marker.
type LogRow =
  | Day of string
  | Node of Commit * prevLane: Lane option
  | TombstoneRow of Tombstone

/// Interleaves day separators and carries the previous row's lane, so
/// every commit paints the rail segment above its node.
let toRows(items: CommitNode list) : LogRow list =
  let now = DateTimeOffset.UtcNow

  let dayOf node =
    match node with
    | LiveCommit commit -> Some(dayLabel(now, commit.CreatedAt))
    | Tombstone _ -> None

  let laneOf node =
    match node with
    | LiveCommit commit -> commit.Author.Lane
    | Tombstone _ -> None

  // The day and lane seen before each item, carried across
  // tombstones: one carried state per item, seeded with the empty one.
  let carry =
    items
    |> List.scan
      (fun (day, lane) node ->
        dayOf node |> Option.orElse day, laneOf node |> Option.orElse lane)
      (None, None)
    |> List.truncate items.Length

  [
    for (prevDay, prevLane), node in List.zip carry items do
      match node with
      | LiveCommit commit ->
        let day = dayLabel(now, commit.CreatedAt)

        if prevDay <> Some day then
          LogRow.Day day
          LogRow.Node(commit, prevLane)
        else
          LogRow.Node(commit, prevLane)
      | Tombstone tombstone -> LogRow.TombstoneRow tombstone
  ]

let keyOf(logRow: LogRow) : string =
  match logRow with
  | LogRow.Day label -> "day:" + label
  | LogRow.Node(commit, _) -> commit.Id
  | LogRow.TombstoneRow tombstone -> tombstone.Id

/// One day separator: the rail runs on, the label rides the content
/// column above a hairline.
let dayRow(label: string) : Control =
  let grid = Grid()

  grid.ColumnDefinitions <- ColumnDefinitions("56,*")

  let line =
    Border()
      .Width(2.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Stretch)
      .Background(Brushes.rail)

  let label' =
    Border()
      .BorderBrush(Brushes.border)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Child(
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .Margin(0.0, 14.0, 16.0, 4.0)
          .Text(label)
      )

  grid.Children(line.Column(0), label'.Column(1))

let row
  (props: Props<'env>, dispatch: Dispatch<Msg>)
  (logRow: LogRow)
  : Control =
  match logRow with
  | LogRow.Day label -> dayRow label
  | LogRow.Node(commit, prevLane) ->
    CommitCard.view {
      Commit = commit
      OnEvent =
        fun event ->
          match event with
          | CommitCard.Opened c ->
            Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
          | CommitCard.StarToggled c -> dispatch(StarToggled c)
          | CommitCard.QuoteRequested c ->
            Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
      Rail = ValueSome prevLane
      Dense = false
    }
  | LogRow.TombstoneRow tombstone -> CommitCard.tombstone tombstone

type Tab = {
  Label: string
  Kind: LogKind
  Url: string
}

let logTabs: Tab list = [
  {
    Label = "Following"
    Kind = Following
    Url = "/log/following"
  }
  {
    Label = "Latest"
    Kind = Latest
    Url = "/log/latest"
  }
  {
    Label = "Trending"
    Kind = Trending
    Url = "/log/trending"
  }
]

let tab(active: LogKind, nav: INavigable, info: Tab) : Control =
  let isActive = active = info.Kind

  Button()
    .Content(info.Label)
    .Padding(12.0, 8.0, 12.0, 8.0)
    .CornerRadius(0.0)
    .BorderThickness(0.0, 0.0, 0.0, 2.0)
    .BorderBrush(if isActive then Brushes.accent else Brushes.transparent)
    .Background(Brushes.transparent)
    .Foreground(if isActive then Brushes.text else Brushes.muted)
    .OnClickHandler(fun _ _ -> Navigation.go(nav, info.Url, ignore))

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =

  let tabStrip =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(4.0)
      .Children([| for info in logTabs -> tab(props.Log, props.Nav, info) |])

  let composer =
    Composer.Page {
      Env = props.Env
      Session = props.Session
      Target = Composer.Fresh
      OnCommitted = fun _ -> dispatch ReloadRequested
    }

  // The staged commit: the composer rides the rail's first row, its
  // dashed line and hollow node waiting to be filled.
  let composerRow =
    let line =
      Line(
        StartPoint = Point(0.0, 0.0),
        EndPoint = Point(0.0, 1.0),
        Stretch = Stretch.Fill,
        StrokeThickness = 2.0,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Stretch
      )
        .Stroke(Brushes.rail)
        .StrokeDashArray(Avalonia.Collections.AvaloniaList<double>(2.0, 2.0))


    let node =
      Ellipse()
        .Width(14.0)
        .Height(14.0)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Top)
        .Margin(0.0, 14.0, 0.0, 0.0)
        .Fill(Brushes.card)
        .Stroke(Brushes.rail)
        .StrokeThickness(2.0)

    let grid = Grid()

    grid.ColumnDefinitions <- ColumnDefinitions("56,*")

    grid.Children(Panel().Children(line, node).Column(0), composer.Column(1))

  let newPill =
    Button()
      .Classes(Theme.name Theme.AccentButton)
      .CornerRadius(999.0)
      .Padding(16.0, 6.0, 16.0, 6.0)
      .Margin(0.0, 8.0, 0.0, 0.0)
      .HorizontalContentAlignment(HorizontalAlignment.Center)
      .Content(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(
            Icons.icon Glyph.ArrowUp,
            TextBlock()
              .Text(
                model
                |> Observable.map(fun current ->
                  match current.New with
                  | Some newItems -> $"{List.length newItems} new commits"
                  | None -> "")
              )
          )
      )
      .IsVisible(model |> Observable.map _.New.IsSome)
      .OnClickHandler(fun _ _ -> dispatch NewRequested)

  let errorText =
    TextBlock()
      .TextWrapping(TextWrapping.Wrap)
      .Foreground(Brushes.danger)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Error |> Option.map describe |> Option.defaultValue "")
      )

  let errorPanel =
    Border()
      .Classes(Theme.name Theme.Card)
      .Margin(0.0, 0.0, 0.0, 8.0)
      .Child(errorText)
      .IsVisible(model |> Observable.map(_.Error.IsSome))

  let busy =
    Status.busy("Loading the log…").IsVisible(model |> Observable.map _.Loading)

  let emptyState =
    // The machine voice opens; the sentence answers.
    StackPanel()
      .Margin(0.0, 0.0, 0.0, 8.0)
      .Children(
        TextBlock()
          .Classes(Theme.name Theme.CodeBlock)
          .Text("fatal: nothing found"),
        TextBlock()
          .Text("Nothing here yet. Commit something.")
          .TextWrapping(TextWrapping.Wrap)
      )
      .IsVisible(
        model
        |> Observable.map(fun current ->
          not current.Loading && current.Error.IsNone && current.Items.IsEmpty)
      )

  let loadMore =
    Button()
      .Content("Load more")
      .Classes(Theme.name Theme.GhostButton)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Margin(0.0, 8.0, 0.0, 16.0)
      .IsVisible(model |> Observable.map _.NextCursor.IsSome)
      .OnClickHandler(fun _ _ -> dispatch MoreRequested)

  let list =
    Feed.view {
      Items = model |> Observable.map(fun current -> toRows current.Items)
      KeyOf = keyOf
      Row = row(props, dispatch)
      Footer = ValueSome loadMore
    }

  // The since-poll: once a minute, ask the server what landed after
  // the head. It runs while the page is attached and stops with it.
  let newTimer =
    DispatcherTimer(
      DispatcherPriority.Background,
      Interval = TimeSpan.FromMinutes 1.0
    )

  newTimer.Tick.Add(fun _ -> dispatch CheckNew)

  let pinned =
    StackPanel()
      .MaxWidth(720.0)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Margin(Theme.pinnedPadding)
      .Children(tabStrip, composerRow, newPill, errorPanel, busy, emptyState)

  let root = Grid()
  root.RowDefinitions <- RowDefinitions("Auto,*")

  root.Children(pinned.Row(0), list.Margin(24.0, 0.0, 24.0, 0.0).Row(1))
  |> ignore

  root.AttachedToVisualTree.Add(fun _ -> newTimer.Start())

  root.DetachedFromVisualTree.Add(fun _ -> newTimer.Stop())

  root

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram (init props) (update props) (view props)

    this.Content <- Program.runWith () program
