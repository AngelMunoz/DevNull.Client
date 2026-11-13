module DevNull.Client.Layouts.Editor.EditorCards

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.CommitMenu
open DevNull.Client.Components.DialogChrome
open DevNull.Client.Components.ReportDialog
open DevNull.Client.Components.ShareDialog
open DevNull.Client.Components.UserFlyout
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
open DevNull.Client.Views
open DevNull.Client.Views.CommitActions
open DevNull.Client.Views.TranslationModel

type Surface =
  | Feed
  | Detail
  | Saved
  | Profile

type State = {
  Busy: string option
  PollChoices: Map<string, string>
  Relations: Map<string, AuthorRelation>
  MenuBusy: string option
  Pinning: string option
  Translations: Book
  Now: DateTimeOffset
  ShareOpen: Commit voption
  ReportOpen: Commit voption
}

type Effect =
  | CommitChanged of Commit
  | CommitDeleted of commitId: string
  | PinChanged of pinned: bool * commit: Commit
  | Failed of AppError

type Msg =
  | StarToggled of Commit
  | StarDone of Result<Commit, AppError>
  | PastelGiven of Commit
  | PastelDone of Result<PastelResult, AppError>
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | PollVoteRequested of Commit
  | PollVotedDone of Result<Commit, AppError>
  | TranslateRequested of Commit
  | OriginalRequested of Commit
  | TranslationRecalled of Commit * Book option
  | TranslationDone of Commit * Result<TranslationPayload, AppError>
  | ShareOpened of Commit
  | ShareClosed
  | Shared
  | ReportOpened of Commit
  | ReportClosed
  | MenuOpened of Commit
  | RelationLoaded of handle: string * AuthorRelation voption
  | AuthorFollowToggled of Commit
  | AuthorFollowDone of Commit * Result<unit, AppError>
  | AuthorMuteToggled of Commit
  | AuthorMuteDone of Commit * Result<MuteResult, AppError>
  | AuthorBlockRequested of Commit
  | AuthorBlockDone of Commit * Result<BlockResult, AppError>
  | DeleteRequested of Commit
  | Deleted of commitId: string * Result<unit, AppError>
  | PinToggled of Commit
  | PinDone of pinned: bool * Result<Commit, AppError>
  | ClockTicked of moment: DateTimeOffset

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideRemotes
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideBrowser> = {
  Env: 'env
  Session: SignedInSession
  Zone: Zone
  Surface: Surface
  Now: unit -> DateTimeOffset
  OnOpenRepo: string -> unit
}

type RowArgs<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideRemotes
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideBrowser> = {
  Props: Props<'env>
  State: IObservable<State>
  Dispatch: Dispatch<Msg>
  OnEvent: Event -> unit
}

let unreadRelation: AuthorRelation = {
  IsFollowing = false
  IsMuting = false
  IsBlocking = false
}

let empty = {
  Busy = None
  PollChoices = Map.empty
  Relations = Map.empty
  MenuBusy = None
  Pinning = None
  Translations = TranslationModel.initial
  Now = DateTimeOffset.MinValue
  ShareOpen = ValueNone
  ReportOpen = ValueNone
}

let initial(props: Props<'env>) = {
  empty with
      Translations =
        TranslationModel.seed(
          (props.Env :> IProvideTranslations).Translations.Snapshot()
        )
      Now = props.Now()
}

let heldRelation(state: State, handle: string) =
  state.Relations |> Map.tryFind handle |> Option.defaultValue unreadRelation

let translateCall(props: Props<'env>, commit: Commit) =
  let language =
    TranslationModel.languageFor(
      (props.Env :> IProvideUserSettings).UserSettings.Current,
      commit
    )

  Commits.translate(
    props.Env,
    {
      KeyFile = props.Session.KeyFile
      CommitId = commit.Id
      Language = language
    }
  )

let translate(props: Props<'env>, commit: Commit) =
  TranslationModel.execute(
    TranslationModel.retryDelays,
    fun () -> translateCall(props, commit)
  )

let update
  (props: Props<'env>, msg: Msg, state: State)
  : State * Cmd<Msg> * Effect list =
  match msg with
  | StarToggled commit ->
    { state with Busy = Some commit.Id },
    Cmd.OfAsync.perform
      CommitActions.toggleStar
      (props.Env, props.Session, commit)
      StarDone,
    []
  | StarDone result ->
    match result with
    | Ok updated ->
      { state with Busy = None }, Cmd.none, [ CommitChanged updated ]
    | Error error -> { state with Busy = None }, Cmd.none, [ Failed error ]
  | PastelGiven commit ->
    { state with Busy = Some commit.Id },
    Cmd.OfAsync.perform
      CommitActions.pastel
      (props.Env,
       {
         Session = props.Session
         Commit = commit
       })
      PastelDone,
    []
  | PastelDone result ->
    match result with
    | Ok answer ->
      { state with Busy = None }, Cmd.none, [ CommitChanged answer.Commit ]
    | Error error -> { state with Busy = None }, Cmd.none, [ Failed error ]
  | PollChoiceChanged(commit, option, chosen) ->
    {
      state with
          PollChoices =
            choose {
              Choices = state.PollChoices
              CommitId = commit.Id
              OptionId = option.Id
              Chosen = chosen
            }
    },
    Cmd.none,
    []
  | PollVoteRequested commit ->
    match selectedOption(state.PollChoices, commit) with
    | None -> state, Cmd.none, []
    | Some option ->
      { state with Busy = Some commit.Id },
      Cmd.OfAsync.perform
        CommitActions.vote
        (props.Env,
         {
           Session = props.Session
           Commit = commit
           Option = option
         })
        PollVotedDone,
      []
  | PollVotedDone result ->
    match result with
    | Ok updated ->
      {
        state with
            Busy = None
            PollChoices = state.PollChoices |> Map.remove updated.Id
      },
      Cmd.none,
      [ CommitChanged updated ]
    | Error error -> { state with Busy = None }, Cmd.none, [ Failed error ]
  | TranslateRequested commit when
    TranslationModel.canReuse(state.Translations, commit)
    ->
    {
      state with
          Translations =
            TranslationModel.showTranslated(state.Translations, commit.Id)
    },
    Cmd.none,
    []
  | TranslateRequested commit ->
    state,
    Cmd.OfAsync.perform
      TranslationModel.recall
      ((props.Env :> IProvideTranslations).Translations,
       state.Translations,
       commit.Id)
      (fun book -> TranslationRecalled(commit, book)),
    []
  | TranslationRecalled(commit, Some book) ->
    { state with Translations = book }, Cmd.none, []
  | TranslationRecalled(commit, None) when
    Set.contains commit.Id state.Translations.Busy
    ->
    state, Cmd.none, []
  | TranslationRecalled(commit, None) ->
    {
      state with
          Translations = TranslationModel.request(state.Translations, commit.Id)
    },
    Cmd.OfAsync.perform translate (props, commit) (fun result ->
      TranslationDone(commit, result)),
    []
  | TranslationDone(commit, result) ->
    let args: TranslationModel.Finish = {
      Book = state.Translations
      Now = state.Now
      CommitId = commit.Id
      Result = result
    }

    {
      state with
          Translations = TranslationModel.finish args
    },
    Cmd.ofAction(fun () ->
      Async.Start(
        TranslationModel.store(
          (props.Env :> IProvideTranslations).Translations,
          args
        )
      )),
    []
  | OriginalRequested commit ->
    {
      state with
          Translations =
            TranslationModel.showOriginal(state.Translations, commit.Id)
    },
    Cmd.none,
    []
  | ShareOpened commit ->
    {
      state with
          ShareOpen = ValueSome commit
    },
    Cmd.none,
    []
  | ShareClosed -> { state with ShareOpen = ValueNone }, Cmd.none, []
  | Shared -> { state with ShareOpen = ValueNone }, Cmd.none, []
  | ReportOpened commit ->
    {
      state with
          ReportOpen = ValueSome commit
    },
    Cmd.none,
    []
  | ReportClosed -> { state with ReportOpen = ValueNone }, Cmd.none, []
  | MenuOpened commit ->
    if
      (Author.id commit.Author) = props.Session.Session.User.Id
      || state.Relations |> Map.containsKey(Author.handle commit.Author)
    then
      state, Cmd.none, []
    else
      state,
      Cmd.OfAsync.perform
        props.Env.Profiles.Fetch
        (Author.handle commit.Author)
        (fun details ->
          let relation =
            details
            |> Option.map(fun loaded -> {
              IsFollowing = loaded.IsFollowing
              IsMuting = loaded.IsMuting
              IsBlocking = false
            })
            |> ValueOption.ofOption

          RelationLoaded(Author.handle commit.Author, relation)),
      []
  | RelationLoaded(handle, ValueSome relation) ->
    {
      state with
          Relations = state.Relations |> Map.add handle relation
    },
    Cmd.none,
    []
  | RelationLoaded(_, ValueNone) -> state, Cmd.none, []
  | AuthorFollowToggled commit ->
    let held = heldRelation(state, Author.handle commit.Author)

    {
      state with
          MenuBusy = Some commit.Id
          Relations =
            state.Relations
            |> Map.add (Author.handle commit.Author) {
              held with
                  IsFollowing = not held.IsFollowing
            }
    },
    Cmd.OfAsync.perform
      CommitActions.toggleFollow
      (props.Env,
       {
         Session = props.Session
         Author = commit.Author
         Flag = held.IsFollowing
       })
      (fun result -> AuthorFollowDone(commit, result)),
    []
  | AuthorFollowDone(commit, result) ->
    match result with
    | Ok() -> { state with MenuBusy = None }, Cmd.none, []
    | Error error ->
      let held = heldRelation(state, Author.handle commit.Author)

      {
        state with
            MenuBusy = None
            Relations =
              state.Relations
              |> Map.add (Author.handle commit.Author) {
                held with
                    IsFollowing = not held.IsFollowing
              }
      },
      Cmd.none,
      [ Failed error ]
  | AuthorMuteToggled commit ->
    let held = heldRelation(state, Author.handle commit.Author)

    {
      state with
          MenuBusy = Some commit.Id
          Relations =
            state.Relations
            |> Map.add (Author.handle commit.Author) {
              held with
                  IsMuting = not held.IsMuting
            }
    },
    Cmd.OfAsync.perform
      CommitActions.toggleMute
      (props.Env,
       {
         Session = props.Session
         Author = commit.Author
         Flag = held.IsMuting
       })
      (fun result -> AuthorMuteDone(commit, result)),
    []
  | AuthorMuteDone(commit, result) ->
    match result with
    | Ok mute ->
      let held = heldRelation(state, Author.handle commit.Author)

      {
        state with
            MenuBusy = None
            Relations =
              state.Relations
              |> Map.add (Author.handle commit.Author) {
                held with
                    IsMuting = mute.IsMuting
              }
      },
      Cmd.none,
      []
    | Error error ->
      let held = heldRelation(state, Author.handle commit.Author)

      {
        state with
            MenuBusy = None
            Relations =
              state.Relations
              |> Map.add (Author.handle commit.Author) {
                held with
                    IsMuting = not held.IsMuting
              }
      },
      Cmd.none,
      [ Failed error ]
  | AuthorBlockRequested commit ->
    let held = heldRelation(state, Author.handle commit.Author)

    { state with MenuBusy = Some commit.Id },
    Cmd.OfAsync.perform
      CommitActions.blockAuthor
      (props.Env,
       {
         Session = props.Session
         Author = commit.Author
         Flag = held.IsBlocking
       })
      (fun result -> AuthorBlockDone(commit, result)),
    []
  | AuthorBlockDone(commit, result) ->
    match result with
    | Ok blocked ->
      let held = heldRelation(state, Author.handle commit.Author)

      {
        state with
            MenuBusy = None
            Relations =
              state.Relations
              |> Map.add (Author.handle commit.Author) {
                held with
                    IsBlocking = blocked.IsBlocking
              }
      },
      Cmd.none,
      []
    | Error error -> { state with MenuBusy = None }, Cmd.none, [ Failed error ]
  | DeleteRequested commit ->
    { state with MenuBusy = Some commit.Id },
    Cmd.OfAsync.perform
      Commits.delete
      (props.Env,
       {
         KeyFile = props.Session.KeyFile
         CommitId = commit.Id
       })
      (fun result -> Deleted(commit.Id, result)),
    []
  | Deleted(commitId, result) ->
    match result with
    | Ok() ->
      { state with MenuBusy = None }, Cmd.none, [ CommitDeleted commitId ]
    | Error error -> { state with MenuBusy = None }, Cmd.none, [ Failed error ]
  | PinToggled commit ->
    let pin = not commit.IsPinned

    let request: CommitPins.ActionRequest = {
      KeyFile = props.Session.KeyFile
      CommitId = commit.Id
    }

    let call() =
      if pin then
        CommitPins.pin(props.Env, request)
      else
        CommitPins.unpin(props.Env, request)

    { state with Pinning = Some commit.Id },
    Cmd.OfAsync.perform call () (fun result -> PinDone(pin, result)),
    []
  | PinDone(pinned, result) ->
    match result with
    | Ok updated ->
      ({ state with Pinning = None }, Cmd.none, [ PinChanged(pinned, updated) ])
    | Error error -> { state with Pinning = None }, Cmd.none, [ Failed error ]
  | ClockTicked moment -> { state with Now = moment }, Cmd.none, []

let mapCmd(map: 'msg -> 'outer, cmd: Cmd<'msg>) : Cmd<'outer> =
  fun dispatch error -> cmd (map >> dispatch) error

let row(args: RowArgs<'env>) : RowProps =
  let state = args.State |> Slices.share
  let busy = state |> Observable.map _.Busy |> Slices.share
  let menuBusy = state |> Observable.map _.MenuBusy |> Slices.share

  let slot(command: Commit -> Msg) =
    ValueSome {
      Command = command >> args.Dispatch
      Busy = busy
    }

  let pin: PinToggle voption =
    match args.Props.Surface with
    | Profile
    | Detail ->
      ValueSome {
        Command = fun commit -> args.Dispatch(PinToggled commit)
        Busy = state |> Observable.map _.Pinning |> Slices.share
      }
    | Feed
    | Saved -> ValueNone

  let menu: MenuSlot = {
    Share =
      match args.Props.Surface with
      | Feed
      | Detail when args.Props.Session.Session.Remotes.IsSome ->
        ValueSome(fun commit -> args.Dispatch(ShareOpened commit))
      | _ -> ValueNone
    Report =
      match args.Props.Surface with
      | Feed
      | Detail -> ValueSome(fun commit -> args.Dispatch(ReportOpened commit))
      | _ -> ValueNone
    Delete = ValueSome(fun commit -> args.Dispatch(DeleteRequested commit))
    ToggleFollow = fun commit -> args.Dispatch(AuthorFollowToggled commit)
    ToggleMute = fun commit -> args.Dispatch(AuthorMuteToggled commit)
    Block = fun commit -> args.Dispatch(AuthorBlockRequested commit)
    Relations =
      state
      |> Observable.map(fun current ->
        fun commit ->
          current.Relations
          |> Map.tryFind(Author.handle commit.Author)
          |> ValueOption.ofOption)
    Busy = menuBusy
  }

  {
    OnEvent = args.OnEvent
    OnOpenExternal = fun url -> args.Props.Env.Browser.Open(url) |> ignore
    ViewerId = args.Props.Session.Session.User.Id
    Pasteis = Observable.single args.Props.Session.Session.PasteisMode
    Now = state |> Observable.map _.Now
    PollChoices = state |> Observable.map _.PollChoices
    Dense = false
    Images = args.Props.Env.ImageCache
    Flyout =
      ValueSome {
        Fetch = args.Props.Env.Profiles.Fetch
        OnOpen = args.Props.OnOpenRepo
        OnOpenOrg = ignore
        OnOpenTag = ignore
        Cache = args.Props.Env.ImageCache
      }
    Pin = pin
    Star = slot StarToggled
    Stars = ValueNone
    Vote = slot PollVoteRequested
    Pastel = slot PastelGiven
    Menu = ValueSome menu
    Translation =
      state
      |> Observable.map(fun current ->
        fun commit ->
          TranslationModel.view {
            Automatic = args.Props.Session.Session.AutomaticTranslation
            Book = current.Translations
            Now = current.Now
            Commit = commit
          })
    Zone = args.Props.Zone
  }

let event
  (dispatch: Dispatch<Msg>, fallback: Event -> unit)
  (cardEvent: Event)
  : unit =
  match cardEvent with
  | CommitCard.PollChoiceChanged(commit, option, chosen) ->
    dispatch(PollChoiceChanged(commit, option, chosen))
  | CommitCard.TranslateRequested commit -> dispatch(TranslateRequested commit)
  | CommitCard.OriginalRequested commit -> dispatch(OriginalRequested commit)
  | CommitCard.MenuOpened commit -> dispatch(MenuOpened commit)
  | other -> fallback other

let layer
  (props: Props<'env>, state: IObservable<State>, dispatch: Dispatch<Msg>)
  : Control =
  let shareLayer =
    DialogChrome.layer {
      Open =
        state
        |> Observable.map(fun current ->
          current.ShareOpen |> ValueOption.isSome)
      Content =
        fun () ->
          let openCommit =
            state
            |> Observable.first
            |> Observable.toEnumerable
            |> Seq.tryHead
            |> Option.bind(fun current ->
              current.ShareOpen |> ValueOption.toOption)

          match openCommit with
          | Some commit ->
            ShareDialog.Page {
              Env = props.Env
              Session = props.Session
              Commit = commit
              Sent = fun _ -> dispatch Shared
              Close = fun () -> dispatch ShareClosed
            }
          | None -> Panel()
      Scrim = true
      OnScrimClick = ValueSome(fun () -> dispatch ShareClosed)
      Place = DialogChrome.Center
    }

  let reportLayer =
    DialogChrome.layer {
      Open =
        state
        |> Observable.map(fun current ->
          current.ReportOpen |> ValueOption.isSome)
      Content =
        fun () ->
          let openCommit =
            state
            |> Observable.first
            |> Observable.toEnumerable
            |> Seq.tryHead
            |> Option.bind(fun current ->
              current.ReportOpen |> ValueOption.toOption)

          match openCommit with
          | Some commit ->
            ReportDialog.Page {
              Env = props.Env
              Session = props.Session
              Commit = commit
              Sent = fun () -> dispatch ReportClosed
              Close = fun () -> dispatch ReportClosed
            }
          | None -> Panel()
      Scrim = true
      OnScrimClick = ValueSome(fun () -> dispatch ReportClosed)
      Place = DialogChrome.Center
    }

  Grid().Children(shareLayer, reportLayer)
