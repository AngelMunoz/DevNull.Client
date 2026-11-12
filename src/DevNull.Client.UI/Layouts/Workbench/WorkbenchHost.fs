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
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.Theme

type HistoryChild = {
  Ref: Ref
  Restore: HistoryColumn.Place option
  View: Control
  Handle: IDisposable
  Refresh: unit -> unit
}

type InspectorChild = {
  CommitId: string
  View: Control
  Handle: IDisposable
}

type ThreadChild = {
  CommitId: string
  View: ThreadMode.View
  Handle: IDisposable
}

type RepositoryChild = {
  RepositoryHandle: string
  View: RepositoryHome.View
  Handle: IDisposable
}

type Model = {
  Selected: Ref
  CheckoutId: string option
  Places: Map<Ref, HistoryColumn.Place>
  StagingOpen: bool
  Staging: Staging.Model
  ThreadId: string option
  Thread: ThreadChild option
  Repository: RepositoryChild option
  RepositoryListOpen: bool
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
  LiveThread: int
  LiveRepository: int
}

[<NoComparison>]
type Msg =
  | RefSelected of ref: Ref
  | CheckoutSet of commitId: string option
  | OpenThread of commitId: string
  | ThreadClosed
  | StagingToggled
  | DraftChanged of text: string
  | StagingPosted of commit: Commit
  | StagingScheduled of entry: ScheduledCommit
  | QueueRequested
  | QueueFetched of Result<ScheduledPage, AppError>
  | WidthChanged of width: float
  | ClockTicked of moment: DateTimeOffset
  | SessionChanged of state: SessionState
  | HistoryReported of ref: Ref * report: HistoryColumn.Report
  | OpenAuthor of handle: string
  | OpenExternal of url: string
  | Quote of Commit
  | HistoryStopped
  | InspectorStopped
  | ThreadStopped
  | RepositoryStopped
  | RepositoryListChanged of open': bool
  | HistoryReady of
    ref: Ref *
    restore: HistoryColumn.Place option *
    view: Control *
    handle: IDisposable *
    refresh: (unit -> unit)
  | InspectorReady of commitId: string * view: Control * handle: IDisposable
  | ThreadReady of
    commitId: string *
    view: ThreadMode.View *
    handle: IDisposable
  | RepositoryReady of
    handle: string *
    view: RepositoryHome.View *
    lifetime: IDisposable

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLogging> = {
  Env: 'env
  Initial: WorkbenchState
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  OnOpenAuthor: string -> unit
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

  member val CurrentThread: TrackedHandle option = None with get, set

  member val CurrentRepository: TrackedHandle option = None with get, set

  member val HostHandle: IDisposable option = None with get, set

  member val Disposed = false with get, set

  member val LiveHistory = 0 with get, set

  member val LiveInspector = 0 with get, set

  member val LiveThread = 0 with get, set

  member val LiveRepository = 0 with get, set

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
    this.LiveThread <- model.LiveThread
    this.LiveRepository <- model.LiveRepository

    this.CurrentHistory <-
      model.History |> Option.bind(fun child -> track child.Handle)

    this.CurrentInspector <-
      model.Inspector |> Option.bind(fun child -> track child.Handle)

    this.CurrentThread <-
      model.Thread |> Option.bind(fun child -> track child.Handle)

    this.CurrentRepository <-
      model.Repository |> Option.bind(fun child -> track child.Handle)

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
      this.CurrentThread |> Option.iter _.Dispose()
      this.CurrentRepository |> Option.iter _.Dispose()
      this.HostHandle |> Option.iter _.Dispose()
      this.LiveHistory <- 0
      this.LiveInspector <- 0
      this.LiveThread <- 0
      this.LiveRepository <- 0
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

[<Struct>]
type ThreadStart = {
  CommitId: string
  Session: SignedInSession
  KeyFile: string
  ViewerId: string
  Pasteis: bool
}

[<Struct>]
type RepositoryStart = {
  Handle: string
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

let threadStartOf(props: Props<'env>, commitId: string) : ThreadStart option =
  match props.Env.Session.Current with
  | SignedIn session ->
    Some {
      CommitId = commitId
      Session = session
      KeyFile = session.KeyFile
      ViewerId = session.Session.User.Id
      Pasteis = session.Session.PasteisMode
    }
  | SignedOut -> None

let fetchQueue(props: Props<'env>) : Async<Result<ScheduledPage, AppError>> =
  ScheduledCommits.list(
    props.Env,
    {
      KeyFile = (sessionData props).KeyFile
      Page = 1
    }
  )

let startHistory
  (props: Props<'env>, dispatch: Msg -> unit, start: HistoryStart)
  : HistoryChild option =
  match Ref.log start.Ref with
  | None -> None
  | Some log ->
    let refresh = new System.Reactive.Subjects.Subject<unit>()

    let childProps: HistoryColumn.Props<'env> = {
      Env = props.Env
      Log = log
      KeyFile = start.KeyFile
      ViewerId = start.ViewerId
      Pasteis = start.Pasteis
      Images = props.Images
      Now = props.Now
      Refresh = refresh :> IObservable<unit>
      Zone = props.Zone
      OnOpenExternal = fun url -> dispatch(OpenExternal url)
      OnOpened = fun commit -> dispatch(OpenThread commit.Id)
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
      Refresh = fun () -> refresh.OnNext()
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
    OnCheckoutChild = fun commitId -> dispatch(CheckoutSet(Some commitId))
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

let startThread
  (props: Props<'env>, dispatch: Msg -> unit, start: ThreadStart)
  : ThreadChild =
  let childProps: ThreadMode.Props<'env> = {
    Env = props.Env
    Session = start.Session
    CommitId = start.CommitId
    KeyFile = start.KeyFile
    ViewerId = start.ViewerId
    Pasteis = start.Pasteis
    Images = props.Images
    Now = props.Now
    Zone = props.Zone
    OnBack = fun () -> dispatch ThreadClosed
    OnOpenThread = fun commit -> dispatch(OpenThread commit.Id)
    OnOpenExternal = fun url -> dispatch(OpenExternal url)
    OnQuote = fun commit -> dispatch(Quote commit)
  }

  let view, rawHandle =
    Programs.runPage(props.Env.Dispatcher, ThreadMode.program childProps)

  let handle = TrackedHandle(rawHandle) :> IDisposable

  {
    CommitId = start.CommitId
    View = view
    Handle = handle
  }

let startRepository
  (props: Props<'env>, dispatch: Msg -> unit, start: RepositoryStart)
  : RepositoryChild =
  let childProps: RepositoryHome.Props<'env> = {
    Env = props.Env
    Handle = start.Handle
    KeyFile = start.KeyFile
    ViewerId = start.ViewerId
    Pasteis = start.Pasteis
    Images = props.Images
    Now = props.Now
    Zone = props.Zone
    OnOpenThread = fun commit -> dispatch(OpenThread commit.Id)
    OnQuote = fun commit -> dispatch(Quote commit)
    OnOpenExternal = fun url -> dispatch(OpenExternal url)
    OnOpenRepository =
      fun handle -> dispatch(RefSelected(Build.repository handle))
    OnListChanged = fun open' -> dispatch(RepositoryListChanged open')
  }

  let view, rawHandle =
    Programs.runPage(props.Env.Dispatcher, RepositoryHome.program childProps)

  let handle = TrackedHandle(rawHandle) :> IDisposable

  {
    RepositoryHandle = start.Handle
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
      dispatch(
        HistoryReady(
          start.Ref,
          start.Place,
          child.View,
          child.Handle,
          child.Refresh
        )
      )
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

let startThreadCmd
  (props: Props<'env>, old: IDisposable option, start: ThreadStart option)
  : Cmd<Msg> =
  Cmd.ofEffect(fun dispatch ->
    match old with
    | Some handle ->
      handle.Dispose()
      dispatch ThreadStopped
    | None -> ()

    match start with
    | Some start ->
      let child = startThread(props, dispatch, start)
      dispatch(ThreadReady(start.CommitId, child.View, child.Handle))
    | None -> ())

let startRepositoryCmd
  (props: Props<'env>, old: IDisposable option, start: RepositoryStart option)
  : Cmd<Msg> =
  Cmd.ofEffect(fun dispatch ->
    match old with
    | Some handle ->
      handle.Dispose()
      dispatch RepositoryStopped
    | None -> ()

    match start with
    | Some start ->
      let child = startRepository(props, dispatch, start)
      dispatch(RepositoryReady(start.Handle, child.View, child.Handle))
    | None -> ())

let stopHistoryCmd(old: IDisposable option) : Cmd<Msg> =
  Cmd.ofEffect(fun dispatch ->
    match old with
    | Some handle ->
      handle.Dispose()
      dispatch HistoryStopped
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
    Staging = Staging.initial
    ThreadId = None
    Thread = None
    Repository = None
    RepositoryListOpen = false
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
    LiveThread = 0
    LiveRepository = 0
  }

  let historyStart: HistoryStart = {
    Ref = selected
    Place = None
    KeyFile = data.KeyFile
    ViewerId = data.ViewerId
    Pasteis = data.Pasteis
  }

  let refStart =
    match Ref.repository selected with
    | Some handle ->
      startRepositoryCmd(
        props,
        None,
        Some {
          Handle = handle
          KeyFile = data.KeyFile
          ViewerId = data.ViewerId
          Pasteis = data.Pasteis
        }
      )
    | None -> startHistoryCmd(props, None, historyStart)

  model,
  Cmd.batch [
    refStart
    startInspectorCmd(props, None, checkoutStart)
    Cmd.ofMsg QueueRequested
  ]

let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
  match msg with
  | RefSelected ref ->
    let data = sessionData props

    match Ref.repository ref with
    | Some handle ->
      {
        model with
            Selected = ref
            History = None
            FetchedAt = None
            ThreadId = None
            Thread = None
            Repository = None
            RepositoryListOpen = false
      },
      Cmd.batch [
        stopHistoryCmd(model.History |> Option.map _.Handle)
        startThreadCmd(props, model.Thread |> Option.map _.Handle, None)

        startRepositoryCmd(
          props,
          model.Repository |> Option.map _.Handle,
          Some {
            Handle = handle
            KeyFile = data.KeyFile
            ViewerId = data.ViewerId
            Pasteis = data.Pasteis
          }
        )

        stateCmd(props, ref, model.CheckoutId)
      ]
    | None ->
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
            ThreadId = None
            Thread = None
            Repository = None
            RepositoryListOpen = false
      },
      Cmd.batch [
        startHistoryCmd(props, model.History |> Option.map _.Handle, start)
        startThreadCmd(props, model.Thread |> Option.map _.Handle, None)

        startRepositoryCmd(props, model.Repository |> Option.map _.Handle, None)

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
  | OpenThread commitId ->
    let data = sessionData props

    let checkoutStart: InspectorStart = {
      CommitId = commitId
      KeyFile = data.KeyFile
      ViewerId = data.ViewerId
      Pasteis = data.Pasteis
    }

    {
      model with
          CheckoutId = Some commitId
          ThreadId = Some commitId
          Thread = None
          Inspector = None
    },
    Cmd.batch [
      startThreadCmd(
        props,
        model.Thread |> Option.map _.Handle,
        threadStartOf(props, commitId)
      )

      startInspectorCmd(
        props,
        model.Inspector |> Option.map _.Handle,
        Some checkoutStart
      )

      stateCmd(props, model.Selected, Some commitId)
    ]
  | ThreadClosed ->
    {
      model with
          ThreadId = None
          Thread = None
    },
    startThreadCmd(props, model.Thread |> Option.map _.Handle, None)
  | StagingToggled ->
    {
      model with
          StagingOpen = not model.StagingOpen
    },
    Cmd.none
  | DraftChanged text ->
    {
      model with
          Staging = { model.Staging with Draft = text }
    },
    Cmd.none
  | StagingPosted _ ->
    {
      model with
          Staging = {
            model.Staging with
                Draft = ""
                Quote = None
          }
    },
    Cmd.ofAction(fun () ->
      model.History |> Option.iter(fun child -> child.Refresh()))
  | StagingScheduled _ ->
    {
      model with
          Staging = {
            model.Staging with
                Draft = ""
                Quote = None
          }
    },
    Cmd.ofMsg QueueRequested
  | QueueRequested ->
    {
      model with
          Staging = {
            model.Staging with
                Queue = {
                  model.Staging.Queue with
                      Loading = true
                      Error = None
                }
          }
    },
    Cmd.OfAsync.perform fetchQueue props QueueFetched
  | QueueFetched result ->
    match result with
    | Ok page ->
      {
        model with
            Staging = {
              model.Staging with
                  Queue = {
                    Items = page.Items
                    Total = page.Total
                    Loading = false
                    Error = None
                  }
            }
      },
      Cmd.none
    | Error error ->
      {
        model with
            Staging = {
              model.Staging with
                  Queue = {
                    model.Staging.Queue with
                        Loading = false
                        Error = Some error
                  }
            }
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
  | Quote commit ->
    {
      model with
          StagingOpen = true
          Staging = {
            model.Staging with
                Quote = Some commit
          }
    },
    Cmd.none
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
  | ThreadStopped ->
    ({
      model with
          LiveThread = max 0 (model.LiveThread - 1)
     },
     Cmd.none)
  | RepositoryStopped ->
    ({
      model with
          LiveRepository = max 0 (model.LiveRepository - 1)
     },
     Cmd.none)
  | RepositoryListChanged open' ->
    ({
      model with
          RepositoryListOpen = open' && model.Repository.IsSome
     },
     Cmd.none)
  | HistoryReady(ref, restore, view, handle, refresh) ->
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
                Refresh = refresh
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
  | ThreadReady(commitId, view, handle) ->
    if Some commitId = model.ThreadId then
      model.Thread |> Option.iter(fun current -> current.Handle.Dispose())

      let live =
        if model.Thread.IsSome then
          model.LiveThread
        else
          model.LiveThread + 1

      ({
        model with
            Thread =
              Some {
                CommitId = commitId
                View = view
                Handle = handle
              }
            LiveThread = live
       },
       Cmd.none)
    else
      handle.Dispose()
      model, Cmd.none
  | RepositoryReady(handle, view, childHandle) ->
    if Some handle = Ref.repository model.Selected then
      model.Repository |> Option.iter(fun current -> current.Handle.Dispose())

      let live =
        if model.Repository.IsSome then
          model.LiveRepository
        else
          model.LiveRepository + 1

      ({
        model with
            Repository =
              Some {
                RepositoryHandle = handle
                View = view
                Handle = childHandle
              }
            LiveRepository = live
       },
       Cmd.none)
    else
      childHandle.Dispose()
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
      current.Selected,
      current.SavedCount,
      current.Staging.Queue.Total,
      current.UnreadCount)
    |> Observable.distinctUntilChanged
    |> Observable.map(fun (selected, saved, scheduled, unread) ->
      WorkbenchChrome.refs(
        {
          Rows = WorkbenchChrome.rows(saved, scheduled, unread)
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

  let repositoryListOpen =
    model
    |> Observable.map _.RepositoryListOpen
    |> Observable.distinctUntilChanged

  let hasRight =
    Observable.combineLatest hasCheckout repositoryListOpen
    |> Observable.map(fun (checkout, listing) -> checkout || listing)
    |> Observable.distinctUntilChanged

  let reserved =
    Observable.combineLatest folded hasRight
    |> Observable.map(fun (isFolded, right) ->
      if isFolded || not right then 0.0 else 380.0)

  let threadOpen =
    model |> Observable.map _.Thread.IsSome |> Observable.distinctUntilChanged

  let repositoryOpen =
    model
    |> Observable.map _.Repository.IsSome
    |> Observable.distinctUntilChanged

  let centerCovered =
    Observable.combineLatest threadOpen repositoryOpen
    |> Observable.map(fun (thread, repository) -> thread || repository)
    |> Observable.distinctUntilChanged

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
      .IsVisible(centerCovered |> Observable.map not)
      .Margin(
        reserved |> Observable.map(fun right -> Thickness(0.0, 0.0, right, 0.0))
      )

  let repositoryPlaceholder = Panel()

  let repositorySlice =
    model
    |> Observable.map(fun current ->
      current.Repository |> Option.map _.View.Center)
    |> Observable.distinctUntilChanged
    |> Observable.map(Option.defaultValue repositoryPlaceholder)

  let repositoryVisible =
    Observable.combineLatest repositoryOpen threadOpen
    |> Observable.map(fun (repository, thread) -> repository && not thread)
    |> Observable.distinctUntilChanged

  let repositoryHost =
    ContentControl()
      .Name("RepositoryRegion")
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .VerticalContentAlignment(VerticalAlignment.Stretch)
      .Content(repositorySlice |> _.ToBinding())
      .IsVisible(repositoryVisible)
      .Margin(
        reserved |> Observable.map(fun right -> Thickness(0.0, 0.0, right, 0.0))
      )

  let threadPlaceholder = Panel()
  let threadParentsPlaceholder = Panel()

  let threadSlice =
    model
    |> Observable.map(fun current -> current.Thread |> Option.map _.View.Center)
    |> Observable.distinctUntilChanged
    |> Observable.map(Option.defaultValue threadPlaceholder)

  let threadHost =
    ContentControl()
      .Name("ThreadRegion")
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .VerticalContentAlignment(VerticalAlignment.Stretch)
      .Content(threadSlice |> _.ToBinding())
      .IsVisible(threadOpen)
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

  let inspectorContent =
    ContentControl()
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .VerticalContentAlignment(VerticalAlignment.Stretch)
      .Content(inspectorSlice |> _.ToBinding())

  let threadParentsSlice =
    model
    |> Observable.map(fun current ->
      current.Thread |> Option.map _.View.Parents)
    |> Observable.distinctUntilChanged
    |> Observable.map(Option.defaultValue threadParentsPlaceholder)

  let threadParentsHost =
    ContentControl()
      .Name("ThreadParentsRegion")
      .Content(threadParentsSlice |> _.ToBinding())
      .IsVisible(threadOpen)

  let repositoryListsPlaceholder = Panel()

  let repositoryListsSlice =
    model
    |> Observable.map(fun current ->
      current.Repository |> Option.map _.View.Lists)
    |> Observable.distinctUntilChanged
    |> Observable.map(Option.defaultValue repositoryListsPlaceholder)

  let repositoryListsVisible =
    Observable.combineLatest repositoryVisible repositoryListOpen
    |> Observable.map(fun (repository, listing) -> repository && listing)
    |> Observable.distinctUntilChanged

  let repositoryListsHost =
    ContentControl()
      .Name("RepositoryListsRegion")
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .VerticalContentAlignment(VerticalAlignment.Stretch)
      .Content(repositoryListsSlice |> _.ToBinding())
      .IsVisible(repositoryListsVisible)

  let inspectorSlot = Grid()
  inspectorSlot.RowDefinitions <- RowDefinitions("Auto,*")

  inspectorSlot.Children(
    threadParentsHost.Row(0),
    inspectorContent.Row(1),
    repositoryListsHost.Row(1)
  )
  |> ignore

  let inspectorRegion =
    Border()
      .Name("InspectorRegion")
      .Width(380.0)
      .HorizontalAlignment(HorizontalAlignment.Right)
      .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0, 0.0, 0.0, 0.0))
      .Child(inspectorSlot)
      .IsVisible(hasRight)

  let main = Grid()

  main.Children(historyHost, repositoryHost, threadHost, inspectorRegion)
  |> ignore

  let stagingProps: Staging.Props<'env> = {
    Env = props.Env
    Session =
      match props.Env.Session.Current with
      | SignedIn session -> Some session
      | SignedOut -> None
    OnToggle = fun () -> dispatch StagingToggled
    OnDraftChanged = fun text -> dispatch(DraftChanged text)
    OnPosted = fun commit -> dispatch(StagingPosted commit)
    OnScheduled = fun entry -> dispatch(StagingScheduled entry)
  }

  let staging =
    Staging.view
      stagingProps
      (model
       |> Observable.map(fun current ->
         {
           Open = current.StagingOpen
           Draft = current.Staging.Draft
           Quote = current.Staging.Quote
           Queue = current.Staging.Queue
         }
         : Staging.View))

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

  root.RowDefinitions <- RowDefinitions("*,Auto,Auto")
  root.ColumnDefinitions <- ColumnDefinitions("240,*")

  root.Children(
    refsRegion.Column(0).Row(0),
    main.Column(1).Row(0),
    staging.Row(1).ColumnSpan(2),
    statusBar.Row(2).ColumnSpan(2)
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
