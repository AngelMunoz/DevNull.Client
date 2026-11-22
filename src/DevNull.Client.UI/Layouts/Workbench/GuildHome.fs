module DevNull.Client.Workbench.GuildHome

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open Microsoft.Extensions.Logging
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.AsyncImage
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.FeedParts
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.GuildBoard
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore

/// What the board reports to its host: the guild row the board read
/// answered, and the last fetch time.
[<Struct>]
type Report = {
  Guild: Guild option
  FetchedAt: DateTimeOffset option
}

type Model = {
  Board: BoardState
  Now: DateTimeOffset
  FetchedAt: DateTimeOffset option
  Busy: string option
  Error: AppError option
  PollChoices: Map<string, string>
  Pasteis: bool
}

type Msg =
  | BoardLoad
  | BoardMore
  | BoardArrived of
    page: int *
    Result<DevNull.Client.Core.ApiTypes.GuildBoard, AppError>
  | ClockTicked of moment: DateTimeOffset
  | RefreshRequested
  | Opened of Commit
  | QuoteRequested of Commit
  | ReplyRequested of Commit
  | StarToggled of Commit
  | StarDone of Result<Commit, AppError>
  | PastelGiven of Commit
  | PastelDone of Result<PastelResult, AppError>
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | PollVoteRequested of Commit
  | PollVotedDone of Result<Commit, AppError>
  | CommitUpdated of Commit
  | CommitDeleted of string

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession option
  Handle: string
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Flyout: DevNull.Client.Components.UserFlyout.Source voption
  Now: IObservable<DateTimeOffset>
  Refresh: IObservable<unit>
  Zone: Zone
  OnOpenExternal: string -> unit
  OnOpened: Commit -> unit
  OnReply: Commit -> unit
  OnQuote: Commit -> unit
  OnStage: string -> unit
  OnReport: Report -> unit
  OnDialog: RowActions.Dialog -> unit
}

let showPage(props: Props<'env>, page: int) =
  Guilds.show(
    props.Env,
    {
      KeyFile = props.KeyFile
      Handle = props.Handle
      Page = page
    }
  )

let star(props: Props<'env>, commit: Commit) : Async<Result<Commit, AppError>> =
  let request: Stars.ActionRequest = {
    KeyFile = props.KeyFile
    CommitId = commit.Id
  }

  if commit.Starred = Some true then
    Stars.unstar(props.Env, request)
  else
    Stars.star(props.Env, request)

let givePastel
  (props: Props<'env>, commit: Commit)
  : Async<Result<PastelResult, AppError>> =
  Pasteis.give(
    props.Env,
    {
      KeyFile = props.KeyFile
      CommitId = commit.Id
    }
  )

let vote
  (props: Props<'env>, commit: Commit, option: PollOption)
  : Async<Result<Commit, AppError>> =
  Commits.vote(
    props.Env,
    {
      KeyFile = props.KeyFile
      CommitId = commit.Id
      OptionId = option.Id
    }
  )

let loggerOf(props: Props<'env>) =
  props.Env.LoggerFactory.CreateLogger "DevNull.Client.Workbench.GuildHome"

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

let report(props: Props<'env>, model: Model) : Cmd<Msg> =
  Cmd.ofAction(fun () ->
    props.OnReport {
      Guild = model.Board.Guild
      FetchedAt = model.FetchedAt
    })

let init(props: Props<'env>) : Model * Cmd<Msg> =
  {
    Board = GuildBoard.load GuildBoard.empty
    Now = DateTimeOffset.UtcNow
    FetchedAt = None
    Busy = None
    Error = None
    PollChoices = Map.empty
    Pasteis = props.Pasteis
  },
  Cmd.OfAsync.perform showPage (props, 1) (fun outcome ->
    BoardArrived(1, outcome))

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
      let log = loggerOf props

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

    landed, report(props, landed)
  | ClockTicked moment -> { model with Now = moment }, Cmd.none
  | RefreshRequested ->
    {
      model with
          Board = GuildBoard.load model.Board
    },
    Cmd.OfAsync.perform showPage (props, 1) (fun outcome ->
      BoardArrived(1, outcome))
  | Opened commit -> model, Cmd.ofAction(fun () -> props.OnOpened commit)
  | QuoteRequested commit -> model, Cmd.ofAction(fun () -> props.OnQuote commit)
  | ReplyRequested commit -> model, Cmd.ofAction(fun () -> props.OnReply commit)
  | StarToggled commit ->
    {
      model with
          Busy = Some commit.Id
          Error = None
    },
    Cmd.OfAsync.perform star (props, commit) StarDone
  | StarDone result ->
    match result with
    | Ok updated ->
      {
        model with
            Board = replaceNode(updated, model.Board)
            Busy = None
      },
      Cmd.none
    | Error error ->
      {
        model with
            Busy = None
            Error = Some error
      },
      Cmd.none
  | PastelGiven commit ->
    {
      model with
          Busy = Some commit.Id
          Error = None
    },
    Cmd.OfAsync.perform givePastel (props, commit) PastelDone
  | PastelDone result ->
    match result with
    | Ok pastel ->
      {
        model with
            Board = replaceNode(pastel.Commit, model.Board)
            Busy = None
      },
      Cmd.none
    | Error error ->
      {
        model with
            Busy = None
            Error = Some error
      },
      Cmd.none
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
    | Some option ->
      {
        model with
            Busy = Some commit.Id
            Error = None
      },
      Cmd.OfAsync.perform vote (props, commit, option) PollVotedDone
    | None -> model, Cmd.none
  | PollVotedDone result ->
    match result with
    | Ok updated ->
      {
        model with
            Board = replaceNode(updated, model.Board)
            Busy = None
            PollChoices = model.PollChoices |> Map.remove updated.Id
      },
      Cmd.none
    | Error error ->
      {
        model with
            Busy = None
            Error = Some error
      },
      Cmd.none
  | CommitUpdated updated ->
    let board =
      if
        model.Board.Commits
        |> List.exists(fun node -> nodeKey node = updated.Id)
      then
        replaceNode(updated, model.Board)
      else
        model.Board

    { model with Board = board }, Cmd.none
  | CommitDeleted commitId -> deleteNode(commitId, model), Cmd.none

let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
  [
    Sub.fromObservable "clock" (props.Now |> Observable.map ClockTicked)

    Sub.fromObservable
      "refresh"
      (props.Refresh |> Observable.map(fun () -> RefreshRequested))
  ]
  |> Sub.batch

// One row of the board's log: the pinned commit leads with its marker
// above it, the order the shared `GuildBoard.ordered` states.
type BoardRow =
  | PinnedRow of node: CommitNode
  | PlainRow of node: CommitNode

let rowKey(item: BoardRow) : string =
  match item with
  | PinnedRow node -> "pinned:" + nodeKey node
  | PlainRow node -> nodeKey node

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
          Cache = props.Images
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

let metaLine(guild: Guild) : string =
  let kind =
    if guild.IsPaused then
      "Public guild · Paused"
    else
      "Public guild"

  $"#{guild.Handle} · {kind} · {shortCount guild.MemberCount} members"

let header(props: Props<'env>, model: IObservable<Model>) : Control =
  let guild =
    model
    |> Observable.map _.Board.Guild
    |> Observable.distinctUntilChanged
    |> Slices.share

  let fetched =
    model
    |> Observable.distinctUntilChangedKey(fun current ->
      current.FetchedAt, minuteBucket current.Now)
    |> Observable.map(fun current ->
      match current.FetchedAt with
      | Some at -> "fetched " + timeAgo(current.Now, at)
      | None -> "not fetched yet")

  let stage =
    Button()
      .Name("GuildBoardStage")
      .Classes(Theme.name Theme.GhostButton)
      .Content(
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .Text($"STAGE → #{props.Handle}")
      )
      .OnClickHandler(fun _ _ -> props.OnStage props.Handle)
      .IsVisible(
        guild
        |> Observable.map(fun current ->
          current |> Option.exists(fun found -> not found.IsPaused))
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
                  .Name("GuildBoardPaused")
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
    .Name("GuildBoardHead")
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
    .Padding(Thickness(16.0, 10.0, 16.0, 8.0))
    .Child(
      DockPanel()
        .LastChildFill(true)
        .Children(
          stage.DockRight().VerticalAlignment(VerticalAlignment.Top),
          StackPanel()
            .Spacing(4.0)
            .Children(
              identity,
              TextBlock()
                .Name("GuildBoardFetched")
                .Classes(Theme.name Theme.Mono)
                .FontSize(12.0)
                .Text(fetched)
            )
        )
    )

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let busy = model |> Observable.map _.Busy |> Slices.share
  let now = model |> Observable.map _.Now |> Slices.share

  let inline slot(command: Commit -> unit) : ActionSlot voption =
    ValueSome { Command = command; Busy = busy }

  let actions =
    props.Session
    |> Option.map(fun session ->
      RowActions.create {
        Env = props.Env
        Session = session
        Now = now
        OfferBasicActions = false
        OnUpdated = CommitUpdated >> dispatch
        OnDeleted = CommitDeleted >> dispatch
        OnDialog = props.OnDialog
        Flyout = props.Flyout
      })

  let rowProps: RowProps = {
    OnEvent =
      fun event ->
        actions |> Option.iter(fun current -> current.Handle event)

        match event with
        | CommitCard.Opened commit -> dispatch(Opened commit)
        | CommitCard.ReplyToggled commit -> dispatch(ReplyRequested commit)
        | CommitCard.QuoteRequested commit -> dispatch(QuoteRequested commit)
        | CommitCard.PollChoiceChanged(commit, option, chosen) ->
          dispatch(PollChoiceChanged(commit, option, chosen))
        | _ -> ()
    OnOpenExternal = props.OnOpenExternal
    ViewerId = props.ViewerId
    ViewerOrgs =
      props.Session
      |> Option.map(fun session -> viewerOrgs session.Session)
      |> Option.defaultValue []
    Pasteis = model |> Observable.map _.Pasteis |> Slices.share
    Now = now
    PollChoices = model |> Observable.map _.PollChoices |> Slices.share
    Dense = true
    Images = props.Images
    Flyout = actions |> Option.map _.Flyout |> Option.defaultValue ValueNone
    Pin = ValueNone
    Star = slot(fun commit -> dispatch(StarToggled commit))
    Stars = ValueNone
    Vote = slot(fun commit -> dispatch(PollVoteRequested commit))
    Pastel = slot(fun commit -> dispatch(PastelGiven commit))
    Menu =
      match actions with
      | Some current -> current.Menu
      | None -> ValueNone
    Translation =
      actions
      |> Option.map _.Translation
      |> Option.defaultValue(Observable.single(fun (_: Commit) -> ValueNone))
    Zone = props.Zone
  }

  let card = CommitCard.feedRow rowProps

  let refused =
    TextBlock()
      .Name("GuildBoardRefused")
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0)
      .Text("This guild is no longer available.")
      .IsVisible(
        model
        |> Observable.map _.Board.Refused
        |> Observable.distinctUntilChanged
      )

  let loading =
    TextBlock()
      .Name("GuildBoardLoading")
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0)
      .Text("Loading the board…")
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Board.Loading && current.Board.Commits.IsEmpty)
        |> Observable.distinctUntilChanged
      )

  let failed =
    StackPanel()
      .Name("GuildBoardFailed")
      .Margin(16.0)
      .Spacing(8.0)
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Board.Failed && not current.Board.Loading)
        |> Observable.distinctUntilChanged
      )
      .Children(
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .Text("The board did not load."),
        Button()
          .Name("GuildBoardRetry")
          .Classes(Theme.name Theme.GhostButton)
          .HorizontalAlignment(HorizontalAlignment.Left)
          .Content("TRY AGAIN")
          .OnClickHandler(fun _ _ -> dispatch BoardLoad)
      )

  let empty =
    TextBlock()
      .Name("GuildBoardEmpty")
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0)
      .Text($"No commits yet. Include #{props.Handle} in a commit.")
      .IsVisible(
        model
        |> Observable.map(fun current ->
          not current.Board.Loading
          && not current.Board.Refused
          && not current.Board.Failed
          && current.Board.Guild.IsSome
          && current.Board.Commits.IsEmpty
          && current.Board.Pinned.IsNone)
        |> Observable.distinctUntilChanged
      )

  let actionError =
    TextBlock()
      .Name("GuildBoardActionError")
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Error |> Option.map describe |> Option.defaultValue "")
        |> Observable.distinctUntilChanged
      )
      .IsVisible(
        model
        |> Observable.map _.Error.IsSome
        |> Observable.distinctUntilChanged
      )

  let loadMore =
    Button()
      .Name("GuildBoardLoadMore")
      .Classes(Theme.name Theme.GhostButton)
      .Margin(16.0, 4.0, 16.0, 12.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Content("LOAD MORE")
      .OnClickHandler(fun _ _ -> dispatch BoardMore)
      .IsVisible(
        model
        |> Observable.map _.Board.HasMore
        |> Observable.distinctUntilChanged
      )

  let pinnedMarker() =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(6.0)
      .Margin(0.0, 8.0, 0.0, 0.0)
      .Children(
        Icons.iconAt(Glyph.Pin, 12.0),
        TextBlock()
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .FontSize(12.0)
          .Text("pinned to board")
      )

  let row(item: BoardRow) : Control =
    match item with
    | PinnedRow node -> StackPanel().Children(pinnedMarker(), card node)
    | PlainRow node -> card node

  let items =
    model
    |> Slices.gateByRefKey (fun current -> current.Board) (fun current ->
      current.Board.Commits.Length, current.Board.Pinned.IsSome)
    |> Observable.map(fun current ->
      current.Board
      |> GuildBoard.ordered
      |> List.map(fun (node, pinned) ->
        if pinned then PinnedRow node else PlainRow node))
    |> Slices.share

  Feed.view {
    Header =
      ValueSome(
        StackPanel()
          .Children(
            header(props, model),
            refused,
            loading,
            failed,
            empty,
            actionError
          )
      )
    Items = items
    KeyOf = rowKey
    Row = row
    Footer = ValueSome loadMore
    HasMore =
      model |> Observable.map _.Board.HasMore |> Observable.distinctUntilChanged
    ScrollTo = ValueNone
    Ride = false
    Restore = ValueNone
    OnScroll = ValueNone
    OnRide = ValueNone
    OnNeedMore = ValueNone
  }

let program(props: Props<'env>) =
  Program.mkProgram
    (fun () -> init props)
    (fun msg model -> update(props, msg, model))
    (view props)
  |> Program.withSubscription(subscribe props)
