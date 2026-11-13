module DevNull.Client.Editor.ThreadTab

open System
open System.Reactive.Disposables
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open DevNull.Client
open DevNull.Client.Editor
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.PageChrome
open DevNull.Client.Components.ReplyPicker
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore
open DevNull.Client.UiClock

[<Struct>]
type Snapshot = {
  CommitId: string
  RepliesCount: int
  Participants: ParticipantRow list
  Loading: bool
  Error: AppError option
}

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideDispatcher
  and 'env :> IProvideRemotes
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideBrowser> = {
  Env: 'env
  Session: SignedInSession
  CommitId: string
  Zone: Zone
  Now: unit -> DateTimeOffset
  OnSnapshot: Snapshot -> unit
  OnFetch: Result<unit, string> -> unit
  OnQuote: Commit -> unit
  OnReplyRequested: Commit -> unit
  OnOpenRepo: string -> unit
}

module State =

  type Model = {
    Loading: bool
    Error: AppError option
    Page: CommitPage option
    Replies: CommitNode list
    RepliesCursor: string option
    Busy: string option
    PollChoices: Map<string, string>
    Now: DateTimeOffset
    Peek: (string * CommitPage) option
    Cards: EditorCards.State
    Deleted: string option
  }

  type Msg =
    | ThreadFetched of Result<CommitPage, AppError>
    | RepliesFetched of Result<Page<CommitNode>, AppError>
    | MoreReplies
    | StarToggled of Commit
    | StarDone of Result<Commit, AppError>
    | PastelGiven of Commit
    | PastelDone of Result<PastelResult, AppError>
    | PollVoteRequested of Commit
    | PollVotedDone of Result<Commit, AppError>
    | CardEvent of Event
    | PeekRequested of commitId: string
    | Peeked of commitId: string * Result<CommitPage, AppError>
    | CardMsg of EditorCards.Msg
    | ClockTicked of moment: DateTimeOffset

  let initial = {
    Loading = true
    Error = None
    Page = None
    Replies = []
    RepliesCursor = None
    Busy = None
    PollChoices = Map.empty
    Now = DateTimeOffset.MinValue
    Peek = None
    Cards = EditorCards.empty
    Deleted = None
  }

  let repliesCount(model: Model) =
    model.Page
    |> Option.bind(fun page -> page.Commit.RepliesCount)
    |> Option.defaultValue model.Replies.Length

  let participantsOf(model: Model) =
    match model.Page with
    | Some page ->
      let author = page.Commit.Author

      let first: ParticipantRow = {
        Id = Author.id author
        Handle = Author.handle author
        IsAuthor = true
      }

      let others =
        page.ReplyParticipants
        |> List.map(fun participant ->
          {
            Id = participant.Id
            Handle = participant.Handle
            IsAuthor = false
          }
          : ParticipantRow)

      first :: others
    | None -> []

  let snapshot(props: Props<'env>, model: Model) = {
    CommitId = props.CommitId
    RepliesCount = repliesCount model
    Participants = participantsOf model
    Loading = model.Loading
    Error = model.Error
  }

  let reportFetch(props: Props<'env>, result: Result<'a, AppError>) =
    props.OnFetch(
      match result with
      | Ok _ -> Ok()
      | Error error -> Error(describe error)
    )

  let peekCached(model: Model, commitId: string) =
    model.Peek
    |> Option.bind(fun (cachedId, page) ->
      if cachedId = commitId then Some page else None)

  let needsPeek(model: Model, commitId: string) =
    peekCached(model, commitId) |> Option.isNone

  let show(props: Props<'env>, commitId: string) =
    Commits.show(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        CommitId = commitId
      }
    )

  let replies(props: Props<'env>, cursor: string option) =
    Commits.replies(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        CommitId = props.CommitId
        Newest = false
        Cursor = cursor
      }
    )

  let peek(props: Props<'env>, commitId: string) = show(props, commitId)

  let toggleStar(props: Props<'env>, commit: Commit) =
    let request: Stars.ActionRequest = {
      KeyFile = props.Session.KeyFile
      CommitId = commit.Id
    }

    if commit.Starred |> Option.contains true then
      Stars.unstar(props.Env, request)
    else
      Stars.star(props.Env, request)

  let pastel(props: Props<'env>, commit: Commit) =
    Pasteis.give(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        CommitId = commit.Id
      }
    )

  let vote(props: Props<'env>, commit: Commit, option: PollOption) =
    Commits.vote(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        CommitId = commit.Id
        OptionId = option.Id
      }
    )

  let withCommit(updated: Commit, model: Model) = {
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

  let announce(props: Props<'env>, model: Model) =
    Cmd.ofAction(fun () -> props.OnSnapshot(snapshot(props, model)))

  let cardsProps(props: Props<'env>) : EditorCards.Props<'env> = {
    Env = props.Env
    Session = props.Session
    Zone = props.Zone
    Surface = EditorCards.Detail
    Now = props.Now
    OnOpenRepo = props.OnOpenRepo
  }

  let init(props: Props<'env>) =
    let model = {
      initial with
          Now = props.Now()
          Cards = EditorCards.initial(cardsProps props)
    }

    model,
    Cmd.batch [
      Cmd.OfAsync.perform show (props, props.CommitId) ThreadFetched
      Cmd.OfAsync.perform replies (props, None) RepliesFetched
    ]

  let update(props: Props<'env>, msg: Msg, model: Model) =
    match msg with
    | ThreadFetched result ->
      reportFetch(props, result)

      match result with
      | Ok page ->
        let next = {
          model with
              Loading = false
              Error = None
              Page = Some page
              Deleted = None
        }

        next, announce(props, next)
      | Error error ->
        let next = {
          model with
              Loading = false
              Error = Some error
        }

        next, announce(props, next)
    | RepliesFetched result ->
      reportFetch(props, result)

      match result with
      | Ok page ->
        let next = {
          model with
              Error = None
              Replies = model.Replies @ page.Items
              RepliesCursor = page.NextCursor
        }

        next, announce(props, next)
      | Error error ->
        let next = { model with Error = Some error }

        next, announce(props, next)
    | MoreReplies ->
      match model.RepliesCursor with
      | Some cursor ->
        model, Cmd.OfAsync.perform replies (props, Some cursor) RepliesFetched
      | None -> model, Cmd.none
    | StarToggled commit ->
      let next = {
        model with
            Error = None
            Busy = Some commit.Id
      }

      next, Cmd.OfAsync.perform toggleStar (props, commit) StarDone
    | StarDone result ->
      match result with
      | Ok updated ->
        let next = {
          withCommit(updated, model) with
              Busy = None
        }

        next, Cmd.none
      | Error error ->
        let next = {
          model with
              Error = Some error
              Busy = None
        }

        next, Cmd.none
    | PastelGiven commit ->
      let next = {
        model with
            Error = None
            Busy = Some commit.Id
      }

      next, Cmd.OfAsync.perform pastel (props, commit) PastelDone
    | PastelDone result ->
      match result with
      | Ok result ->
        let next = {
          withCommit(result.Commit, model) with
              Busy = None
        }

        next, Cmd.none
      | Error error ->
        let next = {
          model with
              Error = Some error
              Busy = None
        }

        next, Cmd.none
    | PollVoteRequested commit ->
      match selectedOption(model.PollChoices, commit) with
      | None -> model, Cmd.none
      | Some option ->
        let next = {
          model with
              Error = None
              Busy = Some commit.Id
        }

        next, Cmd.OfAsync.perform vote (props, commit, option) PollVotedDone
    | PollVotedDone result ->
      match result with
      | Ok updated ->
        let next = {
          withCommit(updated, model) with
              Busy = None
              PollChoices = model.PollChoices |> Map.remove updated.Id
        }

        next, Cmd.none
      | Error error ->
        let next = {
          model with
              Error = Some error
              Busy = None
        }

        next, Cmd.none
    | CardEvent event ->
      match event with
      | QuoteRequested commit ->
        model, Cmd.ofAction(fun () -> props.OnQuote commit)
      | ReplyToggled commit ->
        model, Cmd.ofAction(fun () -> props.OnReplyRequested commit)
      | PollChoiceChanged(commit, option, chosen) ->
        let next = {
          model with
              PollChoices =
                choose {
                  Choices = model.PollChoices
                  CommitId = commit.Id
                  OptionId = option.Id
                  Chosen = chosen
                }
        }

        next, Cmd.none
      | _ -> model, Cmd.none
    | PeekRequested commitId ->
      if needsPeek(model, commitId) then
        model,
        Cmd.OfAsync.perform peek (props, commitId) (fun result ->
          Peeked(commitId, result))
      else
        model, Cmd.none
    | Peeked(commitId, Ok page) ->
      let next = {
        model with
            Peek = Some(commitId, page)
      }

      next, Cmd.none
    | Peeked(_, Error _) -> model, Cmd.none
    | CardMsg msg ->
      let cards, cmd, effects =
        EditorCards.update(cardsProps props, msg, model.Cards)

      let next =
        effects
        |> List.fold
          (fun current effect ->
            match effect with
            | EditorCards.CommitChanged updated -> withCommit(updated, current)
            | EditorCards.CommitDeleted commitId -> {
                current with
                    Deleted = Some commitId
              }
            | EditorCards.PinChanged(_, updated) ->
              withCommit(updated, current)
            | EditorCards.Failed error -> { current with Error = Some error })
          { model with Cards = cards }

      next, EditorCards.mapCmd(CardMsg, cmd)
    | ClockTicked moment ->
      let next = { model with Now = moment }

      next, Cmd.none

  let subscribe(props: Props<'env>, model: Model) : Sub<Msg> =
    [
      Sub.fromObservable
        "clock"
        (props.Env.UiClock.Minute |> Observable.map ClockTicked)

      Sub.fromObservable
        "card-clock"
        (props.Env.UiClock.Minute
         |> Observable.map(fun tick -> CardMsg(EditorCards.ClockTicked tick)))
    ]
    |> Sub.batch

let keyOf(node: CommitNode) =
  match node with
  | LiveCommit commit -> commit.Id
  | Tombstone tombstone -> tombstone.Id

let view
  (
    props: Props<'env>,
    model: IObservable<State.Model>,
    dispatch: Dispatch<State.Msg>
  ) : Control =
  let busy =
    model |> Observable.map(fun current -> current.Busy) |> Slices.share

  let cards = model |> Observable.map _.Cards |> Slices.share

  let cardDispatch msg = dispatch(State.CardMsg msg)

  let baseProps =
    EditorCards.row {
      Props = State.cardsProps props
      State = cards
      Dispatch = cardDispatch
      OnEvent =
        EditorCards.event(
          cardDispatch,
          fun event -> dispatch(State.CardEvent event)
        )
    }

  let mainProps: RowProps = {
    baseProps with
        PollChoices = model |> Observable.map _.PollChoices |> Slices.share
        Dense = false
        Star =
          ValueSome {
            Command = fun commit -> dispatch(State.StarToggled commit)
            Busy = busy
          }
        Vote =
          ValueSome {
            Command = fun commit -> dispatch(State.PollVoteRequested commit)
            Busy = busy
          }
        Pastel =
          ValueSome {
            Command = fun commit -> dispatch(State.PastelGiven commit)
            Busy = busy
          }
  }

  let replyProps: RowProps = { mainProps with Dense = true }

  let breadcrumbs(nodes: CommitNode list) : Control =
    let commits =
      nodes
      |> List.choose(function
        | LiveCommit commit -> Some commit
        | Tombstone _ -> None)

    let crumbs: Control array = [|
      for index, commit in List.indexed commits do
        if index > 0 then
          TextBlock()
            .Text("←")
            .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)

        let button =
          Button()
            .Classes(Theme.name Theme.GhostButton)
            .Content(
              TextBlock().Text(commit.ShortHash).Classes(Theme.name Theme.Mono)
            )

        button.PointerEntered.Add(fun _ ->
          dispatch(State.PeekRequested commit.Id))

        let body =
          model
          |> Observable.distinctUntilChangedKey(fun current ->
            current.Peek |> Option.map fst)
          |> Observable.map(fun current ->
            match current.Peek with
            | Some(id, page) when id = commit.Id -> page.Commit.Body
            | _ -> "")

        let tip =
          TextBlock()
            .Text(body)
            .TextWrapping(TextWrapping.Wrap)
            .MaxWidth(360.0)
            .Classes(Theme.name Theme.FgMuted)

        ToolTip.SetTip(button, tip)
        button
    |]

    if Array.isEmpty crumbs then
      TextBlock()
        .Text("(root)")
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
    else
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(4.0)
        .Children(crumbs)

  let breadcrumbRow =
    ContentControl()
      .Content(
        model
        |> Observable.map _.Page
        |> Slices.gateByRef
        |> Observable.map(function
          | Some page -> breadcrumbs page.Ancestors
          | None -> breadcrumbs [])
        |> _.ToBinding()
      )

  let error = errorPanel(model |> Observable.map(fun current -> current.Error))

  let mainCard =
    ContentControl()
      .Content(
        model
        |> Slices.gateByRefKey
          (fun current ->
            current.Page
            |> Option.map(fun page -> page.Commit)
            |> function
              | Some value -> value
              | None -> null)
          (fun current -> current.Deleted)
        |> Observable.map(fun current ->
          current.Page |> Option.map(fun page -> page.Commit), current.Deleted)
        |> Observable.map(fun (commit, deleted) ->
          match commit, deleted with
          | Some commit, Some commitId when commit.Id = commitId ->
            CommitCard.tombstone()
          | Some commit, _ -> feedRow mainProps (LiveCommit commit)
          | None, _ ->
            TextBlock()
              .Text("Opening the thread…")
              .Classes(Theme.name Theme.FgMuted)
              .HorizontalAlignment(HorizontalAlignment.Center)
              .VerticalAlignment(VerticalAlignment.Center))
        |> _.ToBinding()
      )

  let repliesHeader =
    TextBlock()
      .Text(
        model
        |> Slices.gateByRefKey (fun current -> current.Replies) (fun current ->
          current.Page
          |> Option.map(fun page -> page.Commit.Id, page.Commit.RepliesCount))
        |> Observable.map(fun current ->
          $"REPLIES {(State.snapshot(props, current)).RepliesCount}")
      )
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .Margin(0.0, 8.0, 0.0, 0.0)

  let repliesItems =
    ItemsControl()
      .ItemTemplate(
        FuncDataTemplate<CommitNode>(
          (fun node _ -> feedRow replyProps node),
          false
        )
      )
      .ItemsPanel(FuncTemplate<Panel>(fun () -> StackPanel()))
      .BindItemsSource(
        model
        |> Observable.map(fun current -> current.Replies)
        |> Slices.gateByRef,
        keyOf
      )

  let hasMore =
    model |> Observable.map(fun current -> current.RepliesCursor.IsSome)

  let loadMoreControl = loadMore(hasMore, fun () -> dispatch State.MoreReplies)

  let body =
    StackPanel()
      .Spacing(8.0)
      .Children(
        breadcrumbRow,
        error,
        mainCard,
        repliesHeader,
        repliesItems,
        loadMoreControl
      )

  let content =
    ScrollViewer()
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
      .Content(body)

  Grid()
    .Children(
      content,
      EditorCards.layer(State.cardsProps props, cards, cardDispatch)
    )

let program
  (props: Props<'env>)
  : Program<unit, State.Model, State.Msg, Control> =
  Program.mkProgram
    (fun () -> State.init props)
    (fun msg model -> State.update(props, msg, model))
    (fun model dispatch -> view(props, model, dispatch))
  |> Program.withSubscription(fun model -> State.subscribe(props, model))

module Live =

  let count = new BehaviorSubject<int>(0)

  let started() = count.OnNext(count.Value + 1)

  let stopped() = count.OnNext(max 0 (count.Value - 1))

  let current() = count.Value

  let observe = count :> IObservable<int>

let mount(dispatcher: IDispatcher, props: Props<'env>) =
  let view, handle = DevNull.Client.Programs.runPage(dispatcher, program props)

  Live.started()

  let once = new BooleanDisposable()

  let lifetime =
    Disposable.Create(fun () ->
      if not once.IsDisposed then
        once.Dispose()
        handle.Dispose()
        Live.stopped())

  view, lifetime
