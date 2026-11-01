module DevNull.Client.Views.CommitActions

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

/// Gives one pastel and answers with the updated commit, so every
/// card action settles through one fold.
let pastel
  (
    env: #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp,
    args: PastelArgs
  ) : Async<Result<Commit, AppError>> = async {
  let request: Pasteis.ActionRequest = {
    KeyFile = args.Session.KeyFile
    CommitId = args.Commit.Id
  }

  match! Pasteis.give(env, request) with
  | Ok pastel -> return Ok pastel.Commit
  | Error error -> return Error error
}

/// Logs one card action's failure through the given logger, the
/// action's name and the bounded server message kept as separate
/// fields. Curried so a page binds its logger once.
let failure (logger: ILogger) (action: string) (error: AppError) : unit =
  logger.LogWarning(
    "The {Action} failed: {Error}",
    action,
    bounded 300 (describe error)
  )
