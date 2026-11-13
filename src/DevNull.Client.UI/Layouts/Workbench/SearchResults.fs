module DevNull.Client.Workbench.SearchResults

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Templates
open Avalonia.Input
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Env
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore

type Model = {
  Term: string
  Loading: bool
  Error: AppError option
  Results: CommitNode list
  PollChoices: Map<string, string>
  Now: DateTimeOffset
}

type Msg =
  | Fetched of Result<CommitNode list, AppError>
  | RefreshRequested
  | FirstResultRequested
  | ClockTicked of moment: DateTimeOffset
  | Opened of Commit
  | QuoteRequested of Commit
  | ExternalOpened of url: string
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | PollVoteRequested of Commit
  | PollVotedDone of Result<Commit, AppError>
  | CommitUpdated of Commit
  | CommitDeleted of string
  | CloseRequested

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession option
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  Term: string
  OnOpenThread: Commit -> unit
  OnQuote: Commit -> unit
  OnOpenExternal: string -> unit
  OnClose: unit -> unit
  OnDialog: RowActions.Dialog -> unit
}

let replaceNode(items: CommitNode list, updated: Commit) =
  items
  |> List.map(function
    | LiveCommit commit when commit.Id = updated.Id -> LiveCommit updated
    | node -> node)

let removeNode(items: CommitNode list, commitId: string) =
  items
  |> List.filter(function
    | LiveCommit commit -> commit.Id <> commitId
    | Tombstone _ -> true)

let vote
  (props: Props<'env>, commit: Commit, option: PollOption)
  : Async<Result<Commit, AppError>> =
  Commits.vote(
    props.Env,
    {
      KeyFile = props.KeyFile
      CommitId = commit.Id
      OptionId = option.Id
    }
  )

let fetch(props: Props<'env>) : Async<Result<CommitNode list, AppError>> =
  Search.commits(
    props.Env,
    {
      KeyFile = props.KeyFile
      Term = props.Term
      Limit = Some 30
    }
  )

let init(props: Props<'env>) : Model * Cmd<Msg> =
  ({
    Term = props.Term
    Loading = true
    Error = None
    Results = []
    PollChoices = Map.empty
    Now = DateTimeOffset.UtcNow
   },
   Cmd.OfAsync.perform fetch props Fetched)

let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
  match msg with
  | Fetched result ->
    match result with
    | Ok results ->
      ({
        model with
            Loading = false
            Error = None
            Results = results
       },
       Cmd.none)
    | Error error ->
      ({
        model with
            Loading = false
            Error = Some error
       },
       Cmd.none)
  | RefreshRequested ->
    if model.Loading then
      model, Cmd.none
    else
      ({
        model with
            Loading = true
            Error = None
       },
       Cmd.OfAsync.perform fetch props Fetched)
  | FirstResultRequested ->
    match
      model.Results
      |> List.tryPick(function
        | LiveCommit commit -> Some commit
        | Tombstone _ -> None)
    with
    | Some commit -> model, Cmd.ofAction(fun () -> props.OnOpenThread commit)
    | None -> model, Cmd.none
  | PollChoiceChanged(commit, option, chosen) ->
    ({
      model with
          PollChoices =
            CommitCard.choose {
              Choices = model.PollChoices
              CommitId = commit.Id
              OptionId = option.Id
              Chosen = chosen
            }
     },
     Cmd.none)
  | PollVoteRequested commit ->
    match CommitCard.selectedOption(model.PollChoices, commit) with
    | Some option ->
      ({ model with Error = None },
       Cmd.OfAsync.perform vote (props, commit, option) PollVotedDone)
    | None -> model, Cmd.none
  | PollVotedDone result ->
    match result with
    | Ok updated ->
      ({
        model with
            Results = replaceNode(model.Results, updated)
            PollChoices = model.PollChoices |> Map.remove updated.Id
       },
       Cmd.none)
    | Error error -> ({ model with Error = Some error }, Cmd.none)
  | CommitUpdated updated ->
    ({
      model with
          Results = replaceNode(model.Results, updated)
     },
     Cmd.none)
  | CommitDeleted commitId ->
    ({
      model with
          Results = removeNode(model.Results, commitId)
     },
     Cmd.none)
  | ClockTicked moment -> ({ model with Now = moment }, Cmd.none)
  | Opened commit -> model, Cmd.ofAction(fun () -> props.OnOpenThread commit)
  | QuoteRequested commit -> model, Cmd.ofAction(fun () -> props.OnQuote commit)
  | ExternalOpened url ->
    model, Cmd.ofAction(fun () -> props.OnOpenExternal url)
  | CloseRequested -> model, Cmd.ofAction props.OnClose

let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
  Sub.fromObservable "clock" (props.Now |> Observable.map ClockTicked)

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let now = model |> Observable.map _.Now |> Slices.share
  let pasteis = Observable.single props.Pasteis
  let polls = model |> Observable.map _.PollChoices |> Slices.share

  let actions =
    props.Session
    |> Option.map(fun session ->
      RowActions.create {
        Env = props.Env
        Session = session
        Now = props.Now
        OfferBasicActions = true
        OnUpdated = CommitUpdated >> dispatch
        OnDeleted = CommitDeleted >> dispatch
        OnDialog = props.OnDialog
      })

  let busy =
    actions
    |> Option.map _.Busy
    |> Option.defaultValue(Observable.single(None: string option))

  let inline slot(command: Commit -> unit) : ActionSlot voption =
    ValueSome { Command = command; Busy = busy }

  let rowProps: RowProps = {
    OnEvent =
      fun event ->
        actions |> Option.iter(fun current -> current.Handle event)

        match event with
        | CommitCard.Opened commit -> dispatch(Opened commit)
        | CommitCard.ReplyToggled commit -> dispatch(Opened commit)
        | CommitCard.QuoteRequested commit -> dispatch(QuoteRequested commit)
        | CommitCard.PollChoiceChanged(commit, option, chosen) ->
          dispatch(PollChoiceChanged(commit, option, chosen))
        | _ -> ()
    OnOpenExternal = fun url -> dispatch(ExternalOpened url)
    ViewerId = props.ViewerId
    Pasteis = pasteis
    Now = now
    PollChoices = polls
    Dense = true
    Images = props.Images
    Flyout = ValueNone
    Pin = ValueNone
    Star =
      match actions with
      | Some current -> current.Star
      | None -> ValueNone
    Stars = ValueNone
    Vote = slot(fun commit -> dispatch(PollVoteRequested commit))
    Pastel =
      match actions with
      | Some current -> current.Pastel
      | None -> ValueNone
    Menu =
      match actions with
      | Some current -> current.Menu
      | None -> ValueNone
    Translation =
      actions
      |> Option.map _.Translation
      |> Option.defaultValue(Observable.single(fun (_: Commit) -> ValueNone))
    Zone = props.Zone
  }

  let rows =
    ItemsControl()
      .Name("SearchRows")
      .ItemTemplate(
        FuncDataTemplate<CommitNode>(
          (fun node _ ->
            if isNull(box node) then
              Panel() :> Control
            else
              CommitCard.feedRow rowProps node),
          false
        )
      )
      .BindItemsSource(
        model
        |> Observable.map(fun current ->
          current.Results :> IReadOnlyList<CommitNode>),
        FeedParts.nodeKey
      )

  let refresh =
    Button()
      .Name("SearchRefresh")
      .Classes(Theme.name Theme.GhostButton)
      .Content(Icons.iconAt(Icons.Glyph.Refresh, 12.0))
      .OnClickHandler(fun _ _ -> dispatch RefreshRequested)

  ToolTip.SetTip(refresh, "Refresh results")

  let close =
    Button()
      .Name("SearchClose")
      .Classes(Theme.name Theme.GhostButton)
      .Content(Icons.iconAt(Icons.Glyph.Close, 12.0))
      .OnClickHandler(fun _ _ -> dispatch CloseRequested)

  ToolTip.SetTip(close, "Close search")

  let head =
    DockPanel()
      .LastChildFill(true)
      .Children(
        close.DockRight(),
        refresh.DockRight().Margin(Thickness(0.0, 0.0, 4.0, 0.0)),
        TextBlock()
          .Name("SearchSection")
          .Classes(Theme.name Theme.Mono)
          .Text("COMMITS")
      )

  let summary =
    TextBlock()
      .Name("SearchHeader")
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .Text("Search · manual refresh")

  let term =
    TextBlock()
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .Text("term: " + props.Term)

  let loadingLine =
    PageChrome.collapsingMessagePanel(
      model
      |> Observable.map(fun current ->
        if current.Loading then "Searching commits…" else ""),
      Theme.Muted
    )

  let emptyLine =
    PageChrome.collapsingMessagePanel(
      model
      |> Observable.map(fun current ->
        if
          current.Error.IsNone
          && not current.Loading
          && current.Results.IsEmpty
        then
          "No commits match this search."
        else
          ""),
      Theme.Muted
    )

  let errorLine = PageChrome.errorPanel(model |> Observable.map _.Error)

  let header =
    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Padding(Thickness(16.0, 12.0, 16.0, 10.0))
      .Child(
        StackPanel()
          .Spacing(4.0)
          .Children(head, summary, term, loadingLine, emptyLine, errorLine)
      )

  let body =
    ScrollViewer()
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
      .Content(rows)

  let grid = Grid()

  grid.RowDefinitions <- RowDefinitions("Auto,*")

  grid.Children(header.Row(0), body.Row(1)) |> ignore

  let center =
    Border()
      .Name("SearchCenter")
      .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
      .Focusable(true)
      .OnKeyDownHandler(fun _ args ->
        if args.Key = Key.Enter && args.KeyModifiers = KeyModifiers.None then
          args.Handled <- true
          dispatch FirstResultRequested
        elif args.Key = Key.Escape then
          args.Handled <- true
          dispatch CloseRequested)
      .Child(grid)

  center.AttachedToVisualTree.Add(fun _ -> center.Focus() |> ignore)
  center

let program(props: Props<'env>) =
  Program.mkProgram
    (fun () -> init props)
    (fun msg model -> update(props, msg, model))
    (view props)
  |> Program.withSubscription(subscribe props)
