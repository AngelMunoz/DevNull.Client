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

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
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
    | Tombstone _ -> CommitCard.tombstone()

  let list =
    Feed.view {
      Items = model |> Observable.map _.Items
      KeyOf = FeedParts.nodeKey
      Row = row
      Footer =
        ValueSome(
          PageChrome.loadMore(
            model |> Observable.map _.NextCursor.IsSome,
            fun () -> dispatch MoreRequested
          )
        )
    }

  PageChrome.page {
    Model = model
    Title = "Saved"
    Padding = Theme.pinnedPadding
    ErrorOf = _.Error
    Busy = fun current -> current.Loading, "Loading the saved list…"
    Empty =
      fun current ->
        not current.Loading && current.Error.IsNone && current.Items.IsEmpty,
        "Save commits to find them here."
    Header = []
    Body = fun () -> list
  }

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram (init props) (update props) (view props)

    this.Content <- Program.runWith () program
