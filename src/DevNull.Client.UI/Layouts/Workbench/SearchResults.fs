module DevNull.Client.Workbench.SearchResults

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Templates
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
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Theme

type Model = {
  Term: string
  Loading: bool
  Error: AppError option
  Results: CommitNode list
  Now: DateTimeOffset
}

type Msg =
  | Fetched of Result<CommitNode list, AppError>
  | RefreshRequested
  | ClockTicked of moment: DateTimeOffset
  | Opened of Commit
  | QuoteRequested of Commit
  | ExternalOpened of url: string
  | CloseRequested

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
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
}

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
  let polls = Observable.single(Map.empty: Map<string, string>)
  let translation = Observable.single(fun (_: Commit) -> ValueNone)

  let rowProps: RowProps = {
    OnEvent =
      fun event ->
        match event with
        | CommitCard.Opened commit -> dispatch(Opened commit)
        | CommitCard.QuoteRequested commit -> dispatch(QuoteRequested commit)
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
    Star = ValueNone
    Stars = ValueNone
    Vote = ValueNone
    Pastel = ValueNone
    Menu = ValueNone
    Translation = translation
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
      .Content("Refresh")
      .OnClickHandler(fun _ _ -> dispatch RefreshRequested)

  let close =
    Button()
      .Name("SearchClose")
      .Classes(Theme.name Theme.GhostButton)
      .Content("Close")
      .OnClickHandler(fun _ _ -> dispatch CloseRequested)

  let head = DockPanel().LastChildFill(true)

  head.Children(
    close.DockRight(),
    TextBlock()
      .Name("SearchHeader")
      .Classes(Theme.name Theme.Mono)
      .Text("Search · manual refresh")
  )
  |> ignore

  let term =
    TextBlock()
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .Text("term: " + props.Term)

  let loadingLine =
    PageChrome.messagePanel(
      model
      |> Observable.map(fun current ->
        if current.Loading then "Searching commits…" else ""),
      Theme.Muted
    )

  let emptyLine =
    PageChrome.messagePanel(
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
    StackPanel()
      .Spacing(6.0)
      .Margin(Thickness(16.0, 16.0, 16.0, 8.0))
      .Children(head, term, refresh, loadingLine, emptyLine, errorLine)

  let body =
    ScrollViewer()
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
      .Content(rows)

  let grid = Grid()

  grid.RowDefinitions <- RowDefinitions("Auto,*")

  grid.Children(header.Row(0), body.Row(1)) |> ignore

  Border().Name("SearchCenter").Child(grid)

let program(props: Props<'env>) =
  Program.mkProgram
    (fun () -> init props)
    (fun msg model -> update(props, msg, model))
    (view props)
  |> Program.withSubscription(subscribe props)
