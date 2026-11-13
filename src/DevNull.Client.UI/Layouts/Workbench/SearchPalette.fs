module DevNull.Client.Workbench.SearchPalette

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Templates
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.SearchParts
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.Theme

type Model = {
  Term: string
  Loading: bool
  Error: AppError option
  People: User list
  Searched: bool
  Notice: string option
}

type Msg =
  | TermChanged of text: string
  | PeopleRequested
  | FirstPersonRequested
  | PeopleFetched of Result<ProfileMatches, AppError>
  | PersonPicked of User
  | CommitsRequested
  | CloseRequested

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
  KeyFile: string
  Images: ImageCacheService
  OnOpenRepository: string -> unit
  OnSearchCommits: string -> unit
  OnClose: unit -> unit
}

let inline peopleTermOf(raw: string) = SearchTerms.peopleTerm raw

let inline commitTermOf(raw: string) = SearchTerms.commitTerm raw

let fetchPeople
  (props: Props<'env>, term: string)
  : Async<Result<ProfileMatches, AppError>> =
  Search.profiles(props.Env, { KeyFile = props.KeyFile; Term = term })

let init(_: Props<'env>) : Model * Cmd<Msg> =
  ({
    Term = ""
    Loading = false
    Error = None
    People = []
    Searched = false
    Notice = None
   },
   Cmd.none)

let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
  let requestPeople(model: Model) =
    if model.Loading then
      model, Cmd.none
    else
      match peopleTermOf model.Term with
      | ValueSome term ->
        ({
          model with
              Loading = true
              Error = None
              Notice = None
         },
         Cmd.OfAsync.perform fetchPeople (props, term) PeopleFetched)
      | ValueNone ->
        ({
          model with
              Notice = Some "Type a name or handle first."
              Searched = false
         },
         Cmd.none)

  match msg with
  | TermChanged text ->
    ({
      model with
          Term = text
          Notice = None
     },
     Cmd.none)
  | PeopleRequested -> requestPeople model
  | FirstPersonRequested ->
    match model.People with
    | person :: _ ->
      model, Cmd.ofAction(fun () -> props.OnOpenRepository person.Handle)
    | [] -> requestPeople model
  | PeopleFetched result ->
    match result with
    | Ok matches ->
      ({
        model with
            Loading = false
            Error = None
            People = matches.Users
            Searched = true
       },
       Cmd.none)
    | Error error ->
      ({
        model with
            Loading = false
            Error = Some error
            Searched = true
       },
       Cmd.none)
  | PersonPicked user ->
    model, Cmd.ofAction(fun () -> props.OnOpenRepository user.Handle)
  | CommitsRequested ->
    match commitTermOf model.Term with
    | ValueSome term ->
      model, Cmd.ofAction(fun () -> props.OnSearchCommits term)
    | ValueNone ->
      ({
        model with
            Notice = Some "A commit search needs 3 to 100 characters."
       },
       Cmd.none)
  | CloseRequested -> model, Cmd.ofAction props.OnClose

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let text =
    TextBox()
      .Name("SearchPaletteTerm")
      .PlaceholderText("Search people or commits…")
      .OnTextChangedHandler(fun box _ ->
        let value = box.Text |> Option.ofObj |> Option.defaultValue ""

        dispatch(TermChanged value))
      .OnKeyDownHandler(fun _ args ->
        if args.Key = Key.Enter && args.KeyModifiers = KeyModifiers.None then
          args.Handled <- true
          dispatch FirstPersonRequested
        elif args.Key = Key.Escape then
          args.Handled <- true
          dispatch CloseRequested)

  let close =
    Button()
      .Name("SearchPaletteClose")
      .Classes(Theme.name Theme.GhostButton)
      .Content(Icons.iconAt(Icons.Glyph.Close, 12.0))
      .OnClickHandler(fun _ _ -> dispatch CloseRequested)

  ToolTip.SetTip(close, "Close search")

  let head =
    DockPanel()
      .LastChildFill(true)
      .Children(
        close.DockRight(),
        TextBlock().Classes(Theme.name Theme.Mono).Text("SEARCH")
      )

  let findPeople =
    Button()
      .Name("SearchPeopleButton")
      .Classes(Theme.name Theme.NavItem)
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .Content(TextBlock().Text("Find people"))
      .OnClickHandler(fun _ _ -> dispatch PeopleRequested)

  let searchCommits =
    Button()
      .Name("SearchCommitsButton")
      .Classes(Theme.name Theme.NavItem)
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .Content(TextBlock().Text("Search commits"))
      .OnClickHandler(fun _ _ -> dispatch CommitsRequested)

  let notice =
    TextBlock()
      .Classes(Theme.name Theme.FgMuted)
      .FontSize(12.0)
      .TextWrapping(TextWrapping.Wrap)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Notice |> Option.defaultValue "")
      )

  let loadingLine =
    PageChrome.collapsingMessagePanel(
      model
      |> Observable.map(fun current ->
        if current.Loading then "Reading people…" else ""),
      Theme.Muted
    )

  let emptyLine =
    PageChrome.collapsingMessagePanel(
      model
      |> Observable.map(fun current ->
        if
          current.Searched
          && current.Error.IsNone
          && not current.Loading
          && current.People.IsEmpty
        then
          "No people found."
        else
          ""),
      Theme.Muted
    )

  let errorLine = PageChrome.errorPanel(model |> Observable.map _.Error)

  let rowCtx = {
    Images = props.Images
    Flyout = ValueNone
    Now = DateTimeOffset.UtcNow
  }

  let rows =
    ItemsControl()
      .ItemTemplate(
        FuncDataTemplate<User>(
          (fun user _ ->
            if isNull(box user) then
              Panel() :> Control
            else
              Button()
                .Name("SearchPersonRow")
                .Classes(Theme.name Theme.CommitRow)
                .HorizontalAlignment(HorizontalAlignment.Stretch)
                .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                .Padding(Thickness(12.0, 8.0))
                .Content(peopleIdentity(rowCtx, user))
                .OnClickHandler(fun _ _ -> dispatch(PersonPicked user))
              :> Control),
          false
        )
      )
      .BindItemsSource(
        model
        |> Observable.map(fun current -> current.People :> IReadOnlyList<User>),
        fun user -> user.Id
      )

  let peopleScroller =
    ScrollViewer()
      .MaxHeight(320.0)
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
      .Content(rows)

  let section(text: string, name: string) =
    TextBlock()
      .Name(name)
      .Classes(Theme.name Theme.Mono)
      .Margin(Thickness(0.0, 4.0, 0.0, 0.0))
      .Text(text)

  let content =
    StackPanel()
      .Spacing(8.0)
      .Margin(Thickness(12.0))
      .Children(
        head,
        text,
        section("PEOPLE", "SearchPalettePeopleHeader"),
        findPeople,
        loadingLine,
        emptyLine,
        errorLine,
        notice,
        peopleScroller,
        section("COMMITS", "SearchPaletteCommitsHeader"),
        searchCommits
      )

  Border()
    .Name("SearchPalette")
    .Width(460.0)
    .Classes(
      Theme.name Theme.Card,
      Theme.name Theme.CardBack,
      Theme.name Theme.Hairline
    )
    .Child(content)

let program(props: Props<'env>) =
  Program.mkProgram
    (fun () -> init props)
    (fun msg model -> update(props, msg, model))
    (view props)
