module DevNull.Client.Core.Api

open System
open JDeck
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Core.Pkce
open DevNull.Client.Core.Types
open FsToolkit.ErrorHandling
open IcedTasks

[<Literal>]
let NamespaceName = "devnull-api"

module Signing =

  type Request = {
    KeyFile: string
    Method: string
    Target: string
    Body: string
  }

  type Deps<'Env
    when 'Env :> IProvideClock
    and 'Env :> IProvideNonceSource
    and 'Env :> IProvideSigner> = 'Env

  let signedRequest<'env, 'e
    when Deps<'env> and NonceError<'e> and SigningError<'e>>
    (env: 'env, request: Request)
    : Async<Result<HttpRequest, 'e>> = asyncResult {
    let timestamp = Clock.unixNow env
    let! nonce = Nonces.parse(NonceSource.newHex env)
    let method = request.Method.Trim().ToUpperInvariant()

    let message =
      String.concat "\n" [
        string timestamp
        Nonces.value nonce
        method
        request.Target
        request.Body
      ]

    let! signature = asyncEx {
      let! outcome =
        env.Signer.Sign {
          KeyFile = request.KeyFile
          Namespace = NamespaceName
          Payload = message
        }

      return
        outcome
        |> Result.mapError(fun failure -> 'e.SigningFailed failure.Message)
    }

    return {
      Method = method
      Target = request.Target
      Headers = [
        "User-Agent", userAgent
        "X-Devnull-Timestamp", string timestamp
        "X-Devnull-Nonce", Nonces.value nonce
        "X-SSH-Signature", signature
        "Content-Type", "application/json"
      ]
      Body = if request.Body = "" then Empty else Text request.Body
    }
  }

module Internal =

  type SignedCall<'value> = {
    KeyFile: string
    Method: string
    Target: string
    Body: string
    Decode: string -> Result<'value, DecodeFailure>
  }

  type SendSignedDeps<'Env
    when 'Env :> IProvideClock
    and 'Env :> IProvideNonceSource
    and 'Env :> IProvideSigner
    and 'Env :> IProvideHttp> = 'Env

  let sendSigned<'env, 'e, 'value
    when SendSignedDeps<'env>
    and NonceError<'e>
    and SigningError<'e>
    and TransportError<'e>
    and Rejected<'e>
    and DecodeError<'e>>
    (env: 'env, call: SignedCall<'value>)
    : Async<Result<'value, 'e>> = asyncEx {
    match!
      Signing.signedRequest(
        env,
        {
          KeyFile = call.KeyFile
          Method = call.Method
          Target = call.Target
          Body = call.Body
        }
      )
    with
    | Error error -> return Error error
    | Ok signed ->
      match! env.Http.Send signed with
      | Error failure -> return Error('e.TransportFailed failure.Message)
      | Ok response when isSuccess response.Status ->
        return
          call.Decode response.Body
          |> Result.mapError(fun failure -> 'e.DecodeFailed failure)
      | Ok response ->
        let fallback: Failure = {
          Message = response.Body.Trim()
          Code = Unknown ""
        }

        let failure =
          ApiTypes.Failure.decode response.Body |> Result.defaultValue fallback

        return Error('e.Refused(response.Status, failure.Code, failure.Message))
  }

  let withQuery(target: string, parameters: (string * string) list) =
    match parameters with
    | [] -> target
    | _ ->
      target
      + "?"
      + (parameters
         |> List.map(fun (name, value) ->
           name + "=" + Uri.EscapeDataString value)
         |> String.concat "&")

  let optionalParameter(name: string, value: string option) =
    value |> Option.map(fun value -> [ name, value ]) |> Option.defaultValue []

  let flagParameter(name: string, value: bool) =
    if value then [ name, "1" ] else []

  let decodeUnit(_: string) : Result<unit, DecodeFailure> = Ok()

  let userPath(username: string, suffix: string) =
    "/api/v1/users/" + Uri.EscapeDataString username + suffix

  let commitPath<'e when CommitIdError<'e>>
    (suffix: string, rawId: string)
    : Result<string, 'e> =
    CommitIds.parse rawId
    |> Result.map(fun id -> "/api/v1/commits/" + CommitIds.value id + suffix)

module Device =

  type AuthorizationRequest = {
    BaseAddress: string
    Fingerprint: string
    DeviceName: string
    RedirectUri: string
  }

  type Authorization = {
    Verifier: string
    State: string
    Challenge: string
    Url: string
  }

  let authorizationUrl
    (request: AuthorizationRequest, challenge: string, state: string)
    =
    let query =
      [
        "fingerprint", request.Fingerprint
        "device_name", request.DeviceName
        "code_challenge", challenge
        "code_challenge_method", "S256"
        "state", state
        "redirect_uri", request.RedirectUri
      ]
      |> List.map(fun (name, value) -> name + "=" + Uri.EscapeDataString value)
      |> String.concat "&"

    request.BaseAddress.TrimEnd('/') + "/mobile/authorize?" + query

  let beginAuthorization
    (
      env: #IProvidePkceSource & #IProvideNonceSource,
      request: AuthorizationRequest
    ) : Authorization =
    let verifier = PkceSource.newVerifier env
    let challenge = Pkce.challenge verifier
    let state = NonceSource.newHex env

    {
      Verifier = verifier
      State = state
      Challenge = challenge
      Url = authorizationUrl(request, challenge, state)
    }

  type RedeemRequest = {
    KeyFile: string
    Code: string
    Verifier: string
  }

  let redeem<'env, 'e
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp
    and NonceError<'e>
    and SigningError<'e>
    and TransportError<'e>
    and Rejected<'e>
    and DecodeError<'e>>
    (env: 'env, request: RedeemRequest)
    : Async<Result<Me, 'e>> = asyncEx {
    let body =
      Json.object [
        "code", Encode.string request.Code
        "code_verifier", Encode.string request.Verifier
      ]
      |> _.ToJsonString()

    return!
      Internal.sendSigned(
        env,
        {
          KeyFile = request.KeyFile
          Method = "POST"
          Target = "/api/v1/devices"
          Body = body
          Decode = Me.decode
        }
      )
  }

module Me =

  type Request = { KeyFile: string }

  type PasteisModeRequest = { KeyFile: string; Enabled: bool }

  type LaneRequest = { KeyFile: string; Lane: LaneChoice }

  let get(env, request: Request) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = "/api/v1/me"
        Body = ""
        Decode = Me.decode
      }
    )

  let delete(env, request: Request) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "DELETE"
        Target = "/api/v1/me"
        Body = ""
        Decode = Internal.decodeUnit
      }
    )

  let setPasteisMode(env, request: PasteisModeRequest) =
    let body =
      Json.object [ "enabled", Encode.boolean request.Enabled ]
      |> _.ToJsonString()

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "PUT"
        Target = "/api/v1/me/pasteis-mode"
        Body = body
        Decode = Me.decode
      }
    )

  let syncGithub(env, request: Request) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "PUT"
        Target = "/api/v1/me/github"
        Body = ""
        Decode = Me.decode
      }
    )

  let setLane(env, request: LaneRequest) = asyncResult {
    let! lane = LaneChoices.validate request.Lane

    let body =
      let json =
        match lane with
        | Automatic -> Json.object [ "lane", Encode.Null() ]
        | Palette index -> Json.object [ "lane", Encode.int index ]
        | Custom color ->
          Json.object [ "lane", Encode.Null(); "color", Encode.string color ]

      json.ToJsonString()

    return!
      Internal.sendSigned(
        env,
        {
          KeyFile = request.KeyFile
          Method = "PUT"
          Target = "/api/v1/me/lane"
          Body = body
          Decode = Me.decode
        }
      )
  }

module Users =

  type Request = { KeyFile: string; Username: string }

  type ListRequest = {
    KeyFile: string
    Username: string
    Cursor: string option
  }

  type CommitsRequest = {
    KeyFile: string
    Username: string
    Replies: bool
    Cursor: string option
  }

  type CursorRequest = {
    KeyFile: string
    Cursor: string option
  }

  let get(env, request: Request) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = Internal.userPath(request.Username, "")
        Body = ""
        Decode = ProfileDetails.decode
      }
    )

  let commits(env, request: CommitsRequest) =
    let target =
      Internal.withQuery(
        Internal.userPath(request.Username, "/commits"),
        Internal.flagParameter("replies", request.Replies)
        @ Internal.optionalParameter("cursor", request.Cursor)
      )

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = target
        Body = ""
        Decode = CommitNode.decodePage
      }
    )

  let followers(env, request: ListRequest) =
    let target =
      Internal.withQuery(
        Internal.userPath(request.Username, "/followers"),
        Internal.optionalParameter("cursor", request.Cursor)
      )

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = target
        Body = ""
        Decode = User.decodePage
      }
    )

  let following(env, request: ListRequest) =
    let target =
      Internal.withQuery(
        Internal.userPath(request.Username, "/following"),
        Internal.optionalParameter("cursor", request.Cursor)
      )

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = target
        Body = ""
        Decode = User.decodePage
      }
    )

  let follow(env, request: Request) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "POST"
        Target = Internal.userPath(request.Username, "/follow")
        Body = ""
        Decode = Internal.decodeUnit
      }
    )

  let unfollow(env, request: Request) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "DELETE"
        Target = Internal.userPath(request.Username, "/follow")
        Body = ""
        Decode = Internal.decodeUnit
      }
    )

  let muted(env, request: CursorRequest) =
    let target =
      Internal.withQuery(
        "/api/v1/me/muted",
        Internal.optionalParameter("cursor", request.Cursor)
      )

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = target
        Body = ""
        Decode = User.decodePage
      }
    )

  let mute(env, request: Request) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "POST"
        Target = Internal.userPath(request.Username, "/mute")
        Body = ""
        Decode = MuteResult.decode
      }
    )

  let unmute(env, request: Request) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "DELETE"
        Target = Internal.userPath(request.Username, "/mute")
        Body = ""
        Decode = MuteResult.decode
      }
    )

  let blocked(env, request: CursorRequest) =
    let target =
      Internal.withQuery(
        "/api/v1/me/blocked",
        Internal.optionalParameter("cursor", request.Cursor)
      )

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = target
        Body = ""
        Decode = User.decodePage
      }
    )

  let block(env, request: Request) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "POST"
        Target = Internal.userPath(request.Username, "/block")
        Body = ""
        Decode = BlockResult.decode
      }
    )

  let unblock(env, request: Request) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "DELETE"
        Target = Internal.userPath(request.Username, "/block")
        Body = ""
        Decode = BlockResult.decode
      }
    )

module Commits =

  type Target =
    | New
    | ReplyTo of parentId: string
    | Quote of quotedId: string

  type CreateRequest = {
    KeyFile: string
    Body: string
    Target: Target
  }

  type ShowRequest = { KeyFile: string; CommitId: string }

  type RepliesRequest = {
    KeyFile: string
    CommitId: string
    Newest: bool
    Cursor: string option
  }

  type ActionRequest = { KeyFile: string; CommitId: string }

  type ReportRequest = {
    KeyFile: string
    CommitId: string
    Reason: ReportReason
    Details: string option
  }

  let validateDetails<'e when ReportDetailsError<'e>>
    (details: string option)
    : Result<string option, 'e> =
    match details with
    | Some details when details.Length > 500 ->
      Error('e.ReportDetailsTooLong details.Length)
    | details -> Ok details

  let create(env, request: CreateRequest) = asyncResult {
    let! body = CommitBodies.parse request.Body

    let! target =
      match request.Target with
      | New -> Ok None
      | ReplyTo raw ->
        CommitIds.parse raw
        |> Result.map(fun id ->
          Some("parent_id", Encode.string(CommitIds.value id)))
      | Quote raw ->
        CommitIds.parse raw
        |> Result.map(fun id ->
          Some("quoted_commit_id", Encode.string(CommitIds.value id)))

    let body =
      Json.object(
        [ "body", Encode.string(CommitBodies.value body) ]
        @ Option.toList target
      )
      |> _.ToJsonString()

    return!
      Internal.sendSigned(
        env,
        {
          KeyFile = request.KeyFile
          Method = "POST"
          Target = "/api/v1/commits"
          Body = body
          Decode = Commit.decodeCreated
        }
      )
  }

  let show(env, request: ShowRequest) = asyncResult {
    let! path = Internal.commitPath("", request.CommitId)

    return!
      Internal.sendSigned(
        env,
        {
          KeyFile = request.KeyFile
          Method = "GET"
          Target = path
          Body = ""
          Decode = CommitPage.decode
        }
      )
  }

  let replies(env, request: RepliesRequest) = asyncResult {
    let! path = Internal.commitPath("/replies", request.CommitId)

    let target =
      Internal.withQuery(
        path,
        Internal.flagParameter("newest", request.Newest)
        @ Internal.optionalParameter("cursor", request.Cursor)
      )

    return!
      Internal.sendSigned(
        env,
        {
          KeyFile = request.KeyFile
          Method = "GET"
          Target = target
          Body = ""
          Decode = CommitNode.decodePage
        }
      )
  }

  let delete(env, request: ActionRequest) = asyncResult {
    let! path = Internal.commitPath("", request.CommitId)

    return!
      Internal.sendSigned(
        env,
        {
          KeyFile = request.KeyFile
          Method = "DELETE"
          Target = path
          Body = ""
          Decode = Internal.decodeUnit
        }
      )
  }

  let report(env, request: ReportRequest) = asyncResult {
    let! path = Internal.commitPath("/reports", request.CommitId)
    let! details = validateDetails request.Details

    let body =
      let details =
        details
        |> Option.map(fun details -> [ "details", Encode.string details ])
        |> Option.defaultValue []

      Json.object(
        [ "reason", Encode.string(ReportReasons.wire request.Reason) ] @ details
      )
      |> _.ToJsonString()

    return!
      Internal.sendSigned(
        env,
        {
          KeyFile = request.KeyFile
          Method = "POST"
          Target = path
          Body = body
          Decode = Internal.decodeUnit
        }
      )
  }

module Stars =

  type ListRequest = {
    KeyFile: string
    Cursor: string option
  }

  type ActionRequest = { KeyFile: string; CommitId: string }

  let index(env, request: ListRequest) =
    let target =
      Internal.withQuery(
        "/api/v1/starred",
        Internal.optionalParameter("cursor", request.Cursor)
      )

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = target
        Body = ""
        Decode = CommitNode.decodePage
      }
    )

  let star(env, request: ActionRequest) = asyncResult {
    let! path = Internal.commitPath("/star", request.CommitId)

    return!
      Internal.sendSigned(
        env,
        {
          KeyFile = request.KeyFile
          Method = "POST"
          Target = path
          Body = ""
          Decode = Commit.decodeCreated
        }
      )
  }

  let unstar(env, request: ActionRequest) = asyncResult {
    let! path = Internal.commitPath("/star", request.CommitId)

    return!
      Internal.sendSigned(
        env,
        {
          KeyFile = request.KeyFile
          Method = "DELETE"
          Target = path
          Body = ""
          Decode = Commit.decodeCreated
        }
      )
  }

module Pasteis =

  type ActionRequest = { KeyFile: string; CommitId: string }

  let give(env, request: ActionRequest) = asyncResult {
    let! path = Internal.commitPath("/pasteis", request.CommitId)

    return!
      Internal.sendSigned(
        env,
        {
          KeyFile = request.KeyFile
          Method = "POST"
          Target = path
          Body = ""
          Decode = PastelResult.decode
        }
      )
  }

module Logs =

  type Request = {
    KeyFile: string
    Log: LogKind
    Since: string option
    Cursor: string option
  }

  let get(env, request: Request) = asyncResult {
    let! since = request.Since |> Option.traverseResult CommitIds.parse

    let target =
      Internal.withQuery(
        "/api/v1/log/" + LogKinds.wire request.Log,
        Internal.optionalParameter("since", since |> Option.map CommitIds.value)
        @ Internal.optionalParameter("cursor", request.Cursor)
      )

    return!
      Internal.sendSigned(
        env,
        {
          KeyFile = request.KeyFile
          Method = "GET"
          Target = target
          Body = ""
          Decode = LogPage.decode
        }
      )
  }

module Notifications =

  type ListRequest = {
    KeyFile: string
    Filter: string option
    Through: DateTimeOffset option
    Cursor: string option
  }

  type GroupRequest = {
    KeyFile: string
    NotificationId: string
    Filter: string option
    Cursor: string option
  }

  let list(env, request: ListRequest) =
    let target =
      Internal.withQuery(
        "/api/v1/notifications",
        Internal.optionalParameter("type", request.Filter)
        @ (request.Through
           |> Option.map(fun through -> [
             "through", through.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")
           ])
           |> Option.defaultValue [])
        @ Internal.optionalParameter("cursor", request.Cursor)
      )

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = target
        Body = ""
        Decode = NotificationPage.decode
      }
    )

  let group(env, request: GroupRequest) =
    let target =
      Internal.withQuery(
        "/api/v1/notifications/"
        + Uri.EscapeDataString request.NotificationId
        + "/group",
        Internal.optionalParameter("type", request.Filter)
        @ Internal.optionalParameter("cursor", request.Cursor)
      )

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = target
        Body = ""
        Decode = Notification.decodePage
      }
    )
