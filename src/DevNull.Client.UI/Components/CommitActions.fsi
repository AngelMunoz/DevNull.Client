module DevNull.Client.Components.CommitActions

open Microsoft.Extensions.Logging
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Stores

/// Stars or unstars one commit, the way the card's star action asks:
/// the current flag decides the direction, the request carries the
/// session key file.
val toggleStar:
  env: 'env * session: SignedInSession * commit: Commit ->
    Async<Result<Commit, AppError>>
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp

/// One poll vote: the signed-in session, the commit whose poll it is,
/// and the option the viewer chose.
[<Struct>]
type VoteArgs = {
  Session: SignedInSession
  Commit: Commit
  Option: PollOption
}

/// Votes the chosen option, the way the card's vote action asks, and
/// answers with the commit as its page shows it, poll results
/// included.
val vote:
  env: 'env * args: VoteArgs -> Async<Result<Commit, AppError>>
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp

/// One pastel: the signed-in session and the commit to give it to.
[<Struct>]
type PastelArgs = {
  Session: SignedInSession
  Commit: Commit
}

/// Gives one pastel and answers with the pastel's own result — the
/// updated commit and the count the cycle has left — so the page
/// keeps one fold and its own note.
val pastel:
  env: 'env * args: PastelArgs -> Async<Result<PastelResult, AppError>>
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp

/// One author toggle: the signed-in session, the author the More menu
/// speaks of, and the relation flag the page holds.
[<Struct>]
type ToggleArgs = {
  Session: SignedInSession
  Author: Author
  /// The held state the direction follows: held following unfollows,
  /// held muting unmutes, held blocking unblocks.
  Flag: bool
}

/// Follows or unfollows one author, the way the card menu's follow
/// item asks.
val toggleFollow:
  env: 'env * args: ToggleArgs -> Async<Result<unit, AppError>>
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp

/// Mutes or unmutes one author, the way the card menu's mute item
/// asks, and answers with the server's own mute state.
val toggleMute:
  env: 'env * args: ToggleArgs -> Async<Result<MuteResult, AppError>>
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp

/// Blocks or unblocks one author, the way the card menu's block ask
/// confirms, and answers with the server's own block state.
val blockAuthor:
  env: 'env * args: ToggleArgs -> Async<Result<BlockResult, AppError>>
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp

/// Logs one card action's failure through the given logger, the
/// action's name and the bounded server message kept as separate
/// fields. Curried so a page binds its logger once.
val failure: logger: ILogger -> action: string -> error: AppError -> unit
