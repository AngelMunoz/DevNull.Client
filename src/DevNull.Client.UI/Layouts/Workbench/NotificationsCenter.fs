module DevNull.Client.Workbench.NotificationsCenter

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Theme

type Model = {
  Loading: bool
  More: bool
  Error: AppError option
  Feed: FeedWindow.Window<Notification>
  Anchor: FeedWindow.Anchor voption
  Now: DateTimeOffset
}

type Msg =
  | Fetched of Result<NotificationPage, AppError>
  | MoreRequested
  | Appended of cursor: string option * Result<NotificationPage, AppError>
  | AnchorMoved of FeedWindow.Anchor
  | RefreshRequested
  | ClockTicked of moment: DateTimeOffset
  | Opened of Notification

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
  KeyFile: string
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  OnCheckout: string -> unit
}

let kindLine(notification: Notification) : string =
  match notification.Type with
  | NotificationKind.Followed -> "followed you"
  | NotificationKind.Replied ->
    let whose =
      notification.Organization
      |> Option.map(fun org -> $"@{org.Handle}'s")
      |> Option.defaultValue "your"

    $"replied to {whose} commit"
  | NotificationKind.Quoted ->
    let whose =
      notification.Organization
      |> Option.map(fun org -> $"@{org.Handle}'s")
      |> Option.defaultValue "your"

    $"quoted {whose} commit"
  | NotificationKind.Starred ->
    let whose =
      notification.Organization
      |> Option.map(fun org -> $"@{org.Handle}'s")
      |> Option.defaultValue "your"

    $"starred {whose} commit"
  | NotificationKind.Mentioned ->
    notification.Organization
    |> Option.map(fun org -> $"mentioned @{org.Handle}")
    |> Option.defaultValue "mentioned you"
  | NotificationKind.LeveledUp -> "sent an update"
  | NotificationKind.Approved ->
    notification.Organization
    |> Option.map(fun org -> $"@{org.Handle}'s commit was approved")
    |> Option.defaultValue "Your commit was approved"
  | NotificationKind.Rejected ->
    notification.Organization
    |> Option.map(fun org -> $"@{org.Handle}'s commit was rejected")
    |> Option.defaultValue "Your commit was rejected"
  | NotificationKind.FirstCommit -> "made their first commit"
  | NotificationKind.UnknownKind _ -> "sent an update"

let groupLine(notification: Notification) : string option =
  if notification.EventCount <= 1 then
    None
  else
    let noun =
      match notification.Type with
      | NotificationKind.Followed -> "followers"
      | NotificationKind.Starred -> "stars"
      | _ -> "updates"

    Some $"{notification.EventCount} {noun}"

let inline isUnread(notification: Notification) = notification.UnreadCount > 0

let fetch
  (props: Props<'env>, cursor: string option)
  : Async<Result<NotificationPage, AppError>> =
  Notifications.list(
    props.Env,
    {
      KeyFile = props.KeyFile
      Filter = None
      Through = None
      Cursor = cursor
    }
  )

let windowLimit = 300

let ops: FeedWindow.Ops<Notification> = {
  KeyOf = fun notification -> notification.Id
}

let itemsOf(model: Model) = FeedWindow.items model.Feed

let olderOf(model: Model) = FeedWindow.older model.Feed

let spanOf
  (cursor: string option, page: NotificationPage)
  : FeedWindow.Chunk<Notification> = {
  Request = ValueOption.ofOption cursor
  Items = Array.ofList page.Page.Items
  Older = ValueOption.ofOption page.Page.NextCursor
  Newer = ValueNone
}

let withPage
  (items: Notification list, nextCursor: string option, model: Model)
  : Model = {
  model with
      Feed =
        FeedWindow.addOlder(
          ops,
          {
            Request = ValueNone
            Items = Array.ofList items
            Older = ValueOption.ofOption nextCursor
            Newer = ValueNone
          },
          FeedWindow.empty(FeedWindow.NewestFirst, windowLimit)
        )
}

let trimFeed(model: Model, feed: FeedWindow.Window<Notification>) =
  FeedWindow.trim(
    {
      Ops = ops
      Keep = model.Anchor |> ValueOption.map _.Key
      Edge = FeedWindow.OlderEdge
    },
    feed
  )

let commitOf(notification: Notification) : Commit option =
  match notification.Commit with
  | Some(LiveCommit commit) -> Some commit
  | _ -> None

// One feed row with the chunk request cursor that fetched it, so
// the anchor the feed reports names the page the row came from.
[<Struct>]
type AnchoredNotification = {
  Notification: Notification
  Entry: string option
}

let entryOf(anchored: AnchoredNotification) : string option = anchored.Entry

let init(props: Props<'env>) : Model * Cmd<Msg> =
  {
    Loading = true
    More = false
    Error = None
    Feed = FeedWindow.empty(FeedWindow.NewestFirst, windowLimit)
    Anchor = ValueNone
    Now = DateTimeOffset.UtcNow
  },
  Cmd.OfAsync.perform fetch (props, None) Fetched

let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
  match msg with
  | Fetched result ->
    match result with
    | Ok page ->
      let feed =
        FeedWindow.addOlder(
          ops,
          spanOf(None, page),
          FeedWindow.empty(FeedWindow.NewestFirst, windowLimit)
        )
        |> fun feed -> trimFeed(model, feed)

      {
        model with
            Loading = false
            Error = None
            Feed = feed
      },
      Cmd.none
    | Error error ->
      {
        model with
            Loading = false
            Error = Some error
      },
      Cmd.none
  | MoreRequested ->
    match olderOf model with
    | ValueSome cursor when not model.More && not model.Loading ->
      { model with More = true; Error = None },
      Cmd.OfAsync.perform fetch (props, Some cursor) (fun result ->
        Appended(Some cursor, result))
    | _ -> model, Cmd.none
  | Appended(cursor, result) ->
    match result with
    | Ok page ->
      let feed =
        FeedWindow.addOlder(ops, spanOf(cursor, page), model.Feed)
        |> fun feed -> trimFeed(model, feed)

      {
        model with
            More = false
            Error = None
            Feed = feed
      },
      Cmd.none
    | Error error ->
      {
        model with
            More = false
            Error = Some error
      },
      Cmd.none
  | RefreshRequested ->
    if model.Loading || model.More then
      model, Cmd.none
    else
      {
        model with
            Loading = true
            Error = None
      },
      Cmd.OfAsync.perform fetch (props, None) Fetched
  | AnchorMoved anchor -> { model with Anchor = ValueSome anchor }, Cmd.none
  | ClockTicked moment -> { model with Now = moment }, Cmd.none
  | Opened notification ->
    match commitOf notification with
    | Some commit -> model, Cmd.ofAction(fun () -> props.OnCheckout commit.Id)
    | None -> model, Cmd.none

let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
  Sub.fromObservable "clock" (props.Now |> Observable.map ClockTicked)

let bodyExcerpt(commit: Commit) : string =
  let body =
    commit.Body
    |> plainText
    |> fun text -> text.Replace("\r", " ").Replace("\n", " ")

  if body.Length > 140 then
    body.Substring(0, 140) + "…"
  else
    body

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let now = model |> Observable.map _.Now |> Slices.share

  let row(notification: Notification) : Control =
    let unread =
      Border()
        .Name("NotificationUnreadDot")
        .Width(6.0)
        .Height(6.0)
        .CornerRadius(3.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Classes(Theme.name Theme.AccentDot)
        .IsVisible(isUnread notification)

    let avatar =
      Avatar.view {
        Name = Author.name notification.Actor
        Url = Author.avatarUrl notification.Actor
        Lane = Author.lane notification.Actor
        Size = 32.0
        Cache = props.Images
        Square = Author.isOrganization notification.Actor
        Viewable = false
      }

    let identity =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text("@" + Author.handle notification.Actor),
          TextBlock()
            .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
            .Text(
              now
              |> Observable.distinctUntilChangedKey minuteBucket
              |> Observable.map(fun moment ->
                timeAgo(moment, notification.CreatedAt))
            )
        )

    let group =
      match groupLine notification with
      | Some line ->
        TextBlock().Classes(Theme.name Theme.Mono).Text(line) :> Control
      | None -> Panel() :> Control

    let excerpt =
      match commitOf notification with
      | Some commit ->
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .Text(bodyExcerpt commit)
          .TextWrapping(TextWrapping.NoWrap)
          .TextTrimming(TextTrimming.CharacterEllipsis)
        :> Control
      | None -> Panel() :> Control

    let content =
      StackPanel()
        .Spacing(2.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Children(
          identity,
          TextBlock().Text(kindLine notification),
          group,
          excerpt
        )

    let grid = Grid()

    grid.ColumnDefinitions <- ColumnDefinitions("Auto,Auto,*")

    grid.Children(
      unread.Column(0).Margin(Thickness(0.0, 0.0, 8.0, 0.0)),
      avatar.Column(1).Margin(Thickness(0.0, 0.0, 10.0, 0.0)),
      content.Column(2)
    )
    |> ignore

    Button()
      .Name("NotificationRow")
      .Classes(Theme.name Theme.CommitRow)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .Padding(Thickness(12.0, 8.0))
      .Content(grid)
      .OnClickHandler(fun _ _ -> dispatch(Opened notification))

  let refresh =
    Button()
      .Name("NotificationsRefresh")
      .Classes(Theme.name Theme.GhostButton)
      .Content("Refresh")
      .OnClickHandler(fun _ _ -> dispatch RefreshRequested)

  let head = DockPanel().LastChildFill(true)

  head.Children(
    refresh.DockRight(),
    TextBlock().Classes(Theme.name Theme.Mono).Text("NOTIFICATIONS")
  )
  |> ignore

  let notice =
    TextBlock()
      .Name("NotificationsNotice")
      .Classes(Theme.name Theme.Muted)
      .TextWrapping(TextWrapping.Wrap)
      .Text("Read state stays with the server.")

  let loadingLine =
    PageChrome.collapsingMessagePanel(
      model
      |> Slices.gateByRefKey (fun current -> itemsOf current) (fun current ->
        current.Loading)
      |> Observable.map(fun current ->
        if current.Loading && (itemsOf current).IsEmpty then
          "Reading the inbox…"
        elif current.Loading then
          "Refreshing…"
        else
          "")
      |> Slices.share,
      Theme.Muted
    )

  let emptyLine =
    PageChrome.collapsingMessagePanel(
      model
      |> Slices.gateByRefKey (fun current -> itemsOf current) (fun current ->
        current.Loading, current.Error)
      |> Observable.map(fun current ->
        if
          current.Error.IsNone
          && not current.Loading
          && (itemsOf current).IsEmpty
        then
          "Nothing here yet."
        else
          "")
      |> Slices.share,
      Theme.Muted
    )

  let errorLine = PageChrome.errorPanel(model |> Observable.map _.Error)

  let hasOlder =
    model
    |> Observable.map(fun current -> olderOf current |> ValueOption.isSome)
    |> Slices.share

  let items =
    model
    |> Observable.map(fun current -> current.Feed)
    |> Slices.gateByRef
    |> Observable.map(fun feed ->
      FeedWindow.items feed
      |> List.map(fun notification -> {
        Notification = notification
        Entry =
          FeedWindow.entryOf(ops, notification.Id, feed)
          |> ValueOption.toOption
      }))
    |> Slices.share

  let header =
    StackPanel()
      .Spacing(6.0)
      .Margin(Thickness(16.0, 16.0, 16.0, 8.0))
      .Children(head, notice, loadingLine, emptyLine, errorLine)

  let feed =
    Feed.viewWith(
      {
        Header = ValueSome header
        Items = items
        KeyOf = fun anchored -> anchored.Notification.Id
        Row = fun anchored -> row anchored.Notification
        Footer = ValueNone
        HasMore = hasOlder
        ScrollTo = ValueNone
        Ride = false
        Restore = ValueNone
        OnScroll = ValueNone
        OnRide = ValueNone
        OnNeedMore = ValueNone
      },
      {
        Feed.Advanced.none() with
            AnchorKeyOf =
              ValueSome(fun anchored -> Some anchored.Notification.Id)
            EntryCursorOf = ValueSome entryOf
            RestoreAnchor = ValueNone
            OnAnchor = ValueSome(fun anchor -> dispatch(AnchorMoved anchor))
            Paging =
              ValueSome {
                Order = FeedWindow.NewestFirst
                HasOlder = hasOlder
                HasNewer = Observable.single false
                OnOlder = (fun () -> dispatch MoreRequested)
                OnNewer = ignore
              }
      }
    )

  feed.Name("NotificationsCenter")

let program(props: Props<'env>) =
  Program.mkProgram
    (fun () -> init props)
    (fun msg model -> update(props, msg, model))
    (view props)
  |> Program.withSubscription(subscribe props)
