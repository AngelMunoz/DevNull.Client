module DevNull.Client.Layouts.Workbench.HistoryColumn

open System
open Avalonia
open Avalonia.Controls
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
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Theme

type Model = {
  Loading: bool
  Error: AppError option
  Items: CommitNode list
  NextCursor: string option
  Head: string option
  NewCount: int option
  Ranked: bool
  Polling: bool
  Now: DateTimeOffset
  FetchedAt: DateTimeOffset option
  Busy: string option
  PollChoices: Map<string, string>
  Pasteis: bool
}

type Msg =
  | Fetched of Result<LogPage, AppError>
  | MoreRequested
  | Appended of Result<LogPage, AppError>
  | CheckNew
  | NewChecked of Result<LogPage, AppError>
  | NewRequested
  | NewFetched of Result<LogPage, AppError>
  | ClockTicked of moment: DateTimeOffset
  | PollingChanged of active: bool
  | Opened of Commit
  | QuoteRequested of Commit
  | StarToggled of Commit
  | StarDone of Result<Commit, AppError>
  | PastelGiven of Commit
  | PastelDone of Result<PastelResult, AppError>
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | PollVoteRequested of Commit
  | PollVotedDone of Result<Commit, AppError>

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
  Log: LogKind
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  OnOpenExternal: string -> unit
  OnOpened: Commit -> unit
  OnQuote: Commit -> unit
}

let labelOf(log: LogKind) =
  match log with
  | Latest -> "Latest"
  | Following -> "Following"
  | Trending -> "Trending"

let headOf(items: CommitNode list) =
  items
  |> List.tryPick(function
    | LiveCommit commit -> Some commit.Id
    | Tombstone _ -> None)

let liveIds(items: CommitNode list) =
  items
  |> List.choose(function
    | LiveCommit commit -> Some commit.Id
    | Tombstone _ -> None)

let replaceNode(items: CommitNode list, updated: Commit) =
  items
  |> List.map(function
    | LiveCommit commit when commit.Id = updated.Id -> LiveCommit updated
    | node -> node)

let fetch
  (props: Props<'env>, cursor: string option)
  : Async<Result<LogPage, AppError>> =
  Logs.get(
    props.Env,
    {
      KeyFile = props.KeyFile
      Log = props.Log
      Since = None
      Cursor = cursor
      Arrived = []
      Pending = []
      Everything = false
      Through = []
    }
  )

let checkNew
  (props: Props<'env>, model: Model, head: string)
  : Async<Result<LogPage, AppError>> =
  let ids = liveIds model.Items

  Logs.get(
    props.Env,
    {
      KeyFile = props.KeyFile
      Log = props.Log
      Since = Some head
      Cursor = None
      Arrived = []
      Pending = ids |> List.truncate 100
      Everything = false
      Through = ids |> List.rev |> List.truncate 20
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

let init(props: Props<'env>) : Model * Cmd<Msg> =
  ({
    Loading = true
    Error = None
    Items = []
    NextCursor = None
    Head = None
    NewCount = None
    Ranked = false
    Polling = true
    Now = DateTimeOffset.UtcNow
    FetchedAt = None
    Busy = None
    PollChoices = Map.empty
    Pasteis = props.Pasteis
   },
   Cmd.OfAsync.perform fetch (props, None) Fetched)

let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
  match msg with
  | Fetched result ->
    match result with
    | Ok page ->
      ({
        model with
            Loading = false
            Error = None
            Items = page.Page.Items
            NextCursor = page.Page.NextCursor
            Head = headOf page.Page.Items
            NewCount = None
            Ranked = page.IsRanked
            FetchedAt = Some model.Now
       },
       Cmd.none)
    | Error error ->
      ({
        model with
            Loading = false
            Error = Some error
       },
       Cmd.none)
  | MoreRequested ->
    match model.NextCursor with
    | Some _ ->
      model, Cmd.OfAsync.perform fetch (props, model.NextCursor) Appended
    | None -> model, Cmd.none
  | Appended result ->
    match result with
    | Ok page ->
      ({
        model with
            Error = None
            Items = model.Items @ page.Page.Items
            NextCursor = page.Page.NextCursor
            FetchedAt = Some model.Now
       },
       Cmd.none)
    | Error error -> ({ model with Error = Some error }, Cmd.none)
  | CheckNew ->
    match model.Head, model.Ranked with
    | Some head, false ->
      model, Cmd.OfAsync.perform checkNew (props, model, head) NewChecked
    | _ -> model, Cmd.none
  | NewChecked result ->
    match result with
    | Ok page ->
      ({
        model with
            NewCount =
              page.NewCommitsCount |> Option.filter(fun count -> count > 0)
       },
       Cmd.none)
    | Error _ -> model, Cmd.none
  | NewRequested ->
    ({ model with NewCount = None },
     Cmd.OfAsync.perform fetch (props, None) NewFetched)
  | NewFetched result ->
    match result with
    | Ok page ->
      ({
        model with
            Error = None
            Items = page.Page.Items
            NextCursor = page.Page.NextCursor
            Head = headOf page.Page.Items
            Ranked = page.IsRanked
            FetchedAt = Some model.Now
       },
       Cmd.none)
    | Error error -> ({ model with Error = Some error }, Cmd.none)
  | ClockTicked moment -> ({ model with Now = moment }, Cmd.none)
  | PollingChanged active -> ({ model with Polling = active }, Cmd.none)
  | Opened commit -> model, Cmd.ofAction(fun () -> props.OnOpened commit)
  | QuoteRequested commit -> model, Cmd.ofAction(fun () -> props.OnQuote commit)
  | StarToggled commit ->
    ({
      model with
          Error = None
          Busy = Some commit.Id
     },
     Cmd.OfAsync.perform star (props, commit) StarDone)
  | StarDone result ->
    match result with
    | Ok updated ->
      ({
        model with
            Items = replaceNode(model.Items, updated)
            Busy = None
       },
       Cmd.none)
    | Error error ->
      ({
        model with
            Error = Some error
            Busy = None
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
    | Ok pastel ->
      ({
        model with
            Items = replaceNode(model.Items, pastel.Commit)
            Busy = None
       },
       Cmd.none)
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
            Items = replaceNode(model.Items, updated)
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

let subscribe (props: Props<'env>) (model: Model) : Sub<Msg> =
  [
    Sub.fromObservable "clock" (props.Now |> Observable.map ClockTicked)

    if model.Polling then
      Sub.interval "since-poll" (TimeSpan.FromMinutes 1.0) (fun () -> CheckNew)
  ]
  |> Sub.batch

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let busy = model |> Observable.map _.Busy |> Slices.share

  let inline slot(command: Commit -> unit) : ActionSlot voption =
    ValueSome { Command = command; Busy = busy }

  let rowProps: RowProps = {
    OnEvent =
      function
      | CommitCard.Opened commit -> dispatch(Opened commit)
      | CommitCard.QuoteRequested commit -> dispatch(QuoteRequested commit)
      | CommitCard.PollChoiceChanged(commit, option, chosen) ->
        dispatch(PollChoiceChanged(commit, option, chosen))
      | _ -> ()
    OnOpenExternal = props.OnOpenExternal
    ViewerId = props.ViewerId
    Pasteis = model |> Observable.map _.Pasteis |> Slices.share
    Now = model |> Observable.map _.Now |> Slices.share
    PollChoices = model |> Observable.map _.PollChoices |> Slices.share
    Dense = true
    Images = props.Images
    Flyout = ValueNone
    Pin = ValueNone
    Star = slot(fun commit -> dispatch(StarToggled commit))
    Stars = ValueNone
    Vote = slot(fun commit -> dispatch(PollVoteRequested commit))
    Pastel = slot(fun commit -> dispatch(PastelGiven commit))
    Menu = ValueNone
    Translation = Observable.single(fun (_: Commit) -> ValueNone)
    Zone = props.Zone
  }

  let headLine =
    model |> Observable.map(fun _ -> "HEAD -> " + labelOf props.Log)

  let fetchedLine =
    model
    |> Observable.map(fun current ->
      match current.FetchedAt with
      | Some at -> "fetched " + timeAgo(current.Now, at)
      | None -> "not fetched yet")

  let pillText =
    model
    |> Observable.map(fun current ->
      current.NewCount
      |> Option.map(fun count -> $"{count} new — fetch")
      |> Option.defaultValue "")

  let pill =
    Button()
      .Classes(Theme.name Theme.AccentButton)
      .CornerRadius(999.0)
      .Padding(Thickness(12.0, 4.0, 12.0, 4.0))
      .Content(TextBlock().Text(pillText))
      .IsVisible(model |> Observable.map _.NewCount.IsSome)
      .OnClickHandler(fun _ _ -> dispatch NewRequested)

  let header =
    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Padding(Thickness(16.0, 10.0, 16.0, 8.0))
      .Child(
        DockPanel()
          .LastChildFill(true)
          .Children(
            pill.DockRight(),
            StackPanel()
              .Spacing(2.0)
              .Children(
                TextBlock().Classes(Theme.name Theme.Mono).Text(headLine),
                TextBlock().Classes(Theme.name Theme.Mono).Text(fetchedLine)
              )
          )
      )

  let statusText =
    model
    |> Observable.map(fun current ->
      if current.Loading then
        "Loading the history…"
      elif current.Error.IsSome then
        current.Error |> Option.map describe |> Option.defaultValue ""
      elif current.Items.IsEmpty then
        "Nothing here yet. Commit something."
      else
        "")

  let statusLine =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Margin(Thickness(16.0, 6.0, 16.0, 6.0))
      .Text(statusText)

  let hasMore = model |> Observable.map _.NextCursor.IsSome

  let feed =
    Feed.view {
      Items = model |> Observable.map _.Items |> Slices.gateByRef
      KeyOf = FeedParts.nodeKey
      Row = fun node -> CommitCard.feedRow rowProps node
      Footer =
        ValueSome(
          PageChrome.loadMore(hasMore, fun () -> dispatch MoreRequested)
        )
      HasMore = hasMore
      ScrollTo = ValueNone
      Ride = false
      Restore = ValueNone
      OnScroll = ValueNone
      OnRide = ValueNone
      OnNeedMore = ValueSome(fun () -> dispatch MoreRequested)
    }

  DockPanel()
    .LastChildFill(true)
    .Children(header.DockTop(), statusLine.DockTop(), feed)

let program(props: Props<'env>) =
  Program.mkProgram
    (fun () -> init props)
    (fun msg model -> update(props, msg, model))
    (view props)
  |> Program.withSubscription(subscribe props)
