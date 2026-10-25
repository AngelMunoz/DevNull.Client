module DevNull.Client.Views.CommitActions

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
