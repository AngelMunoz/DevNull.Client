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
  Feed: FeedWindow.Window<CommitNode>
  Anchor: FeedWindow.Anchor voption
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

[<Struct>]
type Place = {
  Anchor: FeedWindow.Anchor voption
  Head: string option
}

[<Struct>]
type Report = {
  Place: Place
  FetchedAt: DateTimeOffset option
}

type Msg =
  | Fetched of cursor: string option * Result<LogPage, AppError>
  | MoreRequested
  | NewerRequested
  | Appended of cursor: string option * Result<LogPage, AppError>
  | Prepended of cursor: string option * Result<LogPage, AppError>
  | AnchorMoved of FeedWindow.Anchor
  | CheckNew
  | NewChecked of Result<LogPage, AppError>
  | NewRequested
  | NewFetched of Result<LogPage, AppError>
  | ClockTicked of moment: DateTimeOffset
  | RefreshRequested
  | PollingChanged of active: bool
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
  Source: HistorySource
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Flyout: DevNull.Client.Components.UserFlyout.Source voption
  Now: IObservable<DateTimeOffset>
  Refresh: IObservable<unit>
  Zone: Zone
  LastVisit: DateTimeOffset option
  OnOpenExternal: string -> unit
  OnOpened: Commit -> unit
  OnReply: Commit -> unit
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

let windowLimit = 300

let ops: FeedWindow.Ops<CommitNode> = {
  KeyOf = DevNull.Client.Components.FeedParts.nodeKey
}

let spanOf
  (request: string option, page: LogPage)
  : FeedWindow.Chunk<CommitNode> = {
  Request = ValueOption.ofOption request
  Items = Array.ofList page.Page.Items
  Older = ValueOption.ofOption page.Page.NextCursor
  Newer = ValueOption.ofOption page.Page.PrevCursor
}

let windowOf
  (request: string option, page: LogPage)
  : FeedWindow.Window<CommitNode> = {
  Order = FeedWindow.NewestFirst
  Chunks = [ spanOf(request, page) ]
  Limit = windowLimit
}

let itemsOf(model: Model) = FeedWindow.items model.Feed

let olderOf(model: Model) : string voption = FeedWindow.older model.Feed

let withPage
  (items: CommitNode list, nextCursor: string option, model: Model)
  : Model = {
  model with
      Feed = {
        Order = FeedWindow.NewestFirst
        Chunks = [
          {
            Request = ValueNone
            Items = Array.ofList items
            Older = ValueOption.ofOption nextCursor
            Newer = ValueNone
          }
        ]
        Limit = windowLimit
      }
}

let newerOf(model: Model) : string voption = FeedWindow.newer model.Feed

let trimWindow
  (window: FeedWindow.Window<CommitNode>, anchor: FeedWindow.Anchor voption)
  =
  let items = FeedWindow.items window
  let count = items.Length

  let index =
    anchor
    |> ValueOption.bind(fun place ->
      items
      |> List.tryFindIndex(fun node ->
        DevNull.Client.Components.FeedParts.nodeKey node = place.Key)
      |> ValueOption.ofOption)

  let edge =
    match index with
    | ValueSome at when at < 40 -> FeedWindow.OlderEdge
    | ValueSome at when at > count - 40 -> FeedWindow.NewerEdge
    | _ -> FeedWindow.OlderEdge

  FeedWindow.trim(
    {
      Ops = ops
      Keep = anchor |> ValueOption.map _.Key
      Edge = edge
    },
    window
  )

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
      Arrived = []
      Pending = []
      Everything = false
      Through = []
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
        Anchor = model.Anchor
        Head = model.Head
      }
      FetchedAt = model.FetchedAt
    })

let init(props: Props<'env>) : Model * Cmd<Msg> =
  {
    Loading = true
    Error = None
    Feed = FeedWindow.empty(FeedWindow.NewestFirst, windowLimit)
    Anchor =
      match props.Restore with
      | Some place -> place.Anchor
      | None -> ValueNone
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
  },
  let cursor =
    props.Restore
    |> Option.bind(fun place -> ValueOption.toOption place.Anchor)
    |> Option.bind(fun anchor -> ValueOption.toOption anchor.Entry)

  Cmd.OfAsync.perform fetch (props, cursor) (fun result ->
    Fetched(cursor, result))

let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
  match msg with
  | Fetched(cursor, result) ->
    match result with
    | Ok page ->
      let next = {
        model with
            Loading = false
            Error = None
            Feed = windowOf(cursor, page)
            Head = headOf page.Page.Items
            NewCount = None
            Ranked = page.IsRanked
            FetchedAt = Some model.Now
      }

      next, report(props, next)
    | Error error ->
      {
        model with
            Loading = false
            Error = Some error
      },
      Cmd.none
  | MoreRequested ->
    match olderOf model with
    | ValueSome cursor ->
      model,
      Cmd.OfAsync.perform fetch (props, Some cursor) (fun result ->
        Appended(Some cursor, result))
    | ValueNone -> model, Cmd.none
  | NewerRequested ->
    match newerOf model with
    | ValueSome cursor ->
      model,
      Cmd.OfAsync.perform fetch (props, Some cursor) (fun result ->
        Prepended(Some cursor, result))
    | ValueNone -> model, Cmd.none
  | Appended(cursor, result) ->
    match result with
    | Ok page ->
      let grown = FeedWindow.addOlder(ops, spanOf(cursor, page), model.Feed)

      let next = {
        model with
            Error = None
            Feed = trimWindow(grown, model.Anchor)
            FetchedAt = Some model.Now
      }

      next, report(props, next)
    | Error error -> { model with Error = Some error }, Cmd.none
  | Prepended(cursor, result) ->
    match result with
    | Ok page ->
      let grown = FeedWindow.addNewer(ops, spanOf(cursor, page), model.Feed)

      let next = {
        model with
            Error = None
            Feed = trimWindow(grown, model.Anchor)
            FetchedAt = Some model.Now
      }

      next, report(props, next)
    | Error error -> { model with Error = Some error }, Cmd.none
  | AnchorMoved anchor ->
    let next = { model with Anchor = ValueSome anchor }
    next, report(props, next)
  | CheckNew ->
    match props.Source, model.Head, model.Ranked with
    | LogSource _, Some head, false ->
      model, Cmd.OfAsync.perform checkNew (props, model, head) NewChecked
    | _ -> model, Cmd.none
  | NewChecked result ->
    match result with
    | Ok page when model.Ranked ->
      {
        model with
            NewCount =
              page.NewCommitsCount |> Option.filter(fun count -> count > 0)
      },
      Cmd.none
    | Ok page ->
      let items = itemsOf model

      let loaded =
        items
        |> List.choose(function
          | LiveCommit commit -> Some commit.Id
          | Tombstone _ -> None)
        |> Set.ofList

      let headAt =
        items
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
          (fun window arrival ->
            FeedWindow.insert(
              {
                Ops = ops
                Item = LiveCommit arrival
                Before =
                  (function
                  | LiveCommit commit -> commit.CreatedAt < arrival.CreatedAt
                  | Tombstone _ -> false)
              },
              window
            ))
          model.Feed

      let next = {
        model with
            Feed = trimWindow(merged, model.Anchor)
            NewCount =
              page.NewCommitsCount |> Option.filter(fun count -> count > 0)
      }

      next, report(props, next)
    | Error _ -> model, Cmd.none
  | NewRequested ->
    { model with NewCount = None },
    Cmd.OfAsync.perform fetch (props, None) NewFetched
  | NewFetched result ->
    match result with
    | Ok page ->
      let merged =
        FeedWindow.mergeNewerPage(ops, spanOf(None, page), model.Feed)

      let next = {
        model with
            Error = None
            Feed = trimWindow(merged, model.Anchor)
            Head = headOf page.Page.Items
            Ranked = page.IsRanked
            FetchedAt = Some model.Now
      }

      next, report(props, next)
    | Error error -> { model with Error = Some error }, Cmd.none
  | ClockTicked moment -> { model with Now = moment }, Cmd.none
  | RefreshRequested ->
    {
      model with
          Loading = true
          Error = None
          NewCount = None
    },
    Cmd.OfAsync.perform fetch (props, None) (fun result ->
      Fetched(None, result))
  | PollingChanged active when active = model.Polling -> model, Cmd.none
  | PollingChanged active -> { model with Polling = active }, Cmd.none
  | Opened commit -> model, Cmd.ofAction(fun () -> props.OnOpened commit)
  | QuoteRequested commit -> model, Cmd.ofAction(fun () -> props.OnQuote commit)
  | ReplyRequested commit -> model, Cmd.ofAction(fun () -> props.OnReply commit)
  | StarToggled commit ->
    {
      model with
          Error = None
          Busy = Some commit.Id
    },
    Cmd.OfAsync.perform star (props, commit) StarDone
  | StarDone result ->
    match result with
    | Ok updated ->
      let feed =
        match props.Source with
        | LogSource _ -> FeedWindow.replace(ops, LiveCommit updated, model.Feed)
        | SavedSource ->
          FeedWindow.remove(
            ops,
            DevNull.Client.Components.FeedParts.nodeKey(LiveCommit updated),
            model.Feed
          )

      {
        model with
            Feed = feed
            Head = headOf(FeedWindow.items feed)
            Busy = None
      },
      Cmd.none
    | Error error ->
      {
        model with
            Error = Some error
            Busy = None
      },
      Cmd.none
  | PastelGiven commit ->
    {
      model with
          Error = None
          Busy = Some commit.Id
    },
    Cmd.OfAsync.perform givePastel (props, commit) PastelDone
  | PastelDone result ->
    match result with
    | Ok pastel ->
      {
        model with
            Feed = FeedWindow.replace(ops, LiveCommit pastel.Commit, model.Feed)
            Busy = None
      },
      Cmd.none
    | Error error ->
      {
        model with
            Error = Some error
            Busy = None
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
            Error = None
            Busy = Some commit.Id
      },
      Cmd.OfAsync.perform vote (props, commit, option) PollVotedDone
    | None -> model, Cmd.none
  | PollVotedDone result ->
    match result with
    | Ok updated ->
      {
        model with
            Feed = FeedWindow.replace(ops, LiveCommit updated, model.Feed)
            Busy = None
            PollChoices = model.PollChoices |> Map.remove updated.Id
      },
      Cmd.none
    | Error error ->
      {
        model with
            Error = Some error
            Busy = None
      },
      Cmd.none
  | CommitUpdated updated when
    FeedWindow.items model.Feed
    |> List.exists(function
      | LiveCommit commit -> commit.Id = updated.Id
      | Tombstone _ -> false)
    ->
    {
      model with
          Feed = FeedWindow.replace(ops, LiveCommit updated, model.Feed)
    },
    Cmd.none
  | CommitUpdated _ -> model, Cmd.none
  | CommitDeleted commitId ->
    let items = itemsOf model

    items
    |> List.tryPick(function
      | LiveCommit commit when commit.Id = commitId -> Some commit
      | _ -> None)
    |> Option.map(fun commit ->
      let feed =
        FeedWindow.replaceKey(
          {
            Ops = ops
            Key = commit.Id
            Item =
              Tombstone {
                Id = commit.Id
                ParentId = commit.ParentId
                DeletedAt = DateTimeOffset.UtcNow
                RepliesCount = commit.RepliesCount
              }
          },
          model.Feed
        )

      {
        model with
            Feed = feed
            Head = headOf(FeedWindow.items feed)
      })
    |> Option.map(fun next -> next, Cmd.none)
    |> Option.defaultValue(model, Cmd.none)

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

// One feed row with the chunk request cursor that fetched it, so
// the anchor the feed reports names the page the row came from.
[<Struct>]
type AnchoredRow = {
  Row: TimelineRow
  Entry: string option
}

let entryOf(anchored: AnchoredRow) : string option = anchored.Entry

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

  let fetchedLine =
    model
    |> Observable.distinctUntilChangedKey(fun current ->
      current.FetchedAt, minuteBucket current.Now)
    |> Observable.map(fun current ->
      match current.FetchedAt with
      | Some at -> "fetched " + timeAgo(current.Now, at)
      | None -> "not fetched yet")

  let pillText =
    model
    |> Observable.distinctUntilChangedKey _.NewCount
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
    |> Slices.gateByRefKey (fun current -> current.Feed) (fun current ->
      current.Loading, current.Error)
    |> Observable.map(fun current ->
      match current.Loading, current.Error, FeedWindow.items current.Feed with
      | true, _, _ -> "Loading the history…"
      | false, Some error, _ -> describe error
      | false, None, [] -> historyEmpty props.Source
      | false, None, _ -> "")

  let statusLine =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Margin(Thickness(16.0, 6.0, 16.0, 6.0))
      .Text(statusText)

  let hasOlder =
    model
    |> Observable.map(fun current -> olderOf current |> ValueOption.isSome)
    |> Slices.share

  let hasNewer =
    model
    |> Observable.map(fun current -> newerOf current |> ValueOption.isSome)
    |> Slices.share

  let restoreAnchor =
    props.Restore |> Option.bind(fun place -> ValueOption.toOption place.Anchor)

  let items =
    model
    |> Slices.gateByRefKey (fun current -> current.Feed) (fun current ->
      current.Ranked, minuteBucket current.Now)
    |> Observable.map(fun current ->
      let feed = current.Feed

      timelineRows(
        FeedWindow.items feed,
        current.Ranked,
        props.LastVisit,
        current.Now
      )
      |> List.map(fun row -> {
        Row = row
        Entry =
          match row with
          | NodeRow node ->
            FeedWindow.entryOf(ops, FeedParts.nodeKey node, feed)
            |> ValueOption.toOption
          | DayRow _
          | LastVisitRow -> None
      }))
    |> Slices.share

  let feed =
    Feed.viewWith(
      {
        Header = ValueNone
        Items = items
        KeyOf = fun anchored -> timelineKey anchored.Row

        Row =
          fun anchored ->
            match anchored.Row with
            | DayRow label -> FeedParts.dayRow label
            | LastVisitRow -> FeedParts.lastVisitRow()
            | NodeRow node -> CommitCard.feedRow rowProps node

        Footer = ValueNone
        HasMore = hasOlder
        ScrollTo = ValueNone
        Ride = false
        Restore = ValueNone
        OnScroll = ValueNone
        OnRide = ValueNone
        OnNeedMore = ValueNone
      },
      {
        Feed.Advanced.none() with
            AnchorKeyOf =
              ValueSome(fun anchored ->
                match anchored.Row with
                | DayRow _ -> None
                | LastVisitRow -> None
                | NodeRow _ -> Some(timelineKey anchored.Row))
            EntryCursorOf = ValueSome entryOf
            RestoreAnchor = restoreAnchor |> ValueOption.ofOption
            OnAnchor = ValueSome(fun anchor -> dispatch(AnchorMoved anchor))
            Paging =
              ValueSome {
                Order = FeedWindow.NewestFirst
                HasOlder = hasOlder
                HasNewer = hasNewer
                OnOlder = (fun () -> dispatch MoreRequested)
                OnNewer = (fun () -> dispatch NewerRequested)
              }
      }
    )

  DockPanel()
    .LastChildFill(true)
    .Children(header.DockTop(), statusLine.DockTop(), feed)

let program(props: Props<'env>) =
  Program.mkProgram
    (fun () -> init props)
    (fun msg model -> update(props, msg, model))
    (view props)
  |> Program.withSubscription(subscribe props)
