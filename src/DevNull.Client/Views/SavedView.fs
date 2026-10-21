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
  | StarToggled of Commit
  | StarDone of Result<Commit, AppError>
  | PastelGiven of Commit
  | PastelDone of Result<PastelResult, AppError>

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
  | StarToggled commit ->
    let toggle() =
      let request: Stars.ActionRequest = {
        KeyFile = props.Session.KeyFile
        CommitId = commit.Id
      }

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
    Status
      .busy("Loading the saved list…")
      .IsVisible(model |> Observable.map _.Loading)

  let emptyState =
    Status
      .empty("Save commits to find them here.")
      .IsVisible(
        model
        |> Observable.map(fun current ->
          not current.Loading && current.Error.IsNone && current.Items.IsEmpty)
      )

  let row(node: CommitNode) : Control =
    match node with
    | LiveCommit commit ->
      CommitCard.view {
        Commit = commit
        Pastels = props.Session.Session.PasteisMode
        Dense = false
        OnEvent =
          fun event ->
            match event with
            | CommitCard.Opened c ->
              Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
            | CommitCard.StarToggled c -> dispatch(StarToggled c)
            | CommitCard.PastelGiven c -> dispatch(PastelGiven c)
            | CommitCard.QuoteRequested _ -> ()
      }
    | Tombstone tombstone -> CommitCard.tombstone tombstone

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
      KeyOf = nodeKey
      Row = row
      Footer = ValueSome loadMore
    }

  let pinned =
    StackPanel()
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Margin(Theme.pinnedPadding)
      .Children(title, errorPanel, busy, emptyState)

  let root = Grid()
  root.RowDefinitions <- RowDefinitions("Auto,*")

  root.Children(pinned.Row(0), list.Row(1))

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram (init props) (update props) (view props)

    this.Content <- Program.runWith () program
