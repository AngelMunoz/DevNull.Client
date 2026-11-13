module DevNull.Client.Layouts.Editor.SearchTab

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

let commitSection
  (
    props: Props<'env>,
    model: State.Model,
    dispatch: Dispatch<State.Msg>,
    ctx: RowCtx
  ) : Control =
  let cardDispatch msg = dispatch(State.CardMsg msg)

  let fallback =
    function
    | Opened commit -> props.OnOpenThread commit
    | ReplyToggled commit -> props.OnReply commit
    | QuoteRequested commit -> props.OnQuote commit
    | MoreRepliesRequested commit -> props.OnOpenThread commit
    | _ -> ()

  let rowProps =
    EditorCards.row {
      Props = State.cardsProps props
      State = Observable.single model.Cards
      Dispatch = cardDispatch
      OnEvent = EditorCards.event(cardDispatch, fallback)
    }
    |> fun row -> { row with Dense = true }

  let rows: Control array =
    match model.Commits with
    | None -> [| status "Searching commits…" |]
    | Some found ->
      if found.Commits.IsEmpty then
        [| status "No commits found." |]
      else
        [| for node in found.Commits -> feedRow rowProps node |]

  StackPanel()
    .Spacing(6.0)
    .Children([| heading "COMMITS"; StackPanel().Spacing(4.0).Children(rows) |])

let sections
  (props: Props<'env>, model: State.Model, dispatch: Dispatch<State.Msg>)
  : Control =
  let ctx: RowCtx = {
    Images = props.Env.ImageCache
    Flyout = ValueNone
    Now = props.Now()
  }

  let people: Control = peopleSection(ctx, model, props.OnOpenRepo)

  let people: Control =
    match model.People with
    | Some found when found.Throttled ->
      StackPanel()
        .Children([| people; status "Search is busy. Try again soon." |])
    | _ -> people

  let commits: Control = commitSection(props, model, dispatch, ctx)

  let commits: Control =
    match model.Commits with
    | Some found when found.Throttled ->
      StackPanel()
        .Children([| commits; status "Search is busy. Try again soon." |])
    | _ -> commits

  let error = errorPanel(Observable.single model.Error)

  StackPanel()
    .Spacing(12.0)
    .Children(
      TextBlock()
        .Text($"SEARCH / {props.Term}")
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted),
      people,
      commits,
      error
    )

let view
  (
    props: Props<'env>,
    model: IObservable<State.Model>,
    dispatch: Dispatch<State.Msg>
  ) : Control =
  let body =
    rebuildOn(
      model,
      (fun current -> current),
      fun current -> sections(props, current, dispatch)
    )

  let cardDispatch msg = dispatch(State.CardMsg msg)

  let content =
    ScrollViewer()
      .Name($"EditorSearchTab-{props.Term}")
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
      .Content(
        ContentControl()
          .Content(body |> _.ToBinding())
          .Margin(Thickness(16.0, 12.0))
      )

  Grid()
    .Children(
      content,
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
