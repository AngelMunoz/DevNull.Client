module DevNull.Client.Editor.SearchTab

open System
open System.Reactive.Disposables
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Editor
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.PageChrome
open DevNull.Client.Components.SearchParts
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
open DevNull.Client.Search
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore
open DevNull.Client.UiClock

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
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Term: string
  Zone: Zone
  Now: unit -> DateTimeOffset
  OnOpenThread: Commit -> unit
  OnReply: Commit -> unit
  OnQuote: Commit -> unit
  OnOpenRepo: string -> unit
  OnShared: User -> unit
}

module State =

  type Model = {
    People: ProfilesFound option
    Commits: CommitsFound option
    Cards: EditorCards.State
    Error: AppError option
  }

  type Msg =
    | PeopleSettled of ProfilesFound
    | CommitsSettled of CommitsFound
    | CardMsg of EditorCards.Msg

  let cardsProps(props: Props<'env>) : EditorCards.Props<'env> = {
    Env = props.Env
    Session = props.Session
    Zone = props.Zone
    Surface = EditorCards.Feed
    Now = props.Now
    OnOpenRepo = props.OnOpenRepo
    OnShared = props.OnShared
  }

  let initial(props: Props<'env>) = {
    People = None
    Commits = None
    Cards = EditorCards.initial(cardsProps props)
    Error = None
  }

  let replace(commits: CommitNode list, updated: Commit) =
    commits
    |> List.map(function
      | LiveCommit commit when commit.Id = updated.Id -> LiveCommit updated
      | node -> node)

  let remove(commits: CommitNode list, commitId: string) =
    commits
    |> List.choose(function
      | LiveCommit commit when commit.Id = commitId -> None
      | node -> Some node)

  let update(props: Props<'env>, msg: Msg, model: Model) =
    match msg with
    | PeopleSettled found -> { model with People = Some found }, Cmd.none
    | CommitsSettled found -> { model with Commits = Some found }, Cmd.none
    | CardMsg msg ->
      let cards, cmd, effects =
        EditorCards.update(cardsProps props, msg, model.Cards)

      let next =
        effects
        |> List.fold
          (fun current effect ->
            let commits =
              current.Commits
              |> Option.map(fun found ->
                match effect with
                | EditorCards.CommitChanged updated -> {
                    found with
                        Commits = replace(found.Commits, updated)
                  }
                | EditorCards.CommitDeleted commitId -> {
                    found with
                        Commits = remove(found.Commits, commitId)
                  }
                | EditorCards.PinChanged _ -> found
                | EditorCards.Failed _ -> found)

            let error =
              match effect with
              | EditorCards.Failed error -> Some error
              | _ -> current.Error

            {
              current with
                  Commits = commits
                  Error = error
            })
          { model with Cards = cards }

      next, EditorCards.mapCmd(CardMsg, cmd)

let status(text: string) : Control =
  TextBlock()
    .Text(text)
    .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)

let peopleSection
  (ctx: RowCtx, model: State.Model, onOpenRepo: string -> unit)
  : Control =
  let rows: Control array =
    match model.People with
    | None -> [| status "Searching people…" |]
    | Some found ->
      if found.Users.IsEmpty then
        [| status "No people found." |]
      else
        [|
          for user in found.Users ->
            Button()
              .Name($"SearchPerson-{user.Handle}")
              .Classes(Theme.name Theme.CommitRow)
              .Content(peopleIdentity(ctx, user))
              .OnClickHandler(fun _ _ -> onOpenRepo user.Handle)
        |]

  StackPanel()
    .Spacing(6.0)
    .Children([| heading "PEOPLE"; StackPanel().Spacing(4.0).Children(rows) |])

let view
  (
    props: Props<'env>,
    model: IObservable<State.Model>,
    dispatch: Dispatch<State.Msg>
  ) : Control =
  let ctx: RowCtx = {
    Images = props.Env.ImageCache
    Flyout = ValueNone
    Now = props.Now()
  }

  let cardDispatch msg = dispatch(State.CardMsg msg)

  let fallback =
    function
    | Opened commit -> props.OnOpenThread commit
    | ReplyToggled commit -> props.OnReply commit
    | QuoteRequested commit -> props.OnQuote commit
    | MoreRepliesRequested commit -> props.OnOpenThread commit
    | _ -> ()

  let cards = model |> Observable.map _.Cards |> Slices.share

  let rowProps =
    EditorCards.row {
      Props = State.cardsProps props
      State = cards
      Dispatch = cardDispatch
      OnEvent = EditorCards.event(cardDispatch, fallback)
    }
    |> fun row -> { row with Dense = true }

  let headerBody =
    rebuildOn(
      model,
      (fun current -> current.People, current.Commits, current.Error),
      fun current ->
        let people: Control = peopleSection(ctx, current, props.OnOpenRepo)

        let people: Control =
          match current.People with
          | Some found when found.Throttled ->
            StackPanel()
              .Children([| people; status "Search is busy. Try again soon." |])
          | _ -> people

        let commitsHead: Control =
          match current.Commits with
          | None ->
            StackPanel()
              .Spacing(6.0)
              .Children([| heading "COMMITS"; status "Searching commits…" |])
          | Some found when found.Commits.IsEmpty ->
            StackPanel()
              .Spacing(6.0)
              .Children([| heading "COMMITS"; status "No commits found." |])
          | Some _ -> heading "COMMITS"

        let commitsHead: Control =
          match current.Commits with
          | Some found when found.Throttled ->
            StackPanel()
              .Children(
                [| commitsHead; status "Search is busy. Try again soon." |]
              )
          | _ -> commitsHead

        let error = errorPanel(Observable.single current.Error)

        StackPanel()
          .Spacing(12.0)
          .Children(
            TextBlock()
              .Text($"SEARCH / {props.Term}")
              .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted),
            people,
            commitsHead,
            error
          )
    )

  let items =
    model
    |> Observable.map(fun current ->
      match current.Commits with
      | Some found -> found.Commits
      | None -> [])

  let feed =
    Feed.view {
      Header = ValueSome(ContentControl().Content(headerBody |> _.ToBinding()))
      Items = items
      KeyOf = FeedParts.nodeKey
      Row = fun node -> feedRow rowProps node
      Footer = ValueNone
      HasMore = Observable.single false
      ScrollTo = ValueNone
      Ride = false
      Restore = ValueNone
      OnScroll = ValueNone
      OnRide = ValueNone
      OnNeedMore = ValueNone
    }

  Grid()
    .Name($"EditorSearchTab-{props.Term}")
    .Children(
      feed,
      EditorCards.layer(
        State.cardsProps props,
        model |> Observable.map _.Cards,
        cardDispatch
      )
    )

let program
  (props: Props<'env>, searches: Search.Session)
  : Program<unit, State.Model, State.Msg, Control> =
  Program.mkProgram
    (fun () ->
      State.initial props, Cmd.ofAction(fun () -> searches.Set props.Term))
    (fun msg model -> State.update(props, msg, model))
    (fun model dispatch -> view(props, model, dispatch))
  |> Program.withSubscription(fun _ ->
    Sub.batch [
      Sub.fromObservable
        "profiles"
        (searches.Profiles |> Observable.map State.PeopleSettled)
      Sub.fromObservable
        "commits"
        (searches.Commits |> Observable.map State.CommitsSettled)
      Sub.fromObservable
        "card-clock"
        (props.Env.UiClock.Minute
         |> Observable.map(fun tick ->
           State.CardMsg(EditorCards.ClockTicked tick)))
    ])

let mount(dispatcher: IDispatcher, props: Props<'env>) =
  let log = props.Env.LoggerFactory.CreateLogger "search-tab"

  let searches =
    new Search.Session(
      (fun term ->
        Search.profiles(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            Term = term
          }
        )),
      (fun term ->
        Search.commits(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            Term = term
            Limit = Some 20
          }
        )),
      log
    )

  let view, handle =
    DevNull.Client.Programs.runPage(dispatcher, program(props, searches))

  view,
  Disposable.Create(fun () ->
    handle.Dispose()
    (searches :> IDisposable).Dispose())
