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
open IcedTasks
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open Navs
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Navigation
open DevNull.Client.Profiles
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.UiClock

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
}

/// The page's state logic: model, messages, update, the row shaping,
/// and the subscriptions that feed it from outside.
module State =

  /// One notification group's expansion: the toggle, the events the
  /// group endpoint returned, and whether that read failed.
  type GroupVm = {
    Open: bool
    Items: Notification list
    Failed: bool
  }

  let closedGroup: GroupVm = {
    Open = false
    Items = []
    Failed = false
  }

  type Model = {
    Loading: bool
    Error: AppError option
    Filter: string option
    Items: Notification list
    NextCursor: string option
    Groups: Map<string, GroupVm>
    /// The unread count of the signed-in session, live from the store.
    Unread: int
    /// The shared clock's last tick, for the relative times.
    Now: DateTimeOffset
  }

  type Msg =
    | Fetched of Result<NotificationPage, AppError>
    | FilterChosen of filter: string option
    | Appended of Result<NotificationPage, AppError>
    | MoreRequested
    | GroupToggled of Notification
    | GroupLoaded of groupId: string * Result<Page<Notification>, AppError>
    | ClockTicked of moment: DateTimeOffset
    | SessionChanged of state: SessionState

  let initial = {
    Loading = true
    Error = None
    Filter = None
    Items = []
    NextCursor = None
    Groups = Map.empty
    Unread = 0
    Now = DateTimeOffset.UtcNow
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
    {
      initial with
          Unread = props.Session.Session.UnreadNotificationsCount
    },
    Cmd.OfAsync.perform
      fetch
      (props.Env,
       {
         Session = props.Session
         Filter = None
         Cursor = None
       })
      Fetched

  /// Reads one group's events: the first event carries the id the group
  /// endpoint takes.
  let loadGroup
    (env, session: SignedInSession, notification: Notification)
    : Async<Result<Page<Notification>, AppError>> =
    Notifications.group(
      env,
      {
        KeyFile = session.KeyFile
        NotificationId = notification.Id
        Filter = None
        Cursor = None
      }
    )

  let groupOf(notification: Notification, model: Model) : GroupVm =
    model.Groups
    |> Map.tryFind notification.GroupId
    |> Option.defaultValue closedGroup

  /// The inbox title: the unread count when one exists.
  let unreadTitle(model: Model) : string =
    if model.Unread > 0 then
      $"Notifications · {model.Unread} unread"
    else
      "Notifications"

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
              Groups = Map.empty
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
    | GroupToggled notification ->
      let group = groupOf(notification, model)

      if group.Open then
        {
          model with
              Groups =
                model.Groups.Add(
                  notification.GroupId,
                  { group with Open = false }
                )
        },
        Cmd.none
      else if List.isEmpty group.Items then
        {
          model with
              Groups =
                model.Groups.Add(
                  notification.GroupId,
                  { group with Open = true }
                )
        },
        Cmd.OfAsync.perform
          loadGroup
          (props.Env, props.Session, notification)
          (fun result -> GroupLoaded(notification.GroupId, result))
      else
        {
          model with
              Groups =
                model.Groups.Add(
                  notification.GroupId,
                  { group with Open = true }
                )
        },
        Cmd.none
    | GroupLoaded(groupId, result) ->
      let group =
        model.Groups |> Map.tryFind groupId |> Option.defaultValue closedGroup

      let next =
        match result with
        | Ok page -> {
            group with
                Items = page.Items
                Failed = false
          }
        | Error _ -> { group with Failed = true }

      {
        model with
            Groups = model.Groups.Add(groupId, next)
      },
      Cmd.none
    | ClockTicked moment -> { model with Now = moment }, Cmd.none
    | SessionChanged state ->
      let unread =
        match state with
        | SignedIn entry -> entry.Session.UnreadNotificationsCount
        | SignedOut -> 0

      { model with Unread = unread }, Cmd.none

  /// The page's inlets from outside: the shared clock and the session
  /// store.
  let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
    [
      Sub.fromObservable
        "clock"
        (props.Env.UiClock.Minute |> Observable.map ClockTicked)

      Sub.fromObservable
        "session"
        (props.Env.Session.Observe |> Observable.map SessionChanged)
    ]
    |> Sub.batch

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

/// The page's semantic regions; the view only composes these.
module Parts =

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
      dispatch: Dispatch<State.Msg>,
      pair: string * string
    ) : Control =
    let wire, label = pair

    let pill =
      Button()
        .Classes(Theme.name Theme.FilterPill)
        .Content(label)
        .OnClickHandler(fun _ _ -> dispatch(State.FilterChosen(Some wire)))

    Theme.bindClass(
      pill,
      Theme.FilterPillActive,
      active |> Observable.map(fun current -> current = Some wire)
    )

    pill

  /// The pill row of filters over the inbox.
  let pillRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let panel = WrapPanel()

    panel.Margin <- Thickness(0.0, 8.0, 0.0, 12.0)
    panel.ItemSpacing <- 8.0

    panel.Children [|
      for pair in filters ->
        filterPill(model |> Observable.map _.Filter, dispatch, pair)
    |]

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

  /// The rail cell of one notification: the neutral rail with the
  /// actor's avatar (the types about their commit) or the type's icon in
  /// a ring. The avatar carries the hover card.
  let railCell
    (
      cache: ImageCacheService,
      flyout: Source voption,
      notification: Notification
    ) : Control =
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
        let plain =
          Avatar.view {
            Name = notification.Actor.Name
            Url = notification.Actor.AvatarUrl
            Lane = notification.Actor.Lane
            Size = 40.0
            Cache = cache
          }

        let avatar =
          match flyout with
          | ValueSome source ->
            UserFlyout.attach(source, notification.Actor, plain)
          | ValueNone -> plain

        Border()
          .Background(Brushes.card)
          .BorderBrush(Brushes.rail)
          .BorderThickness(Thickness(2.0))
          .CornerRadius(40.0)
          .Padding(1.0)
          .HorizontalAlignment(HorizontalAlignment.Center)
          .VerticalAlignment(VerticalAlignment.Top)
          .Margin(0.0, 11.0, 0.0, 0.0)
          .Child(avatar)
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

  /// One expanded event inside a group: the actor, the action, and the
  /// excerpt, off a short spine.
  let eventRow
    (
      cache: ImageCacheService,
      notification: Notification,
      now: IObservable<DateTimeOffset>
    ) : Control =
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
            .Text(
              now
              |> Observable.map(fun moment ->
                timeAgo(moment, notification.CreatedAt))
            )
        )

    let children: Control array = [|
      identity
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Text(actionOf notification.Type)

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
          Cache = cache
        },
        StackPanel()
          .VerticalAlignment(VerticalAlignment.Center)
          .Spacing(4.0)
          .Children(children)
      )

  /// What one row's parts read: the router, the dispatch, the model.
  type RowCtx = {
    Nav: INavigable
    Dispatch: Dispatch<State.Msg>
    Model: IObservable<State.Model>
    Images: ImageCacheService
    Flyout: Source voption
  }

  /// One group's expansion host: the toggle line and, while open, the
  /// events the group endpoint returned. Every piece binds the group's
  /// model entry, so a recycled row never keeps stale state.
  let groupHost(ctx: RowCtx, notification: Notification) : Control =
    let count = notification.EventCount

    let label = $"{count} {nounOf notification.Type}"

    let group =
      ctx.Model
      |> Observable.map(fun current -> State.groupOf(notification, current))

    let now = ctx.Model |> Observable.map _.Now

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
          ctx.Dispatch(State.GroupToggled notification))
        .IsVisible(notification.EventCount > 1)

    let events =
      let content =
        group
        |> Observable.map(fun current ->
          StackPanel().Margin(24.0, 0.0, 0.0, 0.0).Children [|
            for item in current.Items -> eventRow(ctx.Images, item, now)
          |])

      ContentControl()
        .Content(content.ToBinding())
        .IsVisible(group |> Observable.map _.Open)

    let failed =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Margin(0.0, 6.0, 0.0, 0.0)
        .Text("fatal: could not load")
        .IsVisible(group |> Observable.map _.Failed)

    StackPanel().Children(toggle, events, failed)

  /// One inbox row: the rail, the identity line, the action, the
  /// excerpt, and the group host.
  let row (ctx: RowCtx) (notification: Notification) : Control =
    let where =
      commitOf notification |> Option.map(fun commit -> "/commit/" + commit.Id)

    let now = ctx.Model |> Observable.map _.Now

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
            .Text(
              now
              |> Observable.map(fun moment ->
                timeAgo(moment, notification.CreatedAt))
            )
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

      groupHost(ctx, notification)
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
        grid.Children(
          railCell(ctx.Images, ctx.Flyout, notification).Column(0),
          contentColumn.Column(1)
        )
      )
      .OnClickHandler(fun _ _ ->
        where |> Option.iter(fun url -> Navigation.go(ctx.Nav, url, ignore)))

  /// The inbox itself: the virtualized feed over the page's rows.
  let feed
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : Control =
    let ctx: RowCtx = {
      Nav = props.Nav
      Dispatch = dispatch
      Model = model
      Images = props.Env.ImageCache
      Flyout =
        ValueSome {
          Fetch = props.Env.Profiles.Fetch
          OnOpen =
            fun username ->
              Navigation.go(props.Nav, "/profile/" + username, ignore)
          Cache = props.Env.ImageCache
        }
    }

    Feed.view {
      Items = model |> Observable.map(fun current -> State.toRows current.Items)
      KeyOf = State.rowKey
      Row =
        (fun inboxRow ->
          match inboxRow with
          | State.InboxRow.Day label -> FeedParts.dayRow label
          | State.InboxRow.Entry notification -> row ctx notification)
      Footer =
        ValueSome(
          PageChrome.loadMore(
            model |> Observable.map _.NextCursor.IsSome,
            fun () -> dispatch State.MoreRequested
          )
        )
    }

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  PageChrome.page {
    Model = model
    TitleOf = State.unreadTitle
    Padding = Theme.pinnedPadding
    ErrorOf = _.Error
    Busy = fun current -> current.Loading, "Loading the inbox…"
    Empty =
      fun current ->
        not current.Loading && current.Error.IsNone && current.Items.IsEmpty,
        "Nothing here yet. Follows, stars, and replies will show up here."
    Header = [ Parts.pillRow(model, dispatch) ]
    Body = Parts.feed(props, dispatch, model)
  }

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Program.runWithLifecycle () program

    this.Content <- root
    props.Context.AddDisposable lifetime
