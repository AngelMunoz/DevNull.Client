module DevNull.Client.Editor.HistoryTab

open System
open Avalonia.Controls
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Editor
open DevNull.Client.AttachmentSources
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.Feed
open DevNull.Client.Components.PageChrome
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
  NewCount: int
  FetchedAt: DateTimeOffset option
  Head: string option
  Loading: bool
}

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideDispatcher
  and 'env :> IProvideRemotes
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideBrowser> = {
  Env: 'env
  Session: SignedInSession
  Log: LogKind
  Zone: Zone
  Active: IObservable<bool>
  Now: unit -> DateTimeOffset
  Restore: EditorPlace voption
  Refresh: IObservable<unit>
  OnSnapshot: Snapshot -> unit
  OnAnchor: FeedWindow.Anchor -> unit
  OnOpen: Commit -> unit
  OnReplyRequested: Commit -> unit
  OnQuoteRequested: Commit -> unit
  OnOpenRepo: string -> unit
  OnOpenGuild: string -> unit
  OnShared: User -> unit
  OnFetch: Result<unit, string> -> unit
}

let keyOf(node: CommitNode) =
  match node with
  | LiveCommit commit -> commit.Id
  | Tombstone tombstone -> tombstone.Id

let ops: FeedWindow.Ops<CommitNode> = { KeyOf = keyOf }

// One feed row with the chunk request cursor that fetched it, so
// the anchor the feed reports names the page the row came from.
[<Struct>]
type AnchoredNode = {
  Node: CommitNode
  Entry: string option
}

let entryOf(anchored: AnchoredNode) : string option = anchored.Entry

let cardsProps(props: Props<'env>) : EditorCards.Props<'env> = {
  Env = props.Env
  Session = props.Session
  Zone = props.Zone
  Surface = EditorCards.Feed
  Now = props.Now
  OnOpenRepo = props.OnOpenRepo
  OnOpenGuild = props.OnOpenGuild
  OnShared = props.OnShared
}

module State =

  type Model = {
    Loading: bool
    Error: AppError option
    Feed: FeedWindow.Window<CommitNode>
    Anchor: FeedWindow.Anchor voption
    Head: string option
    NewCount: int option
    Ranked: bool
    Polling: bool
    FetchedAt: DateTimeOffset option
    Cards: EditorCards.State
    Now: DateTimeOffset
  }

  type Msg =
    | Fetched of Result<LogPage, AppError>
    | MoreRequested
    | Appended of cursor: string option * Result<LogPage, AppError>
    | NewerRequested
    | Prepended of cursor: string option * Result<LogPage, AppError>
    | AnchorMoved of FeedWindow.Anchor
    | ActiveChanged of active: bool
    | ClockTicked of moment: DateTimeOffset
    | CheckNew
    | NewChecked of Result<LogPage, AppError>
    | Opened of Commit
    | CardMsg of EditorCards.Msg
    | RefreshRequested

  let windowLimit = 300

  let initial = {
    Loading = true
    Error = None
    Feed = FeedWindow.empty(FeedWindow.NewestFirst, windowLimit)
    Anchor = ValueNone
    Head = None
    NewCount = None
    Ranked = false
    Polling = false
    FetchedAt = None
    Cards = EditorCards.empty
    Now = DateTimeOffset.MinValue
  }

  let polling(model: Model) = model.Polling

  let itemsOf(model: Model) = FeedWindow.items model.Feed

  let olderOf(model: Model) = FeedWindow.older model.Feed

  let newerOf(model: Model) = FeedWindow.newer model.Feed

  let spanOf
    (cursor: string option, page: LogPage)
    : FeedWindow.Chunk<CommitNode> = {
    Request = ValueOption.ofOption cursor
    Items = Array.ofList page.Page.Items
    Older = ValueOption.ofOption page.Page.NextCursor
    Newer = ValueNone
  }

  let trimFeed(model: Model, feed: FeedWindow.Window<CommitNode>) =
    FeedWindow.trim(
      {
        Ops = ops
        Keep = model.Anchor |> ValueOption.map _.Key
        Edge = FeedWindow.OlderEdge
      },
      feed
    )

  let withPage
    (items: CommitNode list, nextCursor: string option, model: Model)
    : Model = {
    model with
        Feed =
          FeedWindow.addOlder(
            ops,
            {
              Request = ValueNone
              Items = Array.ofList items
              Older = ValueOption.ofOption nextCursor
              Newer = ValueNone
            },
            FeedWindow.empty(FeedWindow.NewestFirst, windowLimit)
          )
  }

  let snapshot(model: Model) = {
    NewCount = model.NewCount |> Option.defaultValue 0
    FetchedAt = model.FetchedAt
    Head = model.Head
    Loading = model.Loading
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

  let replaceNode(feed: FeedWindow.Window<CommitNode>, updated: Commit) =
    FeedWindow.replace(ops, LiveCommit updated, feed)

  let tombstoneOf(commit: Commit) : Tombstone = {
    Id = commit.Id
    ParentId = commit.ParentId
    DeletedAt = DateTimeOffset.UtcNow
    RepliesCount = commit.RepliesCount
  }

  let insertLate(late: Commit, feed: FeedWindow.Window<CommitNode>) =
    FeedWindow.insert(
      {
        Ops = ops
        Item = LiveCommit late
        Before =
          function
          | Tombstone _ -> false
          | LiveCommit commit -> commit.CreatedAt < late.CreatedAt
      },
      feed
    )

  let fetch(props: Props<'env>, cursor: string option) =
    Logs.get(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        Log = props.Log
        Since = None
        Cursor = cursor
        Arrived = []
        Pending = []
        Everything = false
        Through = []
      }
    )

  let pollRequest
    (props: Props<'env>, model: Model, head: string)
    : Logs.Request = {
    KeyFile = props.Session.KeyFile
    Log = props.Log
    Since = Some head
    Cursor = None
    Arrived = []
    Pending = []
    Everything = false
    Through = []
  }

  let checkNew(props: Props<'env>, model: Model, head: string) =
    Logs.get(props.Env, pollRequest(props, model, head))

  let announce(props: Props<'env>, model: Model) =
    Cmd.ofAction(fun () -> props.OnSnapshot(snapshot model))

  let reportFetch(props: Props<'env>, result: Result<'a, AppError>) =
    props.OnFetch(
      match result with
      | Ok _ -> Ok()
      | Error error -> Error(describe error)
    )

  let init(props: Props<'env>) =
    let restore = props.Restore |> ValueOption.defaultValue EditorPlace.empty

    let model = {
      initial with
          Cards = EditorCards.initial(cardsProps props)
          Now = props.Now()
          Head = (restore: EditorPlace).Head
          Anchor = (restore: EditorPlace).Anchor
    }

    model,
    Cmd.batch [
      Cmd.OfAsync.perform fetch (props, None) Fetched
      announce(props, model)
    ]

  let update(props: Props<'env>, msg: Msg, model: Model) =
    match msg with
    | Fetched result ->
      reportFetch(props, result)

      match result with
      | Ok page ->
        let feed =
          FeedWindow.addOlder(
            ops,
            spanOf(None, page),
            FeedWindow.empty(FeedWindow.NewestFirst, windowLimit)
          )
          |> fun feed -> trimFeed(model, feed)

        let next = {
          model with
              Loading = false
              Error = None
              Feed = feed
              Head = headOf page.Page.Items
              NewCount = None
              Ranked = page.IsRanked
              FetchedAt = Some(props.Now())
        }

        next, announce(props, next)
      | Error error ->
        let next = {
          model with
              Loading = false
              Error = Some error
        }

        next, announce(props, next)
    | MoreRequested ->
      match olderOf model with
      | ValueSome cursor ->
        model,
        Cmd.OfAsync.perform fetch (props, Some cursor) (fun result ->
          Appended(Some cursor, result))
      | ValueNone -> model, Cmd.none
    | Appended(cursor, result) ->
      reportFetch(props, result)

      match result with
      | Ok page ->
        let feed =
          FeedWindow.addOlder(ops, spanOf(cursor, page), model.Feed)
          |> fun feed -> trimFeed(model, feed)

        let next = { model with Error = None; Feed = feed }

        next, Cmd.none
      | Error error ->
        let next = { model with Error = Some error }

        next, Cmd.none
    | NewerRequested ->
      match newerOf model with
      | ValueSome cursor ->
        model,
        Cmd.OfAsync.perform fetch (props, Some cursor) (fun result ->
          Prepended(Some cursor, result))
      | ValueNone -> model, Cmd.none
    | Prepended(cursor, result) ->
      reportFetch(props, result)

      match result with
      | Ok page ->
        let feed = FeedWindow.addNewer(ops, spanOf(cursor, page), model.Feed)

        let next = { model with Error = None; Feed = feed }

        next, Cmd.none
      | Error error ->
        let next = { model with Error = Some error }

        next, Cmd.none
    | AnchorMoved anchor ->
      props.OnAnchor anchor
      { model with Anchor = ValueSome anchor }, Cmd.none
    | ActiveChanged active ->
      let next = { model with Polling = active }

      next, Cmd.none
    | ClockTicked moment ->
      let next = { model with Now = moment }

      next, Cmd.none
    | CheckNew ->
      match model.Head, model.Ranked with
      | Some head, false ->
        model, Cmd.OfAsync.perform checkNew (props, model, head) NewChecked
      | _ -> model, Cmd.none
    | Opened commit -> model, Cmd.ofAction(fun () -> props.OnOpen commit)
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
                    Feed = replaceNode(current.Feed, updated)
              }
            | EditorCards.CommitDeleted commitId ->
              itemsOf current
              |> List.tryPick(function
                | LiveCommit commit when commit.Id = commitId ->
                  Some(Tombstone(tombstoneOf commit))
                | _ -> None)
              |> Option.map(fun item -> {
                current with
                    Feed =
                      FeedWindow.replaceKey(
                        {
                          Ops = ops
                          Key = commitId
                          Item = item
                        },
                        current.Feed
                      )
              })
              |> Option.defaultValue current
            | EditorCards.PinChanged _ -> current
            | EditorCards.Failed error -> { current with Error = Some error })
          { model with Cards = cards }

      next, EditorCards.mapCmd(CardMsg, cmd)
    | RefreshRequested ->
      if model.Loading then
        model, Cmd.none
      else
        model, Cmd.OfAsync.perform fetch (props, None) Fetched
    | NewChecked result ->
      reportFetch(props, result)

      match result with
      | Ok page ->
        let count =
          page.NewCommitsCount |> Option.filter(fun count -> count > 0)

        if model.Ranked then
          let next = { model with NewCount = count }

          next, announce(props, next)
        else
          let items = itemsOf model
          let loaded = liveIds items |> Set.ofList

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

          let mergedFeed =
            late
            |> List.fold
              (fun feed arrival -> insertLate(arrival, feed))
              model.Feed

          let next = {
            model with
                Feed = mergedFeed
                NewCount = count
          }

          next, announce(props, next)
      | Error error ->
        let next = { model with Error = Some error }

        next, Cmd.none

  let subscribe(props: Props<'env>, model: Model) : Sub<Msg> =
    [
      Sub.fromObservable "active" (props.Active |> Observable.map ActiveChanged)

      Sub.fromObservable
        "refresh"
        (props.Refresh |> Observable.map(fun () -> RefreshRequested))

      Sub.fromObservable
        "clock"
        (props.Env.UiClock.Minute |> Observable.map ClockTicked)

      Sub.fromObservable
        "card-clock"
        (props.Env.UiClock.Minute
         |> Observable.map(fun tick -> CardMsg(EditorCards.ClockTicked tick)))

      Sub.fromObservable
        "since-poll"
        (Observable.combineLatest props.Active props.Env.UiClock.Minute
         |> Observable.filter fst
         |> Observable.map(fun _ -> CheckNew))
    ]
    |> Sub.batch

let view
  (
    props: Props<'env>,
    model: IObservable<State.Model>,
    dispatch: Dispatch<State.Msg>
  ) : Control =
  let hasOlder =
    model
    |> Observable.map(fun current ->
      State.olderOf current |> ValueOption.isSome)
    |> Slices.share

  let hasNewer =
    model
    |> Observable.map(fun current ->
      State.newerOf current |> ValueOption.isSome)
    |> Slices.share

  let restoreAnchor =
    props.Restore |> ValueOption.bind(fun (place: EditorPlace) -> place.Anchor)

  let items =
    model
    |> Observable.map(fun current -> current.Feed)
    |> Slices.gateByRef
    |> Observable.map(fun feed ->
      FeedWindow.items feed
      |> List.map(fun node -> {
        Node = node
        Entry =
          FeedWindow.entryOf(ops, keyOf node, feed) |> ValueOption.toOption
      }))
    |> Slices.share

  let cards = model |> Observable.map _.Cards |> Slices.share

  let cardDispatch msg = dispatch(State.CardMsg msg)

  let fallback =
    function
    | Opened commit -> dispatch(State.Opened commit)
    | ReplyToggled commit -> props.OnReplyRequested commit
    | QuoteRequested commit -> props.OnQuoteRequested commit
    | MoreRepliesRequested commit -> props.OnOpen commit
    | _ -> ()

  let rowProps =
    EditorCards.row {
      Props = cardsProps props
      State = cards
      Dispatch = cardDispatch
      OnEvent = EditorCards.event(cardDispatch, fallback)
    }

  let feed =
    DevNull.Client.Components.Feed.viewWith(
      {
        Header = ValueNone
        Items = items
        KeyOf = fun anchored -> keyOf anchored.Node
        Row = fun anchored -> feedRow rowProps anchored.Node
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
        DevNull.Client.Components.Feed.Advanced.none() with
            AnchorKeyOf = ValueSome(fun anchored -> Some(keyOf anchored.Node))
            EntryCursorOf = ValueSome entryOf
            RestoreAnchor = restoreAnchor
            OnAnchor =
              ValueSome(fun anchor -> dispatch(State.AnchorMoved anchor))
            Paging =
              ValueSome {
                Order = FeedWindow.NewestFirst
                HasOlder = hasOlder
                HasNewer = hasNewer
                OnOlder = (fun () -> dispatch State.MoreRequested)
                OnNewer = (fun () -> dispatch State.NewerRequested)
              }
      }
    )

  let error = errorPanel(model |> Observable.map(fun current -> current.Error))

  let layout =
    Grid(RowDefinitions = RowDefinitions "Auto,*")
      .Children(error.Row(0), feed.Row(1))

  Grid()
    .Children(layout, EditorCards.layer(cardsProps props, cards, cardDispatch))

let program
  (props: Props<'env>)
  : Program<unit, State.Model, State.Msg, Control> =
  Program.mkProgram
    (fun () -> State.init props)
    (fun msg model -> State.update(props, msg, model))
    (fun model dispatch -> view(props, model, dispatch))
  |> Program.withSubscription(fun model -> State.subscribe(props, model))

let mount(dispatcher: IDispatcher, props: Props<'env>) =
  DevNull.Client.Programs.runPage(dispatcher, program props)
