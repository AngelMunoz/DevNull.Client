module DevNull.Client.Layouts.Editor.HistoryTab

open System
open Avalonia.Controls
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.Feed
open DevNull.Client.Components.PageChrome
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.UiClock

[<Struct>]
type Snapshot = {
  NewCount: int
  FetchedAt: DateTimeOffset option
  Loading: bool
}

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache> = {
  Env: 'env
  Session: SignedInSession
  Log: LogKind
  Zone: Zone
  Active: IObservable<bool>
  Now: unit -> DateTimeOffset
  OnSnapshot: Snapshot -> unit
}

module State =

  type Model = {
    Loading: bool
    Error: AppError option
    Items: CommitNode list
    NextCursor: string option
    Head: string option
    NewCount: int option
    Ranked: bool
    Polling: bool
    FetchedAt: DateTimeOffset option
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

  let initial = {
    Loading = true
    Error = None
    Items = []
    NextCursor = None
    Head = None
    NewCount = None
    Ranked = false
    Polling = false
    FetchedAt = None
    Now = DateTimeOffset.MinValue
  }

  let polling(model: Model) = model.Polling

  let snapshot(model: Model) = {
    NewCount = model.NewCount |> Option.defaultValue 0
    FetchedAt = model.FetchedAt
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

  let checkNew(props: Props<'env>, model: Model, head: string) =
    Logs.get(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        Log = props.Log
        Since = Some head
        Cursor = None
        Arrived = []
        Pending = liveIds model.Items |> List.truncate 100
        Everything = false
        Through = []
      }
    )

  let announce(props: Props<'env>, model: Model) =
    Cmd.ofAction(fun () -> props.OnSnapshot(snapshot model))

  let init(props: Props<'env>) =
    let model = { initial with Now = props.Now() }

    model,
    Cmd.batch [
      Cmd.OfAsync.perform fetch (props, None) Fetched
      announce(props, model)
    ]

  let update(props: Props<'env>, msg: Msg, model: Model) =
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
    | NewChecked result ->
      match result with
      | Ok page ->
        let count =
          page.NewCommitsCount |> Option.filter(fun count -> count > 0)

        let next = {
          model with
              NewCount = count
              Error = None
        }

        next, announce(props, next)
      | Error error ->
        let next = { model with Error = Some error }

        next, Cmd.none

  let subscribe(props: Props<'env>, model: Model) : Sub<Msg> =
    [
      Sub.fromObservable "active" (props.Active |> Observable.map ActiveChanged)

      Sub.fromObservable
        "clock"
        (props.Env.UiClock.Minute |> Observable.map ClockTicked)

      if polling model then
        Sub.interval "since-poll" (TimeSpan.FromMinutes 1.0) (fun () ->
          CheckNew)
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

  let rowProps: RowProps = {
    OnEvent = ignore
    OnOpenExternal = ignore
    ViewerId = props.Session.Session.User.Id
    Pasteis = Observable.single props.Session.Session.PasteisMode
    Now = model |> Observable.map(fun current -> current.Now)
    PollChoices = Observable.single(Map.empty<string, string>)
    Dense = false
    Images = props.Env.ImageCache
    Flyout = ValueNone
    Pin = ValueNone
    Star = ValueNone
    Stars = ValueNone
    Vote = ValueNone
    Pastel = ValueNone
    Menu = ValueNone
    Translation = Observable.single(fun _ -> ValueNone)
    Zone = props.Zone
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
      Restore = ValueNone
      OnScroll = ValueNone
      OnRide = ValueNone
      OnNeedMore = ValueSome(fun () -> dispatch State.MoreRequested)
    }

  let error = errorPanel(model |> Observable.map(fun current -> current.Error))

  let layout = Grid()

  layout.RowDefinitions <- RowDefinitions("Auto,*")

  layout.Children(error.Row(0), feed.Row(1))

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
