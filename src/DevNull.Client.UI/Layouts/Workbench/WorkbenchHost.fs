module DevNull.Client.Workbench.WorkbenchHost

open System
open System.Reactive.Disposables
open System.Reactive.Subjects
open System.Threading
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Input
open Avalonia.Media
open Avalonia.Threading
open Avalonia.Layout
open Avalonia.VisualTree
open NXUI.Extensions
open Microsoft.Extensions.Logging
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.Search
open DevNull.Client.SettingsStore
open DevNull.Client.Shortcuts
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.TranslationCache
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore
open DevNull.Client.Views
open Navs

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

type NotificationsChild = { View: Control; Handle: IDisposable }

type RemotesChild = {
  Mount: RemotesMount.Mount
  Handle: IDisposable
}

type SearchPaletteChild = { View: Control; Handle: IDisposable }

type SearchChild = {
  Term: string
  View: Control
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
  Notifications: NotificationsChild option
  SearchPalette: SearchPaletteChild option
  Search: SearchChild option
  SearchTerm: string option
  Remotes: RemotesChild option
  ChatTarget: ChatTarget option
  RemoteCount: int
  RemoteSharedSeen: bool
  RemotesOn: bool
  PrefsFrom: RemotesFrom option
  PrefsPull: bool
  Notice: string option
  ShareOpen: Commit option
  ReportOpen: Commit option
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
  LiveNotifications: int
  LiveSearchPalette: int
  LiveSearch: int
  LiveRemotes: int
}

[<NoComparison>]
type Msg =
  | RefSelected of ref: Ref
  | CheckoutSet of commitId: string option
  | ReplyRequested of commitId: string
  | QuoteCleared
  | OpenThread of commitId: string
  | ThreadClosed
  | StagingToggled
  | ComposeRequested
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
  | NotificationsReady of view: Control * handle: IDisposable
  | NotificationsStopped
  | RemotesReady of mount: RemotesMount.Mount * handle: IDisposable
  | RemotesStopped
  | ChatSelected of target: ChatTarget option
  | RemoteCountChanged of count: int
  | RemoteShared of target: ChatTarget option
  | EnableRequested
  | EnableSettled of result: Result<unit, AppError>
  | PrefsFromWanted of from: RemotesFrom
  | PrefsPullWanted
  | PrefsSettled of result: Result<unit, AppError>
  | SearchPaletteToggled
  | EscapeRequested
  | SearchPaletteClosed
  | SearchPaletteReady of view: Control * handle: IDisposable
  | SearchPaletteStopped
  | SearchCommitted of term: string
  | SearchClosed
  | SearchReady of term: string * view: Control * handle: IDisposable
  | SearchStopped
  | DialogRequested of RowActions.Dialog
  | ShareClosed
  | ReportClosed
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
  and 'env :> IProvideLastVisit
  and 'env :> IProvideProfiles
  and 'env :> IProvideRecentSearches
  and 'env :> IProvideRemotes
  and 'env :> IProvideNatives
  and 'env :> IProvideSettings
  and 'env :> IProvideBrowser
  and 'env :> IProvideTranslations
  and 'env :> IProvideUiClock
  and 'env :> IProvideUserSettings
  and 'env :> IProvideLogging> = {
  Env: 'env
  Initial: WorkbenchState
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  OnOpenAuthor: string -> unit
  OnOpenExternal: string -> unit
  OnStateChanged: WorkbenchState -> unit
  OnUseDefaultLayout: unit -> unit
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
type internal MountedHost() =

  let subject = new System.Reactive.Subjects.Subject<Msg>()
  let created = ResizeArray<TrackedHandle>()

  member val CurrentHistory: TrackedHandle option = None with get, set

  member val CurrentInspector: TrackedHandle option = None with get, set

  member val CurrentThread: TrackedHandle option = None with get, set

  member val CurrentRepository: TrackedHandle option = None with get, set

  member val CurrentNotifications: TrackedHandle option = None with get, set

  member val CurrentSearchPalette: TrackedHandle option = None with get, set

  member val CurrentSearch: TrackedHandle option = None with get, set

  member val HostHandle: IDisposable option = None with get, set

  member val Disposed = false with get, set

  member val LiveHistory = 0 with get, set

  member val LiveInspector = 0 with get, set

  member val LiveThread = 0 with get, set

  member val LiveRepository = 0 with get, set

  member val LiveNotifications = 0 with get, set

  member val CurrentRemotes: TrackedHandle option = None with get, set

  member val LiveSearchPalette = 0 with get, set

  member val LiveSearch = 0 with get, set

  member val LiveRemotes = 0 with get, set

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
    this.LiveNotifications <- model.LiveNotifications
    this.LiveRemotes <- model.LiveRemotes
    this.LiveSearchPalette <- model.LiveSearchPalette
    this.LiveSearch <- model.LiveSearch

    this.CurrentHistory <-
      model.History |> Option.bind(fun child -> track child.Handle)

    this.CurrentInspector <-
      model.Inspector |> Option.bind(fun child -> track child.Handle)

    this.CurrentThread <-
      model.Thread |> Option.bind(fun child -> track child.Handle)

    this.CurrentRepository <-
      model.Repository |> Option.bind(fun child -> track child.Handle)

    this.CurrentNotifications <-
      model.Notifications |> Option.bind(fun child -> track child.Handle)

    this.CurrentRemotes <-
      model.Remotes |> Option.bind(fun child -> track child.Handle)

    this.CurrentSearchPalette <-
      model.SearchPalette |> Option.bind(fun child -> track child.Handle)

    this.CurrentSearch <-
      model.Search |> Option.bind(fun child -> track child.Handle)

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
      this.CurrentNotifications |> Option.iter _.Dispose()
      this.CurrentRemotes |> Option.iter _.Dispose()
      this.CurrentSearchPalette |> Option.iter _.Dispose()
      this.CurrentSearch |> Option.iter _.Dispose()
      this.HostHandle |> Option.iter _.Dispose()
      this.LiveHistory <- 0
      this.LiveInspector <- 0
      this.LiveThread <- 0
      this.LiveRepository <- 0
      this.LiveNotifications <- 0
      this.LiveRemotes <- 0
      this.LiveSearchPalette <- 0
      this.LiveSearch <- 0
      this.HostDisposed <- true
      subject.OnCompleted()
      subject.Dispose()

  interface IDisposable with
    member this.Dispose() = this.DisposeCore()

[<Struct>]
type SessionData = {
  Session: SignedInSession option
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
  Session: SignedInSession option
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Handle: string
}

[<Struct>]
type InspectorStart = {
  CommitId: string
  Session: SignedInSession option
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  FocusComposer: bool
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
  Session: SignedInSession option
  KeyFile: string
  ViewerId: string
  Pasteis: bool
}

[<Struct>]
type NotificationsStart = { KeyFile: string }

[<Struct>]
type SearchPaletteStart = { KeyFile: string }

/// The remotes child's start: the signed-in session the mount reads.
[<Struct>]
type RemotesStart = { Session: SignedInSession }

[<Struct>]
type SearchStart = {
  Session: SignedInSession option
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Term: string
}

let sessionOfState(state: SessionState) : SessionData =
  match state with
  | SignedIn session -> {
      Session = Some session
      KeyFile = session.KeyFile
      ViewerId = session.Session.User.Id
      Pasteis = session.Session.PasteisMode
      Handle = session.Session.User.Handle
      Unread = session.Session.UnreadNotificationsCount
    }
  | SignedOut -> {
      Session = None
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

let flyoutOf
  (props: Props<'env>, dispatch: Msg -> unit)
  : DevNull.Client.Components.UserFlyout.Source voption =
  ValueSome {
    Fetch = fun handle -> props.Env.Profiles.Fetch handle
    OnOpen = fun handle -> dispatch(RefSelected(Build.repository handle))
    // The workbench has no organization destination yet.
    OnOpenOrg = ignore
    OnOpenTag = fun tag -> dispatch(SearchCommitted tag)
    Cache = props.Env.ImageCache
  }

let startHistory
  (props: Props<'env>, dispatch: Msg -> unit, start: HistoryStart)
  : HistoryChild option =
  match Ref.source start.Ref with
  | None -> None
  | Some source ->
    let refresh = new System.Reactive.Subjects.Subject<unit>()

    let lastVisit =
      match source, Ref.log start.Ref with
      | LogSource _, Some log ->
        props.Env.LastVisit.LastVisit(start.Handle, LogKinds.wire log)
      | _ -> None

    let childProps: HistoryColumn.Props<'env> = {
      Env = props.Env
      Session = start.Session
      Source = source
      KeyFile = start.KeyFile
      ViewerId = start.ViewerId
      Pasteis = start.Pasteis
      Images = props.Images
      Flyout = flyoutOf(props, dispatch)
      Now = props.Now
      Refresh = refresh :> IObservable<unit>
      Zone = props.Zone
      LastVisit = lastVisit
      OnOpenExternal = fun url -> dispatch(OpenExternal url)
      OnOpened = fun commit -> dispatch(CheckoutSet(Some commit.Id))
      OnReply = fun commit -> dispatch(ReplyRequested commit.Id)
      OnQuote = fun commit -> dispatch(Quote commit)
      Restore = start.Place
      OnReport = fun report -> dispatch(HistoryReported(start.Ref, report))
      OnDialog = DialogRequested >> dispatch
    }

    let view, rawHandle =
      Programs.runPage(props.Env.Dispatcher, HistoryColumn.program childProps)

    let handle = new TrackedHandle(rawHandle) :> IDisposable

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
    Session = start.Session
    CommitId = start.CommitId
    KeyFile = start.KeyFile
    ViewerId = start.ViewerId
    Pasteis = start.Pasteis
    Images = props.Images
    Flyout = flyoutOf(props, dispatch)
    Now = props.Now
    Zone = props.Zone
    OnCheckoutParent = fun commitId -> dispatch(CheckoutSet(Some commitId))
    OnCheckoutChild = fun commitId -> dispatch(CheckoutSet(Some commitId))
    OnOpenAuthor = fun handle -> dispatch(OpenAuthor handle)
    OnOpenExternal = fun url -> dispatch(OpenExternal url)
    OnQuote = fun commit -> dispatch(Quote commit)
    FocusComposer = start.FocusComposer
    OnClose = fun () -> dispatch(CheckoutSet None)
    OnDialog = DialogRequested >> dispatch
  }

  let view, rawHandle =
    Programs.runPage(props.Env.Dispatcher, Inspector.program childProps)

  let handle = new TrackedHandle(rawHandle) :> IDisposable

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
    Flyout = flyoutOf(props, dispatch)
    Now = props.Now
    Zone = props.Zone
    OnBack = fun () -> dispatch ThreadClosed
    OnOpenThread = fun commit -> dispatch(OpenThread commit.Id)
    OnOpenExternal = fun url -> dispatch(OpenExternal url)
    OnQuote = fun commit -> dispatch(Quote commit)
    OnDialog = DialogRequested >> dispatch
  }

  let view, rawHandle =
    Programs.runPage(props.Env.Dispatcher, ThreadMode.program childProps)

  let handle = new TrackedHandle(rawHandle) :> IDisposable

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
    Session = start.Session
    Handle = start.Handle
    KeyFile = start.KeyFile
    ViewerId = start.ViewerId
    Pasteis = start.Pasteis
    Images = props.Images
    Flyout = flyoutOf(props, dispatch)
    Now = props.Now
    Zone = props.Zone
    OnOpened = fun commit -> dispatch(CheckoutSet(Some commit.Id))
    OnReply = fun commit -> dispatch(ReplyRequested commit.Id)
    OnQuote = fun commit -> dispatch(Quote commit)
    OnOpenExternal = fun url -> dispatch(OpenExternal url)
    OnOpenRepository =
      fun handle -> dispatch(RefSelected(Build.repository handle))
    OnListChanged = fun open' -> dispatch(RepositoryListChanged open')
    OnDialog = DialogRequested >> dispatch
  }

  let view, rawHandle =
    Programs.runPage(props.Env.Dispatcher, RepositoryHome.program childProps)

  let handle = new TrackedHandle(rawHandle) :> IDisposable

  {
    RepositoryHandle = start.Handle
    View = view
    Handle = handle
  }

let startNotifications
  (props: Props<'env>, dispatch: Msg -> unit, start: NotificationsStart)
  : NotificationsChild =
  let childProps: NotificationsCenter.Props<'env> = {
    Env = props.Env
    KeyFile = start.KeyFile
    Images = props.Images
    Now = props.Now
    OnCheckout = fun commitId -> dispatch(CheckoutSet(Some commitId))
  }

  let view, rawHandle =
    Programs.runPage(
      props.Env.Dispatcher,
      NotificationsCenter.program childProps
    )

  {
    View = view
    Handle = new TrackedHandle(rawHandle) :> IDisposable
  }

let searchSession
  (props: Props<'env>, session: SignedInSession, limit: int option)
  =
  let log = props.Env.LoggerFactory.CreateLogger "DevNull.Client.SearchDialog"

  new Search.Session(
    (fun term ->
      Search.profiles(
        props.Env,
        {
          KeyFile = session.KeyFile
          Term = term
        }
      )),
    (fun term ->
      Search.commits(
        props.Env,
        {
          KeyFile = session.KeyFile
          Term = term
          Limit = limit
        }
      )),
    log
  )

let navFor(props: Props<'env>, dispatch: Msg -> unit) =
  let log = props.Env.LoggerFactory.CreateLogger "DevNull.Client.Workbench.Nav"
  let ok = Task.FromResult(Ok Unchecked.defaultof<NavigationResult>)

  {
    new INavigable with
      member _.Navigate(url: string) =
        fun (_: CancellationToken) -> task {
          let after(prefix: string) =
            Uri.UnescapeDataString(url.Substring prefix.Length)

          if url.StartsWith("/profile/", StringComparison.Ordinal) then
            dispatch(RefSelected(Build.repository(after "/profile/")))
          elif url.StartsWith("/commit/", StringComparison.Ordinal) then
            dispatch(OpenThread(after "/commit/"))
          elif url.StartsWith("/search/", StringComparison.Ordinal) then
            dispatch(SearchCommitted(after "/search/"))
          else
            log.LogWarning("The workbench has no route for {Url}.", url)

          return Ok Unchecked.defaultof<NavigationResult>
        }

      member _.NavigateByName(_name: string, _parameters: RouteParams) =
        fun (_: CancellationToken) -> ok

      member _.Back() = fun (_: CancellationToken) -> ok

      member _.Forward() = fun (_: CancellationToken) -> ok
  }

let startSearchPalette
  (props: Props<'env>, dispatch: Msg -> unit, start: SearchPaletteStart)
  : SearchPaletteChild =
  let view, handle =
    match sessionData props with
    | { Session = Some session } ->
      SearchDialog.mount(
        props.Env.Dispatcher,
        {
          Env = props.Env
          SignedIn = session
          Searches = searchSession(props, session, Some 5)
          Recents = (props.Env :> IProvideRecentSearches).RecentSearches
          Nav = navFor(props, dispatch)
          InitialTerm = None
          OpenCompose = fun () -> dispatch ComposeRequested
          Close = fun () -> dispatch SearchPaletteClosed
        }
      )
    | _ -> Panel() :> Control, Disposable.Empty

  {
    View = view
    Handle = new TrackedHandle(handle) :> IDisposable
  }

let startSearch
  (props: Props<'env>, dispatch: Msg -> unit, start: SearchStart)
  : SearchChild =
  let view, handle =
    match start.Session with
    | Some session ->
      SearchDialog.mount(
        props.Env.Dispatcher,
        {
          Env = props.Env
          SignedIn = session
          Searches = searchSession(props, session, None)
          Recents = (props.Env :> IProvideRecentSearches).RecentSearches
          Nav = navFor(props, dispatch)
          InitialTerm = Some start.Term
          OpenCompose = fun () -> dispatch ComposeRequested
          Close = fun () -> dispatch SearchClosed
        }
      )
    | None -> Panel() :> Control, Disposable.Empty

  {
    Term = start.Term
    View = view
    Handle = new TrackedHandle(handle) :> IDisposable
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

let startNotificationsCmd
  (props: Props<'env>, old: IDisposable option, start: NotificationsStart option) : Cmd<
                                                                                      Msg
                                                                                     >
  =
  Cmd.ofEffect(fun dispatch ->
    match old with
    | Some handle ->
      handle.Dispose()
      dispatch NotificationsStopped
    | None -> ()

    match start with
    | Some start ->
      let child = startNotifications(props, dispatch, start)
      dispatch(NotificationsReady(child.View, child.Handle))
    | None -> ())

let startRemotes
  (props: Props<'env>, dispatch: Msg -> unit, start: RemotesStart)
  : RemotesChild =
  let mount =
    RemotesMount.mount(
      props.Env.Dispatcher,
      {
        Env = props.Env
        Session = start.Session
        OnOpenProfile =
          fun handle -> dispatch(RefSelected(Build.repository handle))
        OnTargetChanged = fun target -> dispatch(ChatSelected target)
      }
    )

  {
    Mount = mount
    Handle = new TrackedHandle(mount.Lifetime) :> IDisposable
  }

let startRemotesCmd
  (props: Props<'env>, old: IDisposable option, start: RemotesStart option)
  : Cmd<Msg> =
  Cmd.ofEffect(fun dispatch ->
    match old with
    | Some handle ->
      handle.Dispose()
      dispatch RemotesStopped
    | None -> ()

    match start with
    | Some start ->
      let child = startRemotes(props, dispatch, start)
      dispatch(RemotesReady(child.Mount, child.Handle))
    | None -> ())

// The remotes child's start under one ref: the signed-in session while
// the account has remotes on, or none.
let remotesStartFor(data: SessionData, selected: Ref) : RemotesStart option =
  if Ref.remotes selected then
    data.Session
    |> Option.filter(fun session -> session.Session.Remotes.IsSome)
    |> Option.map(fun session -> { Session = session })
  else
    None

let startSearchPaletteCmd
  (props: Props<'env>, old: IDisposable option, start: SearchPaletteStart option) : Cmd<
                                                                                      Msg
                                                                                     >
  =
  Cmd.ofEffect(fun dispatch ->
    match old with
    | Some handle ->
      handle.Dispose()
      dispatch SearchPaletteStopped
    | None -> ()

    match start with
    | Some start ->
      let child = startSearchPalette(props, dispatch, start)
      dispatch(SearchPaletteReady(child.View, child.Handle))
    | None -> ())

let startSearchCmd
  (props: Props<'env>, old: IDisposable option, start: SearchStart option)
  : Cmd<Msg> =
  Cmd.ofEffect(fun dispatch ->
    match old with
    | Some handle ->
      handle.Dispose()
      dispatch SearchStopped
    | None -> ()

    match start with
    | Some start ->
      let child = startSearch(props, dispatch, start)
      dispatch(SearchReady(start.Term, child.View, child.Handle))
    | None -> ())

let stateCmd
  (props: Props<'env>, ref: Ref, checkoutId: string option)
  : Cmd<Msg> =
  Cmd.ofAction(fun () -> props.OnStateChanged(Build.state(ref, checkoutId)))

let init(props: Props<'env>) : Model * Cmd<Msg> =
  let data = sessionData props
  let selected = WorkbenchState.refOf props.Initial
  let checkoutId = WorkbenchState.checkoutId props.Initial

  let checkoutStart: InspectorStart option =
    checkoutId
    |> Option.map(fun commitId -> {
      CommitId = commitId
      Session = data.Session
      KeyFile = data.KeyFile
      ViewerId = data.ViewerId
      Pasteis = data.Pasteis
      FocusComposer = false
    })

  let remotesOn =
    data.Session
    |> Option.map(fun session -> session.Session.Remotes.IsSome)
    |> Option.defaultValue false

  let prefsFrom =
    data.Session
    |> Option.bind(fun session -> session.Session.Remotes |> Option.map _.From)

  let prefsPull =
    data.Session
    |> Option.bind(fun session ->
      session.Session.Remotes |> Option.map _.PullReceipts)
    |> Option.defaultValue false

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
    Notifications = None
    Remotes = None
    ChatTarget = None
    RemoteCount = 0
    RemoteSharedSeen = false
    RemotesOn = remotesOn
    PrefsFrom = prefsFrom
    PrefsPull = prefsPull
    Notice = None
    SearchPalette = None
    Search = None
    SearchTerm = None
    ShareOpen = None
    ReportOpen = None
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
    LiveNotifications = 0
    LiveRemotes = 0
    LiveSearchPalette = 0
    LiveSearch = 0
  }

  let historyStart: HistoryStart = {
    Ref = selected
    Place = None
    Session = data.Session
    KeyFile = data.KeyFile
    ViewerId = data.ViewerId
    Pasteis = data.Pasteis
    Handle = data.Handle
  }

  let refStart =
    match Ref.repository selected with
    | Some handle ->
      startRepositoryCmd(
        props,
        None,
        Some {
          Handle = handle
          Session = data.Session
          KeyFile = data.KeyFile
          ViewerId = data.ViewerId
          Pasteis = data.Pasteis
        }
      )
    | None when Ref.notifications selected ->
      startNotificationsCmd(props, None, Some { KeyFile = data.KeyFile })
    | None when Ref.remotes selected ->
      startRemotesCmd(props, None, remotesStartFor(data, selected))
    | None -> startHistoryCmd(props, None, historyStart)

  model,
  Cmd.batch [
    refStart
    startInspectorCmd(props, None, checkoutStart)
    Cmd.ofMsg QueueRequested
  ]

let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
  let checkout(commitId: string option, focus: bool) : Model * Cmd<Msg> =
    let data = sessionData props

    let start: InspectorStart option =
      commitId
      |> Option.map(fun id -> {
        CommitId = id
        Session = data.Session
        KeyFile = data.KeyFile
        ViewerId = data.ViewerId
        Pasteis = data.Pasteis
        FocusComposer = focus
      })

    {
      model with
          CheckoutId = commitId
          Inspector = None
          ChatTarget = None
    },
    Cmd.batch [
      startInspectorCmd(props, model.Inspector |> Option.map _.Handle, start)

      model.Remotes
      |> Option.map(fun child ->
        Cmd.ofAction(fun () -> child.Mount.Send State.Closed))
      |> Option.defaultValue Cmd.none

      stateCmd(props, model.Selected, commitId)
    ]

  match msg with
  | RefSelected ref ->
    let data = sessionData props

    let cleared = {
      model with
          Selected = ref
          History = None
          FetchedAt = None
          ThreadId = None
          Thread = None
          Repository = None
          RepositoryListOpen = false
          Notifications = None
          Remotes = None
          ChatTarget = None
          SearchPalette = None
          Search = None
          SearchTerm = None
          ShareOpen = None
          ReportOpen = None
    }

    let common = [
      startThreadCmd(props, model.Thread |> Option.map _.Handle, None)

      startSearchPaletteCmd(
        props,
        model.SearchPalette |> Option.map _.Handle,
        None
      )

      startSearchCmd(props, model.Search |> Option.map _.Handle, None)
      startRemotesCmd(
        props,
        model.Remotes |> Option.map _.Handle,
        remotesStartFor(data, ref)
      )
      stateCmd(props, ref, model.CheckoutId)
    ]

    match Ref.repository ref with
    | Some handle ->
      cleared,
      Cmd.batch [
        yield! common
        stopHistoryCmd(model.History |> Option.map _.Handle)
        startNotificationsCmd(
          props,
          model.Notifications |> Option.map _.Handle,
          None
        )

        startRepositoryCmd(
          props,
          model.Repository |> Option.map _.Handle,
          Some {
            Handle = handle
            Session = data.Session
            KeyFile = data.KeyFile
            ViewerId = data.ViewerId
            Pasteis = data.Pasteis
          }
        )
      ]
    | None when Ref.notifications ref ->
      cleared,
      Cmd.batch [
        yield! common
        stopHistoryCmd(model.History |> Option.map _.Handle)
        startRepositoryCmd(props, model.Repository |> Option.map _.Handle, None)
        startNotificationsCmd(
          props,
          model.Notifications |> Option.map _.Handle,
          Some { KeyFile = data.KeyFile }
        )
      ]
    | None when Ref.remotes ref ->
      cleared,
      Cmd.batch [
        yield! common
        stopHistoryCmd(model.History |> Option.map _.Handle)
        startRepositoryCmd(props, model.Repository |> Option.map _.Handle, None)
        startNotificationsCmd(
          props,
          model.Notifications |> Option.map _.Handle,
          None
        )
      ]
    | None ->
      let start: HistoryStart = {
        Ref = ref
        Place = model.Places |> Map.tryFind ref
        Session = data.Session
        KeyFile = data.KeyFile
        ViewerId = data.ViewerId
        Pasteis = data.Pasteis
        Handle = data.Handle
      }

      cleared,
      Cmd.batch [
        yield! common
        startHistoryCmd(props, model.History |> Option.map _.Handle, start)
        startRepositoryCmd(props, model.Repository |> Option.map _.Handle, None)
        startNotificationsCmd(
          props,
          model.Notifications |> Option.map _.Handle,
          None
        )
      ]
  | CheckoutSet commitId when commitId = model.CheckoutId -> model, Cmd.none
  | CheckoutSet commitId -> checkout(commitId, false)
  | ReplyRequested commitId -> checkout(Some commitId, true)
  | OpenThread commitId ->
    let data = sessionData props

    let checkoutStart: InspectorStart = {
      CommitId = commitId
      Session = data.Session
      KeyFile = data.KeyFile
      ViewerId = data.ViewerId
      Pasteis = data.Pasteis
      FocusComposer = false
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
  | ComposeRequested -> { model with StagingOpen = true }, Cmd.none
  | DraftChanged text ->
    if text = model.Staging.Draft then
      model, Cmd.none
    else
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
  | WidthChanged width ->
    let folded = width < 1100.0

    if folded = model.Folded then
      model, Cmd.none
    else
      { model with Folded = folded }, Cmd.none
  | ClockTicked moment -> { model with Now = moment }, Cmd.none
  | SessionChanged state ->
    let data = sessionOfState state

    let on =
      data.Session
      |> Option.map(fun session -> session.Session.Remotes.IsSome)
      |> Option.defaultValue false

    let from =
      data.Session
      |> Option.bind(fun session ->
        session.Session.Remotes |> Option.map _.From)

    let pull =
      data.Session
      |> Option.bind(fun session ->
        session.Session.Remotes |> Option.map _.PullReceipts)
      |> Option.defaultValue false

    let switched = {
      model with
          Handle = data.Handle
          UnreadCount = data.Unread
          Pasteis = data.Pasteis
          RemotesOn = on
          PrefsFrom = from
          PrefsPull = pull
    }

    if (data.Handle, on) = (model.Handle, model.RemotesOn) then
      switched, Cmd.none
    else
      let cleared = {
        switched with
            Remotes = None
            ChatTarget = None
      }

      cleared,
      startRemotesCmd(
        props,
        model.Remotes |> Option.map _.Handle,
        remotesStartFor(data, model.Selected)
      )
  | HistoryReported(ref, report) ->
    match model.Places |> Map.tryFind ref with
    | Some place when place = report.Place -> model, Cmd.none
    | _ ->
      let places = model.Places |> Map.add ref report.Place

      let fetchedAt =
        if ref = model.Selected then
          report.FetchedAt |> Option.orElse model.FetchedAt
        else
          model.FetchedAt

      {
        model with
            Places = places
            FetchedAt = fetchedAt
      },
      Cmd.none
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
  | QuoteCleared ->
    {
      model with
          Staging = { model.Staging with Quote = None }
    },
    Cmd.none
  | HistoryStopped ->
    {
      model with
          LiveHistory = max 0 (model.LiveHistory - 1)
    },
    Cmd.none
  | InspectorStopped ->
    {
      model with
          LiveInspector = max 0 (model.LiveInspector - 1)
    },
    Cmd.none
  | ThreadStopped ->
    {
      model with
          LiveThread = max 0 (model.LiveThread - 1)
    },
    Cmd.none
  | RepositoryStopped ->
    {
      model with
          LiveRepository = max 0 (model.LiveRepository - 1)
    },
    Cmd.none
  | RepositoryListChanged open' ->
    let listed = open' && model.Repository.IsSome

    if listed = model.RepositoryListOpen then
      model, Cmd.none
    else
      {
        model with
            RepositoryListOpen = listed
      },
      Cmd.none
  | NotificationsStopped ->
    {
      model with
          LiveNotifications = max 0 (model.LiveNotifications - 1)
    },
    Cmd.none
  | NotificationsReady(view, childHandle) ->
    if Ref.notifications model.Selected then
      model.Notifications
      |> Option.iter(fun current -> current.Handle.Dispose())

      let live =
        if model.Notifications.IsSome then
          model.LiveNotifications
        else
          model.LiveNotifications + 1

      {
        model with
            Notifications = Some { View = view; Handle = childHandle }
            LiveNotifications = live
      },
      Cmd.none
    else
      childHandle.Dispose()
      model, Cmd.none
  | RemotesStopped ->
    {
      model with
          LiveRemotes = max 0 (model.LiveRemotes - 1)
    },
    Cmd.none
  | RemotesReady(mount, childHandle) ->
    if Ref.remotes model.Selected then
      model.Remotes |> Option.iter(fun current -> current.Handle.Dispose())

      let live =
        if model.Remotes.IsSome then
          model.LiveRemotes
        else
          model.LiveRemotes + 1

      {
        model with
            Remotes = Some { Mount = mount; Handle = childHandle }
            LiveRemotes = live
      },
      Cmd.none
    else
      childHandle.Dispose()
      model, Cmd.none
  | ChatSelected target ->
    if Ref.remotes model.Selected then
      match target with
      | Some _ ->
        {
          model with
              ChatTarget = target
              CheckoutId = None
              Inspector = None
        },
        Cmd.batch [
          startInspectorCmd(props, model.Inspector |> Option.map _.Handle, None)
          stateCmd(props, model.Selected, None)
        ]
      | None -> { model with ChatTarget = None }, Cmd.none
    else
      model, Cmd.none
  | RemoteCountChanged count -> { model with RemoteCount = count }, Cmd.none
  | RemoteShared target ->
    if model.RemoteSharedSeen then
      match target with
      | Some _ when not(Ref.remotes model.Selected) ->
        model, Cmd.ofMsg(RefSelected Build.remotes)
      | _ -> model, Cmd.none
    else
      { model with RemoteSharedSeen = true }, Cmd.none
  | EnableRequested ->
    { model with Notice = None },
    Cmd.OfAsync.perform props.Env.Remotes.SetEnabled true EnableSettled
  | EnableSettled result ->
    match result with
    | Ok() -> model, Cmd.none
    | Error error ->
      {
        model with
            Notice = Some(Errors.describe error)
      },
      Cmd.none
  | PrefsFromWanted from ->
    {
      model with
          PrefsFrom = Some from
          Notice = None
    },
    Cmd.OfAsync.perform
      props.Env.Remotes.SetPreferences
      (from, model.PrefsPull)
      PrefsSettled
  | PrefsPullWanted ->
    let pull = not model.PrefsPull

    match model.PrefsFrom with
    | Some from ->
      {
        model with
            PrefsPull = pull
            Notice = None
      },
      Cmd.OfAsync.perform
        props.Env.Remotes.SetPreferences
        (from, pull)
        PrefsSettled
    | None -> model, Cmd.none
  | PrefsSettled result ->
    match result with
    | Ok() -> model, Cmd.none
    | Error error ->
      {
        model with
            Notice = Some(Errors.describe error)
      },
      Cmd.none
  | EscapeRequested ->
    if model.SearchPalette.IsSome then
      model, Cmd.ofMsg SearchPaletteClosed
    elif model.Search.IsSome then
      model, Cmd.ofMsg SearchClosed
    else
      model, Cmd.none
  | SearchPaletteToggled ->
    let data = sessionData props

    match model.SearchPalette with
    | Some _ ->
      { model with SearchPalette = None },
      startSearchPaletteCmd(
        props,
        model.SearchPalette |> Option.map _.Handle,
        None
      )
    | None ->
      model, startSearchPaletteCmd(props, None, Some { KeyFile = data.KeyFile })
  | SearchPaletteClosed ->
    { model with SearchPalette = None },
    startSearchPaletteCmd(
      props,
      model.SearchPalette |> Option.map _.Handle,
      None
    )
  | SearchPaletteStopped ->
    {
      model with
          LiveSearchPalette = max 0 (model.LiveSearchPalette - 1)
    },
    Cmd.none
  | SearchPaletteReady(view, childHandle) ->
    model.SearchPalette |> Option.iter(fun current -> current.Handle.Dispose())

    let live =
      if model.SearchPalette.IsSome then
        model.LiveSearchPalette
      else
        model.LiveSearchPalette + 1

    {
      model with
          SearchPalette = Some { View = view; Handle = childHandle }
          LiveSearchPalette = live
    },
    Cmd.none
  | SearchCommitted term ->
    let data = sessionData props

    {
      model with
          History = None
          ThreadId = None
          Thread = None
          SearchPalette = None
          Search = None
          SearchTerm = Some term
    },
    Cmd.batch [
      startThreadCmd(props, model.Thread |> Option.map _.Handle, None)

      startSearchPaletteCmd(
        props,
        model.SearchPalette |> Option.map _.Handle,
        None
      )

      stopHistoryCmd(model.History |> Option.map _.Handle)

      startSearchCmd(
        props,
        model.Search |> Option.map _.Handle,
        Some {
          Session = data.Session
          KeyFile = data.KeyFile
          ViewerId = data.ViewerId
          Pasteis = data.Pasteis
          Term = term
        }
      )
    ]
  | DialogRequested(RowActions.Share commit) ->
    {
      model with
          ShareOpen = Some commit
          ReportOpen = None
    },
    Cmd.none
  | DialogRequested(RowActions.Report commit) ->
    {
      model with
          ReportOpen = Some commit
          ShareOpen = None
    },
    Cmd.none
  | ShareClosed -> { model with ShareOpen = None }, Cmd.none
  | ReportClosed -> { model with ReportOpen = None }, Cmd.none
  | SearchStopped ->
    {
      model with
          LiveSearch = max 0 (model.LiveSearch - 1)
    },
    Cmd.none
  | SearchReady(term, view, childHandle) ->
    if model.SearchTerm = Some term then
      model.Search |> Option.iter(fun current -> current.Handle.Dispose())

      let live =
        if model.Search.IsSome then
          model.LiveSearch
        else
          model.LiveSearch + 1

      {
        model with
            Search =
              Some {
                Term = term
                View = view
                Handle = childHandle
              }
            LiveSearch = live
      },
      Cmd.none
    else
      childHandle.Dispose()
      model, Cmd.none
  | SearchClosed ->
    let data = sessionData props

    let restart =
      match Ref.log model.Selected with
      | Some _ when model.History.IsNone ->
        startHistoryCmd(
          props,
          None,
          {
            Ref = model.Selected
            Place = model.Places |> Map.tryFind model.Selected
            Session = data.Session
            KeyFile = data.KeyFile
            ViewerId = data.ViewerId
            Pasteis = data.Pasteis
            Handle = data.Handle
          }
        )
      | _ -> Cmd.none

    {
      model with
          Search = None
          SearchTerm = None
          ShareOpen = None
          ReportOpen = None
    },
    Cmd.batch [
      startSearchCmd(props, model.Search |> Option.map _.Handle, None)
      restart
    ]
  | HistoryReady(ref, restore, view, handle, refresh) ->
    if ref = model.Selected then
      model.History |> Option.iter(fun current -> current.Handle.Dispose())

      let live =
        if model.History.IsSome then
          model.LiveHistory
        else
          model.LiveHistory + 1

      {
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
      Cmd.none
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

      {
        model with
            Inspector =
              Some {
                CommitId = commitId
                View = view
                Handle = handle
              }
            LiveInspector = live
      },
      Cmd.none
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

      {
        model with
            Thread =
              Some {
                CommitId = commitId
                View = view
                Handle = handle
              }
            LiveThread = live
      },
      Cmd.none
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

      {
        model with
            Repository =
              Some {
                RepositoryHandle = handle
                View = view
                Handle = childHandle
              }
            LiveRepository = live
      },
      Cmd.none
    else
      childHandle.Dispose()
      model, Cmd.none

let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
  [
    Sub.fromObservable "clock" (props.Now |> Observable.map ClockTicked)

    Sub.fromObservable
      "session"
      (props.Env.Session.Observe |> Observable.map SessionChanged)

    Sub.fromObservable
      "remotes"
      (props.Env.Remotes.Observe
       |> Observable.map(fun state -> state.Unread |> Option.defaultValue 0)
       |> Observable.distinctUntilChanged
       |> Observable.map RemoteCountChanged)

    Sub.fromObservable
      "remote-target"
      (props.Env.Remotes.Observe
       |> Observable.map _.OpenTarget
       |> Observable.distinctUntilChangedKey(Option.map ChatTargets.key)
       |> Observable.map RemoteShared)
  ]
  |> Sub.batch

let isTextEntry(source: obj) : bool =
  match source with
  | :? TextBox -> true
  | :? Control as control ->
    control.GetVisualAncestors()
    |> Seq.exists(function
      | :? TextBox -> true
      | _ -> false)
  | _ -> false

let historyPlaceholder(selected: Ref) : Control =
  let words =
    match Ref.source selected with
    | Some _ -> "Loading the history…"
    | None -> "Loading…"

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
      current.UnreadCount,
      current.RemotesOn,
      current.RemoteCount)
    |> Observable.distinctUntilChanged
    |> Observable.map
      (fun (selected, saved, scheduled, unread, on, remoteCount) ->
        WorkbenchChrome.refs(
          {
            Rows =
              WorkbenchChrome.rows(
                saved,
                scheduled,
                unread,
                if on then Some remoteCount else None
              )
            Selected = selected
          },
          (fun ref -> dispatch(RefSelected ref)),
          (fun () -> props.OnUseDefaultLayout())
        ))

  let refsHost = ContentControl().Content(refsSlice |> _.ToBinding())

  let searchEntry =
    Button()
      .Name("SearchEntry")
      .Classes(Theme.name Theme.GhostButton)
      .Content("SEARCH")
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Margin(Thickness(12.0, 12.0, 12.0, 6.0))
      .OnClickHandler(fun _ _ -> dispatch SearchPaletteToggled)

  let refsColumn =
    Grid(RowDefinitions = RowDefinitions "Auto,*")
      .Children(
        searchEntry.Row(0),
        ScrollViewer()
          .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
          .Content(refsHost)
          .Row(1)
      )

  let refsRegion =
    Border()
      .Name("RefsRegion")
      .Width(240.0)
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 1.0, 0.0))
      .Child(refsColumn)

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
    |> Slices.share

  let chatOpen =
    model
    |> Observable.map _.ChatTarget.IsSome
    |> Observable.distinctUntilChanged
    |> Slices.share

  let checkoutOrChat =
    Observable.combineLatest hasCheckout chatOpen
    |> Observable.map(fun (checkout, chat) -> checkout || chat)

  let hasRight =
    Observable.combineLatest checkoutOrChat repositoryListOpen
    |> Observable.map(fun (left, listing) -> left || listing)
    |> Observable.distinctUntilChanged
    |> Slices.share

  let reserved =
    Observable.combineLatest folded hasRight
    |> Observable.map(fun (isFolded, right) ->
      if isFolded || not right then 0.0 else 380.0)
    |> Observable.distinctUntilChanged
    |> Slices.share

  let threadOpen =
    model
    |> Observable.map _.Thread.IsSome
    |> Observable.distinctUntilChanged
    |> Slices.share

  let repositoryOpen =
    model
    |> Observable.map _.Repository.IsSome
    |> Observable.distinctUntilChanged
    |> Slices.share

  let notificationsOpen =
    model
    |> Observable.map _.Notifications.IsSome
    |> Observable.distinctUntilChanged
    |> Slices.share

  let searchOpen =
    model
    |> Observable.map _.Search.IsSome
    |> Observable.distinctUntilChanged
    |> Slices.share

  let remotesOpen =
    model
    |> Observable.map _.Remotes.IsSome
    |> Observable.distinctUntilChanged
    |> Slices.share

  let remotesBusy =
    Observable.combineLatest threadOpen searchOpen
    |> Observable.map(fun (thread, search) -> thread || search)

  let remotesVisible =
    Observable.combineLatest remotesBusy remotesOpen
    |> Observable.map(fun (busy, remotes) -> remotes && not busy)
    |> Observable.distinctUntilChanged

  let paletteOpen =
    model
    |> Observable.map _.SearchPalette.IsSome
    |> Observable.distinctUntilChanged

  let coveredThreadRepo =
    Observable.combineLatest threadOpen repositoryOpen
    |> Observable.map(fun (thread, repository) -> thread || repository)
    |> Slices.share

  let coveredThreadRepoNotifications =
    Observable.combineLatest coveredThreadRepo notificationsOpen
    |> Observable.map(fun (covered, notifications) -> covered || notifications)

  let coveredThreadRepoNotificationsRemotes =
    Observable.combineLatest coveredThreadRepoNotifications remotesVisible
    |> Observable.map(fun (covered, remotes) -> covered || remotes)

  let centerCovered =
    Observable.combineLatest coveredThreadRepoNotificationsRemotes searchOpen
    |> Observable.map(fun (covered, search) -> covered || search)
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

  let repositoryBusy =
    Observable.combineLatest threadOpen searchOpen
    |> Observable.map(fun (thread, search) -> thread || search)
    |> Slices.share

  let repositoryVisible =
    Observable.combineLatest repositoryBusy repositoryOpen
    |> Observable.map(fun (busy, repository) -> repository && not busy)
    |> Observable.distinctUntilChanged
    |> Slices.share

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

  let notificationsPlaceholder = Panel()

  let notificationsSlice =
    model
    |> Observable.map(fun current -> current.Notifications |> Option.map _.View)
    |> Observable.distinctUntilChanged
    |> Observable.map(Option.defaultValue notificationsPlaceholder)

  let notificationsBusy =
    Observable.combineLatest threadOpen searchOpen
    |> Observable.map(fun (thread, search) -> thread || search)

  let notificationsVisible =
    Observable.combineLatest notificationsBusy notificationsOpen
    |> Observable.map(fun (busy, notifications) -> notifications && not busy)
    |> Observable.distinctUntilChanged

  let remotesSelected =
    model
    |> Observable.map(fun current -> Ref.remotes current.Selected)
    |> Observable.distinctUntilChanged
    |> Slices.share

  let notificationsHost =
    ContentControl()
      .Name("NotificationsRegion")
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .VerticalContentAlignment(VerticalAlignment.Stretch)
      .Content(notificationsSlice |> _.ToBinding())
      .IsVisible(notificationsVisible)
      .Margin(
        reserved |> Observable.map(fun right -> Thickness(0.0, 0.0, right, 0.0))
      )

  let remotesOn =
    model
    |> Observable.map _.RemotesOn
    |> Observable.distinctUntilChanged
    |> Slices.share

  let remotesPanel =
    RemotesMount.disabledPanel(
      (fun () -> dispatch EnableRequested),
      (fun () -> props.OnUseDefaultLayout())
    )

  let remotesInboxPlaceholder = Panel()

  let remotesSlice =
    model
    |> Observable.map(fun current ->
      current.RemotesOn,
      current.Remotes |> Option.map(fun child -> child.Mount.Inbox))
    |> Observable.distinctUntilChanged
    |> Observable.map(function
      | true, Some inbox -> inbox
      | true, None -> remotesInboxPlaceholder
      | false, _ -> remotesPanel)

  let prefsFrom =
    model
    |> Observable.map _.PrefsFrom
    |> Observable.distinctUntilChanged
    |> Slices.share

  let prefsPull =
    model
    |> Observable.map _.PrefsPull
    |> Observable.distinctUntilChanged
    |> Slices.share

  let fromPill(label: string, from: RemotesFrom) : Control =
    let pill =
      Button()
        .Classes(Theme.name Theme.FilterPill)
        .Content(label)
        .OnClickHandler(fun _ _ -> dispatch(PrefsFromWanted from))

    Theme.bindClass(
      pill,
      Theme.FilterPillActive,
      prefsFrom |> Observable.map(fun current -> current = Some from)
    )
    |> ignore

    pill

  let pullPill: Control =
    let pill =
      Button()
        .Classes(Theme.name Theme.FilterPill)
        .Content("Pull receipts")
        .OnClickHandler(fun _ _ -> dispatch PrefsPullWanted)

    Theme.bindClass(pill, Theme.FilterPillActive, prefsPull) |> ignore

    pill

  let prefsRow =
    Border()
      .Name("RemotesPrefs")
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Padding(Thickness(12.0, 8.0, 12.0, 8.0))
      .Child(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(8.0)
          .Children(
            TextBlock()
              .Classes(Theme.name Theme.Muted)
              .Text("Who can write:")
              .VerticalAlignment(VerticalAlignment.Center),
            fromPill("Following", FromFollowing),
            fromPill("Everyone", FromEveryone),
            pullPill
          )
      )
      .IsVisible(
        Observable.combineLatest remotesSelected remotesOn
        |> Observable.map(fun (selected, on) -> selected && on)
      )

  let remotesHost =
    ContentControl()
      .Name("RemotesInboxHost")
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .VerticalContentAlignment(VerticalAlignment.Stretch)
      .Content(remotesSlice |> _.ToBinding())

  let remotesCenter =
    Grid(RowDefinitions = RowDefinitions "Auto,*")
      .Name("RemotesRegion")
      .Children(prefsRow.Row(0), remotesHost.Row(1))
      .IsVisible(remotesSelected)
      .Margin(
        reserved
        |> Observable.map(fun right ->
          Thickness(0.0, Theme.bandHeight, right, 0.0))
      )

  let searchPlaceholder = Panel()

  let searchSlice =
    model
    |> Observable.map(fun current -> current.Search |> Option.map _.View)
    |> Observable.distinctUntilChanged
    |> Observable.map(Option.defaultValue searchPlaceholder)

  let searchVisible =
    Observable.combineLatest searchOpen threadOpen
    |> Observable.map(fun (search, thread) -> search && not thread)
    |> Observable.distinctUntilChanged

  let searchHost =
    ContentControl()
      .Name("SearchRegion")
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .VerticalContentAlignment(VerticalAlignment.Stretch)
      .Content(searchSlice |> _.ToBinding())
      .IsVisible(searchVisible)
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

  let noChat = chatOpen |> Observable.map not

  let chatPlaceholder = Panel()

  let chatSlice =
    model
    |> Observable.map(fun current ->
      current.Remotes |> Option.map(fun child -> child.Mount.Thread))
    |> Observable.distinctUntilChanged
    |> Observable.map(Option.defaultValue chatPlaceholder)

  let chatHost =
    ContentControl()
      .Name("RemotesThreadHost")
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .VerticalContentAlignment(VerticalAlignment.Stretch)
      .Content(chatSlice |> _.ToBinding())
      .IsVisible(chatOpen)
      .Margin(Thickness(8.0, Theme.bandHeight + 8.0, 8.0, 8.0))

  let inspectorContent =
    ContentControl()
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .VerticalContentAlignment(VerticalAlignment.Stretch)
      .Content(inspectorSlice |> _.ToBinding())
      .IsVisible(noChat)

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
    |> Observable.combineLatest noChat
    |> Observable.map(fun (visible, free) -> visible && free)
    |> Observable.distinctUntilChanged

  let repositoryListsHost =
    ContentControl()
      .Name("RepositoryListsRegion")
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .VerticalContentAlignment(VerticalAlignment.Stretch)
      .Content(repositoryListsSlice |> _.ToBinding())
      .IsVisible(repositoryListsVisible)

  let inspectorSlot =
    Grid(RowDefinitions = RowDefinitions "Auto,*")
      .Children(
        threadParentsHost.Row(0),
        inspectorContent.Row(1),
        repositoryListsHost.Row(1),
        chatHost.Row(1)
      )

  let inspectorRegion =
    Border()
      .Name("InspectorRegion")
      .Width(380.0)
      .HorizontalAlignment(HorizontalAlignment.Right)
      .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0, 0.0, 0.0, 0.0))
      .Child(inspectorSlot)
      .IsVisible(hasRight)

  let palettePlaceholder = Panel()

  let paletteSlice =
    model
    |> Observable.map(fun current -> current.SearchPalette |> Option.map _.View)
    |> Observable.distinctUntilChanged
    |> Observable.map(Option.defaultValue palettePlaceholder)

  let paletteHost =
    ContentControl()
      .Name("SearchPaletteRegion")
      .HorizontalAlignment(HorizontalAlignment.Left)
      .VerticalAlignment(VerticalAlignment.Top)
      .Margin(Thickness(16.0))
      .Content(paletteSlice |> _.ToBinding())
      .IsVisible(paletteOpen)

  let main =
    Grid()
      .Children(
        historyHost,
        notificationsHost,
        repositoryHost,
        searchHost,
        threadHost,
        remotesCenter,
        inspectorRegion,
        paletteHost
      )

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
    OnClearQuote = fun () -> dispatch QuoteCleared
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
         : Staging.View)
       |> Slices.share)

  let statusLine =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .TextWrapping(TextWrapping.NoWrap)
      .TextTrimming(TextTrimming.CharacterEllipsis)
      .Text(
        model
        |> Observable.distinctUntilChangedKey(fun current ->
          current.Selected,
          current.Handle,
          current.FetchedAt,
          minuteBucket current.Now,
          current.Notice)
        |> Observable.map(fun current ->
          WorkbenchChrome.statusText(
            {
              Ref = current.Selected
              Handle = current.Handle
              FetchedAt = current.FetchedAt
              Now = current.Now
            }
            : WorkbenchChrome.StatusModel
          )
          + (current.Notice
             |> Option.map(fun note -> " · " + note)
             |> Option.defaultValue ""))
      )

  let statusBar =
    Border()
      .Name("StatusBar")
      .Classes(name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
      .Padding(Thickness(16.0, 8.0, 16.0, 8.0))
      .Child(statusLine)

  let dialogLayer
    (name: string)
    (openState: IObservable<Commit option>)
    (build: SignedInSession -> Commit -> Control)
    =
    Border()
      .Name(name)
      .Classes(Theme.name Theme.ScrimBack)
      .IsVisible(openState |> Observable.map Option.isSome |> _.ToBinding())
      .Child(
        ContentControl()
          .HorizontalAlignment(HorizontalAlignment.Center)
          .VerticalAlignment(VerticalAlignment.Center)
          .Content(
            openState
            |> Observable.distinctUntilChanged
            |> Observable.map(function
              | Some commit ->
                match sessionData props with
                | { Session = Some session } -> build session commit
                | _ -> Panel() :> Control
              | None -> Panel() :> Control)
            |> _.ToBinding()
          )
      )

  let shareOpen = model |> Observable.map _.ShareOpen |> Slices.share

  let reportOpen = model |> Observable.map _.ReportOpen |> Slices.share

  let shareLayer =
    dialogLayer "ShareDialogLayer" shareOpen (fun session commit ->
      ShareDialog.Page {
        Env = props.Env
        Session = session
        Commit = commit
        Sent = fun _ -> dispatch ShareClosed
        Close = fun () -> dispatch ShareClosed
      }
      :> Control)

  let reportLayer =
    dialogLayer "ReportDialogLayer" reportOpen (fun session commit ->
      ReportDialog.Page {
        Env = props.Env
        Session = session
        Commit = commit
        Sent = fun () -> dispatch ReportClosed
        Close = fun () -> dispatch ReportClosed
      }
      :> Control)

  let root =
    Grid(
      RowDefinitions = RowDefinitions "*,Auto,Auto",
      ColumnDefinitions = ColumnDefinitions "240,*"
    )
      .Name("WorkbenchRoot")
      .OnKeyDownHandler(fun _ args ->
        if args.Key = Key.Escape then
          args.Handled <- true
          dispatch EscapeRequested)
      .Children(
        refsRegion.Column(0).Row(0),
        main.Column(1).Row(0),
        staging.Row(1).ColumnSpan(2),
        statusBar.Row(2).ColumnSpan(2),
        shareLayer.RowSpan(3).ColumnSpan(2),
        reportLayer.RowSpan(3).ColumnSpan(2)
      )

  root.SizeChanged.Add(fun args -> dispatch(WidthChanged args.NewSize.Width))

  root

let program(props: Props<'env>) =
  Program.mkProgram
    (fun () -> init props)
    (fun msg model -> update(props, msg, model))
    (view props)
  |> Program.withSubscription(subscribe props)

let mount(dispatcher: IDispatcher, props: Props<'env>) : Control * IDisposable =
  let mounted = new MountedHost()

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
