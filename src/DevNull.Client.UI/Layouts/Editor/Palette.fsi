module DevNull.Client.Layouts.Editor.Palette

open System
open Avalonia.Controls
open Avalonia.Threading
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Stores
open DevNull.Client.UiClock

/// One command-only action the palette keeps outside the search dialog.
type Command =
  | SwitchRef of LogKind
  | OpenNotifications
  | OpenPanel of EditorPanel.Tab
  | UseDefaultLayout

/// Every command the palette lists, in display order.
val commands: Command list

/// The label one command shows.
val label: command: Command -> string

/// What the palette shell needs from the editor: the signed workers and
/// session, the routes its dialog can emit, the panel and ref actions,
/// the compose surface, and the close control.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging
  and 'env :> IProvideRecentSearches> = {
  Env: 'env
  Session: SignedInSession
  /// Opens a profile repository tab.
  OnOpenRepo: string -> unit
  /// Opens a commit thread tab by id.
  OnOpenCommit: string -> unit
  /// The editor has no organization tab; the callback keeps the route
  /// explicit instead of dropping it in the adapter.
  OnOpenOrg: string -> unit
  /// The editor has no search-results tab; the callback keeps the route
  /// explicit instead of dropping it in the adapter.
  OnOpenSearch: string -> unit
  OnSelectRef: LogKind -> unit
  OnOpenNotifications: unit -> unit
  OnOpenPanel: EditorPanel.Tab -> unit
  OnUseDefaultLayout: unit -> unit
  OnOpenCompose: unit -> unit
  OnClose: unit -> unit
}

/// Hosts the web search dialog over a thin command strip.
val mount: dispatcher: IDispatcher * props: Props<'env> -> Control * IDisposable
