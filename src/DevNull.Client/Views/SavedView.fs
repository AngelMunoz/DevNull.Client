module DevNull.Client.Views.SavedView

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
open DevNull.Client.Navigation
open DevNull.Client.Stores
open DevNull.Client.Theme

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
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
  | Fetched of Result<Page<CommitNode>, AppError>
  | Appended of Result<Page<CommitNode>, AppError>
  | MoreRequested

let initial = {
  Loading = true
  Error = None
  Items = []
  NextCursor = None
}

let fetch(env, session: SignedInSession, cursor: string option) =
  Stars.index(
    env,
    {
      KeyFile = session.KeyFile
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
            Items = page.Items
            NextCursor = page.NextCursor
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
            Items = model.Items @ page.Items
            NextCursor = page.NextCursor
      },
      Cmd.none
    | Error error -> { model with Error = Some error }, Cmd.none

let nodeKey(node: CommitNode) =
  match node with
  | LiveCommit commit -> commit.Id
  | Tombstone tombstone -> tombstone.Id

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let title = TextBlock().Classes(Theme.name Theme.Title).Text("Saved")

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
    Status.busy "Loading the saved list…"
    |> fun control -> control.IsVisible(model |> Observable.map _.Loading)

  let emptyState =
    Status.empty "Save commits to find them here."
    |> fun control ->
      control.IsVisible(
        model
        |> Observable.map(fun current ->
          not current.Loading && current.Error.IsNone && current.Items.IsEmpty)
      )

  let list =
    let template =
      FuncDataTemplate<CommitNode>(
        (fun node _ ->
          match node with
          | LiveCommit commit ->
            CommitCard.view {
              Commit = commit
              OnEvent =
                fun event ->
                  match event with
                  | CommitCard.Opened c ->
                    Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
                  | _ -> ()
            }
          | Tombstone tombstone -> CommitCard.tombstone tombstone),
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
        .Children(title, errorPanel, busy, emptyState, list, loadMore)
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
