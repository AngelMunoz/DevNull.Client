module DevNull.Client.Editor.GuildTab

open System
open System.Reactive.Disposables
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open Microsoft.Extensions.Logging
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Components.AsyncImage
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.FeedParts
open DevNull.Client.Components.PageChrome
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.GuildBoard
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore

[<Struct>]
type Snapshot = {
  Guild: Guild option
  FetchedAt: DateTimeOffset option
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
  and 'env :> IProvideLogging
  and 'env :> IProvideBrowser> = {
  Env: 'env
  Session: SignedInSession
  Handle: string
  Zone: Zone
  Now: unit -> DateTimeOffset
  Refresh: IObservable<unit>
  OnSnapshot: Snapshot -> unit
  OnOpenThread: Commit -> unit
  OnReply: Commit -> unit
  OnQuote: Commit -> unit
  OnOpenRepo: string -> unit
  OnShared: User -> unit
  OnOpenGuild: string -> unit
  OnStage: string -> unit
}

let cardsProps(props: Props<'env>) : EditorCards.Props<'env> = {
  Env = props.Env
  Session = props.Session
  Zone = props.Zone
  Surface = EditorCards.Feed
  Now = props.Now
  OnOpenRepo = props.OnOpenRepo
  OnShared = props.OnShared
  OnOpenGuild = props.OnOpenGuild
}

module State =

  type Model = {
    Board: BoardState
    Cards: EditorCards.State
    Now: DateTimeOffset
    FetchedAt: DateTimeOffset option
    Error: AppError option
  }

  type Msg =
    | BoardLoad
    | BoardMore
    | BoardArrived of page: int * Result<GuildBoard, AppError>
    | RefreshRequested
    | Opened of Commit
    | CardMsg of EditorCards.Msg
    | ClockTicked of moment: DateTimeOffset

  let snapshot(model: Model) : Snapshot = {
    Guild = model.Board.Guild
    FetchedAt = model.FetchedAt
  }

  let showPage(props: Props<'env>, page: int) =
    Guilds.show(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        Handle = props.Handle
        Page = page
      }
    )

  let announce(props: Props<'env>, model: Model) =
    Cmd.ofAction(fun () -> props.OnSnapshot(snapshot model))

  let replaceNode(updated: Commit, board: BoardState) : BoardState = {
    board with
        Commits =
          board.Commits
          |> List.map(fun node ->
            if nodeKey node = updated.Id then
              LiveCommit updated
            else
              node)
        Pinned =
          board.Pinned
          |> Option.map(fun node ->
            if nodeKey node = updated.Id then
              LiveCommit updated
            else
              node)
  }

  let deleteNode(commitId: string, model: Model) : Model =
    model.Board.Commits
    |> List.tryPick(function
      | LiveCommit commit when commit.Id = commitId -> Some commit
      | _ -> None)
    |> Option.map(fun commit ->
      let stone =
        Tombstone {
          Id = commit.Id
          ParentId = commit.ParentId
          DeletedAt = DateTimeOffset.UtcNow
          RepliesCount = commit.RepliesCount
        }

      let swap(node: CommitNode) =
        if nodeKey node = commitId then stone else node

      {
        model with
            Board = {
              model.Board with
                  Commits = model.Board.Commits |> List.map swap
                  Pinned = model.Board.Pinned |> Option.map swap
            }
      })
    |> Option.defaultValue model

  let init(props: Props<'env>) : Model * Cmd<Msg> =
    let model = {
      Board = GuildBoard.load GuildBoard.empty
      Cards = EditorCards.initial(cardsProps props)
      Now = props.Now()
      FetchedAt = None
      Error = None
    }

    model,
    Cmd.batch [
      Cmd.OfAsync.perform showPage (props, 1) (fun outcome ->
        BoardArrived(1, outcome))
      announce(props, model)
    ]

  let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
    match msg with
    | BoardLoad ->
      {
        model with
            Board = GuildBoard.load model.Board
      },
      Cmd.OfAsync.perform showPage (props, 1) (fun outcome ->
        BoardArrived(1, outcome))
    | BoardMore ->
      let next = GuildBoard.more model.Board

      let cmd =
        match next.Wanted with
        | ValueSome page when next.Wanted <> model.Board.Wanted ->
          Cmd.OfAsync.perform showPage (props, page) (fun outcome ->
            BoardArrived(page, outcome))
        | _ -> Cmd.none

      { model with Board = next }, cmd
    | BoardArrived(page, outcome) ->
      match outcome with
      | Ok _ -> ()
      | Error error ->
        let log =
          props.Env.LoggerFactory.CreateLogger "DevNull.Client.Editor.GuildTab"

        log.LogWarning(
          "The board page {Page} of {Handle} did not arrive: {Error}",
          page,
          props.Handle,
          bounded 300 (describe error)
        )

      let board = GuildBoard.arrived(model.Board, page, outcome)

      let landed =
        match outcome with
        | Ok _ -> {
            model with
                Board = board
                FetchedAt = Some model.Now
          }
        | Error _ -> { model with Board = board }

      landed, announce(props, landed)
    | RefreshRequested ->
      {
        model with
            Board = GuildBoard.load model.Board
      },
      Cmd.OfAsync.perform showPage (props, 1) (fun outcome ->
        BoardArrived(1, outcome))
    | Opened commit -> model, Cmd.ofAction(fun () -> props.OnOpenThread commit)
    | CardMsg msg ->
      let cards, cmd, effects =
        EditorCards.update(cardsProps props, msg, model.Cards)

      let next =
        effects
        |> List.fold
          (fun (current: Model) effect ->
            match effect with
            | EditorCards.CommitChanged updated -> {
                current with
                    Board = replaceNode(updated, current.Board)
              }
            | EditorCards.CommitDeleted commitId ->
              deleteNode(commitId, current)
            | EditorCards.PinChanged _ -> current
            | EditorCards.Failed error -> { current with Error = Some error })
          { model with Cards = cards }

      next, EditorCards.mapCmd(CardMsg, cmd)
    | ClockTicked moment -> { model with Now = moment }, Cmd.none

  let subscribe(props: Props<'env>, _: Model) : Sub<Msg> =
    [
      Sub.fromObservable
        "clock"
        (props.Env.UiClock.Minute |> Observable.map ClockTicked)

      Sub.fromObservable
        "card-clock"
        (props.Env.UiClock.Minute
         |> Observable.map(fun tick -> CardMsg(EditorCards.ClockTicked tick)))

      Sub.fromObservable
        "refresh"
        (props.Refresh |> Observable.map(fun () -> RefreshRequested))
    ]
    |> Sub.batch

let markOf(props: Props<'env>, guild: Guild) : Control =
  match guild.IconUrl with
  | Some url ->
    Border()
      .Width(40.0)
      .Height(40.0)
      .CornerRadius(CornerRadius 8.0)
      .ClipToBounds(true)
      .Child(
        AsyncImage.view {
          Cache = props.Env.ImageCache
          Url = url
          DecodeWidth = 80
          DecodeHeight = ValueNone
          Stretch = Stretch.UniformToFill
          ToolTip = ValueSome guild.Name
        }
      )
  | None ->
    Border()
      .Classes(Theme.name Theme.SurfaceBack)
      .Width(40.0)
      .Height(40.0)
      .CornerRadius(CornerRadius 8.0)
      .Child(
        TextBlock()
          .FontSize(22.0)
          .VerticalAlignment(VerticalAlignment.Center)
          .HorizontalAlignment(HorizontalAlignment.Center)
          .Text(guild.Emoji)
      )

let inline metaLine(guild: Guild) : string =
  let kind =
    if guild.IsPaused then
      "Public guild · Paused"
    else
      "Public guild"

  $"#{guild.Handle} · {kind} · {shortCount guild.MemberCount} members"

let header(props: Props<'env>, model: IObservable<State.Model>) : Control =
  let guild =
    model
    |> Observable.map _.Board.Guild
    |> Observable.distinctUntilChanged
    |> Slices.share

  let stage =
    Button()
      .Name("GuildTabStage")
      .Classes(Theme.name Theme.GhostButton)
      .Content(
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .Text($"STAGE → #{props.Handle}")
      )
      .OnClickHandler(fun _ _ -> props.OnStage props.Handle)
      .IsVisible(
        guild
        |> Observable.map(Option.exists(fun found -> not found.IsPaused))
        |> Observable.distinctUntilChanged
      )

  let identity =
    ContentControl()
      .Content(
        guild
        |> Observable.map(fun current ->
          match current with
          | Some found ->
            StackPanel()
              .Spacing(2.0)
              .Children(
                StackPanel()
                  .Orientation(Orientation.Horizontal)
                  .Spacing(10.0)
                  .Children(
                    markOf(props, found),
                    TextBlock()
                      .Classes(Theme.name Theme.Subtitle)
                      .VerticalAlignment(VerticalAlignment.Center)
                      .Text(found.Name)
                  ),
                TextBlock()
                  .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
                  .FontSize(12.0)
                  .TextTrimming(TextTrimming.CharacterEllipsis)
                  .Text(metaLine found),
                (match found.Description with
                 | Some text ->
                   TextBlock()
                     .Classes(Theme.name Theme.Muted)
                     .FontSize(12.0)
                     .TextWrapping(TextWrapping.Wrap)
                     .Text(text)
                   :> Control
                 | None -> Panel() :> Control),
                TextBlock()
                  .Name("GuildTabPaused")
                  .Classes(Theme.name Theme.Muted)
                  .FontSize(12.0)
                  .TextWrapping(TextWrapping.Wrap)
                  .Text(
                    "This guild is paused until it has a moderator. Its board and members are preserved."
                  )
                  .IsVisible(
                    guild
                    |> Observable.map(Option.exists _.IsPaused)
                    |> Observable.distinctUntilChanged
                  )
              )
            :> Control
          | None -> Panel() :> Control)
        |> _.ToBinding()
      )

  Border()
    .Name("GuildTabHead")
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
    .Padding(Thickness(16.0, 10.0, 16.0, 8.0))
    .Child(
      DockPanel()
        .LastChildFill(true)
        .Children(
          stage.DockRight().VerticalAlignment(VerticalAlignment.Top),
          identity
        )
    )

type BoardRow = { Node: CommitNode; Pinned: bool }

let view
  (
    props: Props<'env>,
    model: IObservable<State.Model>,
    dispatch: Dispatch<State.Msg>
  ) : Control =
  let cards = model |> Observable.map _.Cards |> Slices.share

  let cardDispatch msg = dispatch(State.CardMsg msg)

  let fallback =
    function
    | Opened commit -> dispatch(State.Opened commit)
    | ReplyToggled commit -> props.OnReply commit
    | QuoteRequested commit -> props.OnQuote commit
    | MoreRepliesRequested commit -> props.OnOpenThread commit
    | _ -> ()

  let rowProps =
    EditorCards.row {
      Props = cardsProps props
      State = cards
      Dispatch = cardDispatch
      OnEvent = EditorCards.event(cardDispatch, fallback)
    }

  let refused =
    TextBlock()
      .Name("GuildTabRefused")
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0)
      .Text("Guilds are not available on this server.")
      .IsVisible(
        model
        |> Observable.map _.Board.Refused
        |> Observable.distinctUntilChanged
      )

  let failed =
    StackPanel()
      .Spacing(8.0)
      .Margin(16.0)
      .Children(
        TextBlock()
          .Name("GuildTabFailed")
          .Classes(Theme.name Theme.Muted)
          .Text("The board did not load."),
        Button()
          .Name("GuildTabRetry")
          .Classes(Theme.name Theme.GhostButton)
          .Content("RETRY")
          .OnClickHandler(fun _ _ -> dispatch State.BoardLoad)
      )
      .IsVisible(
        model
        |> Observable.map _.Board.Failed
        |> Observable.distinctUntilChanged
      )

  let empty =
    TextBlock()
      .Name("GuildTabEmpty")
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0)
      .Text("No commits on this board yet.")
      .IsVisible(
        model
        |> Observable.map(fun current ->
          not current.Board.Loading
          && not current.Board.Refused
          && not current.Board.Failed
          && current.Board.Guild.IsSome
          && GuildBoard.ordered current.Board |> List.isEmpty)
        |> Observable.distinctUntilChanged
      )

  let actionError = errorPanel(model |> Observable.map _.Error)

  let loadMore =
    Button()
      .Name("GuildTabMore")
      .Classes(Theme.name Theme.GhostButton)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Margin(16.0)
      .Content("LOAD MORE")
      .OnClickHandler(fun _ _ -> dispatch State.BoardMore)
      .IsVisible(
        model
        |> Observable.map _.Board.HasMore
        |> Observable.distinctUntilChanged
      )

  let items =
    model
    |> Observable.map(fun current ->
      GuildBoard.ordered current.Board
      |> List.map(fun (node, pinned) -> { Node = node; Pinned = pinned }))
    |> Slices.share

  let feed =
    Feed.view {
      Header =
        ValueSome(
          StackPanel()
            .Children(header(props, model), refused, failed, empty, actionError)
        )
      Items = items
      KeyOf =
        fun row -> (if row.Pinned then "pinned:" else "") + nodeKey row.Node
      Row =
        fun row ->
          if row.Pinned then
            StackPanel()
              .Spacing(4.0)
              .Children(
                TextBlock()
                  .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
                  .FontSize(11.0)
                  .Margin(Thickness(16.0, 8.0, 0.0, 0.0))
                  .Text("PINNED"),
                feedRow rowProps row.Node
              )
            :> Control
          else
            feedRow rowProps row.Node
      Footer = ValueSome loadMore
      HasMore =
        model
        |> Observable.map _.Board.HasMore
        |> Observable.distinctUntilChanged
      ScrollTo = ValueNone
      Ride = false
      Restore = ValueNone
      OnScroll = ValueNone
      OnRide = ValueNone
      OnNeedMore = ValueNone
    }

  Grid()
    .Name($"EditorGuildTab-{props.Handle}")
    .Children(feed, EditorCards.layer(cardsProps props, cards, cardDispatch))

let program(props: Props<'env>) =
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
