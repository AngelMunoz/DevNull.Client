module DevNull.Client.Components.LogChrome

open System
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open Navs
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open Microsoft.Extensions.Logging
open DevNull.Client.AttachmentSources
open DevNull.Client.Components.Avatar
open DevNull.Client.Components.Composer
open DevNull.Client.ImageCache
open DevNull.Client.Navigation
open DevNull.Client.RouteStore
open DevNull.Client.Stores
open DevNull.Client.SshKeys
open DevNull.Client.Theme
open DevNull.Client.Timeline

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideTimeline
  and 'env :> IProvideRoutes
  and 'env :> IProvideSshKeys
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
}

/// The chrome's state: the tab the route names.
type Model = { Active: LogKind option }

/// The chrome's messages: the route moved, a tab was clicked.
type Msg =
  | PathChanged of path: string
  | TabClicked of kind: LogKind

/// The shared log chrome: the three log tabs and the one composer
/// every tab posts through, the way the site's log header does.
module Parts =

  type Tab = {
    Label: string
    Kind: LogKind
    Url: string
  }

  let logTabs: Tab list = [
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

  /// The tab a path names, when it names one.
  let kindOf(path: string) : LogKind option =
    logTabs |> List.tryFind(fun tab -> path = tab.Url) |> Option.map _.Kind

  /// One log tab: the page-tab shape, underlined while active. A click
  /// on the active tab reloads through the service's gate and scrolls
  /// the feed home; every other click navigates.
  let tab
    (active: IObservable<LogKind option>, dispatch: Dispatch<Msg>, info: Tab)
    : Control =
    let button =
      Button()
        .Classes(Theme.name Theme.TabButton)
        .Content(info.Label)
        .OnClickHandler(fun _ _ -> dispatch(TabClicked info.Kind))

    Theme.bindClass(
      button,
      Theme.TabButtonActive,
      active |> Observable.map(fun current -> current = Some info.Kind)
    )

    button

  /// The three log tabs, edge to edge of the reading column over a
  /// hairline, the way the site's log header does. The band sits above
  /// the client, so the strip clears it.
  let tabStrip
    (active: IObservable<LogKind option>, dispatch: Dispatch<Msg>)
    : Control =
    let strip = Grid()

    strip.ColumnDefinitions <- ColumnDefinitions("*,*,*")

    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Margin(0.0, Theme.bandHeight, 0.0, 0.0)
      .Child(
        strip.Children(
          [|
            for index, info in List.indexed logTabs ->
              tab(active, dispatch, info).Column(index)
          |]
        )
      )

  /// The staged commit: the signed-in user's avatar rides the rail's
  /// first row, ring and all, and the dashed line runs from it down
  /// into the feed below — the site stages the next commit exactly so.
  /// One composer serves every tab; its commits land through the
  /// timeline service.
  let composerRail(props: Props<'env>) : Control =
    let composer =
      Composer.Page {
        Env = props.Env
        Session = props.Session
        Target = Composer.Fresh
        OnCommitted = fun _ -> props.Env.Timeline.CommitLanded()
        Register = ValueNone
        Draft = ValueNone
      }

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

    let avatar =
      Border()
        .Name("ComposerRailNode")
        .Classes(Theme.name Theme.CardBack)
        .BorderThickness(Thickness(2.0))
        .CornerRadius(46.0)
        .Padding(1.0)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Top)
        .Margin(0.0, nodeTop, 0.0, 0.0)
        .Child(
          Avatar.view {
            Name = props.Session.Session.User.Name
            Url = props.Session.Session.User.AvatarUrl
            Lane = None
            Size = 40.0
            Cache = props.Env.ImageCache
          }
        )

    avatar.Classes(Theme.laneClass(Theme.LaneBorder, None)) |> ignore

    let grid = Grid()

    grid.ColumnDefinitions <- ColumnDefinitions("64,*")

    grid.Children(
      Panel().Children(line, avatar).Column(0),
      Border().Margin(0.0, 0.0, 16.0, 0.0).Child(composer).Column(1)
    )

/// The chrome's state logic: the active tab, fed by the route store.
module State =

  let init (props: Props<'env>) () : Model * Cmd<Msg> =
    {
      Active = Parts.kindOf props.Env.Routes.Current
    },
    Cmd.none

  let update (props: Props<'env>) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    let log =
      props.Env.LoggerFactory.CreateLogger "DevNull.Client.Components.LogChrome"

    match msg with
    | PathChanged path ->
      {
        model with
            Active = Parts.kindOf path
      },
      Cmd.none
    | TabClicked kind ->
      // A click on the active tab reloads through the service's gate
      // and scrolls the feed home; every other click navigates.
      if model.Active = Some kind then
        model, Cmd.ofEffect(fun _ -> props.Env.Timeline.ForceReload())
      else
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

  /// The chrome's one inlet from outside: the route store.
  let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
    Sub.fromObservable
      "path"
      (props.Env.Routes.Path |> Observable.map PathChanged)

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =

  let active = model |> Observable.map _.Active

  let root = Grid()

  root.RowDefinitions <- RowDefinitions("Auto,Auto")

  root
    .Children(
      Parts.tabStrip(active, dispatch).Row(0),
      (Parts.composerRail props).Row(1)
    )
    .IsVisible(active |> Observable.map Option.isSome)

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideTimeline
  and 'env :> IProvideRoutes
  and 'env :> IProvideSshKeys
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    // The chrome is replaced, never re-shown, so its program ends on
    // the first detach.
    let root, _ = Program.runWithLifetime this () program

    this.Content <- root
