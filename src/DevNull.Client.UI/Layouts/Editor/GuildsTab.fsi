module DevNull.Client.Editor.GuildsTab

open System
open Avalonia.Controls
open Avalonia.Threading
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.UiClock
open MVUish.Avalonia

/// What one guild directory tab needs from the frame: the signed
/// workers, the session, the zone the times show in, the clock, and
/// the frame's board opener for a picked guild.
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
  /// The directory total each settled read reports, for the frame's
  /// explorer count.
  OnCount: int -> unit
}

/// The directory tab's state logic, exported so the update stays
/// testable without a running page. The parking rules are the site's:
/// one read out at a time, the newest wanted ask parked behind it, and
/// only the answer the surface waits for applied.
module State =

  /// One read the tab asks for: the term it reads for, and the page,
  /// one-based.
  [<Struct>]
  type Ask = { Query: string option; Page: int }

  /// The tab's state: the term as typed, one numbered page of rows,
  /// the ask the rows answer, the ask out and the ask parked behind
  /// it, the directory counts, and the 404 of a flag-off server.
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

  /// The tab's messages: the term, the search ask, the pager, the
  /// answer, and a row pick.
  type Msg =
    | TermTyped of term: string
    | SearchAsked
    | PrevPage
    | NextPage
    | Arrived of ask: Ask * Result<GuildsPage, AppError>
    | Opened of handle: string

  /// The first state and its first directory read.
  val init: props: Props<'env> -> Model * Cmd<Msg>

  /// Applies one message.
  val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The tab's program, with its update loop bound to the caller's
/// dispatcher.
val program:
  props: Props<'env> -> Program<unit, State.Model, State.Msg, Control>

/// The live directory programs of this process, so a host can prove
/// that closing the tab disposed its program exactly once.
module Live =

  /// The number of directory programs mounted and not disposed.
  val current: unit -> int

  /// Every change of that number.
  val observe: IObservable<int>

/// Starts one directory tab. The caller owns the returned handle and
/// disposes it when the tab closes.
val mount: dispatcher: IDispatcher * props: Props<'env> -> Control * IDisposable
