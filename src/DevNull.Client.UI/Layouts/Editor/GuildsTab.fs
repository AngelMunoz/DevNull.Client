module DevNull.Client.Editor.GuildsTab

open System
open System.Reactive.Disposables
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.AsyncImage
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.UiClock

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideDispatcher> = {
  Env: 'env
  Session: SignedInSession
  Zone: Zone
  Now: unit -> DateTimeOffset
  OnOpenGuild: string -> unit
  OnCount: int -> unit
}

module State =

  [<Struct>]
  type Ask = { Query: string option; Page: int }

  type Model = {
    Term: string
    Rows: Guild list
    Settled: Ask voption
    Out: Ask voption
    Wanted: Ask voption
    Total: int
    LastPage: int
    Refused: bool
  }

  type Msg =
    | TermTyped of term: string
    | SearchAsked
    | PrevPage
    | NextPage
    | Arrived of ask: Ask * Result<GuildsPage, AppError>
    | Opened of handle: string

  let read
    (props: Props<'env>, ask: Ask)
    : Async<Ask * Result<GuildsPage, AppError>> = async {
    let! outcome =
      Guilds.list(
        props.Env,
        {
          KeyFile = props.Session.KeyFile
          Query = ask.Query
          Page = ask.Page
        }
      )

    return ask, outcome
  }

  let fire(props: Props<'env>, model: Model, ask: Ask) : Model * Cmd<Msg> =
    match model.Out with
    | ValueSome _ -> { model with Wanted = ValueSome ask }, Cmd.none
    | ValueNone ->
      {
        model with
            Out = ValueSome ask
            Wanted = ValueNone
      },
      Cmd.OfAsync.perform read (props, ask) Arrived

  let init(props: Props<'env>) : Model * Cmd<Msg> =
    fire(
      props,
      {
        Term = ""
        Rows = []
        Settled = ValueNone
        Out = ValueNone
        Wanted = ValueNone
        Total = 0
        LastPage = 1
        Refused = false
      },
      { Query = None; Page = 1 }
    )

  let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
    match msg with
    | TermTyped term -> { model with Term = term }, Cmd.none
    | SearchAsked ->
      let ask = {
        Query = SearchTerms.guildTerm model.Term |> ValueOption.toOption
        Page = 1
      }

      if model.Settled = ValueSome ask then
        model, Cmd.none
      else
        fire(props, model, ask)
    | PrevPage ->
      match model.Settled with
      | ValueSome settled when settled.Page > 1 ->
        fire(props, model, { settled with Page = settled.Page - 1 })
      | _ -> model, Cmd.none
    | NextPage ->
      match model.Settled with
      | ValueSome settled when settled.Page < model.LastPage ->
        fire(props, model, { settled with Page = settled.Page + 1 })
      | _ -> model, Cmd.none
    | Arrived(ask, outcome) ->
      if model.Out = ValueSome ask then
        let landed =
          match outcome with
          | Ok answered -> {
              model with
                  Rows = answered.Items
                  Settled = ValueSome ask
                  Total = answered.Total
                  LastPage = answered.LastPage
                  Refused = false
            }
          | Error(Refused(status, _, _)) when status = 404 -> {
              model with
                  Refused = true
            }
          | Error _ -> model

        let resting = { landed with Out = ValueNone }

        let countCmd =
          match outcome with
          | Ok answered -> Cmd.ofAction(fun () -> props.OnCount answered.Total)
          | Error _ -> Cmd.none

        match resting.Wanted with
        | ValueSome wanted ->
          let next, cmd = fire(props, resting, wanted)
          next, Cmd.batch [ countCmd; cmd ]
        | ValueNone -> resting, countCmd
      else
        model, Cmd.none
    | Opened handle -> model, Cmd.ofAction(fun () -> props.OnOpenGuild handle)

let inline factsOf(guild: Guild) : string =
  [
    if guild.IsJoined then
      "joined"
    if guild.IsPaused then
      "paused"
    $"{shortCount guild.MemberCount} members"
  ]
  |> String.concat " · "

let markOf(props: Props<'env>, guild: Guild) : Control =
  match guild.IconUrl with
  | Some url ->
    Border()
      .Width(24.0)
      .Height(24.0)
      .CornerRadius(CornerRadius 6.0)
      .ClipToBounds(true)
      .Child(
        AsyncImage.view {
          Cache = props.Env.ImageCache
          Url = url
          DecodeWidth = 48
          DecodeHeight = ValueNone
          Stretch = Stretch.UniformToFill
          ToolTip = ValueSome guild.Name
        }
      )
  | None ->
    Border()
      .Classes(Theme.name Theme.SurfaceBack)
      .Width(24.0)
      .Height(24.0)
      .CornerRadius(CornerRadius 6.0)
      .Child(
        TextBlock()
          .FontSize(16.0)
          .VerticalAlignment(VerticalAlignment.Center)
          .HorizontalAlignment(HorizontalAlignment.Center)
          .Text(guild.Emoji)
      )

let row
  (props: Props<'env>, guild: Guild, dispatch: Dispatch<State.Msg>)
  : Control =
  let grid =
    Grid(ColumnDefinitions = ColumnDefinitions "28,120,*,150")
      .Children(
        markOf(props, guild).Column(0),
        TextBlock()
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .FontSize(12.0)
          .Margin(Thickness(8.0, 0.0, 0.0, 0.0))
          .VerticalAlignment(VerticalAlignment.Center)
          .TextTrimming(TextTrimming.CharacterEllipsis)
          .Text("#" + guild.Handle)
          .Column(1),
        TextBlock()
          .Classes(Theme.name Theme.Subtitle)
          .FontSize(13.0)
          .Margin(Thickness(0.0, 0.0, 12.0, 0.0))
          .TextTrimming(TextTrimming.CharacterEllipsis)
          .VerticalAlignment(VerticalAlignment.Center)
          .Text(guild.Name)
          .Column(2),
        TextBlock()
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .FontSize(12.0)
          .TextTrimming(TextTrimming.CharacterEllipsis)
          .VerticalAlignment(VerticalAlignment.Center)
          .Text(factsOf guild)
          .Column(3)
      )

  Border()
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
    .Child(
      Button()
        .Name("GuildsTabRow")
        .Classes(Theme.name Theme.GhostButton)
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .HorizontalContentAlignment(HorizontalAlignment.Stretch)
        .MinHeight(48.0)
        .Padding(Thickness(16.0, 12.0, 16.0, 12.0))
        .Content(grid)
        .OnClickHandler(fun _ _ -> dispatch(State.Opened guild.Handle))
    )

let headText(current: State.Model) : string =
  let page =
    current.Settled |> ValueOption.map _.Page |> ValueOption.defaultValue 1

  let total =
    if current.Total = 1 then
      "1 guild"
    else
      $"{current.Total} guilds"

  let reading = if current.Out.IsSome then " · reading…" else ""

  $"GUILDS · {total} · page {page}/{current.LastPage}{reading}"

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =
  let offered =
    model
    |> Observable.map _.Refused
    |> Observable.map not
    |> Observable.distinctUntilChanged

  let pager =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(8.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Children(
        Button()
          .Name("GuildsTabPrevPage")
          .Classes(Theme.name Theme.GhostButton)
          .Padding(8.0)
          .Content(Icons.iconAt(Glyph.ChevronLeft, 14.0))
          .IsEnabled(
            model
            |> Observable.map(fun current ->
              current.Settled
              |> ValueOption.exists(fun settled -> settled.Page > 1))
            |> Observable.distinctUntilChanged
          )
          .OnClickHandler(fun _ _ -> dispatch State.PrevPage),
        TextBlock()
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .FontSize(12.0)
          .VerticalAlignment(VerticalAlignment.Center)
          .Text(
            model
            |> Observable.map(fun current ->
              let page =
                current.Settled
                |> ValueOption.map _.Page
                |> ValueOption.defaultValue 1

              $"{page}/{current.LastPage}")
            |> Observable.distinctUntilChanged
          ),
        Button()
          .Name("GuildsTabNextPage")
          .Classes(Theme.name Theme.GhostButton)
          .Padding(8.0)
          .Content(Icons.iconAt(Glyph.ChevronRight, 14.0))
          .IsEnabled(
            model
            |> Observable.map(fun current ->
              current.Settled
              |> ValueOption.exists(fun settled ->
                settled.Page < current.LastPage))
            |> Observable.distinctUntilChanged
          )
          .OnClickHandler(fun _ _ -> dispatch State.NextPage)
      )

  let headBar =
    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Padding(Thickness(16.0, 10.0, 16.0, 8.0))
      .Child(
        DockPanel()
          .LastChildFill(true)
          .Children(
            pager.DockRight(),
            TextBlock()
              .Name("GuildsTabHead")
              .Classes(Theme.name Theme.Mono)
              .VerticalAlignment(VerticalAlignment.Center)
              .Text(
                model
                |> Observable.map headText
                |> Observable.distinctUntilChanged
              )
          )
      )

  let searchBox =
    TextBox()
      .Name("GuildsTabSearch")
      .Classes(Theme.name Theme.Mono)
      .MaxLength(100)
      .PlaceholderText("Search guilds")
      .AcceptsReturn(false)
      .Text(model |> Observable.map _.Term)
      .OnTextChangedHandler(fun current _ ->
        current.Text
        |> Option.ofObj
        |> Option.iter(State.TermTyped >> dispatch))
      .OnKeyDownHandler(fun _ args ->
        if args.Key = Key.Enter then
          dispatch State.SearchAsked)

  let searchBar =
    Border()
      .Padding(Thickness(16.0, 8.0, 16.0, 8.0))
      .Child(
        DockPanel()
          .LastChildFill(true)
          .Children(
            Button()
              .Name("GuildsTabSearchGo")
              .Classes(Theme.name Theme.GhostButton)
              .Content("SEARCH")
              .OnClickHandler(fun _ _ -> dispatch State.SearchAsked)
              .DockRight(),
            searchBox
          )
      )
      .IsVisible(offered)

  let refused =
    TextBlock()
      .Name("GuildsTabRefused")
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0)
      .Text("Guilds are not available on this server.")
      .IsVisible(
        model |> Observable.map _.Refused |> Observable.distinctUntilChanged
      )

  let empty =
    TextBlock()
      .Name("GuildsTabEmpty")
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0)
      .Text(
        model
        |> Observable.map(fun current ->
          if String.IsNullOrWhiteSpace current.Term then
            "No guilds found."
          else
            $"No guild matches \"{current.Term}\".")
        |> Observable.distinctUntilChanged
      )
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Settled.IsSome && not current.Refused && current.Rows.IsEmpty)
        |> Observable.distinctUntilChanged
      )

  let note =
    TextBlock()
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .FontSize(12.0)
      .Margin(16.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Text("Join or create a guild on the site.")

  let rows =
    Feed.view {
      Header = ValueNone
      Items = model |> Observable.map _.Rows |> Observable.distinctUntilChanged
      KeyOf = fun guild -> guild.Handle
      Row = fun guild -> row(props, guild, dispatch)
      Footer = ValueSome note
      HasMore = Observable.single false
      ScrollTo = ValueNone
      Ride = false
      Restore = ValueNone
      OnScroll = ValueNone
      OnRide = ValueNone
      OnNeedMore = ValueNone
    }

  DockPanel()
    .Name("EditorGuildsTab")
    .LastChildFill(true)
    .Children(
      StackPanel().Children(headBar, searchBar, refused, empty).DockTop(),
      rows.IsVisible(offered)
    )

let program(props: Props<'env>) =
  Program.mkProgram
    (fun () -> State.init props)
    (fun msg model -> State.update(props, msg, model))
    (view props)

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
