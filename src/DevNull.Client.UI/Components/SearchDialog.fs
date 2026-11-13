module DevNull.Client.Components.SearchDialog

open System
open System.Reactive.Disposables
open System.Text.RegularExpressions
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open Avalonia.VisualTree
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open Navs
open Microsoft.Extensions.Logging
open DevNull.Client
open DevNull.Client.Components.SearchParts
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Search
open DevNull.Client.Stores
open DevNull.Client.UiClock

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging> = {
  Env: 'env
  SignedIn: SignedInSession
  Searches: Session
  Recents: RecentSearchStore
  Nav: INavigable
  InitialTerm: string option
  OpenCompose: unit -> unit
  Close: unit -> unit
}

// The state logic: model, messages, update, and the subscription that
// feeds the dialog from its session and the clock.
module State =

  // One row of the dialog's list.
  type Entry =
    | Compose
    | Person of User
    | Organization of Organization
    | Found of Commit
    | SeeAll
    | Recent of string
    | Suggestion of string

  type Model = {
    Term: string
    Active: int
    Profiles: ProfilesFound voption
    Commits: CommitsFound voption
    Recents: string list
    Now: DateTimeOffset
    // The term whose settle was last written to the recents.
    Remembered: string
  }

  type Msg =
    | Typed of string
    | ProfilesArrived of ProfilesFound
    | CommitsArrived of CommitsFound
    | Moved of index: int
    | Stepped of step: int
    | Hovered of index: int
    | PressedEnter
    | Opened of Entry
    | RecentsCleared
    | ClockTicked of moment: DateTimeOffset

  let initial (props: Props<'env>) () : Model * Cmd<Msg> =
    let term = props.InitialTerm |> Option.defaultValue ""

    let model = {
      Term = term
      Active = 0
      Profiles = ValueNone
      Commits = ValueNone
      Recents = props.Recents.Recent(props.SignedIn.Session.User.Id)
      Now = DateTimeOffset.UtcNow
      Remembered = ""
    }

    let seed =
      if term.Trim() = "" then
        Cmd.none
      else
        Cmd.ofAction(fun () -> props.Searches.Set term)

    model, seed

  let inline trimmed(model: Model) = model.Term.Trim()

  // The profile answer that belongs to the term now typed, when it has
  // settled. A channel with nothing to search for counts as settled.
  let profilesOf(model: Model) : ProfilesFound =
    match SearchTerms.peopleTerm model.Term, model.Profiles with
    | ValueSome term, ValueSome found when found.Term = term -> found
    | _ -> {
        Term = ""
        Users = []
        Organizations = []
        Throttled = false
      }

  // The commit answer that belongs to the term now typed, when it has
  // settled. A channel with nothing to search for counts as settled.
  let commitsOf(model: Model) : CommitsFound =
    match SearchTerms.commitTerm model.Term, model.Commits with
    | ValueSome term, ValueSome found when found.Term = term -> found
    | _ -> {
        Term = ""
        Commits = []
        Throttled = false
      }

  // True while the term owes an answer from a channel it reaches.
  let searching(model: Model) : bool =
    let term = trimmed model

    if term = "" || SearchParts.Intent.awaitsDeveloperName term then
      false
    else
      let profilesOut =
        SearchTerms.peopleTerm model.Term |> ValueOption.isSome
        && (profilesOf model).Term = ""

      let commitsOut =
        SearchTerms.commitTerm model.Term |> ValueOption.isSome
        && (commitsOf model).Term = ""

      profilesOut || commitsOut

  // True when the server asked either channel to slow down.
  let throttled(model: Model) : bool =
    (profilesOf model).Throttled || (commitsOf model).Throttled

  // The compose action answers once the term starts one of its words,
  // the site's own three-letter floor.
  let composeAnswers(term: string) =
    let typed = Regex.Replace(term.Trim().ToLowerInvariant(), "\s+", " ")

    if String.length typed < 3 then
      false
    else
      List.exists
        (fun (keyword: string) ->
          keyword.StartsWith(typed, StringComparison.Ordinal))
        [ "commit"; "write a commit"; "new commit"; "compose" ]

  // The finds the term produced, before the quiet list stands in.
  let results(model: Model) : Entry list =
    let term = trimmed model

    if String.IsNullOrWhiteSpace term then
      []
    else
      let reading = SearchParts.Intent.ofTerm term

      let action = if composeAnswers term then [ Compose ] else []

      let answer = profilesOf model

      let people = [ for user in answer.Users -> Person user ]

      let organizations = [
        for organization in answer.Organizations -> Organization organization
      ]

      let found =
        if SearchParts.Intent.searchesCommits reading then
          commitsOf model
          |> fun answer -> [
            for node in answer.Commits do
              match node with
              | LiveCommit commit -> Found commit
              | Tombstone _ -> ()
          ]
        else
          []

      let seeAll =
        if SearchParts.Intent.searchesCommits reading then
          [ SeeAll ]
        else
          []

      [
        yield! action

        match reading with
        | SearchParts.Intent.Topic ->
          yield! found
          yield! seeAll
          yield! people
          yield! organizations
        | _ ->
          yield! people
          yield! organizations
          yield! found
          yield! seeAll
      ]

  // True when every channel the term reaches has answered and none of
  // them found anything.
  let foundNothing(model: Model) : bool =
    trimmed model <> ""
    && not(searching model)
    && not(throttled model)
    && results model |> List.isEmpty

  // The rows the list shows: the term's finds, or — while nothing is
  // typed or nothing was found — the recents and the suggestions.
  let entriesOf(model: Model) : Entry list =
    let quiet() = [
      for known in model.Recents -> Recent known
      for words in SearchParts.suggestions -> Suggestion words
    ]

    if trimmed model = "" || foundNothing model then
      quiet()
    else
      results model

  // The index clamped into the rows the list shows now.
  let clamped(model: Model) : int =
    let count = entriesOf model |> List.length

    if count = 0 then 0 else Math.Min(model.Active, count - 1)

  let loggerOf(props: Props<'env>) =
    props.Env.LoggerFactory.CreateLogger
      "DevNull.Client.Components.SearchDialog"

  let updateCore (log: ILogger, props: Props<'env>) msg model =
    let closeAndGo(url: string) =
      Cmd.batch [
        Cmd.ofEffect(fun _ -> Navigation.go(props.Nav, url, ignore))
        Cmd.ofAction props.Close
      ]

    // Both answers in and nothing asked to slow down: the term is
    // settled, so it becomes the account's newest recent.
    let settle(model: Model) : Model * Cmd<Msg> =
      let term = trimmed model

      let settled =
        not(String.IsNullOrWhiteSpace (profilesOf model).Term)
        || SearchTerms.peopleTerm model.Term |> ValueOption.isNone

      let commitsSettled =
        not(String.IsNullOrWhiteSpace (commitsOf model).Term)
        || SearchTerms.commitTerm model.Term |> ValueOption.isNone

      if
        (not(String.IsNullOrWhiteSpace term))
        && model.Remembered <> term
        && settled
        && commitsSettled
        && not(throttled model)
      then
        { model with Remembered = term },
        Cmd.ofAction(fun () ->
          props.Recents.Remember(props.SignedIn.Session.User.Id, term)

          log.LogDebug("The search for {Term} became a recent.", term))
      else
        model, Cmd.none

    match msg with
    | Typed text ->
      { model with Term = text; Active = 0 },
      Cmd.ofAction(fun () -> props.Searches.Set text)
    | ProfilesArrived found ->
      let settled, remember =
        settle {
          model with
              Profiles = ValueSome found
        }

      {
        settled with
            Active = clamped settled
      },
      remember
    | CommitsArrived found ->
      let settled, remember = settle { model with Commits = ValueSome found }

      {
        settled with
            Active = clamped settled
      },
      remember
    | Moved index ->
      let count = entriesOf model |> List.length

      let at =
        if count = 0 then
          0
        else
          Math.Max(0, Math.Min(index, count - 1))

      { model with Active = at }, Cmd.none
    | Stepped step ->
      let count = entriesOf model |> List.length

      let at =
        if count = 0 then
          0
        else
          (model.Active + step + count) % count

      { model with Active = at }, Cmd.none
    | Hovered index ->
      // A hover that lands on the row the keys already armed is quiet:
      // a pointer resting over a row must not fight the keys.
      if index = model.Active then
        model, Cmd.none
      else
        let count = entriesOf model |> List.length

        let at =
          if count = 0 then
            0
          else
            Math.Max(0, Math.Min(index, count - 1))

        { model with Active = at }, Cmd.none
    | PressedEnter ->
      model,
      entriesOf model
      |> List.tryItem model.Active
      |> Option.map(fun entry -> Cmd.ofMsg(Opened entry))
      |> Option.defaultValue Cmd.none
    | Opened entry ->
      match entry with
      | Compose ->
        model,
        Cmd.batch [ Cmd.ofAction props.Close; Cmd.ofAction props.OpenCompose ]
      | Person user ->
        model, closeAndGo("/profile/" + Uri.EscapeDataString user.Handle)
      | Organization organization ->
        model, closeAndGo("/org/" + Uri.EscapeDataString organization.Handle)
      | Found commit -> model, closeAndGo("/commit/" + commit.Id)
      | SeeAll -> model, closeAndGo("/search/" + trimmed model)
      | Recent term
      | Suggestion term ->
        { model with Term = term; Active = 0 },
        Cmd.ofAction(fun () -> props.Searches.Set term)
    | RecentsCleared ->
      { model with Recents = [] },
      Cmd.ofAction(fun () ->
        props.Recents.Clear(props.SignedIn.Session.User.Id))
    | ClockTicked moment -> { model with Now = moment }, Cmd.none

  let update(props: Props<'env>) = updateCore(loggerOf props, props)

  // The dialog's inlets: its session's two channels and the clock.
  let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
    [
      Sub.fromObservable
        "profiles"
        (props.Searches.Profiles |> Observable.map ProfilesArrived)

      Sub.fromObservable
        "commits"
        (props.Searches.Commits |> Observable.map CommitsArrived)

      Sub.fromObservable
        "clock"
        (props.Env.UiClock.Minute |> Observable.map ClockTicked)
    ]
    |> Sub.batch

// The dialog's semantic regions.
module Parts =

  // The status line the state asks for, or nothing: the slow-down
  // sentence, the lone-`@` hint, or the fatal line.
  let status(model: IObservable<State.Model>) : Control =
    model
    |> Observable.map(fun current ->
      if State.throttled current then
        "error: too many searches, give it a few seconds"
      elif SearchParts.Intent.awaitsDeveloperName(State.trimmed current) then
        "Type a name or handle to find a developer"
      elif State.foundNothing current then
        $"fatal: nothing matches '{State.trimmed current}'"
      else
        "")
    |> SearchParts.statusLineOf

  // The input row: the loupe, the box, and the esc chip — nothing
  // else rides in it, so the box's layout never moves. While a
  // channel is still out, a two-pixel indeterminate line runs under
  // the whole row, the site's quiet loading mark.
  let inputRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let box =
      TextBox()
        .Name("SearchInput")
        .MaxLength(100)
        .PlaceholderText("Search developers or commits")
        .Background(Brushes.Transparent)
        .BorderThickness(Thickness(0.0))
        .VerticalAlignment(VerticalAlignment.Center)
        .FontSize(15.0)
        .Text(model |> Observable.map(fun current -> current.Term))
        .OnTextChangedHandler(fun box _ ->
          box.Text |> Option.ofObj |> Option.iter(State.Typed >> dispatch))

    let moved(step: int) = dispatch(State.Stepped step)

    box.KeyDown.Add(fun args ->
      match args.Key with
      | Key.Up ->
        args.Handled <- true
        moved -1
      | Key.Down ->
        args.Handled <- true
        moved 1
      | Key.Enter ->
        args.Handled <- true
        dispatch State.PressedEnter
      | _ -> ())

    let esc: Control =
      let chip = SearchParts.kbdChip(KbdTone.Outline, "esc")

      chip
        .DockRight()
        .Margin(8.0, 0.0, 0, 0.0)
        .VerticalAlignmentCenter()
        .HorizontalAlignmentRight()

    let row =
      DockPanel()
        .LastChildFill(true)
        .Children(
          Icons.iconAt(Glyph.Search, 18.0).Margin(0, 0.0, 8.0, 0.0),
          esc,
          box
        )

    let loadingLine =
      ProgressBar()
        .IsIndeterminate(true)
        .Height(2.0)
        .MaxHeight(2.0)
        .Margin(0, 8)
        .IsVisible(model |> Observable.map State.searching)

    StackPanel().Margin(8, 16).Children(row, loadingLine)

  // One row of the list: the entry it shows, its walk index, and the
  // section heading it opens when the entry begins one.
  type Row =
    | Entry of entry: State.Entry * index: int * heading: string option
    | Waiting

  // The props one option row reads: the model's armed slice, the
  // message it opens, and the content it shows.
  [<Struct>]
  type OptionRow = {
    Model: IObservable<State.Model>
    Dispatch: Dispatch<State.Msg>
    Index: int
    Entry: State.Entry
    Content: Control
  }

  // One option row: its content, the armed wash and the enter chip
  // bound to the armed stream, and the click that opens it. The armed
  // state is a binding, so the row never rebuilds for it — a rebuilt
  // list is a replaced scroll content, and its place resets. The
  // chip's slot stays reserved through opacity, never visibility: a
  // row that arms must not re-flow its text.
  let optionRow(row: OptionRow) : Control =
    let armed =
      row.Model
      |> Observable.map(fun current -> current.Active = row.Index)
      |> Observable.distinctUntilChanged

    let shown = armed |> Observable.map(fun on -> if on then 1.0 else 0.0)

    // The armed row wears the primary-soft wash every selected or
    // focused row in the app wears, so its text keeps its own colors;
    // the same stream raises the enter chip at the right edge.
    let wash =
      Border()
        .Classes(Theme.name Theme.PrimarySoftBack)
        .Opacity(shown)
        .CornerRadius(8.0)

    let chip =
      Border()
        .Classes(Theme.name Theme.SurfaceBack)
        .Opacity(shown)
        .VerticalAlignmentCenter()
        .HorizontalAlignmentLeft()

    Button()
      .Classes(Theme.name Theme.GhostButton)
      .HorizontalAlignmentStretch()
      .VerticalAlignmentCenter()
      .Padding(0.0)
      .OnClickHandler(fun _ _ -> row.Dispatch(State.Opened row.Entry))
      .Margin(16, 8)
      .Content(
        Grid()
          .Name($"SearchRow{row.Index}")
          .Classes(Theme.name Theme.GhostButton)
          .Children(
            wash,
            DockPanel()
              .Classes(Theme.name Theme.GhostButton)
              .LastChildFill(true)
              .Margin(12.0, 8.0, 12.0, 8.0)
              .Children(
                chip
                  .Child(SearchParts.kbdChip(KbdTone.Outline, "↵"))
                  .DockRight(),
                row.Content
              )
          )
      )

  // The list: every entry under its heading, built once for the
  // entries it shows — never for the armed row or the clock, so the
  // scroll keeps its place while the keys or the pointer move.
  let list
    (
      ctx: SearchParts.RowCtx,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let headingOf(entry: State.Entry, seen: string) : string =
      match entry with
      | State.Compose -> if seen <> "action" then "Actions" else ""
      | State.Person _ -> if seen <> "developer" then "Developers" else ""
      | State.Organization _ ->
        if seen <> "organization" then "Organizations" else ""
      | State.Found _ ->
        if seen <> "commit" then "Most relevant commits" else ""
      | State.SeeAll -> ""
      | State.Recent _ -> if seen <> "recent" then "Recent searches" else ""
      | State.Suggestion _ ->
        if seen <> "suggestion" then "Try searching for…" else ""

    let mark(entry: State.Entry) =
      match entry with
      | State.Compose -> "action"
      | State.Person _ -> "developer"
      | State.Organization _ -> "organization"
      | State.Found _ -> "commit"
      | State.SeeAll -> ""
      | State.Recent _ -> "recent"
      | State.Suggestion _ -> "suggestion"

    let rowsOf(current: State.Model) : Row list =
      match State.entriesOf current with
      | [] -> [ Waiting ]
      | entries ->
        entries
        |> List.indexed
        |> List.mapFold
          (fun seen (index, entry) ->
            let heading = headingOf(entry, seen)

            Entry(entry, index, if heading = "" then None else Some heading),
            mark entry)
          ""
        |> fst

    let render(row: Row) : Palette.RenderedRow =
      match row with
      | Waiting -> {
          Leading = ValueNone
          Control = SearchParts.statusLine "Searching…"
          Followed = ValueNone
        }
      | Entry(entry, index, heading) ->
        let content: Control =
          match entry with
          | State.Compose ->
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(8.0)
              .Children(
                Icons.iconAt(Glyph.Pen, 16.0),
                TextBlock().FontSize(13.0).Text("Write a commit")
              )
          | State.Person user -> SearchParts.peopleIdentity(ctx, user)
          | State.Organization organization ->
            SearchParts.orgIdentity(ctx, organization)
          | State.Found commit -> SearchParts.commitIdentity(ctx, commit)
          | State.SeeAll -> SearchParts.seeAllIdentity()
          | State.Recent term
          | State.Suggestion term -> SearchParts.termIdentity term

        let option =
          optionRow {
            Model = model
            Dispatch = dispatch
            Index = index
            Entry = entry
            Content = content
          }

        let leading =
          heading |> Option.map SearchParts.heading |> ValueOption.ofOption

        {
          Leading = leading
          Control = option
          Followed = ValueSome option
        }

    Palette.view(
      {
        Name = "SearchDialogScroller"
        MaxHeight = 460.0
      },
      {
        Rows = model |> Observable.map rowsOf
        KeyOf = id
        Render = render
        Active = model |> Observable.map _.Active
        OnHover = ValueSome(State.Hovered >> dispatch)
      }
    )

  // The footer: the keys the dialog answers, as the site lists them.
  let footer() : Control =
    let pair(key: string, words: string) =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(
          SearchParts.kbdChip(KbdTone.Outline, key),
          TextBlock().FontSize(12.0).Text(words)
        )

    DockPanel()
      .LastChildFill(false)
      .Margin(16.0, 8.0, 16.0, 8.0)
      .Children(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(16.0)
          .Children(
            pair("↑↓", "to move"),
            pair("↵", "to open"),
            pair("@", "for developers")
          ),
        pair("Ctrl K", "anywhere").DockRight()
      )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let rowCtx: SearchParts.RowCtx = {
    Images = props.Env.ImageCache
    Flyout =
      ValueSome {
        Fetch = props.Env.Profiles.Fetch
        OnOpen =
          fun username ->
            Navigation.go(
              props.Nav,
              "/profile/" + Uri.EscapeDataString username,
              ignore
            )
        OnOpenOrg =
          fun handle ->
            Navigation.go(
              props.Nav,
              "/org/" + Uri.EscapeDataString handle,
              ignore
            )
        OnOpenTag =
          fun tag ->
            Navigation.go(
              props.Nav,
              "/search/" + Uri.EscapeDataString tag,
              ignore
            )
        Cache = props.Env.ImageCache
      }
    Now = DateTimeOffset.UtcNow
  }

  let listControl = Parts.list(rowCtx, model, dispatch)

  // The list takes the star row, the report card's shape: in a short
  // window the row shrinks and the list scrolls inside the card,
  // while the input and the footer keep their height.
  let cardGrid = Grid()
  cardGrid.RowDefinitions <- RowDefinitions("Auto,Auto,*,Auto")

  let card =
    Border()
      .Name("SearchDialogCard")
      .Classes(Theme.name Theme.Card)
      .MaxWidth(560.0)
      .Margin(24.0, 96.0, 24.0, 24.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Top)
      .Child(
        cardGrid.Children(
          Parts.inputRow(model, dispatch).Row(0),
          (Parts.status model).Row(1),
          listControl.Row(2),
          Border()
            .Classes(Theme.name Theme.Hairline)
            .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
            .Child(Parts.footer())
            .Row(3)
        )
      )

  card.AttachedToVisualTree.Add(fun _ ->
    props.Env.Dispatcher.Post(
      (fun () ->
        card.GetVisualDescendants()
        |> Seq.tryFind(fun visual -> visual.Name = "SearchInput")
        |> Option.iter(fun visual -> (visual :?> TextBox).Focus() |> ignore)),
      DispatcherPriority.Default
    ))

  card

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  let program =
    Program.mkProgram (State.initial props) (State.update props) (view props)
    |> Program.withSubscription(State.subscribe props)

  let root, component =
    Programs.runComponent(this, props.Env.Dispatcher, program)

  let lifetime =
    Disposable.Create(fun () ->
      // Park the top-level so focus can leave the input before the page
      // detaches; a focused input keeps its caret animation alive.
      match this.Tag with
      | :? TopLevel as top ->
        let inside = this.GetVisualDescendants() |> Seq.toList

        let target =
          top.GetVisualDescendants()
          |> Seq.tryPick(function
            | :? Button as button when
              button.Focusable
              && not(
                inside
                |> List.exists(fun visual ->
                  obj.ReferenceEquals(visual, button))
              )
              ->
              Some(button :> IInputElement)
            | _ -> None)

        target
        |> Option.iter(fun control ->
          top.FocusManager.Focus(
            control,
            NavigationMethod.Unspecified,
            KeyModifiers.None
          )
          |> ignore)
      | _ -> ()

      this.Tag <- null

      // An indeterminate bar keeps an animation clock alive; stop it
      // before the page's content is released.
      this.GetVisualDescendants()
      |> Seq.choose(function
        | :? ProgressBar as bar -> Some bar
        | _ -> None)
      |> Seq.iter(fun bar -> bar.IsIndeterminate <- false)

      this.Content <- null
      (props.Searches :> IDisposable).Dispose()
      component.Dispose())

  do
    this.Content <- root

    // The close path reads the top-level back from Tag; the page itself
    // does not otherwise use it.
    this.AttachedToVisualTree.Add(fun _ ->
      this.Tag <- TopLevel.GetTopLevel(this))

    // The overlay's content is released on close; the session's pipes
    // go with it.
    this.DetachedFromVisualTree.Add(fun _ -> lifetime.Dispose())

  interface IDisposable with
    member _.Dispose() = lifetime.Dispose()
