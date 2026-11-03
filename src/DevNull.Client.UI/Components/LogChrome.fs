module DevNull.Client.Components.LogChrome

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open Navs
open Microsoft.Extensions.Logging
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Env
open DevNull.Client.RouteStore

/// The stateless log chrome: the tab strip over the composer rail.
[<Struct>]
type Props = {
  Tabs: TabStrip.Tab list
  Composer: unit -> Control
  RailNode: unit -> Control
  Header: (unit -> Control) voption
  OnReload: unit -> unit
}

/// The log host's input: the route store, the navigation, and the
/// slots the shell builds for the shared chrome.
type PageProps<'env when 'env :> IProvideRoutes and 'env :> IProvideLogging> = {
  Env: 'env
  Nav: INavigable
  Composer: unit -> Control
  RailNode: unit -> Control
  Header: (unit -> Control) voption
  OnReload: unit -> unit
}

/// The chrome's state: the tab the route names.
type Model = { Active: LogKind option }

/// The chrome's messages: the route moved, a tab was clicked.
type Msg =
  | PathChanged of path: string
  | TabClicked of kind: LogKind

/// The log-specific host pieces: the three log tabs and their route
/// names.
module Parts =

  type LogTab = {
    Label: string
    Kind: LogKind
    Url: string
  }

  let logTabs: LogTab list = [
    {
      Label = "Following"
      Kind = Following
      Url = "/log/following"
    }
    {
      Label = "Latest"
      Kind = Latest
      Url = "/log/latest"
    }
    {
      Label = "Trending"
      Kind = Trending
      Url = "/log/trending"
    }
  ]

  /// The log tab a path names, when it names one.
  let inline kindOf(path: string) : LogKind option =
    logTabs |> List.tryFind(fun tab -> path = tab.Url) |> Option.map _.Kind

  /// One log tab: the strip's active slice comes from the route model.
  let tab
    (active: IObservable<LogKind option>, dispatch: Dispatch<Msg>, info: LogTab)
    : TabStrip.Tab = {
    Label = info.Label
    Active =
      active
      |> Observable.map(fun current ->
        match current with
        | Some kind -> kind = info.Kind
        | None -> false)
    Badge = ValueNone
    Choose = fun () -> dispatch(TabClicked info.Kind)
  }

  let tabs
    (active: IObservable<LogKind option>, dispatch: Dispatch<Msg>)
    : TabStrip.Tab list =
    logTabs |> List.map(fun info -> tab(active, dispatch, info))

  /// The staged commit's rail: the host's avatar node on the first
  /// row, the dashed line running from it down into the feed below.
  /// The chrome keeps the 64px column; the host supplies the node and
  /// the composer.
  let composerRail(props: Props) : Control =
    let nodeTop = 9.0

    let line =
      Line(
        StartPoint = Point(0.0, 0.0),
        EndPoint = Point(0.0, 1.0),
        Stretch = Stretch.Fill,
        StrokeThickness = 2.0,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Stretch
      )
        .Classes(Theme.name Theme.RailEdge)
        .StrokeDashArray(Avalonia.Collections.AvaloniaList<double>(2.0, 2.0))
        .Margin(0.0, nodeTop + 46.0, 0.0, 12.0)

    let node = props.RailNode()

    let grid = Grid()

    grid.ColumnDefinitions <- ColumnDefinitions("64,*")

    grid.Children(
      Panel().Children(line, node).Column(0),
      Border().Margin(0.0, 0.0, 16.0, 0.0).Child(props.Composer()).Column(1)
    )

/// The chrome's state logic: the active tab, fed by the route store.
module State =

  let init (props: PageProps<'env>) () : Model * Cmd<Msg> =
    {
      Active = Parts.kindOf props.Env.Routes.Current
    },
    Cmd.none

  let loggerOf(props: PageProps<'env>) =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Components.LogChrome"

  let updateCore
    (log: ILogger, props: PageProps<'env>)
    (msg: Msg)
    (model: Model)
    : Model * Cmd<Msg> =
    match msg with
    | PathChanged path ->
      {
        model with
            Active = Parts.kindOf path
      },
      Cmd.none
    | TabClicked kind ->
      // A click on the active tab reloads; every other click navigates.
      match model.Active with
      | Some active when active = kind ->
        model, Cmd.ofEffect(fun _ -> props.OnReload())
      | _ ->
        model,
        Cmd.ofEffect(fun _ ->
          Parts.logTabs
          |> List.tryFind(fun tab -> tab.Kind = kind)
          |> Option.iter(fun tab ->
            Navigation.go(
              props.Nav,
              tab.Url,
              fun error -> log.LogWarning("{Error}", error)
            )))

  let update(props: PageProps<'env>) = updateCore(loggerOf props, props)

  /// The chrome's one inlet from outside: the route store.
  let subscribe (props: PageProps<'env>) (_: Model) : Sub<Msg> =
    Sub.fromObservable
      "path"
      (props.Env.Routes.Path |> Observable.map PathChanged)

let view(props: Props) : Control =
  let root = Grid()

  root.RowDefinitions <- RowDefinitions("Auto,Auto,Auto")

  let header =
    props.Header |> ValueOption.toList |> List.map(fun build -> build().Row(0))

  let rows: Control array = [|
    yield! header
    TabStrip.view props.Tabs |> _.Row(1)
    Parts.composerRail props |> _.Row(2)
  |]

  root.Margin(Thickness(0.0, Theme.bandHeight, 0.0, 0.0)).Children(rows)

type Page<'env when 'env :> IProvideRoutes and 'env :> IProvideLogging>
  (props: PageProps<'env>) as this =
  inherit UserControl()

  do
    let view' (model: IObservable<Model>) (dispatch: Dispatch<Msg>) : Control =
      let active = model |> Slices.gate _.Active |> Slices.share

      let chrome = {
        Tabs = Parts.tabs(active, dispatch)
        Composer = props.Composer
        RailNode = props.RailNode
        Header = props.Header
        OnReload = props.OnReload
      }

      Grid()
        .Children(view chrome)
        .IsVisible(active |> Observable.map Option.isSome)

    let program =
      Program.mkProgram (State.init props) (State.update props) view'
      |> Program.withSubscription(State.subscribe props)

    // The chrome is replaced, never re-shown, so its program ends on
    // the first detach.
    let root, _ = Program.runWithLifetime this () program

    this.Content <- root
