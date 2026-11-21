module DevNull.Client.Workbench.GuildRegistry

open Avalonia.Controls
open MVUish.Avalonia
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.Stores

/// What the registry needs from its host: the protocol workers, the
/// signed session it reads as, the image cache, and the sink a row
/// pick reaches with the guild's handle.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher> = {
  Env: 'env
  Session: SignedInSession option
  Images: ImageCacheService
  OnOpen: string -> unit
}

/// The guild directory as the workbench reads it: the same wire reads
/// and the same parking rules the site's directory page rides, under
/// the workbench's own presentation — a mono head line with the count
/// and the page, a search row, dense index rows, and the pager's
/// chevrons.
module State =

  /// One read the registry asks for: the term it reads for, and the
  /// page, one-based.
  [<Struct>]
  type Ask = { Query: string option; Page: int }

  /// The registry's state: the term as typed, one numbered page of
  /// rows, the ask the rows answer, the ask out and the ask parked
  /// behind it, the directory counts, and the 404 of a flag-off
  /// server.
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

  /// The registry's messages: the term, the search ask, the pager, the
  /// answer, and a row pick.
  type Msg =
    | TermTyped of term: string
    | SearchAsked
    | PrevPage
    | NextPage
    | Arrived of ask: Ask * Result<GuildsPage, AppError>
    | Opened of handle: string

  /// One directory read, the ask riding beside its answer.
  val read:
    env: 'env * session: SignedInSession * ask: Ask ->
      Async<Ask * Result<GuildsPage, AppError>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp

  /// One ask: the first fires its read, the rest wait behind it as the
  /// newest wanted. No session leaves the state as it stands.
  val fire:
    props: Props<'env> * model: Model * ask: Ask -> Model * Cmd<Msg>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and 'env :> IProvideDispatcher

  /// The registry's first state and its first directory read. A
  /// missing session holds the state empty; the view stands at its
  /// sign-in line.
  val init: props: Props<'env> -> Model * Cmd<Msg>

  /// The registry's one update function.
  val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The registry's program. The host starts it with `Programs.runPage`
/// and keeps the returned handle.
val program:
  props: Props<'env> -> Program<unit, State.Model, State.Msg, Control>
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp
    and 'env :> IProvideDispatcher
