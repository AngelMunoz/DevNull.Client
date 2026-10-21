module DevNull.Client.Views.CommitActions

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
