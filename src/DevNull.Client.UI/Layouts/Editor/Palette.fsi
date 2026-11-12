module DevNull.Client.Editor.Palette

open System
open Avalonia.Controls
open Avalonia.Threading
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open MVUish.Avalonia

/// One real command the palette offers. The host routes every case to
/// the same action its own control runs.
type Command =
  | SwitchRef of LogKind
  | OpenPanel of EditorPanel.Tab
  | UseDefaultLayout

/// The terms one raw query sends to the two search reads. A term is
/// absent when the server's own normalization leaves nothing to send.
[<Struct>]
type SearchPlan = {
  People: string option
  Commits: string option
}

/// Shapes a raw query into the two search terms.
val plan: raw: string -> SearchPlan

/// Every command the palette lists, in display order.
val commands: Command list

/// The label one command shows.
val label: command: Command -> string

/// What one palette needs from its host: the signed workers, the
/// session, the image cache, the four intents a result or command
/// sends, and the close control.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideImageCache> = {
  Env: 'env
  Session: SignedInSession
  OnOpenRepo: string -> unit
  OnOpenThread: Commit -> unit
  OnSelectRef: LogKind -> unit
  OnOpenPanel: EditorPanel.Tab -> unit
  OnUseDefaultLayout: unit -> unit
  OnClose: unit -> unit
}

/// The palette's state logic, exported so the result tests stay data
/// tests without a running page.
module State =

  /// The raw query, its normalized term, and the two search answers.
  type Model = {
    Query: string
    Term: string
    People: User list
    Commits: Commit list
    PeopleLoading: bool
    CommitsLoading: bool
    PeopleError: AppError option
    CommitsError: AppError option
  }

  /// The messages of one palette.
  type Msg =
    | QueryChanged of raw: string
    | PeopleFetched of term: string * Result<ProfileMatches, AppError>
    | CommitsFetched of term: string * Result<CommitNode list, AppError>
    | PersonPicked of User
    | CommitPicked of Commit
    | CommandChosen of Command
    | Close

  /// The empty query, no results, and no read in flight.
  val initial: Model

/// Applies one message.
val update:
  props: Props<'env> * msg: State.Msg * model: State.Model ->
    State.Model * Cmd<State.Msg>

/// The palette card: the query, the two result sections, and the command
/// list.
val view:
  props: Props<'env> *
  model: IObservable<State.Model> *
  dispatch: Dispatch<State.Msg> ->
    Control

/// The palette program, with its update loop bound to the caller's
/// dispatcher.
val program:
  props: Props<'env> -> Program<unit, State.Model, State.Msg, Control>

/// Mounts one palette. The returned handle disposes the program once.
val mount: dispatcher: IDispatcher * props: Props<'env> -> Control * IDisposable
