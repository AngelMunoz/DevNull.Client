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
open FSharp.Control.Reactive
open IcedTasks
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open Navs
open DevNull.Client
open DevNull.Client.AttachmentSources
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
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.Timeline
open DevNull.Client.UiClock

// The hover card source record; the alias keeps its long name out of
// every use site.
type HoverSource = DevNull.Client.Components.UserFlyout.Source

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideSshKeys
  and 'env :> IProvideTimeline
  and 'env :> IProvideLogging> = {
  Env: 'env
  CommitId: string
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
  /// Whether the page opens with the quote composer armed: the
  /// /quote/:id route renders this page over the quoted commit.
  Quoting: bool
}

/// The page's state logic: model, messages, update, and the
/// subscriptions that feed it from outside.
module State =

  /// One nested reply list the page grew past the payload's preview
  /// slice: the replies shown and the cursor the next page asks for.
  [<Struct>]
  type PreviewState = {
    Shown: CommitNode list
    Cursor: string option
  }

  type Model = {
    Loading: bool
    Error: AppError option
    Page: CommitPage option
    Replies: CommitNode list
    RepliesCursor: string option
    Note: string option
    Now: DateTimeOffset
    Pasteis: bool
    Replying: bool
    /// The commit whose poll vote is in flight, so its buttons disable.
    Voting: string option
    /// The option a viewer picked in each open poll, by commit id.
    PollChoices: Map<string, string>
    /// Whether the quote composer is armed: from the route, or from
    /// the focused card's quote action.
    Quoting: bool
    /// The nested reply lists the page grew past their preview
    /// slices, by parent id.
    Expanded: Map<string, PreviewState>
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
    | PollVotedDone of Result<Commit, AppError>
    | PastelGiven of Result<PastelResult, AppError>
    | Deleted of Result<unit, AppError>
    | ClockTicked of moment: DateTimeOffset
    | SessionChanged of state: SessionState
    | NodeMoreRequested of Commit
    | NodeRepliesLoaded of commitId: string * Result<Page<CommitNode>, AppError>

  let initial = {
    Loading = true
    Error = None
    Page = None
    Replies = []
    RepliesCursor = None
    Note = None
    Now = DateTimeOffset.UtcNow
    Pasteis = false
    Replying = false
    Voting = None
    PollChoices = Map.empty
    Quoting = false
    Expanded = Map.empty
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
          Quoting = props.Quoting
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
    let log =
      props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.CommitDetail"

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
    | ThreadFetched result ->
      match result with
      | Ok page ->
        {
          model with
              Loading = false
              Error = None
              Page = Some page
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The thread of {Commit} arrived.", props.CommitId))
      | Error error ->
        {
          model with
              Loading = false
              Error = Some error
        },
        failed "the thread fetch" error
    | RepliesFetched result ->
      match result with
      | Ok page ->
        {
          model with
              Replies = model.Replies @ page.Items
              RepliesCursor = page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The replies of {Commit} arrived ({Count}).",
            props.CommitId,
            page.Items.Length
          ))
      | Error error ->
        { model with Error = Some error }, failed "the replies fetch" error
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
      // Navigation is a command, never an inline effect: navigating
      // inside update disposes this very program before the loop can
      // publish the model, and the loop logs an ObjectDisposedException.
      model,
      Cmd.ofEffect(fun _ ->
        Navigation.go(props.Nav, "/commit/" + commit.Id, ignore))
    | CardEvent(CommitCard.PastelGiven commit) ->
      { model with Error = None; Note = None },
      Cmd.ofMsg(PastelRequested commit.Id)
    | CardEvent(CommitCard.QuoteRequested commit) ->
      // The focused card quotes in place; any other card opens its own
      // thread with the quote composer armed over it.
      if commit.Id = props.CommitId then
        {
          model with
              Quoting = true
              Replying = false
        },
        Cmd.none
      else
        model,
        Cmd.ofEffect(fun _ ->
          Navigation.go(props.Nav, "/quote/" + commit.Id, ignore))
    | CardEvent(CommitCard.ReplyToggled _) ->
      {
        model with
            Replying = not model.Replying
            Quoting = false
      },
      Cmd.none
    | CardEvent(CommitCard.PollChoiceChanged(commit, option, chosen)) ->
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
    | CardEvent(CommitCard.PollVoteRequested commit) ->
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
    | CardEvent(CommitCard.ThreadRequested commit) ->
      model,
      Cmd.ofEffect(fun _ ->
        Navigation.go(props.Nav, "/commit/" + commit.Id, ignore))
    | CardEvent(CommitCard.MoreRepliesRequested commit) ->
      model,
      Cmd.ofEffect(fun _ ->
        Navigation.go(props.Nav, "/commit/" + commit.Id, ignore))
    | NodeMoreRequested commit ->
      // The first ask reads the newest page whole, the way the site's
      // preview does; the next ones walk the cursor it left.
      let cursor =
        model.Expanded |> Map.tryFind commit.Id |> Option.bind _.Cursor

      model,
      Cmd.OfAsync.perform
        Commits.replies
        (props.Env,
         {
           KeyFile = props.Session.KeyFile
           CommitId = commit.Id
           Newest = true
           Cursor = cursor
         })
        (fun page -> NodeRepliesLoaded(commit.Id, page))
    | NodeRepliesLoaded(commitId, result) ->
      match result with
      | Ok page ->
        let known =
          model.Expanded
          |> Map.tryFind commitId
          |> Option.map _.Shown
          |> Option.defaultValue []

        let seen = known |> List.map FeedParts.nodeKey |> Set.ofList

        let fresh =
          page.Items
          |> List.filter(fun node ->
            seen |> Set.contains(FeedParts.nodeKey node) |> not)

        let grown = {
          Shown = known @ fresh
          Cursor = page.NextCursor
        }

        {
          model with
              Expanded = model.Expanded |> Map.add commitId grown
        },
        Cmd.none
      | Error error ->
        { model with Error = Some error },
        failed "the nested replies fetch" error
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

      { model with Error = None },
      Cmd.batch [
        Cmd.OfAsync.perform remove () Deleted

        Cmd.ofAction(fun () ->
          log.LogInformation(
            "The commit {Commit} is being deleted.",
            props.CommitId
          ))
      ]
    | StarDone result ->
      match result with
      | Ok updated ->
        withCommit(updated, model),
        Cmd.ofAction(fun () ->
          log.LogDebug("The star on {Commit} settled.", updated.Id))
      | Error error ->
        { model with Error = Some error }, failed "the star" error
    | PollVotedDone result ->
      match result with
      | Ok updated ->
        {
          withCommit(updated, model) with
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
    | PastelGiven result ->
      match result with
      | Ok pastel ->
        {
          withCommit(pastel.Commit, model) with
              Note =
                Some $"Pastel given. {pastel.PasteisRemaining} left this cycle."
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The pastel on {Commit} landed.", pastel.Commit.Id))
      | Error error ->
        { model with Error = Some error }, failed "the pastel" error
    | Deleted result ->
      match result with
      | Ok _ ->
        model,
        Cmd.batch [
          // The shell answers the deletion with its undo toast; the
          // page itself leaves for the log.
          Cmd.ofAction(fun () ->
            (props.Env :> IProvideTimeline).Timeline.AnnounceDeletion
              props.CommitId)

          Cmd.ofEffect(fun _ -> Navigation.go(props.Nav, "/log/latest", ignore))

          Cmd.ofAction(fun () ->
            log.LogInformation(
              "The commit {Commit} was deleted.",
              props.CommitId
            ))
        ]
      | Error error ->
        { model with Error = Some error }, failed "the commit deletion" error
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

  /// The hover card source every thread card shares.
  let flyoutOf<'env
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp
    and 'env :> IProvideSession
    and 'env :> IProvideUiClock
    and 'env :> IProvideImageCache
    and 'env :> IProvideAttachmentSources
    and 'env :> IProvideProfiles
    and 'env :> IProvideSshKeys
    and 'env :> IProvideTimeline
    and 'env :> IProvideLogging>
    (props: Props<'env>)
    : HoverSource voption =
    let source: HoverSource = {
      Fetch = props.Env.Profiles.Fetch
      OnOpen =
        fun username -> Navigation.go(props.Nav, "/profile/" + username, ignore)
      Cache = props.Env.ImageCache
    }

    ValueSome source

  /// What every thread card reads: the page's dispatch, the live
  /// model, the signed-in viewer, the image cache, and the hover card
  /// source.
  [<Struct>]
  type Ctx = {
    Dispatch: Dispatch<State.Msg>
    Model: IObservable<State.Model>
    ViewerId: string
    Images: ImageCacheService
    Flyout: HoverSource voption
  }

  /// The card context every thread card shares: events route into the
  /// page's card message.
  let commitCtx(ctx: Ctx, dense: bool) : CommitCard.RowProps = {
    OnEvent = State.CardEvent >> ctx.Dispatch
    Pasteis = ctx.Model |> Observable.map _.Pasteis
    ViewerId = ctx.ViewerId
    Now = ctx.Model |> Observable.map _.Now
    Voting = ctx.Model |> Observable.map _.Voting
    PollChoices = ctx.Model |> Observable.map _.PollChoices
    Dense = dense
    Images = ctx.Images
    Flyout = ctx.Flyout
  }

  /// The "Show N more" walk of one node's replies, the muted dot of
  /// the site's continuation.
  let moreNodeReplies
    (dispatch: Dispatch<State.Msg>, commit: Commit, remaining: int)
    : Control =
    let label =
      match remaining with
      | 1 -> "Show the last reply"
      | count -> $"Show {count} more replies"

    Button()
      .Name("NodeMoreReplies")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(4.0, 2.0, 4.0, 2.0)
      .Margin(0.0, 2.0, 0.0, 2.0)
      .HorizontalAlignment(HorizontalAlignment.Left)
      .Content(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(
            Ellipse()
              .Width(10.0)
              .Height(10.0)
              .StrokeThickness(1.5)
              .StrokeDashArray(
                Avalonia.Collections.AvaloniaList<double>(2.0, 2.0)
              )
              .Classes(Theme.name Theme.MutedEdge),
            TextBlock().Text(label)
          )
      )
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        dispatch(State.NodeMoreRequested commit))

  /// Grows one parent's children on the thread page: the payload's
  /// preview slice, or the list the page walked through the cursor,
  /// plus the walk button while replies remain. The host rebuilds
  /// only when the shown list changed.
  let growChildren
    (ctx: Ctx)
    (child: CommitCard.ChainCtx)
    (parent: Commit)
    : Control =
    ContentControl()
      .Content(
        ctx.Model
        |> Observable.map(fun current ->
          current.Expanded |> Map.tryFind parent.Id)
        |> Observable.distinctUntilChanged
        |> Observable.map(fun grown ->
          match grown with
          | Some state -> ValueSome state.Shown
          | None ->
            parent.ReplyPreview |> Option.map _.Data |> ValueOption.ofOption)
        |> Observable.distinctUntilChanged
        |> Observable.map(fun shown ->
          let carried = shown |> ValueOption.defaultValue []

          let remaining =
            parent.RepliesCount
            |> Option.map(fun count -> count - carried.Length)
            |> Option.defaultValue 0

          let children: Control array = [|
            if not carried.IsEmpty then
              CommitCard.chainRows(
                {
                  child with
                      ParentLane = parent.Author.Lane
                      ParentHandle = parent.Author.Handle
                },
                carried
              )

            if remaining > 0 then
              moreNodeReplies(ctx.Dispatch, parent, remaining)
          |]

          StackPanel().Children(children): Control)
        |> _.ToBinding()
      )

  /// The chain context of the thread page: every parent grows its
  /// children through the cursor.
  let threadChain
    (ctx: Ctx, lane: Lane option, handle: string)
    : CommitCard.ChainCtx = {
    Props = commitCtx(ctx, false)
    Depth = 0
    ParentLane = lane
    ParentHandle = handle
    Grow = ValueSome(growChildren ctx)
  }

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
              TextBlock().Text($"@{commit.Author.Handle}"),
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
  let cardHost(ctx: Ctx) : Control =
    let content =
      ctx.Model
      |> Observable.map(fun current ->
        match current.Page with
        | Some page ->
          CommitCard.feedRow (commitCtx(ctx, false)) (LiveCommit page.Commit)
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

  /// The reply column under the card: the root replies as the site's
  /// slim chain. The focused card runs its lane tail to its bottom
  /// edge while replies show, so the first row's elbow starts exactly
  /// at that boundary.
  let replies(ctx: Ctx) : Control =
    ContentControl()
      .Content(
        ctx.Model
        |> Observable.map(fun current ->
          match current.Page with
          | Some page ->
            Some(
              page.Commit.Author.Lane,
              page.Commit.Author.Handle,
              current.Replies
            )
          | None -> None)
        |> Observable.distinctUntilChanged
        |> Observable.map(function
          | Some(lane, handle, nodes) when not nodes.IsEmpty ->
            StackPanel()
              .Margin(40.0, 0.0, 0.0, 0.0)
              .Children(
                CommitCard.chainRows(threadChain(ctx, lane, handle), nodes)
              )
            : Control
          | _ -> Status.empty "")
        |> _.ToBinding()
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

  /// The answer composers under the thread. The reply composer hides
  /// until the card's reply button reveals it; a landed reply reloads
  /// the page. The quote composer arms from the route or the focused
  /// card's quote action; a landed quote opens the new commit's
  /// thread. Both mount once: a rebuild on every model change would
  /// wipe the typed text.
  let answerComposer
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : Control =
    let reply =
      Composer.Page {
        Env = props.Env
        Session = props.Session
        Target = Composer.ReplyTo props.CommitId
        OnCommitted = fun _ -> dispatch State.ReloadRequested
        Register = ValueNone
        Draft = ValueNone
      }

    let quote =
      Composer.Page {
        Env = props.Env
        Session = props.Session
        Target = Composer.Quoting props.CommitId
        OnCommitted =
          fun commit -> Navigation.go(props.Nav, "/commit/" + commit.Id, ignore)
        Register = ValueNone
        Draft = ValueNone
      }

    StackPanel()
      .Children(
        reply.IsVisible(
          model |> Observable.map(fun current -> current.Replying)
        ),
        quote.IsVisible(model |> Observable.map(fun current -> current.Quoting))
      )

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
        Parts.cardHost {
          Dispatch = dispatch
          Model = model
          ViewerId = props.Session.Session.User.Id
          Images = props.Env.ImageCache
          Flyout = Parts.flyoutOf props
        },
        // The replies tie into the card's rail directly; the page-level
        // affordances follow the chain, the way the site's thread does.
        Parts.replies {
          Dispatch = dispatch
          Model = model
          ViewerId = props.Session.Session.User.Id
          Images = props.Env.ImageCache
          Flyout = Parts.flyoutOf props
        },
        Parts.moreReplies(dispatch, model),
        Parts.threadActions(props, dispatch, model),
        Parts.answerComposer(props, dispatch, model)
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
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideSshKeys
  and 'env :> IProvideTimeline
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Program.runWithLifecycle () program

    this.Content <- root
    props.Context.AddDisposable lifetime
