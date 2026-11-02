module DevNull.Client.Components.LogChrome

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Env
open DevNull.Client.RouteStore
open Navs

/// The stateless log chrome: the tab strip over the composer rail. The
/// default log host supplies the slots; a second surface can host the
/// same layout with its own tabs, composer, rail node, and header.
[<Struct>]
type Props = {
  /// The tabs the strip places, one full-width column each.
  Tabs: TabStrip.Tab list
  /// The composer the rail stages, built once by the host.
  Composer: unit -> Control
  /// The signed-in user's avatar node, built once by the host.
  RailNode: unit -> Control
  /// The optional header above the tabs.
  Header: (unit -> Control) voption
  /// The active tab's reload action.
  OnReload: unit -> unit
}

/// One stateless chrome: the optional header, the tab strip, and the
/// composer rail with its dashed line and 64px node column. The chrome
/// owns the band inset.
val view: props: Props -> Control

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

/// The log chrome page: the route-following tabs over the slots. A
/// click on the active tab asks the host to reload; every other click
/// navigates to its log.
type Page<'env when 'env :> IProvideRoutes and 'env :> IProvideLogging> =
  inherit UserControl

  new: props: PageProps<'env> -> Page<'env>
