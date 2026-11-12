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
  OnQuote: Commit -> unit
  OnReplyRequested: Commit -> unit
}

type ThreadEntry = { View: Control; Lifetime: IDisposable }

type Runtime = {
  History: Control
  SwitchRef: LogKind * EditorPlace -> unit
  OpenThread: string -> unit
  CloseThread: string -> unit
  Threads: IObservable<Map<string, Control>>
}

type Model = {
  Frame: EditorState
  Band: WidthBand
  History: HistoryTab.Snapshot
  Places: Map<LogKind, EditorPlace>
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
  | OpenThread of Commit
  | TabSelected of TabId
  | TabClosed of TabId
  | HistoryChanged of HistoryTab.Snapshot
  | ThreadChanged of string * ThreadTab.Snapshot
  | Scrolled of LogKind * float
  | SessionChanged of SessionState
  | RemotesChanged of count: int
  | SavedChanged of count: int
  | QueueChanged of count: int
  | ClockTicked of moment: DateTimeOffset
  | ActivitySelected of ActivityBar.Activity
  | WidthChanged of band: WidthBand

[<Struct>]
type HistorySinks = {
  Snapshot: HistoryTab.Snapshot -> unit
  Scroll: float -> unit
  Open: Commit -> unit
}

type ProgramArgs<'env
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
  History: IObservable<HistoryTab.Snapshot>
  Scrolls: IObservable<LogKind * float>
  Threads: IObservable<string * ThreadTab.Snapshot>
  Opens: IObservable<Commit>
}

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

[<Struct>]
type HistoryArgs<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideRemotes
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache> = {
  Props: Props<'env>
  Log: LogKind
  Place: EditorPlace
  Sinks: HistorySinks
}

let historyProps(args: HistoryArgs<'env>) : HistoryTab.Props<'env> = {
  Env = args.Props.Env
  Session = args.Props.Session
  Log = args.Log
  Zone = args.Props.Zone
  Active = args.Props.Active
  Now = args.Props.Now
  Restore = ValueSome args.Place
  OnSnapshot = args.Sinks.Snapshot
  OnScroll = args.Sinks.Scroll
  OnOpen = args.Sinks.Open
}

let threadProps
  (props: Props<'env>, commitId: string, onSnapshot: ThreadTab.Snapshot -> unit)
  : ThreadTab.Props<'env> = {
  Env = props.Env
  Session = props.Session
  CommitId = commitId
  Zone = props.Zone
  Now = props.Now
  OnSnapshot = onSnapshot
  OnQuote = props.OnQuote
  OnReplyRequested = props.OnReplyRequested
}

let initial(props: Props<'env>, history: HistoryTab.Snapshot) = {
  Frame = EditorState.initial(StoredState.selectedRef props.Stored)
  Band = Full
  History = history
  Places = Map.empty
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
    let place =
      model.Places |> Map.tryFind log |> Option.defaultValue EditorPlace.empty

    let frame =
      EditorState.setDirty(
        TabId.history,
        0,
        EditorState.selectRef(log, model.Frame)
      )
      |> fun frame -> EditorState.selectTab(TabId.history, frame)

    let next = {
      model with
          Frame = frame
          History = HistoryTab.State.snapshot HistoryTab.State.initial
          FlyoutOpen = false
    }

    next, Cmd.ofAction(fun () -> runtime.SwitchRef(log, place))
  | OpenThread commit ->
    let id = TabId.thread commit.Id

    let exists =
      model.Frame
      |> EditorState.tabs
      |> List.exists(fun tab -> EditorTab.id tab = id)

    if exists then
      let next = {
        model with
            Frame = EditorState.selectTab(id, model.Frame)
            FlyoutOpen = false
      }

      next, Cmd.none
    else
      let title = $"{commit.ShortHash} @{Author.handle commit.Author}"
      let count = commit.RepliesCount |> Option.defaultValue 0
      let tab = EditorTab.thread(id, title, count)

      let next = {
        model with
            Frame = EditorState.openTab(tab, model.Frame)
            FlyoutOpen = false
      }

      next, Cmd.ofAction(fun () -> runtime.OpenThread commit.Id)
  | TabSelected id ->
    let next = {
      model with
          Frame = EditorState.selectTab(id, model.Frame)
          FlyoutOpen = false
    }

    next, Cmd.none
  | TabClosed id ->
    let target =
      model.Frame
      |> EditorState.tabs
      |> List.tryFind(fun tab -> EditorTab.id tab = id)

    let next = {
      model with
          Frame = EditorState.closeTab(id, model.Frame)
    }

    let cmd =
      match target with
      | Some tab when not(EditorTab.isPinned tab) ->
        Cmd.ofAction(fun () -> runtime.CloseThread(TabId.key id))
      | _ -> Cmd.none

    next, cmd
  | HistoryChanged history ->
    let log = EditorState.selectedRef model.Frame

    let places =
      match history.Head with
      | Some head ->
        let place =
          model.Places
          |> Map.tryFind log
          |> Option.defaultValue EditorPlace.empty

        model.Places |> Map.add log (EditorPlace.withHead(Some head, place))
      | None -> model.Places

    let frame =
      EditorState.setDirty(TabId.history, history.NewCount, model.Frame)

    let next = {
      model with
          History = history
          Frame = frame
          Places = places
    }

    next, Cmd.none
  | ThreadChanged(commitId, snapshot) ->
    let id = TabId.thread commitId

    let exists =
      model.Frame
      |> EditorState.tabs
      |> List.exists(fun tab -> EditorTab.id tab = id)

    if not exists then
      model, Cmd.none
    else
      let frame = EditorState.setReplies(id, snapshot.RepliesCount, model.Frame)

      let frame =
        if EditorState.activeTab frame = id then
          EditorState.markSeen(id, frame)
        else
          frame

      { model with Frame = frame }, Cmd.none
  | Scrolled(log, offset) ->
    let place =
      model.Places |> Map.tryFind log |> Option.defaultValue EditorPlace.empty

    let next = {
      model with
          Places =
            model.Places |> Map.add log (EditorPlace.withScroll(offset, place))
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

    let frame =
      if activity = ActivityBar.History && model.Band = Full then
        EditorState.selectTab(TabId.history, model.Frame)
      else
        model.Frame

    let next = {
      model with
          Frame = frame
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

let subscribe(args: ProgramArgs<'env>, _model: Model) : Sub<Msg> =
  [
    Sub.fromObservable "history" (args.History |> Observable.map HistoryChanged)

    Sub.fromObservable "threads" (args.Threads |> Observable.map ThreadChanged)

    Sub.fromObservable "scrolls" (args.Scrolls |> Observable.map Scrolled)

    Sub.fromObservable "opens" (args.Opens |> Observable.map OpenThread)

    Sub.fromObservable
      "session"
      (args.Props.Env.Session.Observe |> Observable.map SessionChanged)

    Sub.fromObservable
      "remotes"
      (args.Props.Env.Remotes.Observe
       |> Observable.map(fun state ->
         RemotesChanged(state.Unread |> Option.defaultValue 0)))

    Sub.fromObservable "saved" (args.Props.Saved |> Observable.map SavedChanged)

    Sub.fromObservable "queue" (args.Props.Queue |> Observable.map QueueChanged)

    Sub.fromObservable
      "clock"
      (args.Props.Env.UiClock.Minute |> Observable.map ClockTicked)
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

  let loading =
    TextBlock()
      .Text("Opening the thread…")
      .Classes(Theme.name Theme.FgMuted)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Center)

  let activeContent =
    Observable.combineLatest
      (model |> Observable.map(fun current -> current.Frame))
      args.Runtime.Threads
    |> Observable.map(fun (frame, threads) ->
      let id = EditorState.activeTab frame

      if id = TabId.history then
        args.Runtime.History
      else
        threads |> Map.tryFind(TabId.key id) |> Option.defaultValue loading)
    |> Observable.distinctUntilChanged

  let content = ContentControl().Content(activeContent |> _.ToBinding())

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

  let contentArea = Grid()

  contentArea.Children(content, flyout) |> ignore

  let editor = Grid()

  editor.RowDefinitions <- RowDefinitions("Auto,*")

  editor.Children(tabArea.Row(0), contentArea.Row(1)) |> ignore

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

let program(args: ProgramArgs<'env>) : Program<unit, Model, Msg, Control> =
  Program.mkProgram
    (fun () ->
      initial(args.Props, HistoryTab.State.snapshot HistoryTab.State.initial),
      Cmd.none)
    (fun msg model -> update(args.Runtime, msg, model))
    (fun model dispatch ->
      view {
        Runtime = args.Runtime
        Props = args.Props
        Model = model
        Dispatch = dispatch
      })
  |> Program.withSubscription(fun model -> subscribe(args, model))

let mount(dispatcher: IDispatcher, props: Props<'env>) =
  let snapshots =
    new BehaviorSubject<HistoryTab.Snapshot>(
      HistoryTab.State.snapshot HistoryTab.State.initial
    )

  let selected =
    new BehaviorSubject<LogKind>(StoredState.selectedRef props.Stored)

  let scrolls = new System.Reactive.Subjects.Subject<LogKind * float>()

  let threadSnapshots =
    new System.Reactive.Subjects.Subject<string * ThreadTab.Snapshot>()

  let opens = new System.Reactive.Subjects.Subject<Commit>()
  let entries = new BehaviorSubject<Map<string, ThreadEntry>>(Map.empty)
  let content = ContentControl()
  let historyLifetime = new SerialDisposable()

  let show(log: LogKind, place: EditorPlace) =
    selected.OnNext log
    historyLifetime.Disposable <- Disposable.Empty

    let history, handle =
      HistoryTab.mount(
        dispatcher,
        historyProps {
          Props = props
          Log = log
          Place = place
          Sinks = {
            Snapshot =
              fun snapshot ->
                if selected.Value = log then
                  snapshots.OnNext snapshot
            Scroll =
              fun offset ->
                if selected.Value = log then
                  scrolls.OnNext(log, offset)
            Open =
              fun commit ->
                if selected.Value = log then
                  opens.OnNext commit
          }
        }
      )

    content.Content <- history
    historyLifetime.Disposable <- handle

  show(StoredState.selectedRef props.Stored, EditorPlace.empty)

  let threads =
    entries
    |> Observable.map(fun map -> map |> Map.map(fun _ entry -> entry.View))
    |> Slices.share

  let openThread(commitId: string) =
    if not(Map.containsKey commitId entries.Value) then
      let view, lifetime =
        ThreadTab.mount(
          dispatcher,
          threadProps(
            props,
            commitId,
            fun snapshot -> threadSnapshots.OnNext(commitId, snapshot)
          )
        )

      entries.OnNext(
        entries.Value.Add(commitId, { View = view; Lifetime = lifetime })
      )

  let closeThread(commitId: string) =
    match Map.tryFind commitId entries.Value with
    | Some entry ->
      entries.OnNext(entries.Value.Remove commitId)
      entry.Lifetime.Dispose()
    | None -> ()

  let runtime = {
    History = content
    SwitchRef = show
    OpenThread = openThread
    CloseThread = closeThread
    Threads = threads
  }

  let args: ProgramArgs<'env> = {
    Runtime = runtime
    Props = props
    History = snapshots
    Scrolls = scrolls
    Threads = threadSnapshots
    Opens = opens
  }

  let root, frameHandle =
    DevNull.Client.Programs.runPage(dispatcher, program args)

  let threadsCleanup =
    Disposable.Create(fun () ->
      for KeyValue(_, entry) in entries.Value do
        entry.Lifetime.Dispose()

      entries.OnCompleted()
      entries.Dispose())

  let subjectCleanup =
    Disposable.Create(fun () ->
      snapshots.OnCompleted()
      snapshots.Dispose()
      selected.OnCompleted()
      selected.Dispose()
      scrolls.OnCompleted()
      scrolls.Dispose()
      threadSnapshots.OnCompleted()
      threadSnapshots.Dispose()
      opens.OnCompleted()
      opens.Dispose())

  let handles = new CompositeDisposable()

  handles.Add frameHandle
  handles.Add historyLifetime
  handles.Add threadsCleanup
  handles.Add subjectCleanup

  root, (handles :> IDisposable)
