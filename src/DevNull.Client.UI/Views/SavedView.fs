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
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Env
open Microsoft.Extensions.Logging
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
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging> = {
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
    Voting: string option
    PollChoices: Map<string, string>
  }

  type Msg =
    | Fetched of Result<Page<CommitNode>, AppError>
    | Appended of Result<Page<CommitNode>, AppError>
    | MoreRequested
    | StarToggled of Commit
    | StarDone of Result<Commit, AppError>
    | PollChoiceChanged of Commit * PollOption * chosen: bool
    | PollVoteRequested of Commit
    | PollVotedDone of Result<Commit, AppError>
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
    Voting = None
    PollChoices = Map.empty
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
    let log = props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Saved"

    // Every log line is an effect: update returns it as a command,
    // and the loop writes it after the computation.
    let failed what error =
      Cmd.ofAction(fun () ->
        log.LogWarning(
          "The {What} failed: {Error}",
          what,
          bounded 300 (describe error)
        ))

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
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The saved list fetched {Count} commits.",
            page.Items.Length
          ))
      | Error error ->
        {
          model with
              Loading = false
              Error = Some error
        },
        failed "the saved list fetch" error
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
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The saved list grew by {Count} commits.",
            page.Items.Length
          ))
      | Error error ->
        { model with Error = Some error },
        failed "the saved list next page" error
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
        Cmd.ofAction(fun () ->
          log.LogDebug("The star on {Commit} settled.", updated.Id))
      | Error error ->
        { model with Error = Some error }, failed "the star" error
    | PollChoiceChanged(commit, option, chosen) ->
      {
        model with
            PollChoices =
              CommitCard.choose {
                Choices = model.PollChoices
                CommitId = commit.Id
                OptionId = option.Id
                Chosen = chosen
              }
      },
      Cmd.none
    | PollVoteRequested commit ->
      match CommitCard.selectedOption(model.PollChoices, commit) with
      | None -> model, Cmd.none
      | Some option ->
        let vote() =
          Commits.vote(
            props.Env,
            {
              KeyFile = props.Session.KeyFile
              CommitId = commit.Id
              OptionId = option.Id
            }
          )

        {
          model with
              Error = None
              Voting = Some commit.Id
        },
        Cmd.OfAsync.perform vote () PollVotedDone
    | PollVotedDone result ->
      match result with
      | Ok updated ->
        {
          model with
              Items = replaceNode(model.Items, updated)
              Voting = None
              PollChoices = model.PollChoices |> Map.remove updated.Id
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The vote on {Commit} landed.", updated.Id))
      | Error error ->
        {
          model with
              Error = Some error
              Voting = None
        },
        failed "the poll vote" error
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
        Cmd.ofAction(fun () ->
          log.LogDebug("The pastel on {Commit} landed.", pastel.Commit.Id))
      | Error error ->
        { model with Error = Some error }, failed "the pastel" error
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
      | CommitCard.ThreadRequested c ->
        Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
      | CommitCard.ReplyToggled c ->
        Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
      | CommitCard.MoreRepliesRequested c ->
        Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
      | CommitCard.StarToggled c -> dispatch(State.StarToggled c)
      | CommitCard.PastelGiven c -> dispatch(State.PastelGiven c)
      | CommitCard.PollChoiceChanged(commit, option, chosen) ->
        dispatch(State.PollChoiceChanged(commit, option, chosen))
      | CommitCard.PollVoteRequested commit ->
        dispatch(State.PollVoteRequested commit)
      | CommitCard.QuoteRequested c ->
        Navigation.go(props.Nav, "/quote/" + c.Id, ignore)
      | CommitCard.PinToggled _ -> ()
    Pasteis = model |> Observable.map _.Pasteis
    ViewerId = props.Session.Session.User.Id
    Now = model |> Observable.map _.Now
    Voting = model |> Observable.map _.Voting
    PollChoices = model |> Observable.map _.PollChoices
    Dense = false
    Images = props.Env.ImageCache
    Flyout =
      ValueSome {
        UserFlyout.Fetch = props.Env.Profiles.Fetch
        OnOpen =
          fun username ->
            Navigation.go(props.Nav, "/profile/" + username, ignore)
        Cache = props.Env.ImageCache
      }
    Pin = ValueNone
  }

  /// The saved list itself: the virtualized feed over the rows.
  let feed
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : Control =
    let ctx = commitCtx(props, dispatch, model)

    Feed.view {
      Items = model |> Observable.map _.Items
      KeyOf = FeedParts.nodeKey
      Row =
        fun node ->
          let card = CommitCard.feedRow ctx node

          match node with
          | LiveCommit commit ->
            StackPanel()
              .Children(card, CommitCard.replyBlock(ctx, commit, ValueNone))
            : Control
          | Tombstone _ -> card
      Footer =
        ValueSome(
          PageChrome.loadMore(
            model |> Observable.map _.NextCursor.IsSome,
            fun () -> dispatch State.MoreRequested
          )
        )
      ScrollTop = ValueNone
      Restore = ValueNone
      OnScroll = ValueNone
      OnNeedMore = ValueNone
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
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Program.runWithLifecycle () program

    this.Content <- root
    props.Context.AddDisposable lifetime
