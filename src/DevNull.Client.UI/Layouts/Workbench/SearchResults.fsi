module DevNull.Client.Workbench.SearchResults

open System
open Avalonia.Controls
open MVUish.Avalonia
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache

/// The search center's state: its normalized term, the manual read and
/// its failure, the live results, and the clock the cards read against.
type Model = {
  Term: string
  Loading: bool
  Error: AppError option
  Results: CommitNode list
  Now: DateTimeOffset
}

/// The search center's messages: the read, its manual refresh, the
/// clock, the row intents, and the close request.
type Msg =
  | Fetched of Result<CommitNode list, AppError>
  | RefreshRequested
  | ClockTicked of moment: DateTimeOffset
  | Opened of Commit
  | QuoteRequested of Commit
  | ExternalOpened of url: string
  | CloseRequested

/// What the search center needs from its host: the protocol workers,
/// the signed key file and viewer, the pastéis flag, the image cache,
/// the clock and zone, the normalized term, and the row and close sinks.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  Term: string
  OnOpenThread: Commit -> unit
  OnQuote: Commit -> unit
  OnOpenExternal: string -> unit
  OnClose: unit -> unit
}

/// The search center's first state and its one manual commit read.
val init: props: Props<'env> -> Model * Cmd<Msg>

/// The search center's one update function.
val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The search center's inlet: the host's clock ticks.
val subscribe: props: Props<'env> -> model: Model -> Sub<Msg>

/// The search center's list and its manual refresh control.
val view:
  props: Props<'env> ->
  model: IObservable<Model> ->
  dispatch: Dispatch<Msg> ->
    Control

/// The search center's program. It reads once and never polls.
val program: props: Props<'env> -> Program<unit, Model, Msg, Control>
