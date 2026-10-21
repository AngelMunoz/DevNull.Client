module DevNull.Client.Views.NotificationsView

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Media
open Avalonia.Controls.Templates
open Avalonia.Layout
open NXUI.Extensions
open System.Reactive.Subjects
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
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
}

type Model = {
  Loading: bool
  Error: AppError option
  Filter: string option
  Items: Notification list
  NextCursor: string option
}

type Msg =
  | Fetched of Result<NotificationPage, AppError>
  | FilterChosen of filter: string option
  | Appended of Result<NotificationPage, AppError>
  | MoreRequested

let initial = {
  Loading = true
  Error = None
  Filter = None
  Items = []
  NextCursor = None
}

/// One inbox read: the filter in play and the pager cursor.
[<Struct>]
type Query = {
  Session: SignedInSession
  Filter: string option
  Cursor: string option
}

let fetch(env, query: Query) =
  Notifications.list(
    env,
    {
      KeyFile = query.Session.KeyFile
      Filter = query.Filter
      Through = None
      Cursor = query.Cursor
    }
  )

let init (props: Props<'env>) () =
  initial,
  Cmd.OfAsync.perform
    fetch
    (props.Env,
     {
       Session = props.Session
       Filter = None
       Cursor = None
     })
    Fetched

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
      },
      Cmd.none
    | Error error ->
      {
        model with
            Loading = false
            Error = Some error
      },
      Cmd.none
  | FilterChosen filter ->
    // Clicking the pill already in play clears the filter.
    let next = if model.Filter = filter then None else filter

    if next = model.Filter then
      model, Cmd.none
    else
      {
        model with
            Loading = true
            Error = None
            Filter = next
            Items = []
            NextCursor = None
      },
      Cmd.OfAsync.perform
        fetch
        (props.Env,
         {
           Session = props.Session
           Filter = next
           Cursor = None
         })
        Fetched
  | MoreRequested ->
    model,
    Cmd.OfAsync.perform
      fetch
      (props.Env,
       {
         Session = props.Session
         Filter = model.Filter
         Cursor = model.NextCursor
       })
      Appended
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

/// One filter pill: the wire type it requests and the label it wears.
let filters: (string * string) list = [
  "replies", "Replies"
  "quotes", "Quotes"
  "stars", "Stars"
  "mentions", "Mentions"
  "follows", "Follows"
  "moderation", "Moderation"
]

/// The pill row of the pinned header; the active pill carries the
/// primary-soft wash. The toggle lives in `update`: clicking the pill
/// in play clears the filter.
let filterPill
  (
    active: IObservable<string option>,
    dispatch: Dispatch<Msg>,
    pair: string * string
  ) : Control =
  let wire, label = pair

  let pill =
    Button()
      .Classes(Theme.name Theme.FilterPill)
      .Content(label)
      .OnClickHandler(fun _ _ -> dispatch(FilterChosen(Some wire)))

  active
  |> Observable.add(fun current ->
    if current = Some wire then
      pill.Classes.Add(Theme.name Theme.FilterPillActive) |> ignore
    else
      pill.Classes.Remove(Theme.name Theme.FilterPillActive) |> ignore)

  pill

/// The icon one notification type wears.
let glyphOf(kind: string) : Glyph =
  match kind with
  | "followed" -> Glyph.UserPlus
  | "replied" -> Glyph.Reply
  | "quoted" -> Glyph.Quote
  | "starred" -> Glyph.Star
  | "mentioned" -> Glyph.At
  | "leveled_up" -> Glyph.ChevronsUp
  | "approved" -> Glyph.Check
  | "rejected" -> Glyph.Close
  | _ -> Glyph.Notifications

/// The sentence one notification type says after the actor.
let actionOf(kind: string) : string =
  match kind with
  | "followed" -> "followed you"
  | "replied" -> "replied to your commit"
  | "quoted" -> "quoted your commit"
  | "starred" -> "starred your commit"
  | "mentioned" -> "mentioned you"
  | "approved" -> "Your commit was approved"
  | "rejected" -> "Your commit was rejected"
  | _ -> "sent an update"

/// The noun a grouped row counts.
let nounOf(kind: string) : string =
  match kind with
  | "followed" -> "followers"
  | "starred" -> "stars"
  | _ -> "updates"

/// The commit the notification points at, when it points at one.
let commitOf(notification: Notification) : Commit option =
  match notification.Commit with
  | Some(LiveCommit commit) -> Some commit
  | _ -> None

/// One inbox row of the feed: a day separator or a notification
/// group.
type InboxRow =
  | Day of string
  | Entry of Notification

/// Interleaves day separators between the groups of different days,
/// keeping the first row of every group.
let toRows(items: Notification list) : InboxRow list =
  let now = DateTimeOffset.UtcNow

  let seen = HashSet<string>()

  let distinct =
    items |> List.filter(fun notification -> seen.Add notification.GroupId)

  [
    for index, notification in List.indexed distinct do
      let day = dayLabel(now, notification.CreatedAt)

      let startsDay =
        index = 0 || dayLabel(now, distinct[index - 1].CreatedAt) <> day

      if startsDay then
        InboxRow.Day day

      InboxRow.Entry notification
  ]

let rowKey(inboxRow: InboxRow) : string =
  match inboxRow with
  | InboxRow.Day label -> "day:" + label
  | InboxRow.Entry notification -> notification.GroupId

/// One day separator over the neutral rail.
let dayRow(label: string) : Control =
  let grid = Grid()

  grid.ColumnDefinitions <- ColumnDefinitions("64,*")

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
          .Margin(0.0, 8.0, 16.0, 8.0)
          .Text(label)
      )

  grid.Children(line.Column(0), label'.Column(1))

/// The rail cell of one notification: the neutral rail with the
/// actor's avatar (the types about their commit) or the type's icon in
/// a ring.
let railCell(notification: Notification) : Control =
  let actorsCommit =
    match notification.Type with
    | "replied"
    | "quoted"
    | "mentioned" -> true
    | _ -> false

  let line =
    Border()
      .Width(2.0)
      .Background(Brushes.rail)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Stretch)
      .Margin(0.0, 32.0, 0.0, 0.0)

  let node: Control =
    if actorsCommit then
      Border()
        .Background(Brushes.card)
        .BorderBrush(Brushes.rail)
        .BorderThickness(Thickness(2.0))
        .CornerRadius(40.0)
        .Padding(1.0)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Top)
        .Margin(0.0, 11.0, 0.0, 0.0)
        .Child(
          Avatar.view {
            Name = notification.Actor.Name
            Url = notification.Actor.AvatarUrl
            Lane = notification.Actor.Lane
            Size = 40.0
          }
        )
    else
      Border()
        .Background(Brushes.card)
        .BorderBrush(Brushes.rail)
        .BorderThickness(Thickness(2.0))
        .CornerRadius(20.0)
        .Width(38.0)
        .Height(38.0)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Top)
        .Margin(0.0, 13.0, 0.0, 0.0)
        .Child(
          Icons
            .iconAt(glyphOf notification.Type, 16.0)
            .Foreground(Brushes.laneOf notification.Actor.Lane)
        )

  let rail = Panel()

  rail.Width <- 64.0
  rail.MinHeight <- 64.0

  rail.Children(line, node)

/// The excerpt of the commit the notification points at: boxed for
/// the viewer's own commits, inline for the actor's.
let excerptBox(notification: Notification) : Control option =
  match notification.Type with
  | "starred"
  | "approved"
  | "rejected" ->
    commitOf notification
    |> Option.map(fun commit ->
      Border()
        .BorderBrush(Brushes.border)
        .BorderThickness(Thickness(1.0))
        .CornerRadius(8.0)
        .Padding(10.0)
        .Margin(0.0, 6.0, 0.0, 0.0)
        .Child(
          TextBlock()
            .Text(commit.Body)
            .TextWrapping(TextWrapping.Wrap)
            .MaxLines(3)
            .TextTrimming(TextTrimming.CharacterEllipsis)
        ))
  | _ ->
    commitOf notification
    |> Option.map(fun commit ->
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Margin(0.0, 2.0, 0.0, 0.0)
        .Text(commit.Body)
        .TextWrapping(TextWrapping.Wrap)
        .MaxLines(2)
        .TextTrimming(TextTrimming.CharacterEllipsis))

/// The live expansion state of one group, held outside the recycled
/// row controls.
type GroupState = {
  Open: BehaviorSubject<bool>
  Items: BehaviorSubject<Notification list>
  Failed: BehaviorSubject<bool>
}

/// One expanded event inside a group: the actor, the action, and the
/// excerpt, off a short spine.
let eventRow(notification: Notification) : Control =
  let identity =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(6.0)
      .Children(
        TextBlock()
          .Classes(Theme.name Theme.Subtitle)
          .Text(notification.Actor.Name),
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .Text(timeAgo(DateTimeOffset.UtcNow, notification.CreatedAt))
      )

  let children: Control array = [|
    identity
    TextBlock().Classes(Theme.name Theme.Muted).Text(actionOf notification.Type)

    match commitOf notification with
    | Some commit ->
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Margin(0.0, 2.0, 0.0, 0.0)
        .Text(commit.Body)
        .TextWrapping(TextWrapping.Wrap)
        .MaxLines(2)
        .TextTrimming(TextTrimming.CharacterEllipsis)
    | None -> ()
  |]

  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(10.0)
    .Margin(0.0, 4.0, 0.0, 0.0)
    .Children(
      Avatar.view {
        Name = notification.Actor.Name
        Url = notification.Actor.AvatarUrl
        Lane = notification.Actor.Lane
        Size = 28.0
      },
      StackPanel()
        .VerticalAlignment(VerticalAlignment.Center)
        .Spacing(4.0)
        .Children(children)
    )

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =

  // One group's expansion state, created on first render and reused
  // across the recycle passes.
  let groups = Dictionary<string, GroupState>()

  let stateOf(notification: Notification) : GroupState =
    match groups.TryGetValue notification.GroupId with
    | true, state -> state
    | _ ->
      let state = {
        Open = new BehaviorSubject<bool>(false)
        Items = new BehaviorSubject<Notification list>([])
        Failed = new BehaviorSubject<bool>(false)
      }

      groups[notification.GroupId] <- state
      state

  /// Reads one group's events the first time it opens.
  let loadGroup(notification: Notification, state: GroupState) : unit =
    if List.isEmpty state.Items.Value then
      asyncEx {
        let! result: Result<Page<Notification>, AppError> =
          Notifications.group(
            props.Env,
            {
              KeyFile = props.Session.KeyFile
              NotificationId = notification.Id
              Filter = None
              Cursor = None
            }
          )

        match result with
        | Ok page ->
          state.Items.OnNext page.Items
          state.Failed.OnNext false
        | Error _ -> state.Failed.OnNext true
      }
      |> Async.StartImmediate

  let unread = props.Session.Session.UnreadNotificationsCount

  let unreadLabel =
    if unread > 0 then
      $"Notifications · {unread} unread"
    else
      "Notifications"

  let title = TextBlock().Classes(Theme.name Theme.Title).Text(unreadLabel)

  let activeFilter = model |> Observable.map _.Filter

  let pillRow =
    let panel = WrapPanel()

    panel.Margin <- Thickness(0.0, 8.0, 0.0, 12.0)
    panel.ItemSpacing <- 8.0

    panel.Children [|
      for pair in filters -> filterPill(activeFilter, dispatch, pair)
    |]

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
      .Margin(0.0, 8.0, 0.0, 8.0)
      .Child(errorText)
      .IsVisible(model |> Observable.map(_.Error.IsSome))

  let busy =
    Status
      .busy("Loading the inbox…")
      .IsVisible(model |> Observable.map _.Loading)

  let emptyState =
    Status
      .empty("Nothing here yet. Follows, stars, and replies will show up here.")
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

  /// One group's expansion host: the toggle line and, while open, the
  /// events the group endpoint returned.
  let groupHost(notification: Notification, state: GroupState) : Control =
    let count = notification.EventCount

    let label = $"{count} {nounOf notification.Type}"

    let toggle =
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(6.0, 2.0, 6.0, 2.0)
        .Margin(0.0, 6.0, 0.0, 0.0)
        .Content(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(4.0)
            .Children(
              Icons.iconAt(Glyph.Collapse, 14.0),
              TextBlock().Classes(Theme.name Theme.Mono).Text(label)
            )
        )
        .OnClickHandler(fun _ args ->
          args.Handled <- true

          let next = not state.Open.Value

          state.Open.OnNext next

          if next then
            loadGroup(notification, state))
        .IsVisible(notification.EventCount > 1)

    let events =
      ContentControl()
        .Content(
          state.Items
          |> Observable.map(fun items ->
            StackPanel().Margin(24.0, 0.0, 0.0, 0.0).Children [|
              for item in items -> eventRow item
            |]
            : Control)
          |> _.ToBinding()
        )
        .IsVisible(state.Open)

    let failed =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Margin(0.0, 6.0, 0.0, 0.0)
        .Text("fatal: could not load")
        .IsVisible(state.Failed)

    StackPanel().Children(toggle, events, failed)

  let row(notification: Notification) : Control =
    let state = stateOf notification

    let where =
      commitOf notification |> Option.map(fun commit -> "/commit/" + commit.Id)

    let identity =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .Text(notification.Actor.Name),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text($"@{notification.Actor.Username}")
            .TextTrimming(TextTrimming.CharacterEllipsis),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text(timeAgo(DateTimeOffset.UtcNow, notification.CreatedAt))
        )

    let action =
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Margin(0.0, 2.0, 0.0, 0.0)
        .Text(actionOf notification.Type)

    let children: Control array = [|
      identity
      action

      match excerptBox notification with
      | Some excerpt -> excerpt
      | None -> ()

      groupHost(notification, state)
    |]

    let content = StackPanel().Margin(0.0, 12.0, 16.0, 12.0).Children(children)

    let contentColumn =
      Border()
        .BorderBrush(Brushes.border)
        .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
        .Child(content)

    let grid = Grid()

    grid.ColumnDefinitions <- ColumnDefinitions("64,*")

    Button()
      .Classes(Theme.name Theme.CommitRow)
      .Content(
        grid.Children(railCell(notification).Column(0), contentColumn.Column(1))
      )
      .OnClickHandler(fun _ _ ->
        where |> Option.iter(fun url -> Navigation.go(props.Nav, url, ignore)))

  let feedRow(inboxRow: InboxRow) : Control =
    match inboxRow with
    | InboxRow.Day label -> dayRow label
    | InboxRow.Entry notification -> row notification

  let list =
    Feed.view {
      Items = model |> Observable.map(fun current -> toRows current.Items)
      KeyOf = rowKey
      Row = feedRow
      Footer = ValueSome loadMore
    }

  let pinned =
    StackPanel()
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Margin(Theme.pinnedPadding)
      .Children(title, pillRow, errorPanel, busy, emptyState)

  let root = Grid()
  root.RowDefinitions <- RowDefinitions("Auto,*")

  root.Children(pinned.Row(0), list.Row(1))

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram (init props) (update props) (view props)

    this.Content <- Program.runWith () program
