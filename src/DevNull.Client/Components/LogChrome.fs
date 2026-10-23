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
open Navs
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.AttachmentSources
open DevNull.Client.Components.Avatar
open DevNull.Client.Components.Composer
open DevNull.Client.ImageCache
open DevNull.Client.Navigation
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
  and 'env :> IProvideSshKeys> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
  /// The live route path, seeded with the boot url: the strip
  /// underlines the tab it names and the chrome hides off the log.
  Path: IObservable<string>
}

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
    (active: BehaviorSubject<LogKind option>, props: Props<'env>, info: Tab)
    : Control =
    let button =
      Button()
        .Classes(Theme.name Theme.TabButton)
        .Content(info.Label)
        .OnClickHandler(fun _ _ ->
          if active.Value = Some info.Kind then
            props.Env.Timeline.ForceReload()
          else
            Navigation.go(props.Nav, info.Url, ignore))

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
    (active: BehaviorSubject<LogKind option>, props: Props<'env>)
    : Control =
    let strip = Grid()

    strip.ColumnDefinitions <- ColumnDefinitions("*,*,*")

    Border()
      .BorderBrush(Brushes.border)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Margin(0.0, Theme.bandHeight, 0.0, 0.0)
      .Child(
        strip.Children(
          [|
            for index, info in List.indexed logTabs ->
              tab(active, props, info).Column(index)
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
        .Stroke(Brushes.rail)
        .StrokeDashArray(Avalonia.Collections.AvaloniaList<double>(2.0, 2.0))
        .Margin(0.0, nodeTop + 46.0, 0.0, 12.0)

    let avatar =
      Border()
        .Name("ComposerRailNode")
        .Background(Brushes.card)
        .BorderBrush(Brushes.laneFallback)
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

    let grid = Grid()

    grid.ColumnDefinitions <- ColumnDefinitions("64,*")

    grid.Children(
      Panel().Children(line, avatar).Column(0),
      Border().Margin(0.0, 0.0, 16.0, 0.0).Child(composer).Column(1)
    )

let view(props: Props<'env>) : Control =
  // The active tab cell, fed by the live path the way the shell's
  // layout cell is fed by the client size. The path stream carries its
  // current value on subscribe, so the underline and the visibility
  // start correct.
  let active = new BehaviorSubject<LogKind option>(None)

  props.Path |> Observable.map Parts.kindOf |> Observable.add active.OnNext

  let onLog = active |> Observable.map Option.isSome

  let root = Grid()

  root.RowDefinitions <- RowDefinitions("Auto,Auto")

  root
    .Children(
      Parts.tabStrip(active, props).Row(0),
      (Parts.composerRail props).Row(1)
    )
    .IsVisible(onLog)
