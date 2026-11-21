module DevNull.Client.Views.GuildView

open System
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open Navs
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Components.AsyncImage
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.Composer
open DevNull.Client.Components.Feed
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
open DevNull.Client.Navigation
open DevNull.Client.Programs
open DevNull.Client.Profiles
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore
open Microsoft.Extensions.Logging

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideUserSettings
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Handle: string
  Nav: INavigable
}

module State =

  type Model = {
    Board: BoardState
    Now: DateTimeOffset
  }

  type Msg =
    | BoardLoad
    | BoardMore
    | BoardArrived of page: int * Result<GuildBoard, AppError>
    | ClockTicked of moment: DateTimeOffset

  let showPage(props: Props<'env>, page: int) =
    Guilds.show(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        Handle = props.Handle
        Page = page
      }
    )

  let init (props: Props<'env>) () : Model * Cmd<Msg> =
    {
      Board = GuildBoard.load GuildBoard.empty
      Now = DateTimeOffset.UtcNow
    },
    Cmd.OfAsync.perform showPage (props, 1) (fun outcome ->
      BoardArrived(1, outcome))

  let loggerOf(props: Props<'env>) =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.GuildView"

  let updateCore
    (log: ILogger, props: Props<'env>)
    msg
    (model: Model)
    : Model * Cmd<Msg> =
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
        log.LogWarning(
          "The board page {Page} of {Handle} did not arrive: {Error}",
          page,
          props.Handle,
          bounded 300 (describe error)
        )

      {
        model with
            Board = GuildBoard.arrived(model.Board, page, outcome)
      },
      Cmd.none
    | ClockTicked moment -> { model with Now = moment }, Cmd.none

  let update(props: Props<'env>) = updateCore(loggerOf props, props)

  let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
    Sub.fromObservable
      "clock"
      (props.Env.UiClock.Minute |> Observable.map ClockTicked)

// One row of the bulletin board: the pinned commit leads, its marker
// above it, the way the site's own guild page orders them.
type BoardRow =
  | Pinned of CommitNode
  | Plain of CommitNode

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  // The hover-card source the rows share: every open stays inside the
  // app, a guild chip included.
  let source: UserFlyout.Source = {
    Fetch = props.Env.Profiles.Fetch
    OnOpen =
      fun username ->
        Navigation.go(
          props.Nav,
          "/profile/" + Uri.EscapeDataString username,
          ignore
        )
    OnOpenOrg =
      fun handle ->
        Navigation.go(props.Nav, "/org/" + Uri.EscapeDataString handle, ignore)
    OnOpenGuild =
      fun handle ->
        Navigation.go(
          props.Nav,
          "/guilds/" + Uri.EscapeDataString handle,
          ignore
        )
    OnOpenTag =
      fun tag ->
        Navigation.go(props.Nav, "/search/" + Uri.EscapeDataString tag, ignore)
    Cache = props.Env.ImageCache
  }

  let pastels = new BehaviorSubject<bool>(false)

  let choices = new BehaviorSubject<Map<string, string>>(Map.empty)

  let translation =
    new BehaviorSubject<Commit -> TranslationView voption>(fun _ -> ValueNone)

  let cards: CommitCard.RowProps = {
    OnEvent = ignore
    OnOpenExternal = fun url -> props.Env.Browser.Open url |> ignore
    ViewerId = props.Session.Session.User.Id
    Pasteis = pastels
    Now = model |> Observable.map _.Now
    PollChoices = choices
    Dense = false
    Images = props.Env.ImageCache
    Flyout = ValueSome source
    Pin = ValueNone
    Star = ValueNone
    Stars = ValueNone
    Vote = ValueNone
    Pastel = ValueNone
    Menu = ValueNone
    Translation = translation
    Zone = zoneOf props.Env.UserSettings.Current
  }

  let nodeId(node: CommitNode) =
    match node with
    | LiveCommit commit -> commit.Id
    | Tombstone stone -> stone.Id

  let rowId(item: BoardRow) =
    match item with
    | Pinned node
    | Plain node -> nodeId node

  // The identity header the board read answers; the picker the aimed
  // composer offers holds the same guild row. The page's own lifetime
  // ends the swaps when its program ends.
  let guilds = new BehaviorSubject<Guild list>([])

  let mark(guild: Guild) : Control =
    match guild.IconUrl with
    | Some url ->
      Border()
        .Width(48.0)
        .Height(48.0)
        .CornerRadius(CornerRadius 8.0)
        .ClipToBounds(true)
        .Child(
          AsyncImage.view {
            Cache = props.Env.ImageCache
            Url = url
            DecodeWidth = 96
            DecodeHeight = ValueNone
            Stretch = Stretch.UniformToFill
            ToolTip = ValueSome guild.Name
          }
        )
    | None ->
      TextBlock()
        .FontSize(28.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Text(guild.Emoji)

  let headerOf(guild: Guild) : Control =
    let kind =
      if guild.IsPaused then
        "Public guild · Paused"
      else
        "Public guild"

    StackPanel()
      .Margin(16.0, 16.0, 16.0, 0.0)
      .Spacing(8.0)
      .Children(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(12.0)
          .Children(
            mark guild,
            TextBlock()
              .Classes(Theme.name Theme.Title)
              .VerticalAlignment(VerticalAlignment.Center)
              .Text(guild.Name)
          ),
        TextBlock()
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .FontSize(12.0)
          .Text(
            $"#{guild.Handle} · {kind} · {shortCount guild.MemberCount} members"
          ),
        (match guild.Description with
         | Some text ->
           TextBlock()
             .Classes(Theme.name Theme.Muted)
             .TextWrapping(TextWrapping.Wrap)
             .Text(text)
           :> Control
         | None -> Panel() :> Control),
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .FontSize(12.0)
          .TextWrapping(TextWrapping.Wrap)
          .Text(
            if guild.IsPaused then
              "This guild is paused until it has a moderator. Its board and members are preserved."
            else
              $"Include #{guild.Handle} in any commit to post here."
          )
      )

  let header = ContentControl()

  // The composer the board answers with, aimed at the guild; a paused
  // guild shows none, the way the site's own page does.
  let composerHost = ContentControl()

  model
  |> Observable.map _.Board.Guild
  |> Observable.distinctUntilChanged
  |> Observable.subscribe(fun current ->
    guilds.OnNext(
      match current with
      | Some guild -> [ guild ]
      | None -> []
    )

    header.Content <-
      (match current with
       | Some guild -> headerOf guild
       | None -> Panel() :> Control)

    let offered =
      match current with
      | Some guild when not guild.IsPaused -> true
      | _ -> false

    composerHost.Content <-
      (if offered then
         Composer.Page {
           Env = props.Env
           Session = props.Session
           Target = Composer.Fresh
           Participants = ValueNone
           Guilds = ValueSome(fun () -> guilds.Value)
           Guild = ValueSome props.Handle
           OnCommitted = fun _ -> dispatch State.BoardLoad
           OnScheduled = ValueNone
           OnOpenQueue =
             ValueSome(fun () ->
               Navigation.go(props.Nav, "/scheduled", ignore))
           Draft = ValueNone
           OnBodyReady = ValueNone
         }
         :> Control
       else
         Panel()))
  |> ignore

  let boardHeading =
    TextBlock()
      .Name("BoardHeading")
      .Classes(Theme.name Theme.Title)
      .Margin(16.0, 12.0, 16.0, 8.0)
      .Text("Bulletin board · Latest commits")

  let boardRule =
    Border()
      .Name("BoardRule")
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Margin(16.0, 0.0, 16.0, 4.0)

  let refused =
    TextBlock()
      .Name("BoardRefused")
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
      .Name("BoardLoading")
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
      .Name("BoardFailed")
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
          .Name("BoardRetry")
          .Classes(Theme.name Theme.GhostButton)
          .HorizontalAlignment(HorizontalAlignment.Left)
          .Content("Try again")
          .OnClickHandler(fun _ _ -> dispatch State.BoardLoad)
      )

  let empty =
    TextBlock()
      .Name("BoardEmpty")
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0, 32.0)
      .Text(
        $"No commits yet. Post to the board or include #{props.Handle} in a commit."
      )
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

  let loadMore =
    Button()
      .Name("BoardLoadMore")
      .Classes(Theme.name Theme.GhostButton)
      .Margin(16.0, 4.0, 16.0, 12.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Content("Load more")
      .OnClickHandler(fun _ _ -> dispatch State.BoardMore)
      .IsVisible(
        model
        |> Observable.map _.Board.HasMore
        |> Observable.distinctUntilChanged
      )

  // The board's rows in the order the shared `GuildBoard.ordered`
  // states: the pinned commit leads, its marker above it.
  let rowsOf(board: BoardState) : BoardRow list =
    board
    |> GuildBoard.ordered
    |> List.map(fun (node, pinned) ->
      if pinned then Pinned node else Plain node)

  let card = CommitCard.feedRow cards

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
          .Text("Pinned to board")
      )

  let row(item: BoardRow) : Control =
    match item with
    | Pinned node -> StackPanel().Children(pinnedMarker(), card node)
    | Plain node -> card node

  Feed.view {
    Header =
      ValueSome(
        StackPanel()
          .Children(
            header,
            composerHost,
            boardHeading,
            boardRule,
            refused,
            loading,
            failed,
            empty
          )
      )
    Items =
      model
      |> Observable.map(fun current -> rowsOf current.Board)
      |> Observable.distinctUntilChanged
    KeyOf = rowId
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

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideUserSettings
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    // The board is replace-only: the first detach ends the program.
    Programs.runComponentOnAttach(
      this,
      props.Env.Dispatcher,
      program,
      fun root -> this.Content <- root
    )
    |> ignore
