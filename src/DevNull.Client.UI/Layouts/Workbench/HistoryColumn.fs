module DevNull.Client.Workbench.HistoryColumn

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
  HeardIds: string list
  Offset: float
}

[<Struct>]
type Place = { Offset: float; Head: string option }

[<Struct>]
type Report = {
  Place: Place
  FetchedAt: DateTimeOffset option
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
  | RefreshRequested
  | PollingChanged of active: bool
  | Scrolled of offset: float
  | Opened of Commit
  | QuoteRequested of Commit
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
  Source: HistorySource
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Refresh: IObservable<unit>
  Zone: Zone
  LastVisit: DateTimeOffset option
  OnOpenExternal: string -> unit
  OnOpened: Commit -> unit
  OnQuote: Commit -> unit
  Restore: Place option
  OnReport: Report -> unit
  OnDialog: RowActions.Dialog -> unit
}

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

let insertLate(late: Commit, items: CommitNode list) =
  let at =
    items
    |> List.tryFindIndex(function
      | Tombstone _ -> false
      | LiveCommit commit -> commit.CreatedAt < late.CreatedAt)

  match at with
  | Some index -> items |> List.insertAt index (LiveCommit late)
  | None -> items @ [ LiveCommit late ]

let fetch
  (props: Props<'env>, cursor: string option)
  : Async<Result<LogPage, AppError>> =
  match props.Source with
  | LogSource log ->
    Logs.get(
      props.Env,
      {
        KeyFile = props.KeyFile
        Log = log
        Since = None
        Cursor = cursor
        Arrived = []
        Pending = []
        Everything = false
        Through = []
      }
    )
  | SavedSource -> async {
      let! result =
        Stars.index(
          props.Env,
          {
            KeyFile = props.KeyFile
            Cursor = cursor
          }
        )

      return
        result
        |> Result.map(fun page -> {
          Page = page
          Log = Following
          IsRanked = false
          NewCommitsCount = None
          Suggestions = []
        })
    }

let checkNew
  (props: Props<'env>, model: Model, head: string)
  : Async<Result<LogPage, AppError>> =
  let ids = liveIds model.Items

  let log =
    match props.Source with
    | LogSource log -> log
    | SavedSource -> Following

  Logs.get(
    props.Env,
    {
      KeyFile = props.KeyFile
      Log = log
      Since = Some head
      Cursor = None
      Arrived = if model.Ranked then [] else model.HeardIds
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

  match props.Source with
  | SavedSource -> Stars.unstar(props.Env, request)
  | LogSource _ when commit.Starred = Some true ->
    Stars.unstar(props.Env, request)
  | LogSource _ -> Stars.star(props.Env, request)

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

let report(props: Props<'env>, model: Model) : Cmd<Msg> =
  Cmd.ofAction(fun () ->
    props.OnReport {
      Place = {
        Offset = model.Offset
        Head = model.Head
      }
      FetchedAt = model.FetchedAt
    })

let init(props: Props<'env>) : Model * Cmd<Msg> =
  ({
    Loading = true
    Error = None
    Items = []
    NextCursor = None
    Head = props.Restore |> Option.bind _.Head
    NewCount = None
    Ranked = false
    Polling =
      match props.Source with
      | LogSource _ -> true
      | SavedSource -> false
    Now = DateTimeOffset.UtcNow
    FetchedAt = None
    Busy = None
    PollChoices = Map.empty
    Pasteis = props.Pasteis
    HeardIds = []
    Offset = props.Restore |> Option.map _.Offset |> Option.defaultValue 0.0
   },
   Cmd.OfAsync.perform fetch (props, None) Fetched)

let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
  match msg with
  | Fetched result ->
    match result with
    | Ok page ->
      let next = {
        model with
            Loading = false
            Error = None
            Items = page.Page.Items
            NextCursor = page.Page.NextCursor
            Head = headOf page.Page.Items
            NewCount = None
            Ranked = page.IsRanked
            FetchedAt = Some model.Now
            HeardIds = []
      }

      next, report(props, next)
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
    match props.Source, model.Head, model.Ranked with
    | LogSource _, Some head, false ->
      model, Cmd.OfAsync.perform checkNew (props, model, head) NewChecked
    | _ -> model, Cmd.none
  | NewChecked result ->
    match result with
    | Ok page when model.Ranked ->
      ({
        model with
            NewCount =
              page.NewCommitsCount |> Option.filter(fun count -> count > 0)
       },
       Cmd.none)
    | Ok page ->
      let loaded =
        model.Items
        |> List.choose(function
          | LiveCommit commit -> Some commit.Id
          | Tombstone _ -> None)
        |> Set.ofList

      let headAt =
        model.Items
        |> List.tryPick(function
          | LiveCommit commit when Some commit.Id = model.Head ->
            Some commit.CreatedAt
          | _ -> None)

      let isNewer(commit: Commit) =
        match headAt with
        | Some at -> commit.CreatedAt > at
        | None -> true

      let unloaded =
        page.Page.Items
        |> List.choose(function
          | LiveCommit commit when not(Set.contains commit.Id loaded) ->
            Some commit
          | _ -> None)

      let late =
        unloaded
        |> List.filter(fun commit -> not(isNewer commit))
        |> List.sortByDescending(fun commit -> commit.CreatedAt)

      let merged =
        late
        |> List.fold
          (fun items arrival -> insertLate(arrival, items))
          model.Items

      let mergedLoaded =
        merged
        |> List.choose(function
          | LiveCommit commit -> Some commit.Id
          | Tombstone _ -> None)
        |> Set.ofList

      let heard =
        (model.HeardIds @ (unloaded |> List.filter isNewer |> List.map _.Id))
        |> List.distinct
        |> List.filter(fun id -> not(Set.contains id mergedLoaded))
        |> List.truncate 100

      ({
        model with
            Items = merged
            NewCount =
              page.NewCommitsCount |> Option.filter(fun count -> count > 0)
            HeardIds = heard
       },
       Cmd.none)
    | Error _ -> model, Cmd.none
  | NewRequested ->
    ({ model with NewCount = None },
     Cmd.OfAsync.perform fetch (props, None) NewFetched)
  | NewFetched result ->
    match result with
    | Ok page ->
      let next = {
        model with
            Error = None
            Items = page.Page.Items
            NextCursor = page.Page.NextCursor
            Head = headOf page.Page.Items
            Ranked = page.IsRanked
            FetchedAt = Some model.Now
            HeardIds = []
      }

      next, report(props, next)
    | Error error -> ({ model with Error = Some error }, Cmd.none)
  | ClockTicked moment -> ({ model with Now = moment }, Cmd.none)
  | RefreshRequested ->
    ({
      model with
          Loading = true
          Error = None
          NewCount = None
     },
     Cmd.OfAsync.perform fetch (props, None) Fetched)
  | PollingChanged active -> ({ model with Polling = active }, Cmd.none)
  | Scrolled offset ->
    let next = { model with Offset = offset }
    next, report(props, next)
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
      let items =
        match props.Source with
        | LogSource _ -> replaceNode(model.Items, updated)
        | SavedSource ->
          model.Items
          |> List.filter(function
            | LiveCommit commit -> commit.Id <> updated.Id
            | Tombstone _ -> true)

      ({
        model with
            Items = items
            Head = headOf items
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
  | CommitUpdated updated ->
    ({
      model with
          Items = replaceNode(model.Items, updated)
     },
     Cmd.none)
  | CommitDeleted commitId ->
    ({
      model with
          Items =
            model.Items
            |> List.filter(function
              | LiveCommit commit -> commit.Id <> commitId
              | Tombstone _ -> true)
          Head =
            model.Items
            |> List.tryPick(function
              | LiveCommit commit when commit.Id <> commitId -> Some commit.Id
              | _ -> None)
     },
     Cmd.none)

let subscribe (props: Props<'env>) (model: Model) : Sub<Msg> =
  [
    Sub.fromObservable "clock" (props.Now |> Observable.map ClockTicked)

    Sub.fromObservable
      "refresh"
      (props.Refresh |> Observable.map(fun () -> RefreshRequested))

    match props.Source with
    | LogSource _ when model.Polling ->
      Sub.interval "since-poll" (TimeSpan.FromMinutes 1.0) (fun () -> CheckNew)
    | _ -> ()
  ]
  |> Sub.batch

type TimelineRow =
  | DayRow of label: string
  | LastVisitRow
  | NodeRow of node: CommitNode

let timelineKey(row: TimelineRow) : string =
  match row with
  | DayRow label -> "day:" + label
  | LastVisitRow -> "last-visit"
  | NodeRow node -> FeedParts.nodeKey node

let timelineRows
  (
    items: CommitNode list,
    ranked: bool,
    lastVisit: DateTimeOffset option,
    now: DateTimeOffset
  ) : TimelineRow list =
  let commitOf(node: CommitNode) =
    match node with
    | LiveCommit commit -> Some commit
    | Tombstone _ -> None

  let start: TimelineRow list * bool * bool * DateTimeOffset option =
    ([], false, false, None)

  let rows, _, _, _ =
    items
    |> List.fold
      (fun (rows, seenNewer, placed, previousDay) node ->
        let commit = commitOf node

        let day: DateTimeOffset option =
          commit |> Option.map(fun current -> current.CreatedAt)

        let dayRows =
          if ranked then
            []
          else
            match day, previousDay with
            | Some(at: DateTimeOffset), Some(before: DateTimeOffset) when
                at.DateTime.Date <> before.DateTime.Date
                ->
                [ DayRow(dayLabel(now, at)) ]
            | Some(at: DateTimeOffset), None -> [ DayRow(dayLabel(now, at)) ]
            | _ -> []

        let marker, placed =
          match lastVisit, commit, seenNewer, placed with
          | Some visit, Some current, true, false when
            current.CreatedAt < visit
            ->
            [ LastVisitRow ], true
          | _ -> [], placed

        let seenNewer =
          match lastVisit, commit with
          | Some visit, Some current -> seenNewer || current.CreatedAt > visit
          | _ -> seenNewer

        let additions = [ yield! dayRows; yield! marker; yield NodeRow node ]

        let nextDay =
          match day with
          | Some _ -> day
          | None -> previousDay

        (List.rev additions @ rows, seenNewer, placed, nextDay))
      start

  List.rev rows

let historyHeader(source: HistorySource) : string =
  match source with
  | LogSource log -> "HEAD -> " + Ref.label(Build.history log)
  | SavedSource -> "Saved"

let historyEmpty(source: HistorySource) : string =
  match source with
  | LogSource _ -> "Nothing here yet. Commit something."
  | SavedSource -> "Nothing saved yet."

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let busy = model |> Observable.map _.Busy |> Slices.share

  let inline slot(command: Commit -> unit) : ActionSlot voption =
    ValueSome { Command = command; Busy = busy }

  let actions =
    props.Session
    |> Option.map(fun session ->
      RowActions.create {
        Env = props.Env
        Session = session
        Now = props.Now
        OfferBasicActions = false
        OnUpdated = CommitUpdated >> dispatch
        OnDeleted = CommitDeleted >> dispatch
        OnDialog = props.OnDialog
      })

  let rowProps: RowProps = {
    OnEvent =
      fun event ->
        actions |> Option.iter(fun current -> current.Handle event)

        match event with
        | CommitCard.Opened commit -> dispatch(Opened commit)
        | CommitCard.ReplyToggled commit -> dispatch(Opened commit)
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
                TextBlock()
                  .Classes(Theme.name Theme.Mono)
                  .Text(historyHeader props.Source),
                TextBlock().Classes(Theme.name Theme.Mono).Text(fetchedLine)
              )
          )
      )

  let statusText =
    model
    |> Observable.map(fun current ->
      match current.Loading, current.Error, current.Items with
      | true, _, _ -> "Loading the history…"
      | false, Some error, _ -> describe error
      | false, None, [] -> historyEmpty props.Source
      | false, None, _ -> "")

  let statusLine =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Margin(Thickness(16.0, 6.0, 16.0, 6.0))
      .Text(statusText)

  let hasMore = model |> Observable.map _.NextCursor.IsSome

  let feed =
    Feed.view {
      Items =
        model
        |> Observable.map(fun current ->
          timelineRows(
            current.Items,
            current.Ranked,
            props.LastVisit,
            current.Now
          ))
      KeyOf = timelineKey

      Row =
        function
        | DayRow label -> FeedParts.dayRow label
        | LastVisitRow -> FeedParts.lastVisitRow()
        | NodeRow node -> CommitCard.feedRow rowProps node

      Footer =
        ValueSome(
          PageChrome.loadMore(hasMore, fun () -> dispatch MoreRequested)
        )
      HasMore = hasMore
      ScrollTo = ValueNone
      Ride = false
      Restore = props.Restore |> Option.map _.Offset |> ValueOption.ofOption
      OnScroll = ValueSome(fun offset -> dispatch(Scrolled offset))
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
