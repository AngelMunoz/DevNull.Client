module DevNull.Client.Editor.GuildTab

open System
open Avalonia.Controls
open Avalonia.Threading
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore
open DevNull.Client.AttachmentSources
open DevNull.Client.Env
open MVUish.Avalonia

/// What the frame reads from one guild board tab: the guild row the
/// board read answered, and when the last page landed. The frame aims
/// the terminal composer from the row and refuses the aim while the
/// guild is paused.
[<Struct>]
type Snapshot = {
  Guild: Guild option
  FetchedAt: DateTimeOffset option
}

/// What one guild board tab needs from the frame: the signed workers,
/// the session, the guild's handle, the zone the times show in, the
/// clock, the refresh inlet a post fires, and the frame's sinks.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideDispatcher
  and 'env :> IProvideRemotes
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLogging
  and 'env :> IProvideBrowser> = {
  Env: 'env
  Session: SignedInSession
  Handle: string
  Zone: Zone
  Now: unit -> DateTimeOffset
  Refresh: IObservable<unit>
  OnSnapshot: Snapshot -> unit
  OnOpenThread: Commit -> unit
  OnReply: Commit -> unit
  OnQuote: Commit -> unit
  OnOpenRepo: string -> unit
  /// A share sends its recipient here: the frame opens the chat as a
  /// remote tab. Runs on the UI thread.
  OnShared: User -> unit
  /// A guild chip on a card opens that guild's board tab.
  OnOpenGuild: string -> unit
  /// The stage button: the frame opens the terminal aimed at this
  /// guild.
  OnStage: string -> unit
}

/// The board tab's state logic, exported so the update stays testable
/// without a running page.
module State =

  /// The tab's state: the shared board window, the card actions, the
  /// clock, the last fetch, and the card failure line.
  type Model = {
    Board: DevNull.Client.GuildBoard.BoardState
    Cards: EditorCards.State
    Now: DateTimeOffset
    FetchedAt: DateTimeOffset option
    Error: DevNull.Client.Errors.AppError option
  }

  /// The tab's messages: the board pages, the refresh inlet, the
  /// clock, the card open intent, and the card actions.
  type Msg =
    | BoardLoad
    | BoardMore
    | BoardArrived of
      page: int *
      Result<
        DevNull.Client.Core.ApiTypes.GuildBoard,
        DevNull.Client.Errors.AppError
       >
    | RefreshRequested
    | Opened of Commit
    | CardMsg of EditorCards.Msg
    | ClockTicked of moment: DateTimeOffset

  /// What the frame reads from this model.
  val snapshot: model: Model -> Snapshot

  /// The fresh state: the first board page is on its way.
  val init: props: Props<'env> -> Model * Cmd<Msg>

  /// Applies one message.
  val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

  /// The tab's inlets from outside: the shared clock and the frame's
  /// refresh inlet.
  val subscribe: props: Props<'env> * model: Model -> Sub<Msg>

/// The tab's view: the compact identity head over the virtualized
/// board, the pinned commit leading with its marker.
val view:
  props: Props<'env> *
  model: IObservable<State.Model> *
  dispatch: Dispatch<State.Msg> ->
    Control

/// The tab's program, with its update loop bound to the caller's
/// dispatcher.
val program:
  props: Props<'env> -> Program<unit, State.Model, State.Msg, Control>

/// The live board programs of this process, so a host can prove that
/// closing a tab disposed its program exactly once.
module Live =

  /// The number of board programs mounted and not disposed.
  val current: unit -> int

  /// Every change of that number.
  val observe: IObservable<int>

/// Starts one guild board tab. The caller owns the returned handle
/// and disposes it when the tab closes.
val mount: dispatcher: IDispatcher * props: Props<'env> -> Control * IDisposable
