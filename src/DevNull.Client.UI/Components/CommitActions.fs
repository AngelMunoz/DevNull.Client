module DevNull.Client.Components.CommitActions

open Microsoft.Extensions.Logging
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Stores

/// Stars or unstars one commit, the way the card's star action asks:
/// the current flag decides the direction, the request carries the
/// session key file.
let toggleStar
  (
    env: #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp,
    session: SignedInSession,
    commit: Commit
  ) : Async<Result<Commit, AppError>> =
  let request: Stars.ActionRequest = {
    KeyFile = session.KeyFile
    CommitId = commit.Id
  }

  if commit.Starred |> Option.contains true then
    Stars.unstar(env, request)
  else
    Stars.star(env, request)

/// One poll vote: the signed-in session, the commit whose poll it is,
/// and the option the viewer chose.
[<Struct>]
type VoteArgs = {
  Session: SignedInSession
  Commit: Commit
  Option: PollOption
}

/// Votes the chosen option and answers with the commit as its page
/// shows it, poll results included.
let vote
  (
    env: #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp,
    args: VoteArgs
  ) : Async<Result<Commit, AppError>> =
  let request: Commits.VoteRequest = {
    KeyFile = args.Session.KeyFile
    CommitId = args.Commit.Id
    OptionId = args.Option.Id
  }

  Commits.vote(env, request)

/// One pastel: the signed-in session and the commit to give it to.
[<Struct>]
type PastelArgs = {
  Session: SignedInSession
  Commit: Commit
}

/// Gives one pastel and answers with the pastel's own result — the
/// updated commit and the count the cycle has left — so the page
/// keeps one fold and its own note.
let pastel
  (
    env: #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp,
    args: PastelArgs
  ) : Async<Result<PastelResult, AppError>> =
  let request: Pasteis.ActionRequest = {
    KeyFile = args.Session.KeyFile
    CommitId = args.Commit.Id
  }

  Pasteis.give(env, request)

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

// The signed request one author toggle sends.
let authorRequest(args: ToggleArgs) : Users.Request = {
  KeyFile = args.Session.KeyFile
  Username = (Author.handle args.Author)
}

/// Follows or unfollows one author, the way the card menu's follow
/// item asks.
let toggleFollow
  (
    env: #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp,
    args: ToggleArgs
  ) : Async<Result<unit, AppError>> =
  if args.Flag then
    Users.unfollow(env, authorRequest args)
  else
    Users.follow(env, authorRequest args)

/// Mutes or unmutes one author, the way the card menu's mute item
/// asks, and answers with the server's own mute state.
let toggleMute
  (
    env: #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp,
    args: ToggleArgs
  ) : Async<Result<MuteResult, AppError>> =
  if args.Flag then
    Users.unmute(env, authorRequest args)
  else
    Users.mute(env, authorRequest args)

/// Blocks or unblocks one author, the way the card menu's block ask
/// confirms, and answers with the server's own block state.
let blockAuthor
  (
    env: #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp,
    args: ToggleArgs
  ) : Async<Result<BlockResult, AppError>> =
  if args.Flag then
    Users.unblock(env, authorRequest args)
  else
    Users.block(env, authorRequest args)

/// Logs one card action's failure through the given logger, the
/// action's name and the bounded server message kept as separate
/// fields. Curried so a page binds its logger once.
let failure (logger: ILogger) (action: string) (error: AppError) : unit =
  logger.LogWarning(
    "The {Action} failed: {Error}",
    action,
    bounded 300 (describe error)
  )
