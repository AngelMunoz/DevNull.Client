module DevNull.Client.Views.NotificationsView

open System
open System.Collections.Generic
open Avalonia.Controls
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
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
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
  Items: Notification list
  NextCursor: string option
}

type Msg =
  | Fetched of Result<NotificationPage, AppError>
  | Appended of Result<NotificationPage, AppError>
  | MoreRequested

let initial = {
  Loading = true
  Error = None
  Items = []
  NextCursor = None
}

let fetch(env, session: SignedInSession, cursor: string option) =
  Notifications.list(
    env,
    {
      KeyFile = session.KeyFile
      Filter = None
      Through = None
      Cursor = cursor
    }
  )

let init (props: Props<'env>) () =
  initial, Cmd.OfAsync.perform fetch (props.Env, props.Session, None) Fetched

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
  | MoreRequested ->
    model,
    Cmd.OfAsync.perform
      fetch
      (props.Env, props.Session, model.NextCursor)
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

let row (props: Props<'env>) (notification: Notification) : Control =
  let where =
    match notification.Commit with
    | Some(LiveCommit commit) -> "/commit/" + commit.Id
    | _ -> ""

  Button()
    .Margin(0.0, 0.0, 0.0, 8.0)
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .HorizontalContentAlignment(HorizontalAlignment.Stretch)
    .Background(Brushes.transparent)
    .Content(
      Border()
        .Classes(Theme.name Theme.Card)
        .Child(
          StackPanel()
            .Spacing(2.0)
            .Children(
              StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(6.0)
                .Children(
                  TextBlock()
                    .Classes(Theme.name Theme.Subtitle)
                    .Text(notification.Actor.Name),
                  TextBlock()
                    .Classes(Theme.name Theme.Muted)
                    .Text($"@{notification.Actor.Username}"),
                  TextBlock()
                    .Classes(Theme.name Theme.Muted)
                    .Text(
                      timeAgo(DateTimeOffset.UtcNow, notification.CreatedAt)
                    )
                ),
              TextBlock()
                .Classes(Theme.name Theme.Muted)
                .Text(notification.Type),
              match notification.Commit with
              | Some(LiveCommit commit) ->
                TextBlock()
                  .Text(commit.Body)
                  .TextWrapping(TextWrapping.Wrap)
                  .MaxLines(2)
                  .TextTrimming(TextTrimming.CharacterEllipsis)
              | Some(Tombstone _) ->
                TextBlock()
                  .Classes(Theme.name Theme.Muted)
                  .Text("a deleted commit")
              | None -> TextBlock().Text("")
            )
        )
    )
    .OnClickHandler(fun _ _ ->
      if where <> "" then
        Navigation.go(props.Nav, where, ignore))

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let unread = props.Session.Session.UnreadNotificationsCount

  let unreadLabel =
    if unread > 0 then
      $"Notifications · {unread} unread"
    else
      "Notifications"

  let title = TextBlock().Classes(Theme.name Theme.Title).Text(unreadLabel)

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
    Status.busy "Loading the inbox…"
    |> fun control -> control.IsVisible(model |> Observable.map _.Loading)

  let emptyState =
    Status.empty "Nothing here yet."
    |> fun control ->
      control.IsVisible(
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
      Items = model |> Observable.map _.Items
      KeyOf = _.Id
      Row = row props
      Footer = ValueSome loadMore
    }

  let pinned =
    StackPanel()
      .MaxWidth(720.0)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Margin(Theme.pagePadding)
      .Children(title, errorPanel, busy, emptyState)

  let root = Grid()
  root.RowDefinitions <- RowDefinitions("Auto,*")

  root.Children(pinned.Row(0), list.Margin(24.0, 0.0, 24.0, 0.0).Row(1))
  |> ignore

  root

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
