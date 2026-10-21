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
open DevNull.Client.UiClock

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
}

/// The page's state logic: model, messages, update, and the
/// subscriptions that feed it from outside.
module State =

  type Model = {
    Loading: bool
    Error: AppError option
    Items: CommitNode list
    NextCursor: string option
    Now: DateTimeOffset
    Pasteis: bool
  }

  type Msg =
    | Fetched of Result<Page<CommitNode>, AppError>
    | Appended of Result<Page<CommitNode>, AppError>
    | MoreRequested
    | StarToggled of Commit
    | StarDone of Result<Commit, AppError>
    | PastelGiven of Commit
    | PastelDone of Result<PastelResult, AppError>
    | ClockTicked of moment: DateTimeOffset
    | SessionChanged of state: SessionState

  let initial = {
    Loading = true
    Error = None
    Items = []
    NextCursor = None
    Now = DateTimeOffset.UtcNow
    Pasteis = false
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
    {
      initial with
          Pasteis = props.Session.Session.PasteisMode
    },
    Cmd.OfAsync.perform fetch (props.Env, props.Session, None) Fetched

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
    | ClockTicked moment -> { model with Now = moment }, Cmd.none
    | SessionChanged state ->
      let pasteis =
        match state with
        | SignedIn entry -> entry.Session.PasteisMode
        | SignedOut -> model.Pasteis

      { model with Pasteis = pasteis }, Cmd.none

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

/// The page's semantic regions; the view only composes these.
module Parts =

  /// The row context every saved card shares.
  let commitCtx
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : CommitCard.RowProps = {
    OnEvent =
      function
      | CommitCard.Opened c ->
        Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
      | CommitCard.StarToggled c -> dispatch(State.StarToggled c)
      | CommitCard.PastelGiven c -> dispatch(State.PastelGiven c)
      | CommitCard.QuoteRequested _ -> ()
    Pasteis = model |> Observable.map _.Pasteis
    Now = model |> Observable.map _.Now
    Dense = false
  }

  /// The saved list itself: the virtualized feed over the rows.
  let feed
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : Control =
    Feed.view {
      Items = model |> Observable.map _.Items
      KeyOf = FeedParts.nodeKey
      Row = CommitCard.feedRow(commitCtx(props, dispatch, model))
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
    TitleOf = fun _ -> "Saved"
    Padding = Theme.pinnedPadding
    ErrorOf = _.Error
    Busy = fun current -> current.Loading, "Loading the saved list…"
    Empty =
      fun current ->
        not current.Loading && current.Error.IsNone && current.Items.IsEmpty,
        "Save commits to find them here."
    Header = []
    Body = Parts.feed(props, dispatch, model)
  }

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Program.runWithLifecycle () program

    this.Content <- root
    props.Context.AddDisposable lifetime
