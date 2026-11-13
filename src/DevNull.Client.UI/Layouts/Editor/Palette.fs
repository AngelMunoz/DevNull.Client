module DevNull.Client.Editor.Palette

open System
open System.Reactive.Disposables
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Components.PageChrome
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.Stores

type Command =
  | SwitchRef of LogKind
  | OpenPanel of EditorPanel.Tab
  | UseDefaultLayout

[<Struct>]
type SearchPlan = {
  People: string option
  Commits: string option
}

let plan(raw: string) = {
  People = SearchTerms.peopleTerm raw |> ValueOption.toOption
  Commits = SearchTerms.commitTerm raw |> ValueOption.toOption
}

let commands = [
  SwitchRef LogKind.Latest
  SwitchRef LogKind.Following
  SwitchRef LogKind.Trending
  OpenPanel EditorPanel.Notifications
  OpenPanel EditorPanel.Activity
  OpenPanel EditorPanel.Terminal
  UseDefaultLayout
]

let label(command: Command) =
  match command with
  | SwitchRef log -> $"switch ref: {LogLabel.ofKind log}"
  | OpenPanel tab ->
    let name =
      match tab with
      | EditorPanel.Notifications -> "NOTIFICATIONS"
      | EditorPanel.Activity -> "ACTIVITY"
      | EditorPanel.Terminal -> "TERMINAL"

    $"open panel: {name}"
  | UseDefaultLayout -> "use default layout"

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideImageCache> = {
  Env: 'env
  Session: SignedInSession
  OnOpenRepo: string -> unit
  OnOpenThread: Commit -> unit
  OnSelectRef: LogKind -> unit
  OnOpenPanel: EditorPanel.Tab -> unit
  OnUseDefaultLayout: unit -> unit
  OnClose: unit -> unit
}

module State =

  type Model = {
    Query: string
    Term: string
    People: User list
    Commits: Commit list
    PeopleLoading: bool
    CommitsLoading: bool
    PeopleError: AppError option
    CommitsError: AppError option
  }

  type Msg =
    | QueryChanged of raw: string
    | PeopleFetched of term: string * Result<ProfileMatches, AppError>
    | CommitsFetched of term: string * Result<CommitNode list, AppError>
    | PersonPicked of User
    | CommitPicked of Commit
    | CommandChosen of Command
    | Close

  let initial = {
    Query = ""
    Term = ""
    People = []
    Commits = []
    PeopleLoading = false
    CommitsLoading = false
    PeopleError = None
    CommitsError = None
  }

let searchProfiles
  (props: Props<'env>, term: string)
  : Async<Result<ProfileMatches, AppError>> =
  Search.profiles(
    props.Env,
    {
      KeyFile = props.Session.KeyFile
      Term = term
    }
  )

let searchCommits
  (props: Props<'env>, term: string)
  : Async<Result<CommitNode list, AppError>> =
  Search.commits(
    props.Env,
    {
      KeyFile = props.Session.KeyFile
      Term = term
      Limit = Some 8
    }
  )

let update(props: Props<'env>, msg: State.Msg, model: State.Model) =
  match msg with
  | State.QueryChanged raw ->
    if raw = model.Query then
      model, Cmd.none
    else
      let term = raw.Trim()
      let nextPlan = plan term

      let next = {
        model with
            Query = raw
            Term = term
            People = []
            Commits = []
            PeopleLoading = nextPlan.People.IsSome
            CommitsLoading = nextPlan.Commits.IsSome
            PeopleError = None
            CommitsError = None
      }

      let searches: Cmd<State.Msg> list = [
        match nextPlan.People with
        | Some term ->
          yield
            Cmd.OfAsync.perform searchProfiles (props, term) (fun result ->
              State.PeopleFetched(term, result))
        | None -> ()

        match nextPlan.Commits with
        | Some term ->
          yield
            Cmd.OfAsync.perform searchCommits (props, term) (fun result ->
              State.CommitsFetched(term, result))
        | None -> ()
      ]

      next, Cmd.batch searches
  | State.PeopleFetched(term, result) ->
    if model.Term <> term then
      model, Cmd.none
    else
      match result with
      | Ok matches ->
        {
          model with
              People = matches.Users
              PeopleLoading = false
              PeopleError = None
        },
        Cmd.none
      | Error error ->
        {
          model with
              People = []
              PeopleLoading = false
              PeopleError = Some error
        },
        Cmd.none
  | State.CommitsFetched(term, result) ->
    if model.Term <> term then
      model, Cmd.none
    else
      match result with
      | Ok nodes ->
        let commits =
          nodes
          |> List.choose(function
            | LiveCommit commit -> Some commit
            | Tombstone _ -> None)

        {
          model with
              Commits = commits
              CommitsLoading = false
              CommitsError = None
        },
        Cmd.none
      | Error error ->
        {
          model with
              Commits = []
              CommitsLoading = false
              CommitsError = Some error
        },
        Cmd.none
  | State.PersonPicked user ->
    model,
    Cmd.ofAction(fun () ->
      props.OnOpenRepo user.Handle
      props.OnClose())
  | State.CommitPicked commit ->
    model,
    Cmd.ofAction(fun () ->
      props.OnOpenThread commit
      props.OnClose())
  | State.CommandChosen command ->
    model,
    Cmd.ofAction(fun () ->
      match command with
      | SwitchRef log -> props.OnSelectRef log
      | OpenPanel tab -> props.OnOpenPanel tab
      | UseDefaultLayout -> props.OnUseDefaultLayout()

      props.OnClose())
  | State.Close -> model, Cmd.ofAction props.OnClose

let section(text: string) : Control =
  TextBlock()
    .Text(text)
    .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
    .Margin(0.0, 8.0, 0.0, 2.0)

let rowButton(name: string, content: Control, onClick: unit -> unit) : Control =
  Button()
    .Name(name)
    .Classes(Theme.name Theme.NavItem)
    .HorizontalContentAlignment(HorizontalAlignment.Stretch)
    .Padding(8.0, 5.0)
    .Content(content)
    .OnClickHandler(fun _ _ -> onClick())

let personRow
  (props: Props<'env>, user: User, dispatch: Dispatch<State.Msg>)
  : Control =
  let identity =
    StackPanel()
      .VerticalAlignment(VerticalAlignment.Center)
      .Spacing(1.0)
      .Children(
        TextBlock().Text(user.Name).Classes(Theme.name Theme.Subtitle),
        DevNull.Client.Components.ProfileCard.laneHandle(user.Lane, user.Handle)
      )

  let avatar =
    DevNull.Client.Components.Avatar.view {
      Name = user.Name
      Url = user.AvatarUrl
      Lane = user.Lane
      Size = 24.0
      Cache = props.Env.ImageCache
      Square = false
      Viewable = false
    }

  let content = Grid()
  content.ColumnDefinitions <- ColumnDefinitions("Auto,*,Auto")

  content.Children(
    avatar.Column(0),
    identity.Column(1),
    TextBlock()
      .Text("open →")
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .Column(2)
  )
  |> ignore

  rowButton(
    $"PalettePerson-{user.Handle}",
    content,
    fun () -> dispatch(State.PersonPicked user)
  )

let commitRow
  (props: Props<'env>, commit: Commit, dispatch: Dispatch<State.Msg>)
  : Control =
  let author = commit.Author

  let identity =
    StackPanel()
      .VerticalAlignment(VerticalAlignment.Center)
      .Spacing(1.0)
      .Children(
        TextBlock()
          .Text($"{commit.ShortHash} · @{Author.handle author}")
          .Classes(Theme.name Theme.Mono),
        TextBlock()
          .Text(commit.Body)
          .Classes(Theme.name Theme.Muted)
          .MaxLines(1)
          .TextTrimming(TextTrimming.CharacterEllipsis)
      )

  let avatar =
    DevNull.Client.Components.Avatar.view {
      Name = Author.name author
      Url = Author.avatarUrl author
      Lane = Author.lane author
      Size = 24.0
      Cache = props.Env.ImageCache
      Square = false
      Viewable = false
    }

  let content = Grid()
  content.ColumnDefinitions <- ColumnDefinitions("Auto,*,Auto")

  content.Children(
    avatar.Column(0),
    identity.Column(1),
    TextBlock()
      .Text("open →")
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .Column(2)
  )
  |> ignore

  rowButton(
    $"PaletteCommit-{commit.Id}",
    content,
    fun () -> dispatch(State.CommitPicked commit)
  )

let resultsView
  (props: Props<'env>, model: State.Model, dispatch: Dispatch<State.Msg>)
  : Control =
  let children: Control array = [|
    if String.IsNullOrWhiteSpace model.Query then
      yield
        TextBlock()
          .Text("Type a name, a commit, or a command.")
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .Margin(4.0, 8.0)

    if model.PeopleLoading || model.CommitsLoading then
      yield
        TextBlock()
          .Text("Searching…")
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .Margin(4.0, 4.0)

    match model.PeopleError with
    | Some error ->
      yield
        TextBlock()
          .Text(describe error)
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .Margin(4.0, 4.0)
    | None -> ()

    if not(List.isEmpty model.People) then
      yield section "PEOPLE"

      for user in model.People do
        yield personRow(props, user, dispatch)

    match model.CommitsError with
    | Some error ->
      yield
        TextBlock()
          .Text(describe error)
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .Margin(4.0, 4.0)
    | None -> ()

    if not(List.isEmpty model.Commits) then
      yield section "COMMITS"

      for commit in model.Commits do
        yield commitRow(props, commit, dispatch)

    if
      not(String.IsNullOrWhiteSpace model.Query)
      && not model.PeopleLoading
      && not model.CommitsLoading
      && List.isEmpty model.People
      && List.isEmpty model.Commits
    then
      yield
        TextBlock()
          .Text("No people or commits match.")
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .Margin(4.0, 8.0)
  |]

  StackPanel().Spacing(2.0).Children(children)

let commandRows(dispatch: Dispatch<State.Msg>) : Control array = [|
  for index, command in List.indexed commands do
    let content =
      DockPanel()
        .LastChildFill(true)
        .Children(
          TextBlock().Text(label command).Classes(Theme.name Theme.Mono),
          TextBlock()
            .Text("↵")
            .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
            .DockRight()
        )

    yield
      rowButton(
        $"PaletteCommand-{index}",
        content,
        fun () -> dispatch(State.CommandChosen command)
      )
|]

let view
  (
    props: Props<'env>,
    model: IObservable<State.Model>,
    dispatch: Dispatch<State.Msg>
  ) : Control =
  let query =
    TextBox()
      .Name("PaletteQuery")
      .MaxLength(100)
      .PlaceholderText("Search people, commits, or commands")
      .Background(Brushes.Transparent)
      .BorderThickness(Thickness(0.0))
      .FontSize(15.0)
      .Text(model |> Observable.map(fun current -> current.Query))
      .OnTextChangedHandler(fun box _ ->
        box.Text
        |> Option.ofObj
        |> Option.defaultValue ""
        |> State.QueryChanged
        |> dispatch)

  let closeOnEscape(args: KeyEventArgs) =
    if args.Key = Key.Escape then
      args.Handled <- true
      dispatch State.Close

  query.KeyDown.Add closeOnEscape

  let inputGrid = Grid()
  inputGrid.ColumnDefinitions <- ColumnDefinitions("Auto,*")

  inputGrid.Children(
    DevNull.Client.Icons
      .iconAt(DevNull.Client.Icons.Glyph.Search, 16.0)
      .Column(0),
    query.Column(1)
  )
  |> ignore

  let inputRow =
    Border()
      .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(6.0)
      .Padding(8.0, 4.0)
      .Child(inputGrid)

  let results =
    DevNull.Client.Components.PageChrome.rebuildOn(
      model,
      (fun current ->
        current.Query,
        current.Term,
        current.People,
        current.Commits,
        current.PeopleLoading,
        current.CommitsLoading,
        current.PeopleError,
        current.CommitsError),
      fun current -> resultsView(props, current, dispatch)
    )

  let resultsHost =
    ScrollViewer()
      .MaxHeight(280.0)
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
      .Content(ContentControl().Content(results |> _.ToBinding()))

  let commandList = StackPanel().Spacing(2.0).Children(commandRows dispatch)

  let card =
    Border()
      .Name("PaletteCard")
      .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(10.0)
      .Padding(12.0)
      .MaxWidth(620.0)
      .MaxHeight(560.0)
      .Child(
        StackPanel()
          .Spacing(4.0)
          .Children(
            inputRow,
            resultsHost,
            Border()
              .Classes(Theme.name Theme.Hairline)
              .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
              .Margin(0.0, 4.0),
            section "COMMANDS",
            commandList
          )
      )

  card.KeyDown.Add closeOnEscape
  card.Focusable <- true

  query.AttachedToVisualTree.Add(fun _ -> query.Focus() |> ignore)

  card

let program
  (props: Props<'env>)
  : Program<unit, State.Model, State.Msg, Control> =
  Program.mkProgram
    (fun () -> State.initial, Cmd.none)
    (fun msg model -> update(props, msg, model))
    (fun model dispatch -> view(props, model, dispatch))

let mount(dispatcher: IDispatcher, props: Props<'env>) =
  let view, handle = DevNull.Client.Programs.runPage(dispatcher, program props)
  let once = new BooleanDisposable()

  let lifetime =
    Disposable.Create(fun () ->
      if not once.IsDisposed then
        once.Dispose()
        handle.Dispose())

  view, lifetime
