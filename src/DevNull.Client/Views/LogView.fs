module DevNull.Client.Views.LogView

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
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
}

type Msg =
  | Fetched of Result<LogPage, AppError>
  | ReloadRequested
  | Appended of Result<LogPage, AppError>
  | MoreRequested
  | StarToggled of Commit
  | BookmarkToggled of Commit
  | StarDone of Result<Commit, AppError>
  | BookmarkDone of Result<Commit, AppError>

let initial = {
  Loading = true
  Error = None
  Items = []
  NextCursor = None
}

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
      },
      Cmd.none
    | Error error ->
      {
        model with
            Loading = false
            Error = Some error
      },
      Cmd.none
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
  | BookmarkToggled commit ->
    let toggle() =
      let request: Bookmarks.ActionRequest = {
        KeyFile = props.Session.KeyFile
        CommitId = commit.Id
      }

      if commit.IsSaved |> Option.contains true then
        Bookmarks.unsave(props.Env, request)
      else
        Bookmarks.save(props.Env, request)

    { model with Error = None }, Cmd.OfAsync.perform toggle () BookmarkDone
  | StarDone result ->
    match result with
    | Ok updated ->
      {
        model with
            Items = replaceNode(model.Items, updated)
      },
      Cmd.none
    | Error error -> { model with Error = Some error }, Cmd.none
  | BookmarkDone result ->
    match result with
    | Ok updated ->
      {
        model with
            Items = replaceNode(model.Items, updated)
      },
      Cmd.none
    | Error error -> { model with Error = Some error }, Cmd.none

let nodeKey(node: CommitNode) =
  match node with
  | LiveCommit commit -> commit.Id
  | Tombstone tombstone -> tombstone.Id

let row
  (props: Props<'env>, dispatch: Dispatch<Msg>)
  (node: CommitNode)
  : Control =
  match node with
  | LiveCommit commit ->
    CommitCard.view {
      Commit = commit
      OnEvent =
        fun event ->
          match event with
          | CommitCard.Opened c ->
            Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
          | CommitCard.StarToggled c -> dispatch(StarToggled c)
          | CommitCard.BookmarkToggled c -> dispatch(BookmarkToggled c)
          | CommitCard.QuoteRequested c ->
            Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
    }
  | Tombstone tombstone -> CommitCard.tombstone tombstone

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
  Button()
    .Content(info.Label)
    .Padding(12.0, 8.0, 12.0, 8.0)
    .CornerRadius(8.0)
    .Background(
      if active = info.Kind then
        Brushes.card :> IBrush
      else
        Brushes.transparent
    )
    .Foreground(if active = info.Kind then Brushes.text else Brushes.muted)
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
    Status.busy "Loading the log…"
    |> fun control -> control.IsVisible(model |> Observable.map _.Loading)

  let emptyState =
    Status.empty "Nothing here yet. Commit something."
    |> fun control ->
      control.IsVisible(
        model
        |> Observable.map(fun current ->
          not current.Loading && current.Error.IsNone && current.Items.IsEmpty)
      )

  let list =
    let template =
      FuncDataTemplate<CommitNode>(
        (fun node _ -> row (props, dispatch) node),
        false
      )

    let items = ItemsControl().ItemTemplate(template)

    items.BindItemsSource(
      model
      |> Observable.map(fun current ->
        current.Items :> IReadOnlyList<CommitNode>),
      nodeKey
    )

  let loadMore =
    Button()
      .Content("Load more")
      .Classes(Theme.name Theme.GhostButton)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Margin(0.0, 8.0, 0.0, 16.0)
      .IsVisible(model |> Observable.map _.NextCursor.IsSome)
      .OnClickHandler(fun _ _ -> dispatch MoreRequested)

  ScrollViewer()
    .Padding(24.0, 20.0, 24.0, 0.0)
    .Content(
      StackPanel()
        .MaxWidth(720.0)
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .Children(
          tabStrip,
          composer,
          errorPanel,
          busy,
          emptyState,
          list,
          loadMore
        )
    )

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram (init props) (update props) (view props)

    this.Content <- Program.runWith () program
