module DevNull.Client.Layouts.Editor.EditorRoot

open System
open System.Reactive.Disposables
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Components.PageChrome
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Remotes
open DevNull.Client.Stores
open DevNull.Client.UiClock

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideRemotes
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache> = {
  Env: 'env
  Session: SignedInSession
  Stored: StoredState
  Lane: Lane option
  Zone: Zone
  Saved: IObservable<int>
  Queue: IObservable<int>
  Active: IObservable<bool>
  Now: unit -> DateTimeOffset
}

type Runtime = {
  History: Control
  SwitchRef: LogKind -> unit
}

type Model = {
  Frame: EditorState
  Band: WidthBand
  History: HistoryTab.Snapshot
  Unread: int
  RemoteCount: int
  Saved: int
  Queue: int
  Activity: ActivityBar.Activity
  FlyoutOpen: bool
  Now: DateTimeOffset
}

type Msg =
  | RefSelected of LogKind
  | TabSelected of TabId
  | TabClosed of TabId
  | HistoryChanged of HistoryTab.Snapshot
  | SessionChanged of SessionState
  | RemotesChanged of count: int
  | SavedChanged of count: int
  | QueueChanged of count: int
  | ClockTicked of moment: DateTimeOffset
  | ActivitySelected of ActivityBar.Activity
  | WidthChanged of band: WidthBand

type ViewArgs<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideRemotes
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache> = {
  Runtime: Runtime
  Props: Props<'env>
  Model: IObservable<Model>
  Dispatch: Dispatch<Msg>
}

let historyProps
  (props: Props<'env>)
  (log: LogKind)
  (onSnapshot: HistoryTab.Snapshot -> unit)
  : HistoryTab.Props<'env> = {
  Env = props.Env
  Session = props.Session
  Log = log
  Zone = props.Zone
  Active = props.Active
  Now = props.Now
  OnSnapshot = onSnapshot
}

let initial(props: Props<'env>, history: HistoryTab.Snapshot) = {
  Frame = EditorState.initial(StoredState.selectedRef props.Stored)
  Band = Full
  History = history
  Unread = props.Session.Session.UnreadNotificationsCount
  RemoteCount = props.Env.Remotes.Current.Unread |> Option.defaultValue 0
  Saved = 0
  Queue = 0
  Activity = ActivityBar.History
  FlyoutOpen = false
  Now = props.Now()
}

let unreadOf(state: SessionState) =
  match state with
  | SignedIn session -> session.Session.UnreadNotificationsCount
  | SignedOut -> 0

let update(runtime: Runtime, msg: Msg, model: Model) =
  match msg with
  | RefSelected log ->
    let frame =
      EditorState.setDirty(
        TabId.history,
        0,
        EditorState.selectRef(log, model.Frame)
      )

    let next = {
      model with
          Frame = frame
          History = HistoryTab.State.snapshot HistoryTab.State.initial
          FlyoutOpen = false
    }

    next, Cmd.ofAction(fun () -> runtime.SwitchRef log)
  | TabSelected id ->
    let next = {
      model with
          Frame = EditorState.selectTab(id, model.Frame)
          FlyoutOpen = false
    }

    next, Cmd.none
  | TabClosed id ->
    let next = {
      model with
          Frame = EditorState.closeTab(id, model.Frame)
    }

    next, Cmd.none
  | HistoryChanged history ->
    let frame =
      EditorState.setDirty(TabId.history, history.NewCount, model.Frame)

    let next = {
      model with
          History = history
          Frame = frame
    }

    next, Cmd.none
  | SessionChanged state ->
    let next = { model with Unread = unreadOf state }

    next, Cmd.none
  | RemotesChanged count ->
    let next = { model with RemoteCount = count }

    next, Cmd.none
  | SavedChanged count ->
    let next = { model with Saved = count }

    next, Cmd.none
  | QueueChanged count ->
    let next = { model with Queue = count }

    next, Cmd.none
  | ClockTicked moment ->
    let next = { model with Now = moment }

    next, Cmd.none
  | ActivitySelected activity ->
    let flyout =
      activity = ActivityBar.History
      && model.Band <> Full
      && not model.FlyoutOpen

    let next = {
      model with
          Activity = activity
          FlyoutOpen = flyout
    }

    next, Cmd.none
  | WidthChanged band ->
    if band = model.Band then
      model, Cmd.none
    else
      let next = {
        model with
            Band = band
            FlyoutOpen = false
      }

      next, Cmd.none

let subscribe
  (
    props: Props<'env>,
    snapshots: IObservable<HistoryTab.Snapshot>,
    _model: Model
  ) : Sub<Msg> =
  [
    Sub.fromObservable "history" (snapshots |> Observable.map HistoryChanged)

    Sub.fromObservable
      "session"
      (props.Env.Session.Observe |> Observable.map SessionChanged)

    Sub.fromObservable
      "remotes"
      (props.Env.Remotes.Observe
       |> Observable.map(fun state ->
         RemotesChanged(state.Unread |> Option.defaultValue 0)))

    Sub.fromObservable "saved" (props.Saved |> Observable.map SavedChanged)

    Sub.fromObservable "queue" (props.Queue |> Observable.map QueueChanged)

    Sub.fromObservable
      "clock"
      (props.Env.UiClock.Minute |> Observable.map ClockTicked)
  ]
  |> Sub.batch

let view(args: ViewArgs<'env>) : Control =
  let model = args.Model
  let dispatch = args.Dispatch
  let props = args.Props

  let activity =
    rebuildOn(
      model,
      (fun current -> current.Activity, current.Unread, current.RemoteCount),
      (fun current ->
        ActivityBar.view {
          Selected = current.Activity
          Badges = {
            Unread = current.Unread
            Remotes = current.RemoteCount
          }
          OnSelect = fun activity -> dispatch(ActivitySelected activity)
        })
    )

  let explorer =
    rebuildOn(
      model,
      (fun current ->
        EditorState.selectedRef current.Frame, current.Saved, current.Queue),
      (fun current ->
        Explorer.view {
          Selected = EditorState.selectedRef current.Frame
          Saved = current.Saved
          Queue = current.Queue
          Viewer = props.Session.Session.User.Handle
          OnSelectRef = fun log -> dispatch(RefSelected log)
        })
    )

  let activityControl = ContentControl().Content(activity |> _.ToBinding())

  let sidebar =
    ContentControl()
      .Width(240.0)
      .Content(explorer |> _.ToBinding())
      .IsVisible(model |> Observable.map(fun current -> current.Band = Full))

  let tabStrip =
    rebuildOn(
      model,
      (fun current ->
        EditorState.tabs current.Frame,
        EditorState.activeTab current.Frame,
        current.Band),
      (fun current ->
        TabStrip.view {
          Tabs = EditorState.tabs current.Frame
          Active = EditorState.activeTab current.Frame
          Band = current.Band
          OnSelect = fun id -> dispatch(TabSelected id)
          OnClose = fun id -> dispatch(TabClosed id)
        })
    )

  let tabArea = ContentControl().Content(tabStrip |> _.ToBinding())

  let historyView = args.Runtime.History

  historyView.IsVisible(
    model
    |> Observable.map(fun current ->
      EditorState.activeTab current.Frame = TabId.history)
  )
  |> ignore

  let placeholder =
    TextBlock()
      .Text("Nothing is open here yet.")
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Center)
      .IsVisible(
        model
        |> Observable.map(fun current ->
          EditorState.activeTab current.Frame <> TabId.history)
      )

  let flyoutExplorer =
    ContentControl().Width(240.0).Content(explorer |> _.ToBinding())

  let flyout =
    Border()
      .Name("EditorSidebarFlyout")
      .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
      .Width(240.0)
      .HorizontalAlignment(HorizontalAlignment.Left)
      .VerticalAlignment(VerticalAlignment.Stretch)
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.FlyoutOpen && current.Band <> Full)
      )
      .Child(flyoutExplorer)

  let content = Grid()

  content.Children(historyView, placeholder, flyout) |> ignore

  let editor = Grid()

  editor.RowDefinitions <- RowDefinitions("Auto,*")

  editor.Children(tabArea.Row(0), content.Row(1)) |> ignore

  let status =
    rebuildOn(
      model,
      (fun current ->
        EditorState.selectedRef current.Frame,
        current.History,
        current.Now,
        current.Band),
      (fun current ->
        StatusBar.view {
          Ref = EditorState.selectedRef current.Frame
          Lane = props.Lane
          Key = props.Session.Session.DeviceFingerprint
          FetchedAt = current.History.FetchedAt
          Now = current.Now
          NewCount = current.History.NewCount
          Band = current.Band
        })
    )

  let statusBar = ContentControl().Content(status |> _.ToBinding())

  let root = Grid()

  root.Name <- "EditorRoot"
  root.RowDefinitions <- RowDefinitions("*,Auto")
  root.ColumnDefinitions <- ColumnDefinitions("48,Auto,*")

  root.Children(
    activityControl.Column(0),
    sidebar.Column(1),
    editor.Column(2),
    statusBar.Row(1).ColumnSpan(3)
  )
  |> ignore

  root.SizeChanged.Add(fun _ ->
    WidthBand.ofWidth root.Bounds.Width |> WidthChanged |> dispatch)

  root

let program
  (
    runtime: Runtime,
    props: Props<'env>,
    snapshots: IObservable<HistoryTab.Snapshot>
  ) : Program<unit, Model, Msg, Control> =
  Program.mkProgram
    (fun () ->
      initial(props, HistoryTab.State.snapshot HistoryTab.State.initial),
      Cmd.none)
    (fun msg model -> update(runtime, msg, model))
    (fun model dispatch ->
      view {
        Runtime = runtime
        Props = props
        Model = model
        Dispatch = dispatch
      })
  |> Program.withSubscription(fun model -> subscribe(props, snapshots, model))

let mount(dispatcher: IDispatcher, props: Props<'env>) =
  let snapshots =
    new BehaviorSubject<HistoryTab.Snapshot>(
      HistoryTab.State.snapshot HistoryTab.State.initial
    )

  let selected =
    new BehaviorSubject<LogKind>(StoredState.selectedRef props.Stored)

  let content = ContentControl()
  let historyLifetime = new SerialDisposable()

  let show(log: LogKind) =
    selected.OnNext log
    historyLifetime.Disposable <- Disposable.Empty

    let history, handle =
      HistoryTab.mount(
        dispatcher,
        historyProps props log (fun snapshot ->
          if selected.Value = log then
            snapshots.OnNext snapshot)
      )

    content.Content <- history
    historyLifetime.Disposable <- handle

  show(StoredState.selectedRef props.Stored)

  let runtime = { History = content; SwitchRef = show }

  let root, frameHandle =
    DevNull.Client.Programs.runPage(
      dispatcher,
      program(runtime, props, snapshots)
    )

  let snapshotsCleanup =
    Disposable.Create(fun () ->
      snapshots.OnCompleted()
      snapshots.Dispose())

  let handles = new CompositeDisposable()

  handles.Add frameHandle
  handles.Add historyLifetime
  handles.Add snapshotsCleanup

  handles.Add(
    Disposable.Create(fun () ->
      selected.OnCompleted()
      selected.Dispose())
  )

  root, (handles :> IDisposable)
