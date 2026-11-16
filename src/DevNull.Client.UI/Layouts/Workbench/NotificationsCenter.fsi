module DevNull.Client.Workbench.NotificationsCenter

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Theme

/// The inbox's state: the loaded notifications in a bounded window,
/// the reading anchor, what is loading, the failure, and the clock the
/// rows read against.
type Model = {
  Loading: bool
  More: bool
  Error: AppError option
  Feed: FeedWindow.Window<Notification>
  Anchor: FeedWindow.Anchor voption
  Now: DateTimeOffset
}

/// The inbox's messages: the first page, the cursor, a manual refresh,
/// the clock, and the one row a reader opens.
type Msg =
  | Fetched of Result<NotificationPage, AppError>
  | MoreRequested
  | Appended of cursor: string option * Result<NotificationPage, AppError>
  | AnchorMoved of FeedWindow.Anchor
  | RefreshRequested
  | ClockTicked of moment: DateTimeOffset
  | Opened of Notification

/// What the inbox needs from its host: the protocol workers, the signed
/// key file, the image cache, the clock, and the host's checkout sink.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
  KeyFile: string
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  OnCheckout: string -> unit
}

/// The sentence one notification type says after the actor.
val kindLine: notification: Notification -> string

/// The group count line, when the API grouped more than one event.
val groupLine: notification: Notification -> string option

/// True while the server reports the row unread.
val inline isUnread: notification: Notification -> bool

/// The rows the window holds, in display order.
val internal itemsOf: model: Model -> Notification list

/// The older cursor of the window.
val internal olderOf: model: Model -> string voption

/// A model with one loaded page, for tests and fixtures.
val internal withPage:
  items: Notification list * nextCursor: string option * model: Model -> Model

/// The inbox's first state and its first page read.
val init: props: Props<'env> -> Model * Cmd<Msg>

/// The inbox's one update function.
val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The inbox's inlet: the host's clock ticks.
val subscribe: props: Props<'env> -> model: Model -> Sub<Msg>

/// The inbox's center list.
val view:
  props: Props<'env> ->
  model: IObservable<Model> ->
  dispatch: Dispatch<Msg> ->
    Control

/// The inbox's program. The host starts it with `Programs.runPage` and
/// keeps the returned handle.
val program: props: Props<'env> -> Program<unit, Model, Msg, Control>
