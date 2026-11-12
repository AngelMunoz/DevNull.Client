module DevNull.Client.Workbench.WorkbenchHost

open System
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Threading
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.Theme

type HistoryChild = {
  Ref: Ref
  Restore: HistoryColumn.Place option
  View: Control
  Handle: IDisposable
}

type InspectorChild = {
  CommitId: string
  View: Control
  Handle: IDisposable
}

type Model = {
  Selected: Ref
  CheckoutId: string option
  Places: Map<Ref, HistoryColumn.Place>
  StagingOpen: bool
  Handle: string
  FetchedAt: DateTimeOffset option
  SavedCount: int
  UnreadCount: int
  Folded: bool
  Now: DateTimeOffset
  Pasteis: bool
  History: HistoryChild option
  Inspector: InspectorChild option
  LiveHistory: int
  LiveInspector: int
}

[<NoComparison>]
type Msg =
  | RefSelected of ref: Ref
  | CheckoutSet of commitId: string option
  | StagingToggled
  | WidthChanged of width: float
  | ClockTicked of moment: DateTimeOffset
  | SessionChanged of state: SessionState
  | HistoryReported of ref: Ref * report: HistoryColumn.Report
  | OpenAuthor of handle: string
  | OpenExternal of url: string
  | Quote of Commit
  | HistoryStopped
  | InspectorStopped
  | HistoryReady of
    ref: Ref *
    restore: HistoryColumn.Place option *
    view: Control *
    handle: IDisposable
  | InspectorReady of commitId: string * view: Control * handle: IDisposable

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession> = {
  Env: 'env
  Initial: WorkbenchState
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  OnOpenAuthor: string -> unit
  OnQuote: Commit -> unit
  OnOpenExternal: string -> unit
  OnStateChanged: WorkbenchState -> unit
}

[<Sealed>]
type TrackedHandle(raw: IDisposable) =

  member val Disposed = false with get, set

  member this.Dispose() =
    if not this.Disposed then
      this.Disposed <- true
      raw.Dispose()

  interface IDisposable with
    member this.Dispose() = this.Dispose()

[<Sealed>]
type MountedHost() =

  let subject = new System.Reactive.Subjects.Subject<Msg>()
  let created = ResizeArray<TrackedHandle>()

  member val CurrentHistory: TrackedHandle option = None with get, set

  member val CurrentInspector: TrackedHandle option = None with get, set

  member val HostHandle: IDisposable option = None with get, set

  member val Disposed = false with get, set

  member val LiveHistory = 0 with get, set

  member val LiveInspector = 0 with get, set

  member val HostDisposed = false with get, set

  member _.Subject = subject

  member _.Created = created |> Seq.toList

  member this.Record(model: Model) =
    let track(handle: IDisposable) =
      match handle with
      | :? TrackedHandle as tracked ->
        if
          created
          |> Seq.exists(fun item -> obj.ReferenceEquals(item, tracked))
          |> not
        then
          created.Add tracked

        Some tracked
      | _ -> None

    this.LiveHistory <- model.LiveHistory
    this.LiveInspector <- model.LiveInspector

    this.CurrentHistory <-
      model.History |> Option.bind(fun child -> track child.Handle)

    this.CurrentInspector <-
      model.Inspector |> Option.bind(fun child -> track child.Handle)

  member this.Attach(hostHandle: IDisposable) =
    this.HostHandle <- Some hostHandle

  member this.Send(msg: Msg) =
    if not this.Disposed then
      subject.OnNext msg

  member this.DisposeCore() =
    if not this.Disposed then
      this.Disposed <- true
      this.CurrentHistory |> Option.iter _.Dispose()
      this.CurrentInspector |> Option.iter _.Dispose()
      this.HostHandle |> Option.iter _.Dispose()
      this.LiveHistory <- 0
      this.LiveInspector <- 0
      this.HostDisposed <- true
      subject.OnCompleted()
      subject.Dispose()

  interface IDisposable with
    member this.Dispose() = this.DisposeCore()

[<Struct>]
type SessionData = {
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Handle: string
  Unread: int
}

[<Struct>]
type HistoryStart = {
  Ref: Ref
  Place: HistoryColumn.Place option
  KeyFile: string
  ViewerId: string
  Pasteis: bool
}

[<Struct>]
type InspectorStart = {
  CommitId: string
  KeyFile: string
  ViewerId: string
  Pasteis: bool
}

let sessionOfState(state: SessionState) : SessionData =
  match state with
  | SignedIn session -> {
      KeyFile = session.KeyFile
      ViewerId = session.Session.User.Id
      Pasteis = session.Session.PasteisMode
      Handle = session.Session.User.Handle
      Unread = session.Session.UnreadNotificationsCount
    }
  | SignedOut -> {
      KeyFile = ""
      ViewerId = ""
      Pasteis = false
      Handle = ""
      Unread = 0
    }

let sessionData(props: Props<'env>) : SessionData =
  sessionOfState props.Env.Session.Current

let startHistory
  (props: Props<'env>, dispatch: Msg -> unit, start: HistoryStart)
  : HistoryChild option =
  match Ref.log start.Ref with
  | None -> None
  | Some log ->
    let childProps: HistoryColumn.Props<'env> = {
      Env = props.Env
      Log = log
      KeyFile = start.KeyFile
      ViewerId = start.ViewerId
      Pasteis = start.Pasteis
      Images = props.Images
      Now = props.Now
      Zone = props.Zone
      OnOpenExternal = fun url -> dispatch(OpenExternal url)
      OnOpened = fun commit -> dispatch(CheckoutSet(Some commit.Id))
      OnQuote = fun commit -> dispatch(Quote commit)
      Restore = start.Place
      OnReport = fun report -> dispatch(HistoryReported(start.Ref, report))
    }

    let view, rawHandle =
      Programs.runPage(props.Env.Dispatcher, HistoryColumn.program childProps)

    let handle = TrackedHandle(rawHandle) :> IDisposable

    Some {
      Ref = start.Ref
      Restore = start.Place
      View = view
      Handle = handle
    }

let startInspector
  (props: Props<'env>, dispatch: Msg -> unit, start: InspectorStart)
  : InspectorChild =
  let childProps: Inspector.Props<'env> = {
    Env = props.Env
    CommitId = start.CommitId
    KeyFile = start.KeyFile
    ViewerId = start.ViewerId
    Pasteis = start.Pasteis
    Images = props.Images
    Now = props.Now
    Zone = props.Zone
    OnCheckoutParent = fun commitId -> dispatch(CheckoutSet(Some commitId))
    OnOpenAuthor = fun handle -> dispatch(OpenAuthor handle)
    OnOpenExternal = fun url -> dispatch(OpenExternal url)
    OnQuote = fun commit -> dispatch(Quote commit)
  }

  let view, rawHandle =
    Programs.runPage(props.Env.Dispatcher, Inspector.program childProps)

  let handle = TrackedHandle(rawHandle) :> IDisposable

  {
    CommitId = start.CommitId
    View = view
    Handle = handle
  }

let startHistoryCmd
  (props: Props<'env>, old: IDisposable option, start: HistoryStart)
  : Cmd<Msg> =
  Cmd.ofEffect(fun dispatch ->
    match old with
    | Some handle ->
      handle.Dispose()
      dispatch HistoryStopped
    | None -> ()

    match startHistory(props, dispatch, start) with
    | Some child ->
      dispatch(HistoryReady(start.Ref, start.Place, child.View, child.Handle))
    | None -> ())

let startInspectorCmd
  (props: Props<'env>, old: IDisposable option, start: InspectorStart option)
  : Cmd<Msg> =
  Cmd.ofEffect(fun dispatch ->
    match old with
    | Some handle ->
      handle.Dispose()
      dispatch InspectorStopped
    | None -> ()

    match start with
    | Some start ->
      let child = startInspector(props, dispatch, start)
      dispatch(InspectorReady(start.CommitId, child.View, child.Handle))
    | None -> ())

let stateCmd
  (props: Props<'env>, ref: Ref, checkoutId: string option)
  : Cmd<Msg> =
  Cmd.ofAction(fun () -> props.OnStateChanged(Build.state(ref, checkoutId)))

let init(props: Props<'env>) : Model * Cmd<Msg> =
  let data = sessionData props
  let selected = WorkbenchState.refOf props.Initial
  let checkoutId = WorkbenchState.checkoutId props.Initial

  let checkoutStart =
    checkoutId
    |> Option.map(fun commitId -> {
      CommitId = commitId
      KeyFile = data.KeyFile
      ViewerId = data.ViewerId
      Pasteis = data.Pasteis
    })

  let model: Model = {
    Selected = selected
    CheckoutId = checkoutId
    Places = Map.empty
    StagingOpen = false
    Handle = data.Handle
    FetchedAt = None
    SavedCount = 0
    UnreadCount = data.Unread
    Folded = false
    Now = DateTimeOffset.UtcNow
    Pasteis = data.Pasteis
    History = None
    Inspector = None
    LiveHistory = 0
    LiveInspector = 0
  }

  let historyStart: HistoryStart = {
    Ref = selected
    Place = None
    KeyFile = data.KeyFile
    ViewerId = data.ViewerId
    Pasteis = data.Pasteis
  }

  model,
  Cmd.batch [
    startHistoryCmd(props, None, historyStart)
    startInspectorCmd(props, None, checkoutStart)
  ]

let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
  match msg with
  | RefSelected ref ->
    let data = sessionData props

    let start: HistoryStart = {
      Ref = ref
      Place = model.Places |> Map.tryFind ref
      KeyFile = data.KeyFile
      ViewerId = data.ViewerId
      Pasteis = data.Pasteis
    }

    {
      model with
          Selected = ref
          History = None
          FetchedAt = None
    },
    Cmd.batch [
      startHistoryCmd(props, model.History |> Option.map _.Handle, start)
      stateCmd(props, ref, model.CheckoutId)
    ]
  | CheckoutSet commitId ->
    let data = sessionData props

    let start =
      commitId
      |> Option.map(fun id -> {
        CommitId = id
        KeyFile = data.KeyFile
        ViewerId = data.ViewerId
        Pasteis = data.Pasteis
      })

    {
      model with
          CheckoutId = commitId
          Inspector = None
    },
    Cmd.batch [
      startInspectorCmd(props, model.Inspector |> Option.map _.Handle, start)

      stateCmd(props, model.Selected, commitId)
    ]
  | StagingToggled ->
    {
      model with
          StagingOpen = not model.StagingOpen
    },
    Cmd.none
  | WidthChanged width -> { model with Folded = width < 1100.0 }, Cmd.none
  | ClockTicked moment -> { model with Now = moment }, Cmd.none
  | SessionChanged state ->
    let data = sessionOfState state

    ({
      model with
          Handle = data.Handle
          UnreadCount = data.Unread
          Pasteis = data.Pasteis
     },
     Cmd.none)
  | HistoryReported(ref, report) ->
    let places = model.Places |> Map.add ref report.Place

    let fetchedAt =
      if ref = model.Selected then
        report.FetchedAt |> Option.orElse model.FetchedAt
      else
        model.FetchedAt

    ({
      model with
          Places = places
          FetchedAt = fetchedAt
     },
     Cmd.none)
  | OpenAuthor handle ->
    model, Cmd.ofAction(fun () -> props.OnOpenAuthor handle)
  | OpenExternal url -> model, Cmd.ofAction(fun () -> props.OnOpenExternal url)
  | Quote commit -> model, Cmd.ofAction(fun () -> props.OnQuote commit)
  | HistoryStopped ->
    ({
      model with
          LiveHistory = max 0 (model.LiveHistory - 1)
     },
     Cmd.none)
  | InspectorStopped ->
    ({
      model with
          LiveInspector = max 0 (model.LiveInspector - 1)
     },
     Cmd.none)
  | HistoryReady(ref, restore, view, handle) ->
    if ref = model.Selected then
      model.History |> Option.iter(fun current -> current.Handle.Dispose())

      let live =
        if model.History.IsSome then
          model.LiveHistory
        else
          model.LiveHistory + 1

      ({
        model with
            History =
              Some {
                Ref = ref
                Restore = restore
                View = view
                Handle = handle
              }
            LiveHistory = live
       },
       Cmd.none)
    else
      handle.Dispose()
      model, Cmd.none
  | InspectorReady(commitId, view, handle) ->
    if Some commitId = model.CheckoutId then
      model.Inspector |> Option.iter(fun current -> current.Handle.Dispose())

      let live =
        if model.Inspector.IsSome then
          model.LiveInspector
        else
          model.LiveInspector + 1

      ({
        model with
            Inspector =
              Some {
                CommitId = commitId
                View = view
                Handle = handle
              }
            LiveInspector = live
       },
       Cmd.none)
    else
      handle.Dispose()
      model, Cmd.none

let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
  [
    Sub.fromObservable "clock" (props.Now |> Observable.map ClockTicked)

    Sub.fromObservable
      "session"
      (props.Env.Session.Observe |> Observable.map SessionChanged)
  ]
  |> Sub.batch

let historyPlaceholder(selected: Ref) : Control =
  let words =
    match Ref.log selected with
    | Some _ -> "Loading the history…"
    | None -> "This ref opens in a later phase."

  TextBlock()
    .Classes(Theme.name Theme.Muted)
    .Margin(Thickness(16.0))
    .Text(words)

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let refsSlice =
    model
    |> Observable.map(fun current ->
      current.Selected, current.SavedCount, current.UnreadCount)
    |> Observable.distinctUntilChanged
    |> Observable.map(fun (selected, saved, unread) ->
      WorkbenchChrome.refs(
        {
          Rows = WorkbenchChrome.rows(saved, unread)
          Selected = selected
        },
        fun ref -> dispatch(RefSelected ref)
      ))

  let refsHost = ContentControl().Content(refsSlice |> _.ToBinding())

  let refsRegion =
    Border()
      .Name("RefsRegion")
      .Width(240.0)
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 1.0, 0.0))
      .Child(
        ScrollViewer()
          .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
          .Content(refsHost)
      )

  let folded =
    model |> Observable.map _.Folded |> Observable.distinctUntilChanged

  let hasCheckout =
    model
    |> Observable.map _.CheckoutId.IsSome
    |> Observable.distinctUntilChanged

  let reserved =
    Observable.combineLatest folded hasCheckout
    |> Observable.map(fun (isFolded, checkout) ->
      if isFolded || not checkout then 0.0 else 380.0)

  let historySlice =
    model
    |> Observable.map(fun current ->
      current.History |> Option.map _.View, current.Selected)
    |> Observable.distinctUntilChanged
    |> Observable.map(fun (content, selected) ->
      content |> Option.defaultWith(fun () -> historyPlaceholder selected))

  let historyHost =
    ContentControl()
      .Name("HistoryRegion")
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .VerticalContentAlignment(VerticalAlignment.Stretch)
      .Content(historySlice |> _.ToBinding())
      .Margin(
        reserved |> Observable.map(fun right -> Thickness(0.0, 0.0, right, 0.0))
      )

  let inspectorPlaceholder =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Margin(Thickness(16.0))
      .Text("No commit checked out.")

  let inspectorSlice =
    model
    |> Observable.map(fun current -> current.Inspector |> Option.map _.View)
    |> Observable.distinctUntilChanged
    |> Observable.map(Option.defaultValue inspectorPlaceholder)

  let inspectorRegion =
    Border()
      .Name("InspectorRegion")
      .Width(380.0)
      .HorizontalAlignment(HorizontalAlignment.Right)
      .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0, 0.0, 0.0, 0.0))
      .Child(
        ContentControl()
          .HorizontalContentAlignment(HorizontalAlignment.Stretch)
          .VerticalContentAlignment(VerticalAlignment.Stretch)
          .Content(inspectorSlice |> _.ToBinding())
      )
      .IsVisible(hasCheckout)

  let main = Grid()

  main.Children(historyHost, inspectorRegion) |> ignore

  let statusLine =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .Text(
        model
        |> Observable.map(fun current ->
          WorkbenchChrome.statusText(
            {
              Ref = current.Selected
              Handle = current.Handle
              FetchedAt = current.FetchedAt
              Now = current.Now
            }
            : WorkbenchChrome.StatusModel
          ))
      )

  let statusBar = Border()
  statusBar.Name <- "StatusBar"
  statusBar.Classes(Theme.name Theme.Hairline) |> ignore
  statusBar.BorderThickness <- Thickness(0.0, 1.0, 0.0, 0.0)
  statusBar.Padding <- Thickness(16.0, 8.0, 16.0, 8.0)
  statusBar.Child <- statusLine

  let root = Grid().Name("WorkbenchRoot")

  root.RowDefinitions <- RowDefinitions("*,Auto")
  root.ColumnDefinitions <- ColumnDefinitions("240,*")

  root.Children(
    refsRegion.Column(0).Row(0),
    main.Column(1).Row(0),
    statusBar.Row(1).ColumnSpan(2)
  )
  |> ignore

  root.SizeChanged.Add(fun args -> dispatch(WidthChanged args.NewSize.Width))

  root

let program(props: Props<'env>) =
  Program.mkProgram
    (fun () -> init props)
    (fun msg model -> update(props, msg, model))
    (view props)
  |> Program.withSubscription(subscribe props)

let mount(dispatcher: IDispatcher, props: Props<'env>) : Control * IDisposable =
  let mounted = MountedHost()

  let wrappedInit() =
    let model, cmd = init props
    mounted.Record model
    model, cmd

  let wrappedUpdate msg model =
    let next, cmd = update(props, msg, model)
    mounted.Record next
    next, cmd

  let mountedProgram =
    Program.mkProgram wrappedInit wrappedUpdate (view props)
    |> Program.withSubscription(fun model ->
      Sub.batch [
        subscribe props model
        Sub.fromObservable "mount" (mounted.Subject :> IObservable<Msg>)
      ])

  let view, hostHandle = Programs.runPage(dispatcher, mountedProgram)

  mounted.Attach hostHandle
  view, (mounted :> IDisposable)
