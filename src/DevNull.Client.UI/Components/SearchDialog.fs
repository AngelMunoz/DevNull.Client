module DevNull.Client.Components.SearchDialog

open System
open System.Reactive.Subjects
open System.Text.RegularExpressions
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open Avalonia.VisualTree
open NXUI.Extensions
open FSharp.Control.Reactive
open IcedTasks
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
open DevNull.Client.Navigation
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Search
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.UiClock

/// Everything the search dialog reads: the env it searches through,
/// the signed account it searches as, the session that owns the
/// debounced calls, the recents it shows when the box is empty, the
/// router it opens finds on, and the two things beside finding —
/// opening the composer and closing itself.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging> = {
  Env: 'env
  SignedIn: SignedInSession
  Searches: Session
  Recents: RecentSearchStore
  Nav: INavigable
  OpenCompose: unit -> unit
  Close: unit -> unit
}

/// The state logic: model, messages, update, and the subscription that
/// feeds the dialog from its session and the clock.
module State =

  /// One row of the dialog's list.
  type Entry =
    | Compose
    | Person of User
    | Found of Commit
    | SeeAll
    | Recent of string
    | Suggestion of string

  type Model = {
    Term: string
    Active: int
    People: PeopleFound voption
    Commits: CommitsFound voption
    Recents: string list
    Now: DateTimeOffset
    /// The term whose settle was last written to the recents.
    Remembered: string
  }

  type Msg =
    | Typed of string
    | PeopleArrived of PeopleFound
    | CommitsArrived of CommitsFound
    | Moved of index: int
    | Stepped of step: int
    | Hovered of index: int
    | PressedEnter
    | Opened of Entry
    | RecentsCleared
    | ClockTicked of moment: DateTimeOffset

  let initial (props: Props<'env>) () : Model * Cmd<Msg> =
    let model = {
      Term = ""
      Active = 0
      People = ValueNone
      Commits = ValueNone
      Recents = props.Recents.Recent(props.SignedIn.Session.User.Id)
      Now = DateTimeOffset.UtcNow
      Remembered = ""
    }

    model, Cmd.none

  let inline trimmed(model: Model) = model.Term.Trim()

  /// The people answer that belongs to the term now typed, when it has
  /// settled. A channel with nothing to search for counts as settled.
  let peopleOf(model: Model) : PeopleFound =
    match SearchTerms.peopleTerm model.Term, model.People with
    | ValueSome term, ValueSome found when found.Term = term -> found
    | _ -> {
        Term = ""
        Users = []
        Throttled = false
      }

  /// The commit answer that belongs to the term now typed, when it has
  /// settled. A channel with nothing to search for counts as settled.
  let commitsOf(model: Model) : CommitsFound =
    match SearchTerms.commitTerm model.Term, model.Commits with
    | ValueSome term, ValueSome found when found.Term = term -> found
    | _ -> {
        Term = ""
        Commits = []
        Throttled = false
      }

  /// True while the term owes an answer from a channel it reaches.
  let searching(model: Model) : bool =
    let term = trimmed model

    if term = "" || SearchParts.Intent.awaitsDeveloperName term then
      false
    else
      let peopleOut =
        SearchTerms.peopleTerm model.Term |> ValueOption.isSome
        && (peopleOf model).Term = ""

      let commitsOut =
        SearchTerms.commitTerm model.Term |> ValueOption.isSome
        && (commitsOf model).Term = ""

      peopleOut || commitsOut

  /// True when the server asked either channel to slow down.
  let throttled(model: Model) : bool =
    (peopleOf model).Throttled || (commitsOf model).Throttled

  /// The compose action answers once the term starts one of its words,
  /// the site's own three-letter floor.
  let composeAnswers(term: string) =
    let typed = Regex.Replace(term.Trim().ToLowerInvariant(), "\s+", " ")

    if String.length typed < 3 then
      false
    else
      List.exists
        (fun (keyword: string) ->
          keyword.StartsWith(typed, StringComparison.Ordinal))
        [ "commit"; "write a commit"; "new commit"; "compose" ]

  /// The finds the term produced, before the quiet list stands in.
  let results(model: Model) : Entry list =
    let term = trimmed model

    if String.IsNullOrWhiteSpace term then
      []
    else
      let reading = SearchParts.Intent.ofTerm term

      let action = if composeAnswers term then [ Compose ] else []

      let people =
        peopleOf model
        |> fun found -> [ for user in found.Users -> Person user ]

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

      if reading = SearchParts.Intent.Topic then
        action @ found @ seeAll @ people
      else
        action @ people @ found @ seeAll

  /// True when every channel the term reaches has answered and none of
  /// them found anything.
  let foundNothing(model: Model) : bool =
    trimmed model <> ""
    && not(searching model)
    && not(throttled model)
    && results model |> List.isEmpty

  /// The rows the list shows: the term's finds, or — while nothing is
  /// typed or nothing was found — the recents and the suggestions.
  let entriesOf(model: Model) : Entry list =
    let quiet() =
      [ for known in model.Recents -> Recent known ]
      @ [ for words in SearchParts.suggestions -> Suggestion words ]

    if trimmed model = "" || foundNothing model then
      quiet()
    else
      results model

  /// The index clamped into the rows the list shows now.
  let clamped(model: Model) : int =
    let count = entriesOf model |> List.length

    if count = 0 then 0 else Math.Min(model.Active, count - 1)

  let update (props: Props<'env>) msg model =
    let log =
      props.Env.LoggerFactory.CreateLogger
        "DevNull.Client.Components.SearchDialog"

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
        not(String.IsNullOrWhiteSpace (peopleOf model).Term)
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
    | PeopleArrived found ->
      let settled, remember = settle { model with People = ValueSome found }

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
      | Found commit -> model, closeAndGo("/commit/" + commit.Id)
      | SeeAll -> model, closeAndGo("/search/" + trimmed model)
      | Recent term
      | Suggestion term ->
        { model with Term = term; Active = 0 },
        Cmd.ofAction(fun () -> props.Searches.Set term)
    | RecentsCleared ->
      props.Recents.Clear(props.SignedIn.Session.User.Id)
      { model with Recents = [] }, Cmd.none
    | ClockTicked moment -> { model with Now = moment }, Cmd.none

  /// The dialog's inlets: its session's two channels and the clock.
  /// The view fills the lane holder with its own subscription.
  let subscribe
    (props: Props<'env>, lane: BehaviorSubject<Sub<Msg>>)
    (_: Model)
    : Sub<Msg> =
    [
      Sub.fromObservable
        "people"
        (props.Searches.People |> Observable.map PeopleArrived)

      Sub.fromObservable
        "commits"
        (props.Searches.Commits |> Observable.map CommitsArrived)

      Sub.fromObservable
        "clock"
        (props.Env.UiClock.Minute |> Observable.map ClockTicked)

      Sub.ofId "search-lane-holder" (fun _ -> {
        new IDisposable with
          member _.Dispose() =
            lane.OnCompleted()
            lane.Dispose()
      })

      lane.Value
    ]
    |> Sub.batch

/// The props and the lane subscription one dialog view reads and fills.
type ViewCtx<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging> = {
  Props: Props<'env>
  Lane: BehaviorSubject<Sub<State.Msg>>
}

/// The dialog's semantic regions.
module Parts =

  /// One keyboard chip, the site's kbd look.
  let keyChip(key: string) : Control =
    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(4.0)
      .Padding(6.0, 1.0, 6.0, 1.0)
      .Child(
        TextBlock().Classes(Theme.name Theme.Mono).FontSize(12.0).Text(key)
      )

  /// The status line the state asks for, or nothing: the slow-down
  /// sentence, the lone-`@` hint, or the fatal line.
  let status(model: IObservable<State.Model>) : Control =
    let line =
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
      |> Observable.distinctUntilChanged

    let block =
      TextBlock()
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
        .FontSize(13.0)
        .Margin(20.0, 12.0, 20.0, 12.0)
        .Text(line |> _.ToBinding())

    Border()
      .Child(block)
      .IsVisible(line |> Observable.map(String.IsNullOrWhiteSpace >> not))

  /// The input row: the loupe, the box, and the esc chip — nothing
  /// else rides in it, so the box's layout never moves. While a
  /// channel is still out, a two-pixel indeterminate line runs under
  /// the whole row, the site's quiet loading mark.
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

    box.TextChanged
    |> Observable.add(fun _ ->
      match box.Text with
      | null -> ()
      | value -> dispatch(State.Typed value))

    let esc: Control =
      let chip = keyChip "esc"

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

  /// One option row: its content, the armed surface and the enter chip
  /// bound to the armed stream, hover arming on a real pointer move,
  /// and the click that opens it. The armed state is a binding, so the
  /// row never rebuilds for it — a rebuilt list is a replaced scroll
  /// content, and its place resets. The chip's slot stays reserved
  /// through opacity, never visibility: a row that arms must not
  /// re-flow its text.
  let optionRow
    (
      ctx: SearchParts.RowCtx,
      dispatch: Dispatch<State.Msg>,
      armed: IObservable<bool>,
      index: int,
      entry: State.Entry,
      content: Control
    ) : Control =
    let shown = armed |> Observable.map(fun on -> if on then 1.0 else 0.0)

    let enter: Control = keyChip "↵"

    let surface =
      Border()
        .Classes(Theme.name Theme.SurfaceBack)
        .Opacity(shown)
        .VerticalAlignmentCenter()
        .HorizontalAlignmentLeft()

    Button()
      .Classes(Theme.name Theme.GhostButton)
      .HorizontalAlignmentStretch()
      .VerticalAlignmentCenter()
      .OnClickHandler(fun _ _ -> dispatch(State.Opened entry))
      .OnPointerEnteredHandler(fun _ _ -> dispatch(State.Hovered index))
      .Margin(16, 8)
      .Content(
        DockPanel()
          .Name($"SearchRow{index}")
          .Classes(Theme.name Theme.GhostButton)
          .LastChildFill(true)
          .Children(surface.Child(enter).DockRight(), content.DockLeft())
      )


  /// The list: every entry under its heading, built once for the
  /// entries it shows — never for the armed row or the clock, so the
  /// scroll keeps its place while the keys or the pointer move.
  let list
    (
      ctx: SearchParts.RowCtx,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control * Sub<State.Msg> =
    let rows = new BehaviorSubject<Control array>([||])

    let build(entries: State.Entry list) : Control =
      let headingOf(entry: State.Entry, seen: string) : string =
        match entry with
        | State.Compose -> if seen <> "action" then "Actions" else ""
        | State.Person _ -> if seen <> "developer" then "Developers" else ""
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
        | State.Found _ -> "commit"
        | State.SeeAll -> ""
        | State.Recent _ -> "recent"
        | State.Suggestion _ -> "suggestion"

      let rowOf
        (seen: string, index: int, entry: State.Entry)
        : Control option * Control list =
        let headingText = headingOf(entry, seen)

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
          | State.Found commit -> SearchParts.commitIdentity(ctx, commit)
          | State.SeeAll -> SearchParts.seeAllIdentity()
          | State.Recent term
          | State.Suggestion term -> SearchParts.termIdentity term

        let armed =
          model |> Observable.map(fun current -> current.Active = index)

        let option = optionRow(ctx, dispatch, armed, index, entry, content)

        let children = [
          if headingText <> "" then
            SearchParts.heading headingText

          option
        ]

        Some option, children

      let rowsAndChildren =
        List.indexed entries
        |> List.mapFold
          (fun seen (index, entry) -> rowOf(seen, index, entry), mark entry)
          ""
        |> fst

      let children = rowsAndChildren |> List.collect snd |> Array.ofList

      let controls = rowsAndChildren |> List.choose fst |> Array.ofList

      let built =
        if children.Length = 0 then
          SearchParts.statusLine "Searching…"
        else
          ItemsControl().ItemsSource(children)

      // The rows attach when the content control takes the list. The
      // post runs after that, so the armed row can find its scroller.
      Dispatcher.UIThread.Post(fun () -> rows.OnNext controls)

      built

    let content =
      model
      |> Observable.map State.entriesOf
      |> Observable.distinctUntilChanged
      |> Observable.map build

    let scroller =
      ScrollViewer().MaxHeight(460.0).Content(content |> _.ToBinding())

    // The keys' row rides into view the moment it becomes armed, and
    // when a new answer replaces the rows while row zero is armed. The
    // subscription retires with the scroller, the way the feed's does.
    let followSub =
      Sub.ofId "follow-armed" (fun _ ->
        let feed =
          SearchParts.followRows(
            (model |> Observable.map(fun current -> current.Active)),
            rows :> IObservable<Control array>
          )

        {
          new IDisposable with
            member _.Dispose() =
              feed.Dispose()
              rows.OnCompleted()
        })
      |> Sub.untilDetached scroller

    scroller, followSub

  /// The footer: the keys the dialog answers, as the site lists them.
  let footer() : Control =
    let pair(key: string, words: string) =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(keyChip key, TextBlock().FontSize(12.0).Text(words))

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
  (viewCtx: ViewCtx<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let props = viewCtx.Props

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
        Cache = props.Env.ImageCache
      }
    Now = DateTimeOffset.UtcNow
  }

  let listControl, followSub = Parts.list(rowCtx, model, dispatch)

  viewCtx.Lane.OnNext followSub

  let card =
    Border()
      .Name("SearchDialogCard")
      .Classes(Theme.name Theme.Card)
      .MaxWidth(560.0)
      .Margin(24.0, 96.0, 24.0, 24.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Top)
      .Child(
        StackPanel()
          .Children(
            Parts.inputRow(model, dispatch),
            Parts.status model,
            listControl,
            Border()
              .Classes(Theme.name Theme.Hairline)
              .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
              .Child(Parts.footer())
          )
      )

  card.AttachedToVisualTree.Add(fun _ ->
    Dispatcher.UIThread.Post(fun () ->
      card.GetVisualDescendants()
      |> Seq.tryFind(fun visual -> visual.Name = "SearchInput")
      |> Option.iter(fun visual -> (visual :?> TextBox).Focus() |> ignore)))

  card

/// The search dialog: built on open, torn down on detach.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let lane = new BehaviorSubject<Sub<State.Msg>>(Sub.none)

    let program =
      Program.mkProgram
        (State.initial props)
        (State.update props)
        (view { Props = props; Lane = lane })
      |> Program.withSubscription(State.subscribe(props, lane))

    let root, _ = Program.runWithLifetime this () program

    this.Content <- root

    // The overlay's content is released on close; the session's pipes
    // go with it.
    this.DetachedFromVisualTree.Add(fun _ ->
      (props.Searches :> IDisposable).Dispose())
