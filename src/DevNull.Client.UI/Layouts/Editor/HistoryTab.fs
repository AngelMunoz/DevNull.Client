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
  OnScroll: float -> unit
  OnOpen: Commit -> unit
  OnReplyRequested: Commit -> unit
  OnQuoteRequested: Commit -> unit
  OnOpenRepo: string -> unit
  OnFetch: Result<unit, string> -> unit
}

let cardsProps(props: Props<'env>) : EditorCards.Props<'env> = {
  Env = props.Env
  Session = props.Session
  Zone = props.Zone
  Surface = EditorCards.Feed
  Now = props.Now
  OnOpenRepo = props.OnOpenRepo
}

module State =

  type Model = {
    Loading: bool
    Error: AppError option
    Items: CommitNode list
    NextCursor: string option
    Head: string option
    NewCount: int option
    HeardIds: string list
    Ranked: bool
    Polling: bool
    FetchedAt: DateTimeOffset option
    Cards: EditorCards.State
    Now: DateTimeOffset
  }

  type Msg =
    | Fetched of Result<LogPage, AppError>
    | MoreRequested
    | Appended of Result<LogPage, AppError>
    | ActiveChanged of active: bool
    | ClockTicked of moment: DateTimeOffset
    | CheckNew
    | NewChecked of Result<LogPage, AppError>
    | Opened of Commit
    | CardMsg of EditorCards.Msg
    | RefreshRequested

  let initial = {
    Loading = true
    Error = None
    Items = []
    NextCursor = None
    Head = None
    NewCount = None
    HeardIds = []
    Ranked = false
    Polling = false
    FetchedAt = None
    Cards = EditorCards.empty
    Now = DateTimeOffset.MinValue
  }

  let polling(model: Model) = model.Polling

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

  let replaceNode(items: CommitNode list, updated: Commit) =
    items
    |> List.map(function
      | LiveCommit commit when commit.Id = updated.Id -> LiveCommit updated
      | node -> node)

  let tombstoneOf(commit: Commit) : Tombstone = {
    Id = commit.Id
    ParentId = commit.ParentId
    DeletedAt = DateTimeOffset.UtcNow
    RepliesCount = commit.RepliesCount
  }

  let insertLate(late: Commit, items: CommitNode list) : CommitNode list =
    let at =
      items
      |> List.tryFindIndex(function
        | Tombstone _ -> false
        | LiveCommit commit -> commit.CreatedAt < late.CreatedAt)

    match at with
    | Some index -> items |> List.insertAt index (LiveCommit late)
    | None -> items @ [ LiveCommit late ]

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
    Arrived = if model.Ranked then [] else model.HeardIds
    Pending = liveIds model.Items |> List.truncate 100
    Everything = false
    Through = liveIds model.Items |> List.rev |> List.truncate 20
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
          Head = restore.Head
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
        let next = {
          model with
              Loading = false
              Error = None
              Items = page.Page.Items
              NextCursor = page.Page.NextCursor
              Head = headOf page.Page.Items
              NewCount = None
              HeardIds = []
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
      match model.NextCursor with
      | Some _ ->
        model, Cmd.OfAsync.perform fetch (props, model.NextCursor) Appended
      | None -> model, Cmd.none
    | Appended result ->
      reportFetch(props, result)

      match result with
      | Ok page ->
        let next = {
          model with
              Error = None
              Items = model.Items @ page.Page.Items
              NextCursor = page.Page.NextCursor
        }

        next, Cmd.none
      | Error error ->
        let next = { model with Error = Some error }

        next, Cmd.none
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
                    Items = replaceNode(current.Items, updated)
              }
            | EditorCards.CommitDeleted commitId ->
              let items =
                current.Items
                |> List.map(function
                  | LiveCommit commit when commit.Id = commitId ->
                    Tombstone(tombstoneOf commit)
                  | node -> node)

              { current with Items = items }
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
          let loaded = liveIds model.Items |> Set.ofList

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

          let mergedLoaded = merged |> liveIds |> Set.ofList

          let heard =
            (model.HeardIds
             @ (unloaded
                |> List.filter isNewer
                |> List.map(fun commit -> commit.Id)))
            |> List.distinct
            |> List.filter(fun id -> not(Set.contains id mergedLoaded))
            |> List.truncate 100

          let next = {
            model with
                Items = merged
                NewCount = count
                HeardIds = heard
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
  let hasMore =
    model |> Observable.map(fun current -> current.NextCursor.IsSome)

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
      State = model |> Observable.map _.Cards
      Dispatch = cardDispatch
      OnEvent = EditorCards.event(cardDispatch, fallback)
    }

  let feed =
    DevNull.Client.Components.Feed.view {
      Items = model |> Observable.map(fun current -> current.Items)
      KeyOf = State.keyOf
      Row = fun node -> feedRow rowProps node
      Footer =
        ValueSome(loadMore(hasMore, fun () -> dispatch State.MoreRequested))
      HasMore = hasMore
      ScrollTo = ValueNone
      Ride = false
      Restore =
        props.Restore
        |> ValueOption.map(fun place -> place.Scroll)
        |> ValueOption.filter(fun scroll -> scroll > 0.0)
      OnScroll = ValueSome(fun offset -> props.OnScroll offset)
      OnRide = ValueNone
      OnNeedMore = ValueSome(fun () -> dispatch State.MoreRequested)
    }

  let error = errorPanel(model |> Observable.map(fun current -> current.Error))

  let layout =
    Grid(RowDefinitions = RowDefinitions "Auto,*")
      .Children(error.Row(0), feed.Row(1))

  Grid()
    .Children(
      layout,
      EditorCards.layer(
        cardsProps props,
        model |> Observable.map _.Cards,
        cardDispatch
      )
    )

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
