module DevNull.Client.Editor.RemoteThreadTab

open System
open System.Collections.Generic
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
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore

[<Struct>]
type Snapshot = {
  Key: string
  Title: string
  Unread: int
  Loading: bool
  Error: AppError option
}

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
  and 'env :> IProvideLogging
  and 'env :> IProvideSettings
  and 'env :> IProvideNatives> = {
  Env: 'env
  Session: SignedInSession
  Target: ChatTarget
  Zone: Zone
  Now: unit -> DateTimeOffset
  Active: IObservable<bool>
  OnSnapshot: Snapshot -> unit
  OnOpenProfile: string -> unit
}

module State =

  type Model = {
    Thread: DevNull.Client.Views.State.ThreadModel
    Live: ThreadState
    Loading: bool
    Error: AppError option
    Active: bool
  }

  let inline keyOf(target: ChatTarget) : string = ChatTargets.key target

  let inline stashedOf(props: Props<'env>, key: string) : string =
    props.Env.Settings.Current.ChatDrafts
    |> Map.tryFind key
    |> Option.filter(fun text -> text <> "")
    |> Option.defaultValue ""

  let titleOf
    (viewerId: string, target: ChatTarget, live: ThreadState)
    : string =
    match target with
    | Partner partner -> $"{partner.Name} @{partner.Handle}"
    | Org org ->
      match org.Name with
      | Some name -> name
      | None ->
        let others = live.Members |> List.filter(fun m -> m.User.Id <> viewerId)

        match others with
        | [] -> "Just you"
        | _ ->
          others
          |> List.map(fun m -> m.User.Name.Split(' ')[0])
          |> String.concat ", "

  let inline unreadOf(viewerId: string, model: Model) : int =
    model.Live.Messages
    |> List.filter(fun m ->
      m.Id > model.Thread.MarkedThrough
      && m.UserId <> viewerId
      && m.Event.IsNone)
    |> List.length

  let snapshot(props: Props<'env>, model: Model) : Snapshot = {
    Key = keyOf props.Target
    Title = titleOf(props.Session.Session.User.Id, props.Target, model.Live)
    Unread = unreadOf(props.Session.Session.User.Id, model)
    Loading = model.Loading
    Error = model.Error
  }

  let inline announce(props: Props<'env>, model: Model) =
    Cmd.ofAction(fun () -> props.OnSnapshot(snapshot(props, model)))

  let inline remotesProps
    (props: Props<'env>)
    : DevNull.Client.Views.Props<'env> = {
    Env = props.Env
    Session = props.Session
    OnOpenProfile = props.OnOpenProfile
  }

  let initial(target: ChatTarget) : Model = {
    Thread = {
      DraftKey = keyOf target
      Draft = ""
      Mention = None
      Images = []
      LinkChoice = LinkCard.FirstLink
      Voice = None
      Recording = false
      OrgName = ""
      OrgMembers = []
      OrgShare = true
      OrgRename = ""
      Notice = None
      PinAsk = None
      Busy = None
      PinnedPanel = false
      Landing = None
      LandingBeat = 0
      Riding = true
      MarkedThrough = 0
      SeenThrough = 0
      Unseen = 0
      Behind = false
      PingHeard = 0
    }
    Live = emptyThread target
    Loading = true
    Error = None
    Active = false
  }

  let init(props: Props<'env>) : Model * Cmd<DevNull.Client.Views.State.Msg> =
    let key = keyOf props.Target
    let baseModel = initial props.Target

    let model = {
      baseModel with
          Thread = {
            baseModel.Thread with
                DraftKey = key
                Draft = stashedOf(props, key)
          }
    }

    model, announce(props, model)

  let update
    (props: Props<'env>, msg: DevNull.Client.Views.State.Msg, model: Model)
    : Model * Cmd<DevNull.Client.Views.State.Msg> =
    let viewerId = props.Session.Session.User.Id
    let key = keyOf props.Target
    let store = props.Env.Remotes

    match msg with
    | DevNull.Client.Views.State.StoreChanged state ->
      let live =
        state.Threads
        |> Map.tryFind key
        |> Option.defaultValue(emptyThread props.Target)

      let loading =
        match model.Loading && live.IsLoaded with
        | true -> false
        | false -> model.Loading

      let reset =
        match key <> model.Thread.DraftKey with
        | true -> {
            model.Thread with
                DraftKey = key
                Draft = stashedOf(props, key)
                Mention = None
                Images = []
                LinkChoice = LinkCard.FirstLink
                Voice = None
                Recording = false
                PinAsk = None
                PinnedPanel = false
          }
        | false -> model.Thread

      let latestSeen =
        live.Messages
        |> List.tryLast
        |> Option.map _.Id
        |> Option.defaultValue 0

      let unseenCount =
        live.Messages
        |> List.filter(fun m ->
          m.Id > reset.SeenThrough && m.UserId <> viewerId && m.Event.IsNone)
        |> List.length

      let seen =
        match reset.Riding with
        | true ->
          match latestSeen > 0 with
          | false -> {
              reset with
                  SeenThrough = 0
                  Unseen = 0
            }
          | true ->
            match model.Active with
            | true -> {
                reset with
                    SeenThrough = latestSeen
                    Unseen = 0
              }
            | false -> { reset with Unseen = unseenCount }
        | false -> { reset with Unseen = unseenCount }

      let latest =
        match live.IsLoaded with
        | true ->
          live.Messages
          |> List.tryLast
          |> Option.map _.Id
          |> Option.defaultValue 0
        | false -> seen.MarkedThrough

      let marked, reading =
        match
          model.Active
          && seen.Riding
          && state.WindowActive
          && live.IsLoaded
          && latest > seen.MarkedThrough
        with
        | true ->
          { seen with MarkedThrough = latest },
          Cmd.OfAsync.perform
            (fun (store: RemotesStore, who: ChatTarget, focused: bool) ->
              store.MarkRead(who, focused))
            (store, props.Target, true)
            DevNull.Client.Views.State.ReadSettled
        | false -> seen, Cmd.none

      let heard =
        live.Messages
        |> List.tryFindBack(fun m -> m.Event |> Option.contains Ping)
        |> Option.map _.Id
        |> Option.defaultValue 0

      let heldBefore = model.Thread.PingHeard <> 0
      let now = DateTimeOffset.UtcNow

      let own =
        live.Messages
        |> List.tryFind(fun m -> m.Id = heard)
        |> Option.map(fun m -> m.UserId = viewerId)
        |> Option.defaultValue false

      let muted =
        live.MutedUntilAt
        |> Option.map(fun until -> until > now)
        |> Option.defaultValue false

      let sound =
        match heldBefore && heard > model.Thread.PingHeard && live.IsLoaded with
        | true ->
          match props.Env.Natives.Sounds with
          | ValueSome head when own || not muted ->
            Cmd.OfFunc.attempt
              (fun (sounds: ISystemSounds) -> sounds.Notify())
              head
              (fun failure ->
                DevNull.Client.Views.State.MenuNotice failure.Message)
          | _ -> Cmd.none
        | false -> Cmd.none

      let thread = { marked with PingHeard = heard }

      let next = {
        model with
            Thread = thread
            Live = live
            Loading = loading
            Error = None
      }

      match
        obj.ReferenceEquals(next.Thread, model.Thread)
        && obj.ReferenceEquals(next.Live, model.Live)
        && next.Loading = model.Loading
      with
      | true when next.Error = model.Error -> model, Cmd.none
      | _ -> next, Cmd.batch [ reading; sound; announce(props, next) ]
    | DevNull.Client.Views.State.Settled active ->
      match active = model.Active with
      | true -> model, Cmd.none
      | false ->
        let next = { model with Active = active }

        let marked, reading =
          match active && next.Thread.Riding && next.Live.IsLoaded with
          | true ->
            let latest =
              next.Live.Messages
              |> List.tryLast
              |> Option.map _.Id
              |> Option.defaultValue 0

            match
              latest > next.Thread.MarkedThrough && store.Current.WindowActive
            with
            | true ->
              {
                next.Thread with
                    MarkedThrough = latest
              },
              Cmd.OfAsync.perform
                (fun (store: RemotesStore, who: ChatTarget, focused: bool) ->
                  store.MarkRead(who, focused))
                (store, props.Target, true)
                DevNull.Client.Views.State.ReadSettled
            | false -> next.Thread, Cmd.none
          | false -> next.Thread, Cmd.none

        let finalModel = { next with Thread = marked }
        finalModel, Cmd.batch [ reading; announce(props, finalModel) ]
    | DevNull.Client.Views.State.ClockTicked _ -> model, Cmd.none
    | DevNull.Client.Views.State.SessionChanged _ -> model, Cmd.none
    | DevNull.Client.Views.State.TabChosen _
    | DevNull.Client.Views.State.Searched _
    | DevNull.Client.Views.State.SearchLanded _
    | DevNull.Client.Views.State.MoreWanted
    | DevNull.Client.Views.State.Opened _
    | DevNull.Client.Views.State.Closed
    | DevNull.Client.Views.State.SettleWanted _
    | DevNull.Client.Views.State.OrgCreateWanted
    | DevNull.Client.Views.State.OrgNameChanged _
    | DevNull.Client.Views.State.OrgCreated _ -> model, Cmd.none
    | DevNull.Client.Views.State.RecordWanted ->
      match model.Active with
      | false -> model, Cmd.none
      | true ->
        let thread, cmds =
          DevNull.Client.Views.RemotesThread.update {
            Props = remotesProps props
            Target = props.Target
            Msg = msg
            Model = model.Thread
          }

        // Same reference means no model change, but the command still
        // runs: several thread messages (ping, retry, older pages)
        // keep the model and carry only an effect.
        match obj.ReferenceEquals(thread, model.Thread) with
        | true -> model, cmds
        | false ->
          let next = { model with Thread = thread }
          next, Cmd.batch [ cmds; announce(props, next) ]
    | DevNull.Client.Views.State.DraftChanged(text, _) ->
      match model.Active with
      | false ->
        let next = {
          model.Thread with
              Draft = text
              Mention = DevNull.Client.Core.RemotesText.mentionOf(text, 0)
        }

        let changed = next <> model.Thread

        match changed with
        | false -> model, Cmd.none
        | true ->
          let updated = { model with Thread = next }

          updated,
          Cmd.batch [
            Cmd.ofAction(fun () ->
              props.Env.Settings.SetChatDraft(model.Thread.DraftKey, text))
            announce(props, updated)
          ]
      | true ->
        let thread, cmds =
          DevNull.Client.Views.RemotesThread.update {
            Props = remotesProps props
            Target = props.Target
            Msg = msg
            Model = model.Thread
          }

        match obj.ReferenceEquals(thread, model.Thread) with
        | true -> model, cmds
        | false ->
          let next = { model with Thread = thread }
          next, Cmd.batch [ cmds; announce(props, next) ]
    | DevNull.Client.Views.State.RideMoved(riding, behind) ->
      match model.Active with
      | false ->
        let thread = {
          model.Thread with
              Riding = riding
              Behind = behind
        }

        match thread = model.Thread with
        | true -> model, Cmd.none
        | false ->
          let next = { model with Thread = thread }
          next, announce(props, next)
      | true ->
        let thread, cmds =
          DevNull.Client.Views.RemotesThread.update {
            Props = remotesProps props
            Target = props.Target
            Msg = msg
            Model = model.Thread
          }

        match obj.ReferenceEquals(thread, model.Thread) with
        | true -> model, cmds
        | false ->
          let next = { model with Thread = thread }
          next, Cmd.batch [ cmds; announce(props, next) ]
    | _ ->
      let thread, cmds =
        DevNull.Client.Views.RemotesThread.update {
          Props = remotesProps props
          Target = props.Target
          Msg = msg
          Model = model.Thread
        }

      // Same reference means no model change, but the command still
      // runs: several thread messages (ping, retry, older pages,
      // reactions, picks, previews) keep the model and carry only an
      // effect. Dropping the command here silenced them.
      match obj.ReferenceEquals(thread, model.Thread) with
      | true -> model, cmds
      | false ->
        let next = { model with Thread = thread }
        next, Cmd.batch [ cmds; announce(props, next) ]

  let subscribe
    (props: Props<'env>, model: Model)
    : Sub<DevNull.Client.Views.State.Msg> =
    let key = keyOf props.Target

    let storeSlice(state: RemotesState) : string =
      let live = state.Threads |> Map.tryFind key

      let count = live |> Option.map _.Messages.Length |> Option.defaultValue -1

      let head = live |> Option.map _.Head |> Option.defaultValue -1

      let until = live |> Option.map _.HeadUntil |> Option.defaultValue -1

      let loaded = live |> Option.map _.IsLoaded |> Option.defaultValue false

      let pending = live |> Option.map _.Pending.Length |> Option.defaultValue 0

      let active = state.WindowActive

      $"{count}:{head}:{until}:{loaded}:{pending}:{active}"

    let storeSub =
      props.Env.Remotes.Observe
      |> Observable.distinctUntilChangedKey storeSlice
      |> Observable.map DevNull.Client.Views.State.StoreChanged
      |> Sub.fromObservable "store"

    let watchSub =
      Sub.ofId ("watch:" + key) (fun _ ->
        props.Env.Remotes.Watcher.WatchThread props.Target)

    let clockSub =
      props.Env.UiClock.Minute
      |> Observable.map DevNull.Client.Views.State.ClockTicked
      |> Sub.fromObservable "clock"

    let activeSub =
      props.Active
      |> Observable.map DevNull.Client.Views.State.Settled
      |> Sub.fromObservable "active"

    let pending =
      model.Live.Messages
      |> List.exists(fun m ->
        m.LinkPreview |> Option.map _.IsPending |> Option.defaultValue false)

    match pending with
    | true ->
      Sub.batch [
        storeSub
        watchSub
        clockSub
        activeSub
        Sub.interval "link-previews" (TimeSpan.FromSeconds 5.0) (fun () ->
          DevNull.Client.Views.State.PreviewsChecked)
      ]
    | false -> Sub.batch [ storeSub; watchSub; clockSub; activeSub ]

let view
  (
    props: Props<'env>,
    model: IObservable<State.Model>,
    dispatch: Dispatch<DevNull.Client.Views.State.Msg>
  ) : Control =
  let tabModel =
    model
    |> Observable.map(fun m ->
      { Thread = m.Live; Model = m.Thread }
      : DevNull.Client.Views.ThreadPane.TabModel)
    |> Slices.share

  let loading =
    model
    |> Observable.map _.Loading
    |> Observable.distinctUntilChanged
    |> Slices.share

  let failure =
    model
    |> Observable.map _.Error
    |> Observable.distinctUntilChanged
    |> Slices.share

  let paneProps: DevNull.Client.Views.Props<'env> = {
    Env = props.Env
    Session = props.Session
    OnOpenProfile = props.OnOpenProfile
  }

  let pane =
    DevNull.Client.Views.ThreadPane.forTarget {
      Props = paneProps
      Target = props.Target
      Model = tabModel
      Dispatch = dispatch
    }

  let loadingLine =
    TextBlock()
      .Text("Opening the conversation…")
      .Classes(Theme.name Theme.FgMuted)
      .IsVisible(loading)

  let errorLine =
    TextBlock()
      .Text(
        failure
        |> Observable.map(fun error ->
          error |> Option.map describe |> Option.defaultValue "")
      )
      .Classes(Theme.name Theme.FgMuted)
      .IsVisible(failure |> Observable.map _.IsSome)

  let status = StackPanel().Spacing(4.0).Children(loadingLine, errorLine)

  Grid(RowDefinitions = RowDefinitions "Auto,*")
    .Children(status.Row(0), pane.Row(1))

let program
  (props: Props<'env>)
  : Program<unit, State.Model, DevNull.Client.Views.State.Msg, Control> =
  Program.mkProgram
    (fun () -> State.init props)
    (fun msg model -> State.update(props, msg, model))
    (fun model dispatch -> view(props, model, dispatch))
  |> Program.withSubscription(fun model -> State.subscribe(props, model))

module Live =

  let count = new BehaviorSubject<int>(0)

  let started() = count.OnNext(count.Value + 1)

  let stopped() = count.OnNext(max 0 (count.Value - 1))

  let current() = count.Value

  let observe = count :> IObservable<int>

let mount(dispatcher: IDispatcher, props: Props<'env>) =
  let view, handle = DevNull.Client.Programs.runPage(dispatcher, program props)

  Live.started()

  let once = new BooleanDisposable()

  let lifetime =
    Disposable.Create(fun () ->
      if not once.IsDisposed then
        once.Dispose()
        handle.Dispose()
        Live.stopped())

  view, lifetime
