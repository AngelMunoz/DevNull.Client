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
  NewCount: int option
  Ranked: bool
}

type Msg =
  | Fetched of Result<LogPage, AppError>
  | ReloadRequested
  | Appended of Result<LogPage, AppError>
  | MoreRequested
  | StarToggled of Commit
  | StarDone of Result<Commit, AppError>
  | PastelGiven of Commit
  | PastelDone of Result<PastelResult, AppError>
  | CheckNew
  | NewChecked of Result<LogPage, AppError>
  | NewRequested
  | NewFetched of Result<LogPage, AppError>

let initial = {
  Loading = true
  Error = None
  Items = []
  NextCursor = None
  Head = None
  NewCount = None
  Ranked = false
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
            NewCount = None
            Ranked = page.IsRanked
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
    // A ranked log is score ordered, so "newer than head" means
    // nothing; the site skips the poll there too.
    match model.Head, model.Ranked with
    | Some head, false ->
      model, Cmd.OfAsync.perform checkNew (props, head) NewChecked
    | _ -> model, Cmd.none
  | NewChecked result ->
    match result with
    | Ok page ->
      // The since read answers the same first page; only its count is
      // news. The commits themselves come with the pill's refresh.
      {
        model with
            NewCount =
              page.NewCommitsCount |> Option.filter(fun count -> count > 0)
      },
      Cmd.none
    | Error _ -> model, Cmd.none
  | NewRequested ->
    { model with NewCount = None },
    Cmd.OfAsync.perform fetch (props, None) NewFetched
  | NewFetched result ->
    match result with
    | Ok page ->
      // The pill's refresh replaces the feed with the fresh first page,
      // the way the site reloads its log; no duplicate can survive.
      {
        model with
            Items = page.Page.Items
            NextCursor = page.Page.NextCursor
            Head = headOf page.Page.Items
            Ranked = page.IsRanked
      },
      Cmd.none
    | Error error -> { model with Error = Some error }, Cmd.none
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
    { model with Error = None },
    Cmd.OfAsync.perform
      CommitActions.toggleStar
      (props.Env, props.Session, commit)
      StarDone
  | StarDone result ->
    match result with
    | Ok updated ->
      {
        model with
            Items = replaceNode(model.Items, updated)
      },
      Cmd.none
    | Error error -> { model with Error = Some error }, Cmd.none
  | PastelGiven commit ->
    let give() =
      Pasteis.give(
        props.Env,
        {
          KeyFile = props.Session.KeyFile
          CommitId = commit.Id
        }
      )

    { model with Error = None }, Cmd.OfAsync.perform give () PastelDone
  | PastelDone result ->
    match result with
    | Ok pastel ->
      {
        model with
            Items = replaceNode(model.Items, pastel.Commit)
      },
      Cmd.none
    | Error error -> { model with Error = Some error }, Cmd.none

/// One row of the log feed: a day separator, a commit on the rail, or
/// a deletion marker.
type LogRow =
  | Day of string
  | Node of Commit
  | TombstoneRow of Tombstone

/// Interleaves day separators between the commits of different days,
/// carrying the day across tombstones. A ranked log is score ordered,
/// its days interleave, so it rides without separators the way the
/// site's ranked feed does.
let toRows(items: CommitNode list, ranked: bool) : LogRow list =
  let now = DateTimeOffset.UtcNow

  let nodeRow node =
    match node with
    | LiveCommit commit -> LogRow.Node commit
    | Tombstone tombstone -> LogRow.TombstoneRow tombstone

  if ranked then
    [ for node in items -> nodeRow node ]
  else
    let dayOf node =
      match node with
      | LiveCommit commit -> Some(dayLabel(now, commit.CreatedAt))
      | Tombstone _ -> None

    // The day seen before each item, carried across tombstones: one
    // carried state per item, seeded with the empty one.
    let carry =
      items
      |> List.scan (fun day node -> dayOf node |> Option.orElse day) None
      |> List.truncate items.Length

    [
      for prevDay, node in List.zip carry items do
        match node with
        | LiveCommit commit ->
          let day = dayLabel(now, commit.CreatedAt)

          if prevDay <> Some day then
            LogRow.Day day
            LogRow.Node commit
          else
            LogRow.Node commit
        | Tombstone tombstone -> LogRow.TombstoneRow tombstone
    ]

let keyOf(logRow: LogRow) : string =
  match logRow with
  | LogRow.Day label -> "day:" + label
  | LogRow.Node commit -> commit.Id
  | LogRow.TombstoneRow tombstone -> tombstone.Id

let row
  (props: Props<'env>, dispatch: Dispatch<Msg>)
  (logRow: LogRow)
  : Control =
  match logRow with
  | LogRow.Day label -> FeedParts.dayRow label
  | LogRow.Node commit ->
    CommitCard.view {
      Commit = commit
      OnEvent =
        fun event ->
          match event with
          | CommitCard.Opened c ->
            Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
          | CommitCard.StarToggled c -> dispatch(StarToggled c)
          | CommitCard.PastelGiven c -> dispatch(PastelGiven c)
          | CommitCard.QuoteRequested c ->
            Navigation.go(props.Nav, "/quote/" + c.Id, ignore)
      Pastels = props.Session.Session.PasteisMode
      Dense = false
    }
  | LogRow.TombstoneRow _ -> CommitCard.tombstone()

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

/// One log tab: the page-tab shape, underlined while active.
let tab(active: LogKind, nav: INavigable, info: Tab) : Control =
  let button =
    Button()
      .Classes(Theme.name Theme.TabButton)
      .Content(info.Label)
      .OnClickHandler(fun _ _ -> Navigation.go(nav, info.Url, ignore))

  if active = info.Kind then
    button.Classes.Add(Theme.name Theme.TabButtonActive) |> ignore

  button

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =

  // The tabs run edge to edge of the reading column over a hairline,
  // the way the site's log header does. The band sits above the client,
  // so the strip clears it.
  let tabStrip =
    let strip = Grid()

    strip.ColumnDefinitions <- ColumnDefinitions("*,*,*")

    Border()
      .BorderBrush(Brushes.border)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Margin(0.0, Theme.bandHeight, 0.0, 0.0)
      .Child(
        strip.Children(
          [|
            for index, info in List.indexed logTabs ->
              tab(props.Log, props.Nav, info).Column(index)
          |]
        )
      )

  let composer =
    Composer.Page {
      Env = props.Env
      Session = props.Session
      Target = Composer.Fresh
      OnCommitted = fun _ -> dispatch ReloadRequested
    }

  // The staged commit: the signed-in user's avatar rides the rail's
  // first row, ring and all, and the dashed line runs from it down
  // into the day separators — the site stages the next commit exactly
  // so. The input keeps the cards' right inset.
  let composerRow =
    let nodeTop = 9.0

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
        .Margin(0.0, nodeTop + 46.0, 0.0, 0.0)

    let avatar =
      Border()
        .Name("ComposerRailNode")
        .Background(Brushes.card)
        .BorderBrush(Brushes.laneFallback)
        .BorderThickness(Thickness(2.0))
        .CornerRadius(46.0)
        .Padding(1.0)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Top)
        .Margin(0.0, nodeTop, 0.0, 0.0)
        .Child(
          Avatar.view {
            Name = props.Session.Session.User.Name
            Url = props.Session.Session.User.AvatarUrl
            Lane = None
            Size = 40.0
          }
        )

    let grid = Grid()

    grid.ColumnDefinitions <- ColumnDefinitions("64,*")

    grid.Children(
      Panel().Children(line, avatar).Column(0),
      Border().Margin(0.0, 0.0, 16.0, 0.0).Child(composer).Column(1)
    )

  let newPill =
    Button()
      .Classes(Theme.name Theme.AccentButton)
      .CornerRadius(999.0)
      .Padding(16.0, 6.0, 16.0, 6.0)
      .Margin(0.0, 8.0, 0.0, 0.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
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
                  match current.NewCount with
                  | Some count -> $"{count} new commits"
                  | None -> "")
              )
          )
      )
      .IsVisible(model |> Observable.map _.NewCount.IsSome)
      .OnClickHandler(fun _ _ -> dispatch NewRequested)

  let list =
    Feed.view {
      Items =
        model
        |> Observable.map(fun current -> toRows(current.Items, current.Ranked))
      KeyOf = keyOf
      Row = row(props, dispatch)
      Footer =
        ValueSome(
          PageChrome.loadMore(
            model |> Observable.map _.NextCursor.IsSome,
            fun () -> dispatch MoreRequested
          )
        )
    }

  // The since-poll: once a minute, ask the server what landed after
  // the head. It runs while the page is attached and stops with it.
  let newTimer =
    DispatcherTimer(
      DispatcherPriority.Background,
      Interval = TimeSpan.FromMinutes 1.0
    )

  newTimer.Tick.Add(fun _ -> dispatch CheckNew)

  // The pinned area takes no horizontal inset: the composer's rail must
  // sit on the same line as the day separators and the cards below,
  // which all start at the reading column's left edge.
  let pinnedPage =
    PageChrome.page {
      Model = model
      Title = ""
      Padding = Thickness(0.0, Theme.bandHeight + 8.0, 0.0, 0.0)
      ErrorOf = _.Error
      Busy = fun current -> current.Loading, "Loading the log…"
      Empty =
        fun current ->
          not current.Loading && current.Error.IsNone && current.Items.IsEmpty,
          "Nothing here yet. Commit something."
      Header = [ (fun () -> composerRow); fun () -> newPill ]
      Body = fun () -> list
    }

  let root = Grid()
  root.RowDefinitions <- RowDefinitions("Auto,*")

  root.Children(tabStrip.Row(0), pinnedPage.Row(1)) |> ignore

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
