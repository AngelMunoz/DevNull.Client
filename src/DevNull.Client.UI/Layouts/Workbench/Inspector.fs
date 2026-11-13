module DevNull.Client.Workbench.Inspector

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore

type Model = {
  CommitId: string
  Loading: bool
  Error: AppError option
  Commit: Commit option
  Ancestors: CommitNode list
  ReplyParticipants: ReplyParticipant list
  Replies: CommitNode list
  RepliesCursor: string option
  RepliesLoading: bool
  RepliesError: AppError option
  Busy: string option
  Pinning: string option
  PollChoices: Map<string, string>
  Now: DateTimeOffset
}

type Msg =
  | Shown of Result<CommitPage, AppError>
  | RepliesFetched of Result<Page<CommitNode>, AppError>
  | MoreRepliesRequested
  | RepliesAppended of Result<Page<CommitNode>, AppError>
  | ClockTicked of moment: DateTimeOffset
  | StarToggled of Commit
  | StarDone of Result<Commit, AppError>
  | PinToggled of Commit
  | PinDone of pinned: bool * Result<Commit, AppError>
  | PastelGiven of Commit
  | PastelDone of Result<PastelResult, AppError>
  | ParentWalked of commitId: string
  | ChildOpened of commitId: string
  | AuthorOpened of handle: string
  | QuoteRequested of Commit
  | ReplyToggled of Commit
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | PollVoteRequested of Commit
  | PollVotedDone of Result<Commit, AppError>
  | CommitUpdated of Commit
  | CommitDeleted of string
  | CloseRequested

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
  CommitId: string
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Flyout: DevNull.Client.Components.UserFlyout.Source voption
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  OnCheckoutParent: string -> unit
  OnCheckoutChild: string -> unit
  OnOpenAuthor: string -> unit
  OnOpenExternal: string -> unit
  OnQuote: Commit -> unit
  OnOpenThread: Commit -> unit
  OnClose: unit -> unit
  OnDialog: RowActions.Dialog -> unit
}

let show(props: Props<'env>) : Async<Result<CommitPage, AppError>> =
  Commits.show(
    props.Env,
    {
      KeyFile = props.KeyFile
      CommitId = props.CommitId
    }
  )

let fetchReplies
  (props: Props<'env>, cursor: string option)
  : Async<Result<Page<CommitNode>, AppError>> =
  Commits.replies(
    props.Env,
    {
      KeyFile = props.KeyFile
      CommitId = props.CommitId
      Newest = true
      Cursor = cursor
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

let pin
  (props: Props<'env>, commit: Commit, pinned: bool)
  : Async<Result<Commit, AppError>> =
  let request: CommitPins.ActionRequest = {
    KeyFile = props.KeyFile
    CommitId = commit.Id
  }

  if pinned then
    CommitPins.pin(props.Env, request)
  else
    CommitPins.unpin(props.Env, request)

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

let init(props: Props<'env>) : Model * Cmd<Msg> =
  ({
    CommitId = props.CommitId
    Loading = true
    Error = None
    Commit = None
    Ancestors = []
    ReplyParticipants = []
    Replies = []
    RepliesCursor = None
    RepliesLoading = false
    RepliesError = None
    Busy = None
    Pinning = None
    PollChoices = Map.empty
    Now = DateTimeOffset.UtcNow
   },
   Cmd.OfAsync.perform show props Shown)

let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
  match msg with
  | Shown result ->
    match result with
    | Ok page ->
      ({
        model with
            Loading = false
            Error = None
            Commit = Some page.Commit
            Ancestors = page.Ancestors
            ReplyParticipants = page.ReplyParticipants
            RepliesLoading = true
            RepliesError = None
       },
       Cmd.OfAsync.perform fetchReplies (props, None) RepliesFetched)
    | Error error ->
      ({
        model with
            Loading = false
            Error = Some error
       },
       Cmd.none)
  | RepliesFetched result ->
    match result with
    | Ok page ->
      ({
        model with
            Replies = page.Items
            RepliesCursor = page.NextCursor
            RepliesLoading = false
            RepliesError = None
       },
       Cmd.none)
    | Error error ->
      ({
        model with
            RepliesLoading = false
            RepliesError = Some error
       },
       Cmd.none)
  | MoreRepliesRequested ->
    match model.RepliesCursor with
    | Some _ ->
      ({
        model with
            RepliesLoading = true
            RepliesError = None
       },
       Cmd.OfAsync.perform
         fetchReplies
         (props, model.RepliesCursor)
         RepliesAppended)
    | None -> model, Cmd.none
  | RepliesAppended result ->
    match result with
    | Ok page ->
      ({
        model with
            Replies = model.Replies @ page.Items
            RepliesCursor = page.NextCursor
            RepliesLoading = false
            RepliesError = None
       },
       Cmd.none)
    | Error error ->
      ({
        model with
            RepliesLoading = false
            RepliesError = Some error
       },
       Cmd.none)
  | ClockTicked moment -> ({ model with Now = moment }, Cmd.none)
  | StarToggled commit ->
    ({
      model with
          Error = None
          Busy = Some commit.Id
     },
     Cmd.OfAsync.perform star (props, commit) StarDone)
  | StarDone result ->
    match result with
    | Ok updated when updated.Id = model.CommitId ->
      ({
        model with
            Commit = Some updated
            Busy = None
       },
       Cmd.none)
    | Ok _ -> ({ model with Busy = None }, Cmd.none)
    | Error error ->
      ({
        model with
            Error = Some error
            Busy = None
       },
       Cmd.none)
  | PinToggled commit ->
    let pinned = not commit.IsPinned

    ({
      model with
          Error = None
          Pinning = Some commit.Id
     },
     Cmd.OfAsync.perform pin (props, commit, pinned) (fun result ->
       PinDone(pinned, result)))
  | PinDone(_, result) ->
    match result with
    | Ok updated ->
      ({
        model with
            Commit =
              model.Commit
              |> Option.map(fun current ->
                if current.Id = updated.Id then updated else current)
            Pinning = None
       },
       Cmd.none)
    | Error error ->
      ({
        model with
            Error = Some error
            Pinning = None
       },
       Cmd.none)
  | PastelGiven commit ->
    ({
      model with
          Error = None
          Busy = Some commit.Id
     },
     Cmd.OfAsync.perform givePastel (props, commit) PastelDone)
  | PastelDone result ->
    match result with
    | Ok pastel when pastel.Commit.Id = model.CommitId ->
      ({
        model with
            Commit = Some pastel.Commit
            Busy = None
       },
       Cmd.none)
    | Ok _ -> ({ model with Busy = None }, Cmd.none)
    | Error error ->
      ({
        model with
            Error = Some error
            Busy = None
       },
       Cmd.none)
  | PollChoiceChanged(commit, option, chosen) ->
    ({
      model with
          PollChoices =
            CommitCard.choose {
              Choices = model.PollChoices
              CommitId = commit.Id
              OptionId = option.Id
              Chosen = chosen
            }
     },
     Cmd.none)
  | PollVoteRequested commit ->
    match CommitCard.selectedOption(model.PollChoices, commit) with
    | Some option ->
      ({
        model with
            Error = None
            Busy = Some commit.Id
       },
       Cmd.OfAsync.perform vote (props, commit, option) PollVotedDone)
    | None -> model, Cmd.none
  | PollVotedDone result ->
    match result with
    | Ok updated ->
      ({
        model with
            Commit =
              model.Commit
              |> Option.map(fun current ->
                if current.Id = updated.Id then updated else current)
            Busy = None
            PollChoices = model.PollChoices |> Map.remove updated.Id
       },
       Cmd.none)
    | Error error ->
      ({
        model with
            Error = Some error
            Busy = None
       },
       Cmd.none)
  | CommitUpdated updated ->
    ({
      model with
          Commit =
            model.Commit
            |> Option.map(fun current ->
              if current.Id = updated.Id then updated else current)
     },
     Cmd.none)
  | CommitDeleted commitId when commitId = model.CommitId ->
    model, Cmd.ofAction props.OnClose
  | CommitDeleted _ -> model, Cmd.none
  | ParentWalked commitId ->
    model, Cmd.ofAction(fun () -> props.OnCheckoutParent commitId)
  | ChildOpened commitId ->
    model, Cmd.ofAction(fun () -> props.OnCheckoutChild commitId)
  | AuthorOpened handle ->
    model, Cmd.ofAction(fun () -> props.OnOpenAuthor handle)
  | QuoteRequested commit -> model, Cmd.ofAction(fun () -> props.OnQuote commit)
  | ReplyToggled commit ->
    model, Cmd.ofAction(fun () -> props.OnOpenThread commit)
  | CloseRequested -> model, Cmd.ofAction props.OnClose

let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
  Sub.fromObservable "clock" (props.Now |> Observable.map ClockTicked)

let laneLabel(lane: Lane option) =
  match lane with
  | Some(LaneIndex index) -> $"lane {index}"
  | Some(LaneColor color) -> $"lane {color}"
  | None -> "no lane"

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let busy = model |> Observable.map _.Busy |> Slices.share
  let pinning = model |> Observable.map _.Pinning |> Slices.share
  let now = model |> Observable.map _.Now |> Slices.share
  let pasteis = Observable.single props.Pasteis
  let polls = model |> Observable.map _.PollChoices |> Slices.share

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

  let translation =
    actions
    |> Option.map _.Translation
    |> Option.defaultValue(Observable.single(fun (_: Commit) -> ValueNone))

  let inline slot(command: Commit -> unit) : ActionSlot voption =
    ValueSome { Command = command; Busy = busy }

  let rowProps(onEvent: Event -> unit) : RowProps = {
    OnEvent =
      fun event ->
        actions |> Option.iter(fun current -> current.Handle event)
        onEvent event
    OnOpenExternal = props.OnOpenExternal
    ViewerId = props.ViewerId
    Pasteis = pasteis
    Now = now
    PollChoices = polls
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
    Translation = translation
    Zone = props.Zone
  }

  let checkoutProps: RowProps = {
    rowProps(fun event ->
      match event with
      | CommitCard.QuoteRequested commit -> dispatch(QuoteRequested commit)
      | CommitCard.ReplyToggled commit -> dispatch(ReplyToggled commit)
      | CommitCard.PollChoiceChanged(commit, option, chosen) ->
        dispatch(PollChoiceChanged(commit, option, chosen))
      | _ -> ()) with
        Dense = false
        Pin =
          ValueSome {
            Command = fun commit -> dispatch(PinToggled commit)
            Busy = pinning
          }
  }

  let ancestorProps =
    rowProps(fun event ->
      match event with
      | CommitCard.Opened commit -> dispatch(ParentWalked commit.Id)
      | CommitCard.ReplyToggled commit -> dispatch(ReplyToggled commit)
      | CommitCard.QuoteRequested commit -> dispatch(QuoteRequested commit)
      | CommitCard.PollChoiceChanged(commit, option, chosen) ->
        dispatch(PollChoiceChanged(commit, option, chosen))
      | _ -> ())

  let childProps =
    rowProps(fun event ->
      match event with
      | CommitCard.Opened commit -> dispatch(ChildOpened commit.Id)
      | CommitCard.ReplyToggled commit -> dispatch(ReplyToggled commit)
      | CommitCard.QuoteRequested commit -> dispatch(QuoteRequested commit)
      | CommitCard.PollChoiceChanged(commit, option, chosen) ->
        dispatch(PollChoiceChanged(commit, option, chosen))
      | _ -> ())

  let authorBlock(author: Author) : Control =
    let lane = Author.lane author

    let identity =
      StackPanel()
        .Spacing(2.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .Text(Author.name author),
          ProfileCard.laneHandle(lane, Author.handle author),
          TextBlock().Classes(Theme.name Theme.Mono).Text(laneLabel lane)
        )

    let grid = Grid()
    grid.ColumnDefinitions <- ColumnDefinitions("Auto,*")

    grid.Children(
      Avatar.view {
        Name = (Author.name author)
        Url = (Author.avatarUrl author)
        Lane = lane
        Size = 40.0
        Cache = props.Images
        Square = (Author.isOrganization author)
        Viewable = false
      }
      |> _.Column(0)
      |> _.Margin(0.0, 0.0, 12.0, 0.0),
      identity.Column(1)
    )

    Button()
      .Classes(Theme.name Theme.GhostButton)
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .Content(grid)
      .OnClickHandler(fun _ _ -> dispatch(AuthorOpened(Author.handle author)))

  let close =
    Button()
      .Name("InspectorClose")
      .Classes(Theme.name Theme.GhostButton)
      .Content(Icons.iconAt(Icons.Glyph.Close, 12.0))
      .Margin(Thickness(0.0, 0.0, 8.0, 0.0))
      .OnClickHandler(fun _ _ -> dispatch CloseRequested)

  ToolTip.SetTip(close, "Close checkout")

  let header =
    DockPanel()
      .LastChildFill(true)
      .Children(
        close.DockRight(),
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .Text(
            model
            |> Observable.map(fun current ->
              current.Commit
              |> Option.map(fun commit -> "CHECKOUT " + commit.ShortHash)
              |> Option.defaultValue "CHECKOUT")
          )
      )

  let busyLine =
    PageChrome.collapsingMessagePanel(
      model
      |> Observable.map(fun current ->
        if current.Loading then
          "Reading the commit…"
        elif current.Commit.IsNone && current.Error.IsNone then
          "No commit checked out."
        else
          ""),
      Theme.Muted
    )

  let errorLine = PageChrome.errorPanel(model |> Observable.map _.Error)

  let authorHost =
    ContentControl()
      .Content(
        model
        |> Observable.map _.Commit
        |> Observable.distinctUntilChanged
        |> Observable.map(function
          | Some commit -> authorBlock commit.Author
          | None -> Panel() :> Control)
        |> _.ToBinding()
      )

  let cardHost =
    ContentControl()
      .Content(
        model
        |> Observable.map _.Commit
        |> Observable.distinctUntilChanged
        |> Observable.map(function
          | Some commit -> CommitCard.feedRow checkoutProps (LiveCommit commit)
          | None -> Panel() :> Control)
        |> _.ToBinding()
      )

  let parentsBlock =
    ContentControl()
      .Content(
        model
        |> Observable.map _.Ancestors
        |> Observable.distinctUntilChanged
        |> Observable.map(function
          | [] -> Panel() :> Control
          | nodes ->
            StackPanel()
              .Spacing(6.0)
              .Children(
                TextBlock().Classes(Theme.name Theme.Mono).Text("PARENTS"),
                StackPanel()
                  .Spacing(2.0)
                  .Children(
                    [|
                      for node in nodes -> CommitCard.feedRow ancestorProps node
                    |]
                  )
              )
            :> Control)
        |> _.ToBinding()
      )

  let repliesRows =
    ContentControl()
      .Content(
        model
        |> Observable.map _.Replies
        |> Observable.distinctUntilChanged
        |> Observable.map(fun nodes ->
          StackPanel()
            .Spacing(2.0)
            .Children(
              [| for node in nodes -> CommitCard.feedRow childProps node |]
            )
          :> Control)
        |> _.ToBinding()
      )

  let repliesBlock =
    StackPanel()
      .Name("InspectorReplies")
      .Spacing(6.0)
      .Children(
        TextBlock().Classes(Theme.name Theme.Mono).Text("REPLIES"),
        repliesRows,
        PageChrome.collapsingMessagePanel(
          model
          |> Observable.map(fun current ->
            if current.RepliesLoading then
              "Loading replies…"
            elif current.Replies.IsEmpty && current.RepliesError.IsNone then
              "No replies yet."
            else
              ""),
          Theme.Muted
        ),
        PageChrome.errorPanel(model |> Observable.map _.RepliesError),
        PageChrome.loadMore(
          model
          |> Observable.map(fun current ->
            current.RepliesCursor.IsSome && not current.RepliesLoading),
          fun () -> dispatch MoreRepliesRequested
        )
      )

  ScrollViewer()
    .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
    .Content(
      StackPanel()
        .Spacing(12.0)
        .Margin(Thickness(16.0, Theme.bandHeight + 8.0, 56.0, 16.0))
        .Children(
          header,
          busyLine,
          errorLine,
          authorHost,
          cardHost,
          parentsBlock,
          repliesBlock
        )
    )

let program(props: Props<'env>) =
  Program.mkProgram
    (fun () -> init props)
    (fun msg model -> update(props, msg, model))
    (view props)
  |> Program.withSubscription(subscribe props)
