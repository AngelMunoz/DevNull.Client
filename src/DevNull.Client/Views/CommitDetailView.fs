module DevNull.Client.Views.CommitDetailView

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Shapes
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
open DevNull.Client.UiClock

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock> = {
  Env: 'env
  CommitId: string
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
    Page: CommitPage option
    Replies: CommitNode list
    RepliesCursor: string option
    Note: string option
    Now: DateTimeOffset
    Pasteis: bool
  }

  type Msg =
    | ThreadFetched of Result<CommitPage, AppError>
    | RepliesFetched of Result<Page<CommitNode>, AppError>
    | ReloadRequested
    | MoreReplies
    | CardEvent of CommitCard.Event
    | PastelRequested of commitId: string
    | DeleteRequested
    | StarDone of Result<Commit, AppError>
    | PastelGiven of Result<PastelResult, AppError>
    | Deleted of Result<unit, AppError>
    | ClockTicked of moment: DateTimeOffset
    | SessionChanged of state: SessionState

  let initial = {
    Loading = true
    Error = None
    Page = None
    Replies = []
    RepliesCursor = None
    Note = None
    Now = DateTimeOffset.UtcNow
    Pasteis = false
  }

  let thread(env, session: SignedInSession, commitId: string) =
    Commits.show(
      env,
      {
        KeyFile = session.KeyFile
        CommitId = commitId
      }
    )

  let replies
    (env, session: SignedInSession, commitId: string, cursor: string option)
    =
    Commits.replies(
      env,
      {
        KeyFile = session.KeyFile
        CommitId = commitId
        Newest = false
        Cursor = cursor
      }
    )

  let init (props: Props<'env>) () =
    {
      initial with
          Pasteis = props.Session.Session.PasteisMode
    },
    Cmd.batch [
      Cmd.OfAsync.perform
        thread
        (props.Env, props.Session, props.CommitId)
        ThreadFetched

      Cmd.OfAsync.perform
        replies
        (props.Env, props.Session, props.CommitId, None)
        RepliesFetched
    ]

  let withCommit(updated: Commit, model: Model) : Model = {
    model with
        Page =
          model.Page |> Option.map(fun page -> { page with Commit = updated })
        Replies =
          model.Replies
          |> List.map(function
            | LiveCommit commit when commit.Id = updated.Id ->
              LiveCommit updated
            | node -> node)
  }

  let update (props: Props<'env>) msg model =
    match msg with
    | ThreadFetched result ->
      match result with
      | Ok page ->
        {
          model with
              Loading = false
              Error = None
              Page = Some page
        },
        Cmd.none
      | Error error ->
        {
          model with
              Loading = false
              Error = Some error
        },
        Cmd.none
    | RepliesFetched result ->
      match result with
      | Ok page ->
        {
          model with
              Replies = model.Replies @ page.Items
              RepliesCursor = page.NextCursor
        },
        Cmd.none
      | Error error -> { model with Error = Some error }, Cmd.none
    | ReloadRequested ->
      {
        model with
            Loading = true
            Error = None
      },
      Cmd.OfAsync.perform
        thread
        (props.Env, props.Session, props.CommitId)
        ThreadFetched
    | MoreReplies ->
      model,
      Cmd.OfAsync.perform
        replies
        (props.Env, props.Session, props.CommitId, model.RepliesCursor)
        RepliesFetched
    | CardEvent(CommitCard.StarToggled commit) ->
      { model with Error = None },
      Cmd.OfAsync.perform
        CommitActions.toggleStar
        (props.Env, props.Session, commit)
        StarDone
    | CardEvent(CommitCard.Opened commit) ->
      Navigation.go(props.Nav, "/commit/" + commit.Id, ignore)
      model, Cmd.none
    | CardEvent(CommitCard.PastelGiven commit) ->
      { model with Error = None; Note = None },
      Cmd.ofMsg(PastelRequested commit.Id)
    | CardEvent(CommitCard.QuoteRequested commit) ->
      Navigation.go(props.Nav, "/quote/" + commit.Id, ignore)
      model, Cmd.none
    | PastelRequested commitId ->
      let give() =
        Pasteis.give(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            CommitId = commitId
          }
        )

      { model with Error = None; Note = None },
      Cmd.OfAsync.perform give () PastelGiven
    | DeleteRequested ->
      let remove() =
        Commits.delete(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            CommitId = props.CommitId
          }
        )

      { model with Error = None }, Cmd.OfAsync.perform remove () Deleted
    | StarDone result ->
      match result with
      | Ok updated -> withCommit(updated, model), Cmd.none
      | Error error -> { model with Error = Some error }, Cmd.none
    | PastelGiven result ->
      match result with
      | Ok pastel ->
        {
          withCommit(pastel.Commit, model) with
              Note =
                Some $"Pastel given. {pastel.PasteisRemaining} left this cycle."
        },
        Cmd.none
      | Error error -> { model with Error = Some error }, Cmd.none
    | Deleted result ->
      match result with
      | Ok _ ->
        Navigation.go(props.Nav, "/log/latest", ignore)
        model, Cmd.none
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

  /// The card context every thread card shares: events route into the
  /// page's card message.
  let commitCtx
    (dispatch: Dispatch<State.Msg>, model: IObservable<State.Model>, dense: bool) : CommitCard.RowProps = {
    OnEvent = State.CardEvent >> dispatch
    Pasteis = model |> Observable.map _.Pasteis
    Now = model |> Observable.map _.Now
    Dense = dense
  }

  /// One reply row: indented off a dashed spine, dense card.
  let replyRow
    (
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>,
      node: CommitNode
    ) : Control =
    match node with
    | LiveCommit commit ->
      let spine =
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

      let grid = Grid()

      grid.ColumnDefinitions <- ColumnDefinitions("32,*")

      let card = CommitCard.feedRow (commitCtx(dispatch, model, true)) node

      grid.Children(spine.Column(0), card.Column(1))
    | Tombstone _ -> CommitCard.tombstone()

  /// One ancestor hop above the card: the author and a one-line body.
  let ancestorRow(props: Props<'env>, node: CommitNode) : Control =
    match node with
    | LiveCommit commit ->
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(4.0, 2.0, 4.0, 2.0)
        .Content(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(6.0)
            .Children(
              TextBlock().Text($"@{commit.Author.Username}"),
              TextBlock()
                .Classes(Theme.name Theme.Muted)
                .Text(commit.Body)
                .MaxLines(1)
                .TextTrimming(TextTrimming.CharacterEllipsis)
            )
        )
        .OnClickHandler(fun _ _ ->
          Navigation.go(props.Nav, "/commit/" + commit.Id, ignore))
    | Tombstone _ ->
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Text("deleted")
        .Margin(4.0, 2.0)

  /// The back affordance above the thread.
  let backLine(props: Props<'env>) : Control =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Content("Back to the log")
      .OnClickHandler(fun _ _ ->
        Navigation.go(props.Nav, "/log/latest", ignore))

  /// The transient note line (the pastel cycle count after a give).
  let noteLine(model: IObservable<State.Model>) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Text(
        model
        |> Observable.map(fun current -> current.Note |> Option.defaultValue "")
      )
      .IsVisible(model |> Observable.map _.Note.IsSome)

  /// The loading line while the thread reads.
  let busy(model: IObservable<State.Model>) : Control =
    Status
      .busy("Loading the commit…")
      .IsVisible(model |> Observable.map _.Loading)

  // The card is rebuilt whenever the commit changes: the snapshot keeps
  // the star visual honest.
  let cardHost
    (dispatch: Dispatch<State.Msg>, model: IObservable<State.Model>)
    : Control =
    let content =
      model
      |> Observable.map(fun current ->
        match current.Page with
        | Some page ->
          CommitCard.feedRow
            (commitCtx(dispatch, model, false))
            (LiveCommit page.Commit)
        | None -> Status.empty "")

    ContentControl().Content(content.ToBinding())

  /// The pastel and delete affordances under the card.
  let threadActions
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : Control =
    let pastelButton =
      let button =
        Button()
          .Name("ThreadPastelButton")
          .Classes(Theme.name Theme.NavButton)
          .Content("Give a pastel")
          .OnClickHandler(fun _ _ ->
            dispatch(State.PastelRequested props.CommitId))

      // The control exists only while the mode is on.
      ContentControl()
        .Content(
          model
          |> Observable.map(fun current ->
            if current.Pasteis then button :> Control else Panel())
          |> _.ToBinding()
        )

    let deleteButton =
      Button()
        .Classes(Theme.name Theme.DangerButton)
        .Content("Delete commit")
        .IsVisible(
          model
          |> Observable.map(fun current ->
            match current.Page with
            | Some page ->
              page.Commit.Author.Id = props.Session.Session.User.Id
            | None -> false)
        )
        .OnClickHandler(fun _ _ -> dispatch State.DeleteRequested)

    // The viewer with pastéis mode off sees no pastel control at all,
    // the way the site hides them; the flag is live.
    let actionChildren: Control array = [| pastelButton; deleteButton |]

    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(8.0)
      .Margin(0.0, 8.0, 0.0, 12.0)
      .Children(actionChildren)

  /// The ancestor chain above the card.
  let ancestors(props: Props<'env>, model: IObservable<State.Model>) : Control =
    ItemsControl()
      .ItemTemplate(
        FuncDataTemplate<CommitNode>(
          (fun node _ -> ancestorRow(props, node)),
          false
        )
      )
      .BindItemsSource(
        model
        |> Observable.map(fun current ->
          match current.Page with
          | Some page -> page.Ancestors :> IReadOnlyList<CommitNode>
          | None -> [] :> IReadOnlyList<CommitNode>),
        FeedParts.nodeKey
      )

  /// The reply column under the card.
  let replies
    (dispatch: Dispatch<State.Msg>, model: IObservable<State.Model>)
    : Control =
    ItemsControl()
      .ItemTemplate(
        FuncDataTemplate<CommitNode>(
          (fun node _ -> replyRow(dispatch, model, node)),
          false
        )
      )
      .BindItemsSource(
        model
        |> Observable.map(fun current ->
          current.Replies :> IReadOnlyList<CommitNode>),
        FeedParts.nodeKey
      )

  /// The pager under the replies, while another page exists.
  let moreReplies
    (dispatch: Dispatch<State.Msg>, model: IObservable<State.Model>)
    : Control =
    Button()
      .Content("More replies")
      .Classes(Theme.name Theme.GhostButton)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Margin(0.0, 8.0, 0.0, 8.0)
      .IsVisible(model |> Observable.map _.RepliesCursor.IsSome)
      .OnClickHandler(fun _ _ -> dispatch State.MoreReplies)

  /// The reply composer under the thread; a landed reply reloads the
  /// page.
  let replyComposer
    (props: Props<'env>, dispatch: Dispatch<State.Msg>)
    : Control =
    Composer.Page {
      Env = props.Env
      Session = props.Session
      Target = Composer.ReplyTo props.CommitId
      OnCommitted = fun _ -> dispatch State.ReloadRequested
      Register = ValueNone
    }

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let content =
    StackPanel()
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Children(
        Parts.backLine props,
        PageChrome.errorPanel(model |> Observable.map _.Error),
        Parts.noteLine model,
        Parts.busy model,
        Parts.ancestors(props, model),
        Parts.cardHost(dispatch, model),
        Parts.threadActions(props, dispatch, model),
        Parts.replies(dispatch, model),
        Parts.moreReplies(dispatch, model),
        Parts.replyComposer(props, dispatch)
      )

  // The page scrolls one axis: a wrapped body and composer measure
  // against the column, never an unbounded extent.
  ScrollViewer()
    .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
    .Padding(Theme.pagePadding)
    .Content(content)

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
