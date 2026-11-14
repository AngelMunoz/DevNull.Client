module DevNull.Client.Editor.EditorRoot

open System
open System.Reactive.Disposables
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open IcedTasks
open Microsoft.Extensions.Logging
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Editor
open DevNull.Client.AttachmentSources
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.PageChrome
open DevNull.Client.Components.ReplyPicker
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.SettingsStore
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
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
  and 'env :> IProvideImageCache
  and 'env :> IProvideDispatcher
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideBrowser
  and 'env :> IProvideRecentSearches
  and 'env :> IProvideSettings
  and 'env :> IProvideNatives
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Stored: StoredState
  Lane: Lane option
  Zone: Zone
  Saved: IObservable<int>
  Active: IObservable<bool>
  Now: unit -> DateTimeOffset
  /// The host's switch back to the default layout.
  OnUseDefaultLayout: unit -> unit
}

type ThreadEntry = { View: Control; Lifetime: IDisposable }

type ComposerEvent =
  | Posted of Commit
  | Scheduled of ScheduledCommit

[<Struct>]
type TerminalRequest =
  | FreshTerm
  | QuoteTerm of Commit
  | ReplyTerm of Commit * ParticipantRow list

type Runtime = {
  History: Control
  SwitchRef: LogKind * EditorPlace -> unit
  OpenThread: string -> unit
  CloseThread: string -> unit
  CloseList: string -> unit
  Threads: IObservable<Map<string, Control>>
  Terminal: Control
  TerminalQuote: IObservable<bool>
  ShowTerminal: TerminalRequest -> unit
  RefreshHistory: unit -> unit
  OpenRepo: string -> unit
  CloseRepo: string -> unit
  Repos: IObservable<Map<string, Control>>
  OpenSearch: string -> unit
  CloseSearch: string -> unit
  Searches: IObservable<Map<string, Control>>
  OpenSaved: unit -> unit
  OpenQueue: unit -> unit
  Lists: IObservable<Map<string, Control>>
  /// One remote chat tab per open conversation.
  OpenRemote: ChatTarget -> unit
  CloseRemote: string -> unit
  Remotes: IObservable<Map<string, Control>>
  /// The inbox program's control (the list while the account is on,
  /// the enable panel while it is off). The panel shows it on its
  /// Remotes tab; the view never moves it between parents.
  RemotesInbox: Control
  /// Closes every remote tab and remounts the inbox body for the
  /// new sign-in state.
  ResetRemotes: SessionState -> unit
  PaletteHost: Control
  OpenPalette: unit -> unit
  ClosePalette: unit -> unit
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
  Panel: EditorPanel.State
  Problems: EditorPanel.ProblemsState
  Output: EditorPanel.OutputLine list
  ThreadParticipants: Map<string, ParticipantRow list>
  PaletteOpen: bool
  UserId: string option
  AccountRemotes: MeRemotes option
}

type Msg =
  | RefSelected of LogKind
  | OpenThread of Commit
  | TabSelected of TabId
  | TabClosed of TabId
  | HistoryChanged of HistoryTab.Snapshot
  | ThreadChanged of string * ThreadTab.Snapshot
  | RemoteOpened of ChatTarget
  | RemoteChanged of string * RemoteThreadTab.Snapshot
  | Scrolled of LogKind * float
  | SessionChanged of SessionState
  | RemotesChanged of count: int
  | SavedChanged of count: int
  | ClockTicked of moment: DateTimeOffset
  | ActivitySelected of ActivityBar.Activity
  | WidthChanged of band: WidthBand
  | PanelToggled of EditorPanel.Tab
  | PanelSelected of EditorPanel.Tab
  | OpenNotifications
  | PanelClosed
  | ProblemsFetched of Result<NotificationPage, AppError>
  | ProblemsMoreRequested
  | ProblemsAppended of Result<NotificationPage, AppError>
  | QueueFetched of Result<ScheduledPage, AppError>
  | FetchObserved of string * Result<unit, string>
  | QuoteRequested of Commit
  | ReplyRequested of Commit
  | ComposerChanged of ComposerEvent
  | OpenRepo of string
  | OpenSearch of string
  | OpenSaved
  | OpenQueue
  | OpenThreadById of string
  | OpenCompose
  | PaletteToggled
  | PaletteClosed

[<Struct>]
type HistorySinks = {
  Snapshot: HistoryTab.Snapshot -> unit
  Scroll: float -> unit
  Open: Commit -> unit
  Reply: Commit -> unit
  Quote: Commit -> unit
  OpenRepo: string -> unit
  Shared: User -> unit
  Fetch: Result<unit, string> -> unit
}

type ProgramArgs<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideRemotes
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideDispatcher
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideBrowser
  and 'env :> IProvideRecentSearches
  and 'env :> IProvideSettings
  and 'env :> IProvideNatives
  and 'env :> IProvideLogging> = {
  Runtime: Runtime
  Props: Props<'env>
  History: IObservable<HistoryTab.Snapshot>
  Scrolls: IObservable<LogKind * float>
  Threads: IObservable<string * ThreadTab.Snapshot>
  RemoteOpens: IObservable<ChatTarget>
  RemoteSnapshots: IObservable<string * RemoteThreadTab.Snapshot>
  Opens: IObservable<Commit>
  Quotes: IObservable<Commit>
  Replies: IObservable<Commit>
  ComposerEvents: IObservable<ComposerEvent>
  Fetches: IObservable<string * Result<unit, string>>
  RepoOpens: IObservable<string>
  PaletteClose: IObservable<unit>
  PaletteRefs: IObservable<LogKind>
  PalettePanels: IObservable<EditorPanel.Tab>
}

type ViewArgs<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideRemotes
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideDispatcher
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideBrowser
  and 'env :> IProvideRecentSearches
  and 'env :> IProvideSettings
  and 'env :> IProvideNatives
  and 'env :> IProvideLogging> = {
  Runtime: Runtime
  Props: Props<'env>
  Model: IObservable<Model>
  Dispatch: Dispatch<Msg>
}

type UpdateArgs<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideRemotes
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideDispatcher
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideBrowser
  and 'env :> IProvideRecentSearches
  and 'env :> IProvideSettings
  and 'env :> IProvideNatives
  and 'env :> IProvideLogging> = { Runtime: Runtime; Props: Props<'env> }

[<Struct>]
type HistoryArgs<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideRemotes
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideDispatcher
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideBrowser
  and 'env :> IProvideRecentSearches
  and 'env :> IProvideSettings
  and 'env :> IProvideNatives
  and 'env :> IProvideLogging> = {
  Props: Props<'env>
  Log: LogKind
  Place: EditorPlace
  Refresh: IObservable<unit>
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
  Refresh = args.Refresh
  OnSnapshot = args.Sinks.Snapshot
  OnScroll = args.Sinks.Scroll
  OnOpen = args.Sinks.Open
  OnReplyRequested = args.Sinks.Reply
  OnQuoteRequested = args.Sinks.Quote
  OnOpenRepo = args.Sinks.OpenRepo
  OnShared = args.Sinks.Shared
  OnFetch = args.Sinks.Fetch
}

[<Struct>]
type ThreadHooks = {
  Snapshot: ThreadTab.Snapshot -> unit
  Fetch: Result<unit, string> -> unit
  Quote: Commit -> unit
  Reply: Commit -> unit
  OpenRepo: string -> unit
  Shared: User -> unit
}

let threadProps
  (props: Props<'env>, commitId: string, hooks: ThreadHooks)
  : ThreadTab.Props<'env> = {
  Env = props.Env
  Session = props.Session
  CommitId = commitId
  Zone = props.Zone
  Now = props.Now
  OnSnapshot = hooks.Snapshot
  OnFetch = hooks.Fetch
  OnQuote = hooks.Quote
  OnReplyRequested = hooks.Reply
  OnOpenRepo = hooks.OpenRepo
  OnShared = hooks.Shared
}

[<Struct>]
type RepoHooks = {
  OpenCommit: Commit -> unit
  OpenReply: Commit -> unit
  OpenQuote: Commit -> unit
  OpenRepo: string -> unit
  Shared: User -> unit
}

let repoProps
  (props: Props<'env>, handle: string, hooks: RepoHooks)
  : RepoTab.Props<'env> = {
  Env = props.Env
  Session = props.Session
  Handle = handle
  Zone = props.Zone
  Now = props.Now
  OnOpenCommit = hooks.OpenCommit
  OnReply = hooks.OpenReply
  OnQuote = hooks.OpenQuote
  OnOpenRepo = hooks.OpenRepo
  OnShared = hooks.Shared
}

let inline remoteTitle(target: ChatTarget) : string =
  match target with
  | Partner partner -> $"{partner.Name} @{partner.Handle}"
  | Org org -> org.Name |> Option.defaultValue "org"

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
  Panel = EditorPanel.closed
  Problems = EditorPanel.emptyProblems
  Output = []
  ThreadParticipants = Map.empty
  PaletteOpen = false
  UserId = Some props.Session.Session.User.Id
  AccountRemotes = props.Session.Session.Remotes
}

let unreadOf(state: SessionState) =
  match state with
  | SignedIn session -> session.Session.UnreadNotificationsCount
  | SignedOut -> 0

let readNotifications
  (props: Props<'env>, cursor: string option)
  : Async<Result<NotificationPage, AppError>> =
  Notifications.list(
    props.Env,
    {
      KeyFile = props.Session.KeyFile
      Filter = None
      Through = None
      Cursor = cursor
    }
  )

let readQueue(props: Props<'env>) : Async<Result<ScheduledPage, AppError>> =
  ScheduledCommits.list(
    props.Env,
    {
      KeyFile = props.Session.KeyFile
      Page = 1
    }
  )

let addOutput(refName: string, result: Result<unit, string>, model: Model) = {
  model with
      Output =
        EditorPanel.appendOutput(
          {
            Ref = refName
            Result =
              match result with
              | Ok() -> "ok"
              | Error message -> $"failed: {message}"
            At = model.Now
          },
          model.Output
        )
}

let openNotificationsPanel
  (args: UpdateArgs<'env>, panel: EditorPanel.State, model: Model)
  =
  let openKind = panel.Open && EditorPanel.isKindTab panel.Tab

  let next = {
    model with
        Panel = panel
        Activity = ActivityBar.Problems
        FlyoutOpen = false
        Problems =
          if openKind then
            EditorPanel.beginProblems model.Problems
          else
            model.Problems
  }

  let cmd =
    if openKind then
      Cmd.OfAsync.perform
        (fun () -> readNotifications(args.Props, None))
        ()
        ProblemsFetched
    else
      Cmd.none

  next, cmd

let openRepo(args: UpdateArgs<'env>, handle: string, model: Model) =
  let tab = EditorTab.repo(TabId.repo handle, handle)

  let next = {
    model with
        Frame = EditorState.openTab(tab, model.Frame)
        Activity = ActivityBar.Account
        FlyoutOpen = false
  }

  next, Cmd.ofAction(fun () -> args.Runtime.OpenRepo handle)

let openSearch(args: UpdateArgs<'env>, term: string, model: Model) =
  let tab = EditorTab.search(TabId.search term, term)

  let next = {
    model with
        Frame = EditorState.openTab(tab, model.Frame)
        FlyoutOpen = false
  }

  next, Cmd.ofAction(fun () -> args.Runtime.OpenSearch term)

let openList(args: UpdateArgs<'env>, kind: ListTab.Kind, model: Model) =
  let id, title, start =
    match kind with
    | ListTab.Saved -> TabId.saved, "SAVED", args.Runtime.OpenSaved
    | ListTab.Queue -> TabId.queue, "QUEUE", args.Runtime.OpenQueue

  let tab = EditorTab.ordinary(id, title)

  let next = {
    model with
        Frame = EditorState.openTab(tab, model.Frame)
        FlyoutOpen = false
  }

  next, Cmd.ofAction start

let update(args: UpdateArgs<'env>, msg: Msg, model: Model) =
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

    next, Cmd.ofAction(fun () -> args.Runtime.SwitchRef(log, place))
  | OpenThreadById commitId ->
    let id = TabId.thread commitId

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
      let short = commitId.Substring(0, min 8 commitId.Length)
      let tab = EditorTab.thread(id, short, 0)

      let next = {
        model with
            Frame = EditorState.openTab(tab, model.Frame)
            FlyoutOpen = false
      }

      next, Cmd.ofAction(fun () -> args.Runtime.OpenThread commitId)
  | OpenCompose ->
    let next = {
      model with
          Panel = EditorPanel.openOn(EditorPanel.Terminal, model.Panel)
          Activity = ActivityBar.Problems
          FlyoutOpen = false
    }

    next, Cmd.ofAction(fun () -> args.Runtime.ShowTerminal FreshTerm)
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

      next, Cmd.ofAction(fun () -> args.Runtime.OpenThread commit.Id)
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

    let closed =
      match target with
      | Some tab when not(EditorTab.isPinned tab) -> true
      | _ -> false

    let next = {
      model with
          Frame = EditorState.closeTab(id, model.Frame)
          ThreadParticipants =
            if closed then
              model.ThreadParticipants |> Map.remove(TabId.key id)
            else
              model.ThreadParticipants
    }

    let cmd =
      if closed && TabId.isList id then
        Cmd.ofAction(fun () -> args.Runtime.CloseList(TabId.key id))
      elif closed && TabId.isRepo id then
        Cmd.ofAction(fun () -> args.Runtime.CloseRepo(TabId.key id))
      elif closed && TabId.isSearch id then
        Cmd.ofAction(fun () -> args.Runtime.CloseSearch(TabId.key id))
      elif closed && TabId.isRemote id then
        Cmd.ofAction(fun () -> args.Runtime.CloseRemote(TabId.key id))
      elif closed then
        Cmd.ofAction(fun () -> args.Runtime.CloseThread(TabId.key id))
      else
        Cmd.none

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

      {
        model with
            Frame = frame
            ThreadParticipants =
              model.ThreadParticipants.Add(commitId, snapshot.Participants)
      },
      Cmd.none
  | RemoteOpened target ->
    match model.AccountRemotes with
    | None -> model, Cmd.none
    | Some _ ->
      let id = TabId.remote target

      let next = {
        model with
            Frame =
              EditorState.openTab(
                EditorTab.remote(id, remoteTitle target),
                model.Frame
              )
            Activity = ActivityBar.Remotes
            FlyoutOpen = false
      }

      next, Cmd.ofAction(fun () -> args.Runtime.OpenRemote target)
  | RemoteChanged(key, snapshot) ->
    let wanted = "remote:" + key

    let target =
      model.Frame
      |> EditorState.tabs
      |> List.tryFind(fun tab -> TabId.key(EditorTab.id tab) = wanted)

    match target with
    | None -> model, Cmd.none
    | Some tab ->
      let id = EditorTab.id tab

      // The tab reports its own title once the first read lands; the
      // frame never clears the dot itself, it only carries the tab's
      // unread count, and selecting the tab marks it seen.
      let frame =
        if snapshot.Loading then
          model.Frame
        else
          model.Frame
          |> EditorState.tabs
          |> List.map(fun current ->
            if EditorTab.id current = id then
              EditorTab.withTitle(snapshot.Title, current)
            else
              current)
          |> fun tabs -> EditorState.withTabs(tabs, model.Frame)

      {
        model with
            Frame = EditorState.setDirty(id, snapshot.Unread, frame)
      },
      Cmd.none
  | Scrolled(log, offset) ->
    let place =
      model.Places |> Map.tryFind log |> Option.defaultValue EditorPlace.empty

    let next = EditorPlace.withScroll(offset, place)

    if next = place then
      model, Cmd.none
    else
      {
        model with
            Places = model.Places |> Map.add log next
      },
      Cmd.none
  | SessionChanged state ->
    let user =
      match state with
      | SignedIn signed -> Some signed.Session.User.Id
      | SignedOut -> None

    let account =
      match state with
      | SignedIn signed -> signed.Session.Remotes
      | SignedOut -> None

    let switched = user <> model.UserId

    let flipped =
      account |> Option.isSome <> (model.AccountRemotes |> Option.isSome)

    // Chats, drafts, and the session in the tab props belong to the
    // old user; the sidebar body belongs to the old account. This is
    // the one path that cleans both up.
    let frame =
      if switched || flipped then
        model.Frame
        |> EditorState.tabs
        |> List.filter(fun tab -> TabId.isRemote(EditorTab.id tab))
        |> List.fold
          (fun frame tab -> EditorState.closeTab(EditorTab.id tab, frame))
          model.Frame
      else
        model.Frame

    let next = {
      model with
          Unread = unreadOf state
          UserId = user
          AccountRemotes = account
          Frame = frame
    }

    if switched || flipped then
      next, Cmd.ofAction(fun () -> args.Runtime.ResetRemotes state)
    else
      next, Cmd.none
  | RemotesChanged count ->
    let next = { model with RemoteCount = count }

    next, Cmd.none
  | SavedChanged count ->
    let next = { model with Saved = count }

    next, Cmd.none
  | ClockTicked moment ->
    let next = { model with Now = moment }

    next, Cmd.none
  | ActivitySelected activity ->
    match activity with
    | ActivityBar.Problems ->
      let panel =
        EditorPanel.toggleNotifications(model.Problems.Items, model.Panel)

      openNotificationsPanel(args, panel, model)
    | ActivityBar.History ->
      let flyout = model.Band <> Full && not model.FlyoutOpen

      let frame =
        if model.Band = Full then
          EditorState.selectTab(TabId.history, model.Frame)
        else
          model.Frame

      let next = {
        model with
            Frame = frame
            Activity = ActivityBar.History
            FlyoutOpen = flyout
            Panel = EditorPanel.close model.Panel
      }

      next, Cmd.none
    | ActivityBar.Account ->
      openRepo(args, args.Props.Session.Session.User.Handle, model)
    | ActivityBar.Remotes ->
      let next = {
        model with
            Activity = ActivityBar.Remotes
            FlyoutOpen = false
            Panel = EditorPanel.openOn(EditorPanel.Remotes, model.Panel)
      }

      next, Cmd.none
  | OpenRepo handle -> openRepo(args, handle, model)
  | OpenSearch term -> openSearch(args, term, model)
  | OpenSaved -> openList(args, ListTab.Saved, model)
  | OpenQueue -> openList(args, ListTab.Queue, model)
  | PaletteToggled ->
    if model.PaletteOpen then
      { model with PaletteOpen = false },
      Cmd.ofAction(fun () -> args.Runtime.ClosePalette())
    else
      {
        model with
            PaletteOpen = true
            FlyoutOpen = false
      },
      Cmd.ofAction(fun () -> args.Runtime.OpenPalette())
  | PaletteClosed ->
    { model with PaletteOpen = false },
    Cmd.ofAction(fun () -> args.Runtime.ClosePalette())
  | PanelToggled tab ->
    let panel = EditorPanel.toggle(tab, model.Panel)
    openNotificationsPanel(args, panel, model)
  | PanelSelected tab ->
    match tab with
    | EditorPanel.Remotes ->
      let next = {
        model with
            Panel = EditorPanel.select(tab, model.Panel)
            Activity = ActivityBar.Remotes
            FlyoutOpen = false
      }

      next, Cmd.none
    | _ ->
      let panel = EditorPanel.select(tab, model.Panel)
      openNotificationsPanel(args, panel, model)
  | OpenNotifications ->
    let panel = EditorPanel.openFirst(model.Problems.Items, model.Panel)
    openNotificationsPanel(args, panel, model)
  | PanelClosed ->
    {
      model with
          Panel = EditorPanel.close model.Panel
          Activity = ActivityBar.History
    },
    Cmd.none
  | ProblemsMoreRequested ->
    match model.Problems.NextCursor with
    | Some cursor when not model.Problems.Loading ->
      let next = {
        model with
            Problems = {
              model.Problems with
                  Loading = true
                  Error = None
            }
      }

      next,
      Cmd.OfAsync.perform
        (fun () -> readNotifications(args.Props, Some cursor))
        ()
        ProblemsAppended
    | _ -> model, Cmd.none
  | ProblemsAppended result ->
    match result with
    | Ok page ->
      let known = model.Problems.Items |> List.map _.Id |> Set.ofList

      let appended =
        page.Page.Items
        |> List.filter(fun notification -> not(known.Contains notification.Id))

      let items = model.Problems.Items @ appended

      let next = {
        model with
            Panel = EditorPanel.reconcile(items, model.Panel)
            Problems = {
              Loading = false
              Error = None
              Items = items
              NextCursor = page.Page.NextCursor
            }
      }

      addOutput("notifications", Ok(), next), Cmd.none
    | Error error ->
      let next = {
        model with
            Problems = {
              model.Problems with
                  Loading = false
                  Error = Some error
            }
      }

      addOutput("notifications", Error(describe error), next), Cmd.none
  | ProblemsFetched result ->
    match result with
    | Ok page ->
      let next = {
        model with
            Panel = EditorPanel.reconcile(page.Page.Items, model.Panel)
            Problems = {
              Loading = false
              Error = None
              Items = page.Page.Items
              NextCursor = page.Page.NextCursor
            }
      }

      addOutput("notifications", Ok(), next), Cmd.none
    | Error error ->
      let next = {
        model with
            Problems = {
              model.Problems with
                  Loading = false
                  Error = Some error
            }
      }

      addOutput("notifications", Error(describe error), next), Cmd.none
  | QueueFetched result ->
    match result with
    | Ok page ->
      addOutput("queue", Ok(), { model with Queue = page.Total }), Cmd.none
    | Error error -> addOutput("queue", Error(describe error), model), Cmd.none
  | FetchObserved(refName, result) ->
    addOutput(refName, result, model), Cmd.none
  | QuoteRequested commit ->
    let next = {
      model with
          Panel = EditorPanel.openOn(EditorPanel.Terminal, model.Panel)
          Activity = ActivityBar.Problems
          FlyoutOpen = false
    }

    next, Cmd.ofAction(fun () -> args.Runtime.ShowTerminal(QuoteTerm commit))
  | ReplyRequested commit ->
    let active = EditorState.activeTab model.Frame

    let participants =
      if TabId.isHistory active then
        []
      else
        model.ThreadParticipants
        |> Map.tryFind(TabId.key active)
        |> Option.defaultValue []

    let next = {
      model with
          Panel = EditorPanel.openOn(EditorPanel.Terminal, model.Panel)
          Activity = ActivityBar.Problems
          FlyoutOpen = false
    }

    next,
    Cmd.ofAction(fun () ->
      args.Runtime.ShowTerminal(ReplyTerm(commit, participants)))
  | ComposerChanged event ->
    match event with
    | Posted _ ->
      let next = {
        model with
            Panel = EditorPanel.close model.Panel
            Activity = ActivityBar.History
      }

      next,
      Cmd.batch [
        Cmd.ofAction(fun () -> args.Runtime.RefreshHistory())
        Cmd.ofAction(fun () -> args.Runtime.ShowTerminal FreshTerm)
      ]
    | Scheduled _ ->
      model, Cmd.OfAsync.perform readQueue args.Props QueueFetched
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

    Sub.fromObservable "repos" (args.RepoOpens |> Observable.map OpenRepo)

    Sub.fromObservable
      "palette-close"
      (args.PaletteClose |> Observable.map(fun () -> PaletteClosed))

    Sub.fromObservable
      "palette-refs"
      (args.PaletteRefs |> Observable.map RefSelected)

    Sub.fromObservable
      "palette-panels"
      (args.PalettePanels |> Observable.map PanelSelected)

    Sub.fromObservable "quotes" (args.Quotes |> Observable.map QuoteRequested)

    Sub.fromObservable "replies" (args.Replies |> Observable.map ReplyRequested)

    Sub.fromObservable
      "composer"
      (args.ComposerEvents |> Observable.map ComposerChanged)

    Sub.fromObservable
      "fetches"
      (args.Fetches
       |> Observable.map(fun (refName, result) ->
         FetchObserved(refName, result)))

    Sub.fromObservable
      "session"
      (args.Props.Env.Session.Observe |> Observable.map SessionChanged)

    Sub.fromObservable
      "remotes"
      (args.Props.Env.Remotes.Observe
       |> Observable.map(fun state ->
         RemotesChanged(state.Unread |> Option.defaultValue 0)))

    Sub.fromObservable "saved" (args.Props.Saved |> Observable.map SavedChanged)

    Sub.fromObservable
      "clock"
      (args.Props.Env.UiClock.Minute |> Observable.map ClockTicked)

    Sub.fromObservable
      "remote-opens"
      (args.RemoteOpens |> Observable.map RemoteOpened)

    Sub.fromObservable
      "remote-snaps"
      (args.RemoteSnapshots
       |> Observable.map(fun (key, snapshot) -> RemoteChanged(key, snapshot)))
  ]
  |> Sub.batch

let view(args: ViewArgs<'env>) : Control =
  let model = args.Model
  let dispatch = args.Dispatch
  let props = args.Props

  let inline bandIndex(band: WidthBand) =
    match band with
    | Full -> 0
    | Folded -> 1
    | Trimmed -> 2

  let inline activityIndex(activity: ActivityBar.Activity) =
    match activity with
    | ActivityBar.History -> 0
    | ActivityBar.Problems -> 1
    | ActivityBar.Remotes -> 2
    | ActivityBar.Account -> 3

  let inline tabKey tab =
    TabId.key(EditorTab.id tab)
    + "|"
    + EditorTab.title tab
    + "|"
    + string(EditorTab.dirty tab)
    + "|"
    + string(EditorTab.repliesCount tab)

  let activity =
    rebuildOn(
      model,
      (fun current ->
        activityIndex current.Activity, current.Unread, current.RemoteCount),
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
        LogKinds.wire(EditorState.selectedRef current.Frame),
        current.Saved,
        current.Queue),
      (fun current ->
        Explorer.view {
          Selected = EditorState.selectedRef current.Frame
          Saved = current.Saved
          Queue = current.Queue
          QueueHint = None
          Viewer = props.Session.Session.User.Handle
          OnSelectRef = fun log -> dispatch(RefSelected log)
          OnOpenSaved = fun () -> dispatch OpenSaved
          OnOpenQueue = fun () -> dispatch OpenQueue
          OnOpenViewer =
            fun () -> dispatch(OpenRepo props.Session.Session.User.Handle)
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
        current.Frame
        |> EditorState.tabs
        |> List.map tabKey
        |> String.concat ";",
        TabId.key(EditorState.activeTab current.Frame),
        bandIndex current.Band),
      (fun current ->
        TabStrip.view {
          Tabs = EditorState.tabs current.Frame
          Active = EditorState.activeTab current.Frame
          Band = current.Band
          OnSelect = fun id -> dispatch(TabSelected id)
          OnClose = fun id -> dispatch(TabClosed id)
        })
    )

  let search =
    Button()
      .Name("EditorSearch")
      .Classes(Theme.name Theme.NavItem)
      .Width(36.0)
      .Height(32.0)
      .Padding(Thickness(6.0))
      .Content(iconAt(Glyph.Search, 16.0))
      .OnClickHandler(fun _ _ -> dispatch PaletteToggled)

  ToolTip.SetTip(search, "Search")

  let tabStripHost = ContentControl().Content(tabStrip |> _.ToBinding())

  let header = Grid()
  header.ColumnDefinitions <- ColumnDefinitions("*,Auto")
  header.Children(tabStripHost.Column(0), search.Column(1)) |> ignore

  let tabArea = ContentControl().Content(header)

  let loading =
    TextBlock()
      .Text("Opening the thread…")
      .Classes(Theme.name Theme.FgMuted)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Center)

  // One combine per line, each named and shared: no deep inline nest,
  // no cold re-subscription per emission.
  let frame =
    model |> Observable.map _.Frame |> Slices.gateByRef |> Slices.share

  let threads = args.Runtime.Threads
  let repos = args.Runtime.Repos
  let lists = args.Runtime.Lists
  let searches = args.Runtime.Searches
  let remotes = args.Runtime.Remotes

  let frameThreads = Observable.combineLatest frame threads |> Slices.share
  let withRepos = Observable.combineLatest frameThreads repos |> Slices.share
  let withLists = Observable.combineLatest withRepos lists |> Slices.share
  let withSearches = Observable.combineLatest withLists searches |> Slices.share

  let activeContent =
    Observable.combineLatest withSearches remotes
    |> Observable.map
      (fun (((((frame, threads), repos), lists), searches), remotes) ->
        let id = EditorState.activeTab frame

        if id = TabId.history then
          args.Runtime.History
        elif TabId.isList id then
          lists |> Map.tryFind(TabId.key id) |> Option.defaultValue loading
        elif TabId.isRepo id then
          repos |> Map.tryFind(TabId.key id) |> Option.defaultValue loading
        elif TabId.isSearch id then
          searches |> Map.tryFind(TabId.key id) |> Option.defaultValue loading
        elif TabId.isRemote id then
          remotes |> Map.tryFind(TabId.key id) |> Option.defaultValue loading
        else
          threads |> Map.tryFind(TabId.key id) |> Option.defaultValue loading)
    |> Observable.distinctUntilChanged
    |> Slices.share

  let content = ContentControl().Content(activeContent |> _.ToBinding())

  let contentArea = Grid(RowDefinitions = RowDefinitions "*,Auto")

  let panelHeight =
    Observable.combineLatest
      (contentArea.GetObservable Visual.BoundsProperty
       |> Observable.map _.Height)
      args.Runtime.TerminalQuote
    |> Observable.map(fun (total, quoting) ->
      EditorPanel.heightOf(total, quoting))
    |> Observable.distinctUntilChanged
    |> Slices.share

  let panel =
    EditorPanel
      .view(
        {
          State = model |> Observable.map _.Panel
          Problems = model |> Observable.map _.Problems
          Output = model |> Observable.map _.Output |> Slices.gateByRef
          Terminal = args.Runtime.Terminal
          Remotes = args.Runtime.RemotesInbox
          Now = model |> Observable.map _.Now |> Slices.share
          OnSelect = fun tab -> dispatch(PanelSelected tab)
          OnClose = fun () -> dispatch PanelClosed
          OnOpenCommit = fun commit -> dispatch(OpenThread commit)
          OnLoadMore = fun () -> dispatch ProblemsMoreRequested
          Height = panelHeight
        }
      )
      .RowSpan(2)

  let spacer =
    Border()
      .Height(panelHeight)
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Panel.Open && current.Band = Full)
      )

  let dim =
    Border()
      .RowSpan(2)
      .Name("EditorPanelDim")
      .Classes(Theme.name Theme.ScrimBack)
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Panel.Open && current.Band <> Full)
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

  let paletteOverlay =
    Border()
      .Name("PaletteOverlay")
      .Classes(Theme.name Theme.ScrimBack)
      .IsVisible(model |> Observable.map(fun current -> current.PaletteOpen))
      .Child
      args.Runtime.PaletteHost

  contentArea.Children(content.Row(0), spacer.Row(1), flyout, dim, panel)
  |> ignore

  let editor =
    Grid(RowDefinitions = RowDefinitions "Auto,*")
      .Children(
        tabArea.Row(0),
        contentArea.Row(1),
        paletteOverlay.Row(0).RowSpan(2)
      )

  let status =
    rebuildOn(
      model,
      (fun current ->
        LogKinds.wire(EditorState.selectedRef current.Frame),
        current.History.FetchedAt
        |> Option.map _.Ticks
        |> Option.defaultValue 0L,
        current.History.NewCount,
        minuteBucket current.Now,
        bandIndex current.Band),
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

  let defaultLayoutButton =
    Button()
      .Name("EditorDefaultLayout")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(Thickness(8.0, 2.0))
      .Content("default layout")

  defaultLayoutButton.Click.Add(fun _ -> props.OnUseDefaultLayout())
  ToolTip.SetTip(defaultLayoutButton, "Use the default layout")

  let statusRow =
    Grid(ColumnDefinitions = ColumnDefinitions("*,Auto"))
      .Children(statusBar.Column(0), defaultLayoutButton.Column(1))


  Grid(
    RowDefinitions = RowDefinitions "*,Auto",
    ColumnDefinitions = ColumnDefinitions "56,Auto,*"
  )
    .Name("EditorRoot")
    .Margin(0.0, Theme.bandHeight, 0.0, 0.0)
    .Children(
      activityControl.Column(0),
      sidebar.Column(1),
      editor.Column(2),
      statusRow.Row(1).ColumnSpan(3)
    )
    .OnSizeChangedHandler(fun sender _ ->
      WidthBand.ofWidth sender.Bounds.Width |> WidthChanged |> dispatch)


type EditorCommand =
  | OpenTerminal
  | OpenPalette
  | Escape
  | OpenProblems

[<Sealed>]
type EditorMount
  (
    root: Control,
    send: EditorCommand -> unit,
    overlay: unit -> bool,
    dispose: unit -> unit
  ) =
  member _.Root = root

  member _.Send(command) = send command

  member _.OverlayOpen() = overlay()

  interface IDisposable with
    member _.Dispose() = dispose()

let private programFor
  (args: ProgramArgs<'env>, external: IObservable<Msg>, onModel: Model -> unit)
  : Program<unit, Model, Msg, Control> =
  Program.mkProgram
    (fun () ->
      let model, cmd =
        initial(args.Props, HistoryTab.State.snapshot HistoryTab.State.initial),
        Cmd.OfAsync.perform readQueue args.Props QueueFetched

      onModel model
      model, cmd)
    (fun msg model ->
      let next, cmd =
        update(
          {
            Runtime = args.Runtime
            Props = args.Props
          },
          msg,
          model
        )

      onModel next
      next, cmd)
    (fun model dispatch ->
      view {
        Runtime = args.Runtime
        Props = args.Props
        Model = model
        Dispatch = dispatch
      })
  |> Program.withSubscription(fun model ->
    Sub.batch [ subscribe(args, model); Sub.fromObservable "mount" external ])

let program(args: ProgramArgs<'env>) : Program<unit, Model, Msg, Control> =
  programFor(args, Observable.empty, ignore)

let mountFor(dispatcher: IDispatcher, props: Props<'env>) : EditorMount =
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
  let repoOpens = new System.Reactive.Subjects.Subject<string>()
  let remoteOpens = new System.Reactive.Subjects.Subject<ChatTarget>()

  let remoteSnapshots =
    new System.Reactive.Subjects.Subject<string * RemoteThreadTab.Snapshot>()

  let paletteClose = new System.Reactive.Subjects.Subject<unit>()
  let paletteRefs = new System.Reactive.Subjects.Subject<LogKind>()
  let palettePanels = new System.Reactive.Subjects.Subject<EditorPanel.Tab>()
  let historyRefresh = new System.Reactive.Subjects.Subject<unit>()
  let quotes = new System.Reactive.Subjects.Subject<Commit>()
  let replies = new System.Reactive.Subjects.Subject<Commit>()

  let composerEvents = new System.Reactive.Subjects.Subject<ComposerEvent>()

  let fetches =
    new System.Reactive.Subjects.ReplaySubject<string * Result<unit, string>>(
      EditorPanel.outputCap
    )

  let entries = new BehaviorSubject<Map<string, ThreadEntry>>(Map.empty)

  let repoEntries = new BehaviorSubject<Map<string, ThreadEntry>>(Map.empty)
  let listEntries = new BehaviorSubject<Map<string, ThreadEntry>>(Map.empty)

  let searchEntries = new BehaviorSubject<Map<string, ThreadEntry>>(Map.empty)

  let remoteEntries = new BehaviorSubject<Map<string, ThreadEntry>>(Map.empty)

  let content = ContentControl()
  let term = ContentControl()
  let terminalQuote = new BehaviorSubject<bool>(false)
  let quoteLifetime = new SerialDisposable()
  let historyLifetime = new SerialDisposable()

  let paletteHost =
    ContentControl()
      .Name("PaletteHost")
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Top)
      .Margin(Thickness(0.0, 48.0, 0.0, 0.0))

  let paletteLifetime = new SerialDisposable()

  let commandPath = new System.Reactive.Subjects.Subject<Msg>()
  let current = new BehaviorSubject<Model option>(None)

  let rec showTerminal(request: TerminalRequest) =
    terminalQuote.OnNext(
      match request with
      | QuoteTerm _ -> true
      | FreshTerm
      | ReplyTerm _ -> false
    )

    let target, quoted, participants =
      match request with
      | FreshTerm -> DevNull.Client.Components.Composer.Fresh, None, ValueNone
      | QuoteTerm commit ->
        DevNull.Client.Components.Composer.Quoting commit.Id,
        Some commit,
        ValueNone
      | ReplyTerm(commit, rows) ->
        DevNull.Client.Components.Composer.ReplyTo commit.Id,
        None,
        ValueSome rows

    let stripFor(verb: string, commit: Commit) : Control =
      DevNull.Client.Components.Composer.targetStrip(
        DevNull.Client.Components.Composer.targetLabel(verb, commit),
        ValueSome(fun () -> showTerminal FreshTerm)
      )

    let strip: Control voption =
      match request with
      | FreshTerm -> ValueNone
      | QuoteTerm commit -> ValueSome(stripFor("Quoting", commit))
      | ReplyTerm(commit, _) -> ValueSome(stripFor("Replying to", commit))

    let composer =
      DevNull.Client.Components.Composer.Page {
        Env = props.Env
        Session = props.Session
        Target = target
        Participants = participants
        OnCommitted = fun commit -> composerEvents.OnNext(Posted commit)
        OnScheduled =
          ValueSome(fun scheduled -> composerEvents.OnNext(Scheduled scheduled))
        OnOpenQueue = ValueNone
        Draft = ValueNone
        OnBodyReady = ValueSome(fun body -> body.Focus() |> ignore)
      }

    let card: Control voption =
      match quoted with
      | Some commit ->
        let view, handle =
          QuoteCard.mount(
            dispatcher,
            {
              Env = props.Env
              Session = props.Session
              Zone = props.Zone
              Commit = commit
              Now = props.Now
              OnOpenThread = fun target -> opens.OnNext target
              OnReply = fun target -> replies.OnNext target
              OnQuote = fun target -> quotes.OnNext target
              OnOpenRepo = fun handle -> repoOpens.OnNext handle
              OnShared = fun user -> remoteOpens.OnNext(Partner user)
            }
          )

        quoteLifetime.Disposable <- handle
        ValueSome view
      | None ->
        quoteLifetime.Disposable <- Disposable.Empty
        ValueNone

    let scroller =
      ScrollViewer()
        .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
        .VerticalScrollBarVisibility(ScrollBarVisibility.Auto)
        .Content(
          match card with
          | ValueSome quoted -> quoted
          | ValueNone -> Panel() :> Control
        )
        .Row(0)

    let bottom: Control array = [|
      match strip with
      | ValueSome bar -> bar
      | ValueNone -> ()

      composer
    |]

    let layout =
      Grid(RowDefinitions = RowDefinitions "*,Auto")
        .Children(scroller, StackPanel().Spacing(8.0).Children(bottom).Row(1))


    term.Content <- layout

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
          Refresh = historyRefresh
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
            Reply =
              fun commit ->
                if selected.Value = log then
                  replies.OnNext commit
            Quote =
              fun commit ->
                if selected.Value = log then
                  quotes.OnNext commit
            OpenRepo = fun handle -> repoOpens.OnNext handle
            Shared = fun user -> remoteOpens.OnNext(Partner user)
            Fetch =
              fun result ->
                if selected.Value = log then
                  fetches.OnNext($"history {LogLabel.ofKind log}", result)
          }
        }
      )

    content.Content <- history
    historyLifetime.Disposable <- handle

  show(StoredState.selectedRef props.Stored, EditorPlace.empty)
  showTerminal FreshTerm

  let openPalette() =
    if isNull paletteHost.Content then
      let view, handle =
        Palette.mount(
          dispatcher,
          {
            Env = props.Env
            Session = props.Session
            OnOpenRepo = fun handle -> repoOpens.OnNext handle
            OnOpenCommit =
              fun commitId -> commandPath.OnNext(OpenThreadById commitId)
            OnOpenOrg = ignore
            OnOpenSearch = fun term -> commandPath.OnNext(OpenSearch term)
            OnSelectRef = fun log -> paletteRefs.OnNext log
            OnOpenNotifications = fun () -> commandPath.OnNext OpenNotifications
            OnOpenPanel = fun tab -> palettePanels.OnNext tab
            OnUseDefaultLayout = props.OnUseDefaultLayout
            OnOpenCompose = fun () -> commandPath.OnNext OpenCompose
            OnClose = fun () -> paletteClose.OnNext()
          }
        )

      paletteHost.Content <- view
      paletteLifetime.Disposable <- handle

  let closePalette() =
    paletteLifetime.Disposable <- Disposable.Empty
    paletteHost.Content <- null

  let threads =
    entries
    |> Observable.map(fun map -> map |> Map.map(fun _ entry -> entry.View))
    |> Slices.share

  let repos =
    repoEntries
    |> Observable.map(fun map -> map |> Map.map(fun _ entry -> entry.View))
    |> Slices.share

  let lists =
    listEntries
    |> Observable.map(fun map -> map |> Map.map(fun _ entry -> entry.View))
    |> Slices.share

  let searches =
    searchEntries
    |> Observable.map(fun map -> map |> Map.map(fun _ entry -> entry.View))
    |> Slices.share

  let remotes =
    remoteEntries
    |> Observable.map(fun map -> map |> Map.map(fun _ entry -> entry.View))
    |> Slices.share

  let openList(kind: ListTab.Kind) =
    let id =
      match kind with
      | ListTab.Saved -> TabId.saved
      | ListTab.Queue -> TabId.queue

    let key = TabId.key id

    if not(Map.containsKey key listEntries.Value) then
      let view, lifetime =
        ListTab.mount(
          dispatcher,
          {
            Env = props.Env
            Session = props.Session
            Kind = kind
            Zone = props.Zone
            Now = props.Now
            OnOpenThread = fun commit -> opens.OnNext commit
            OnReply = fun commit -> replies.OnNext commit
            OnQuote = fun commit -> quotes.OnNext commit
            OnOpenRepo = fun handle -> repoOpens.OnNext handle
            OnShared = fun user -> remoteOpens.OnNext(Partner user)
          }
        )

      listEntries.OnNext(
        listEntries.Value.Add(key, { View = view; Lifetime = lifetime })
      )

  let openSaved() = openList ListTab.Saved
  let openQueue() = openList ListTab.Queue

  let closeList(key: string) =
    match Map.tryFind key listEntries.Value with
    | Some entry ->
      listEntries.OnNext(listEntries.Value.Remove key)
      entry.Lifetime.Dispose()
    | None -> ()

  let openThread(commitId: string) =
    if not(Map.containsKey commitId entries.Value) then
      let view, lifetime =
        ThreadTab.mount(
          dispatcher,
          threadProps(
            props,
            commitId,
            {
              Snapshot =
                fun snapshot -> threadSnapshots.OnNext(commitId, snapshot)
              Fetch = fun result -> fetches.OnNext($"thread {commitId}", result)
              Quote = fun commit -> quotes.OnNext commit
              Reply = fun commit -> replies.OnNext commit
              OpenRepo = fun handle -> repoOpens.OnNext handle
              Shared = fun user -> remoteOpens.OnNext(Partner user)
            }
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

  let openRepo(handle: string) =
    let key = TabId.key(TabId.repo handle)

    if not(Map.containsKey key repoEntries.Value) then
      let view, lifetime =
        RepoTab.mount(
          dispatcher,
          repoProps(
            props,
            handle,
            {
              OpenCommit = fun commit -> opens.OnNext commit
              OpenReply = fun commit -> replies.OnNext commit
              OpenQuote = fun commit -> quotes.OnNext commit
              OpenRepo = fun next -> repoOpens.OnNext next
              Shared = fun user -> remoteOpens.OnNext(Partner user)
            }
          )
        )

      repoEntries.OnNext(
        repoEntries.Value.Add(key, { View = view; Lifetime = lifetime })
      )

  let closeRepo(key: string) =
    match Map.tryFind key repoEntries.Value with
    | Some entry ->
      repoEntries.OnNext(repoEntries.Value.Remove key)
      entry.Lifetime.Dispose()
    | None -> ()

  let openSearch(term: string) =
    let key = TabId.key(TabId.search term)

    if not(Map.containsKey key searchEntries.Value) then
      let view, lifetime =
        SearchTab.mount(
          dispatcher,
          {
            Env = props.Env
            Session = props.Session
            Term = term
            Zone = props.Zone
            Now = props.Now
            OnOpenThread = fun commit -> opens.OnNext commit
            OnReply = fun commit -> replies.OnNext commit
            OnQuote = fun commit -> quotes.OnNext commit
            OnOpenRepo = fun handle -> repoOpens.OnNext handle
            OnShared = fun user -> remoteOpens.OnNext(Partner user)
          }
        )

      searchEntries.OnNext(
        searchEntries.Value.Add(key, { View = view; Lifetime = lifetime })
      )

  let closeSearch(key: string) =
    match Map.tryFind key searchEntries.Value with
    | Some entry ->
      searchEntries.OnNext(searchEntries.Value.Remove key)
      entry.Lifetime.Dispose()
    | None -> ()

  let openRemote(target: ChatTarget) =
    let key = TabId.key(TabId.remote target)

    if not(Map.containsKey key remoteEntries.Value) then
      let id = TabId.remote target

      let active =
        Observable.combineLatest
          props.Active
          (current
           |> Observable.map(fun held ->
             held
             |> Option.exists(fun model ->
               EditorState.activeTab model.Frame = id)))
        |> Observable.map(fun (window, mine) -> window && mine)
        |> Observable.distinctUntilChanged

      let view, lifetime =
        RemoteThreadTab.mount(
          dispatcher,
          {
            Env = props.Env
            Session = props.Session
            Target = target
            Zone = props.Zone
            Now = props.Now
            Active = active
            OnSnapshot =
              fun snapshot -> remoteSnapshots.OnNext(snapshot.Key, snapshot)
            OnOpenProfile = fun handle -> repoOpens.OnNext handle
          }
        )

      remoteEntries.OnNext(
        remoteEntries.Value.Add(key, { View = view; Lifetime = lifetime })
      )

  let closeRemote(key: string) =
    match Map.tryFind key remoteEntries.Value with
    | Some entry ->
      remoteEntries.OnNext(remoteEntries.Value.Remove key)
      entry.Lifetime.Dispose()
    | None -> ()

  // One list program for the layout: the panel shows its control on
  // the Remotes tab, so the inbox poll runs once for the layout's life.
  let listLifetime = new SerialDisposable()
  let listRoot = new BehaviorSubject<Control>(Panel())

  let inboxHost =
    ContentControl()
      .Name("EditorRemotesInbox")
      .Content(listRoot |> _.ToBinding())

  let enableRemotes() =
    asyncEx {
      match! props.Env.Remotes.SetEnabled true with
      | Ok() -> ()
      | Error error ->
        let log =
          props.Env.LoggerFactory.CreateLogger
            "DevNull.Client.Editor.EditorRoot"

        log.LogWarning("Turning remotes on failed: {Error}", describe error)
    }
    |> Async.StartImmediate

  let mountListBody(session: SignedInSession option) =
    match session with
    | Some signed when signed.Session.Remotes.IsSome ->
      let list, handle =
        RemoteList.mount(
          dispatcher,
          {
            Env = props.Env
            Session = signed
            Zone = props.Zone
            OnOpen = fun target -> remoteOpens.OnNext target
            OnOpenProfile = fun handle -> repoOpens.OnNext handle
          }
        )

      listLifetime.Disposable <- handle
      listRoot.OnNext list
    | _ ->
      listLifetime.Disposable <- Disposable.Empty

      listRoot.OnNext(
        DevNull.Client.Views.RemotesMount.disabledPanel(
          enableRemotes,
          props.OnUseDefaultLayout
        )
      )

  let resetRemotes(state: SessionState) =
    for KeyValue(key, _) in remoteEntries.Value do
      closeRemote key

    let session =
      match state with
      | SignedIn signed -> Some signed
      | SignedOut -> None

    mountListBody session

  mountListBody(Some props.Session)

  let runtime = {
    History = content
    SwitchRef = show
    OpenThread = openThread
    CloseThread = closeThread
    CloseList = closeList
    Threads = threads
    Terminal = term
    TerminalQuote = terminalQuote
    ShowTerminal = showTerminal
    RefreshHistory = fun () -> historyRefresh.OnNext()
    OpenRepo = openRepo
    CloseRepo = closeRepo
    Repos = repos
    OpenSearch = openSearch
    CloseSearch = closeSearch
    Searches = searches
    OpenSaved = openSaved
    OpenQueue = openQueue
    Lists = lists
    OpenRemote = openRemote
    CloseRemote = closeRemote
    Remotes = remotes
    RemotesInbox = inboxHost
    ResetRemotes = resetRemotes
    PaletteHost = paletteHost
    OpenPalette = openPalette
    ClosePalette = closePalette
  }

  let args: ProgramArgs<'env> = {
    Runtime = runtime
    Props = props
    History = snapshots
    Scrolls = scrolls
    Threads = threadSnapshots
    RemoteOpens = remoteOpens
    RemoteSnapshots = remoteSnapshots
    Opens = opens
    Quotes = quotes
    Replies = replies
    ComposerEvents = composerEvents
    Fetches = fetches
    RepoOpens = repoOpens
    PaletteClose = paletteClose
    PaletteRefs = paletteRefs
    PalettePanels = palettePanels
  }

  let mountedProgram =
    programFor(
      args,
      commandPath :> IObservable<Msg>,
      fun model -> current.OnNext(Some model)
    )

  let root, frameHandle =
    DevNull.Client.Programs.runPage(dispatcher, mountedProgram)

  let threadsCleanup =
    Disposable.Create(fun () ->
      for KeyValue(_, entry) in entries.Value do
        entry.Lifetime.Dispose()

      entries.OnCompleted()
      entries.Dispose())

  let listsCleanup =
    Disposable.Create(fun () ->
      for KeyValue(_, entry) in listEntries.Value do
        entry.Lifetime.Dispose()

      listEntries.OnCompleted()
      listEntries.Dispose())

  let reposCleanup =
    Disposable.Create(fun () ->
      for KeyValue(_, entry) in repoEntries.Value do
        entry.Lifetime.Dispose()

      repoEntries.OnCompleted()
      repoEntries.Dispose())

  let searchesCleanup =
    Disposable.Create(fun () ->
      for KeyValue(_, entry) in searchEntries.Value do
        entry.Lifetime.Dispose()

      searchEntries.OnCompleted()
      searchEntries.Dispose())

  let remotesCleanup =
    Disposable.Create(fun () ->
      for KeyValue(_, entry) in remoteEntries.Value do
        entry.Lifetime.Dispose()

      remoteEntries.OnCompleted()
      remoteEntries.Dispose())

  let listCleanup =
    Disposable.Create(fun () ->
      listRoot.OnCompleted()
      listRoot.Dispose())

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
      remoteOpens.OnCompleted()
      remoteOpens.Dispose()
      remoteSnapshots.OnCompleted()
      remoteSnapshots.Dispose()
      opens.OnCompleted()
      opens.Dispose()
      repoOpens.OnCompleted()
      repoOpens.Dispose()
      paletteClose.OnCompleted()
      paletteClose.Dispose()
      paletteRefs.OnCompleted()
      paletteRefs.Dispose()
      palettePanels.OnCompleted()
      palettePanels.Dispose()
      historyRefresh.OnCompleted()
      historyRefresh.Dispose()
      quotes.OnCompleted()
      quotes.Dispose()
      replies.OnCompleted()
      replies.Dispose()
      composerEvents.OnCompleted()
      composerEvents.Dispose()
      fetches.OnCompleted()
      fetches.Dispose()
      commandPath.OnCompleted()
      commandPath.Dispose()
      current.OnCompleted()
      current.Dispose())

  let handles = new CompositeDisposable()

  handles.Add frameHandle
  handles.Add historyLifetime
  handles.Add quoteLifetime
  handles.Add threadsCleanup
  handles.Add listsCleanup
  handles.Add reposCleanup
  handles.Add searchesCleanup
  handles.Add remotesCleanup
  handles.Add listLifetime
  handles.Add listCleanup
  handles.Add paletteLifetime
  handles.Add subjectCleanup

  let send(command: EditorCommand) =
    match current.Value with
    | Some model ->
      match command with
      | OpenTerminal -> commandPath.OnNext(PanelSelected EditorPanel.Terminal)
      | OpenProblems -> commandPath.OnNext OpenNotifications
      | OpenPalette ->
        if not model.PaletteOpen then
          commandPath.OnNext PaletteToggled
      | Escape ->
        if model.PaletteOpen then
          commandPath.OnNext PaletteClosed
        elif model.Panel.Open then
          commandPath.OnNext PanelClosed
    | None -> ()

  let overlayOpen() =
    current.Value |> Option.exists(fun model -> model.PaletteOpen)

  new EditorMount(
    root,
    send,
    overlayOpen,
    fun () -> (handles :> IDisposable).Dispose()
  )

let mount(dispatcher: IDispatcher, props: Props<'env>) =
  let mounted = mountFor(dispatcher, props)
  mounted.Root, (mounted :> IDisposable)
