module DevNull.Client.Core.Api

open System
open System.Text
open FSharp.UMX
open JDeck
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Core.Pkce
open DevNull.Client.Core.Types
open FsToolkit.ErrorHandling
open IcedTasks

/// One staged image as the CLI composite defines it: a name and raw bytes.
type ImageAttachment = DevNull.Client.Core.Commit.ImageAttachment

/// One parsed image: its bytes, its content type, and its SHA-256.
type PostableImage = DevNull.Client.Core.Commit.Image

/// One staged voice note: its bytes, its content type, and its SHA-256.
type VoiceAttachment = DevNull.Client.Core.Commit.Voice

/// One voice note's loudness bars: 48 values between 0 and 100.
type Waveform = DevNull.Client.Core.Commit.Waveform

[<Literal>]
let NamespaceName = "devnull-api"

[<Literal>]
let ImagesNamespaceName = "devnull-api-images"

module Signing =

  type Request = {
    KeyFile: string
    Method: string
    Target: string
    Body: string
  }

  type ImageRequest = {
    KeyFile: string
    Method: string
    Target: string
    Body: string
    Images: PostableImage list
  }

  type FilesRequest = {
    KeyFile: string
    Method: string
    Target: string
    Body: string
    PayloadName: string
    Files: (string * PostableImage) list
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

  let signUnder<'env, 'e when 'env :> IProvideSigner and SigningError<'e>>
    (env: 'env, namespaceName: string, keyFile: string, message: string)
    : Async<Result<string, 'e>> = asyncEx {
    let! outcome =
      env.Signer.Sign {
        KeyFile = keyFile
        Namespace = namespaceName
        Payload = message
      }

    return
      outcome
      |> Result.mapError(fun failure -> 'e.SigningFailed failure.Message)
  }

  /// Signs one v1 request with named files: the JSON body rides a
  /// payload part, each file rides under its own part name, and the
  /// signed message carries the comma-separated file hashes on one
  /// line before the body, under the images namespace.
  let signedFilesRequest<'env, 'e
    when Deps<'env> and NonceError<'e> and SigningError<'e>>
    (env: 'env, request: FilesRequest)
    : Async<Result<HttpRequest, 'e>> = asyncResult {
    let timestamp = Clock.unixNow env
    let! nonce = Nonces.parse(NonceSource.newHex env)
    let method = request.Method.Trim().ToUpperInvariant()

    let hashes =
      request.Files |> List.map(fun (_, file) -> file.Hash) |> String.concat ","

    let message =
      String.concat "\n" [
        string timestamp
        Nonces.value nonce
        method
        request.Target
        hashes
        request.Body
      ]

    let! signature =
      signUnder(env, ImagesNamespaceName, request.KeyFile, message)

    let payload = {
      Name = "payload"
      FileName = request.PayloadName
      ContentType = "application/json"
      Content = Encoding.UTF8.GetBytes request.Body
    }

    let files =
      request.Files
      |> List.map(fun (name, file) -> {
        Name = name
        FileName = file.FileName
        ContentType = file.ContentType
        Content = file.Bytes
      })

    return {
      Method = method
      Target = request.Target
      Headers = [
        "User-Agent", userAgent
        "X-Devnull-Timestamp", string timestamp
        "X-Devnull-Nonce", Nonces.value nonce
        "X-SSH-Signature", signature
      ]
      Body =
        Multipart {
          Fields = []
          Files = payload :: files
        }
    }
  }

  /// Signs one v1 request with images: the JSON body rides a payload
  /// part, the images ride `images[]` files beside it, and the signed
  /// message carries the comma-separated image hashes on one line
  /// before the body, under the images namespace.
  let signedImageRequest<'env, 'e
    when Deps<'env> and NonceError<'e> and SigningError<'e>>
    (env: 'env, request: ImageRequest)
    : Async<Result<HttpRequest, 'e>> =
    signedFilesRequest(
      env,
      {
        KeyFile = request.KeyFile
        Method = request.Method
        Target = request.Target
        Body = request.Body
        PayloadName = "commit.json"
        Files = request.Images |> List.map(fun image -> "images[]", image)
      }
    )

module Internal =

  type SignedCall<'value> = {
    KeyFile: string
    Method: string
    Target: string
    Body: string
    Decode: string -> Result<'value, DecodeFailure>
  }

  type ImageCall<'value> = {
    KeyFile: string
    Method: string
    Target: string
    Body: string
    Images: PostableImage list
    Decode: string -> Result<'value, DecodeFailure>
  }

  type FileCall<'value> = {
    KeyFile: string
    Method: string
    Target: string
    Body: string
    PayloadName: string
    Files: (string * PostableImage) list
    Decode: string -> Result<'value, DecodeFailure>
  }

  type SendSignedDeps<'Env
    when 'Env :> IProvideClock
    and 'Env :> IProvideNonceSource
    and 'Env :> IProvideSigner
    and 'Env :> IProvideHttp> = 'Env

  /// Sends one signed request and decodes its answer, reading the
  /// refusal envelope off every non-success status.
  let deliver<'env, 'e, 'value
    when 'env :> IProvideHttp
    and TransportError<'e>
    and Rejected<'e>
    and DecodeError<'e>>
    (
      env: 'env,
      signed: HttpRequest,
      decode: string -> Result<'value, DecodeFailure>
    ) : Async<Result<'value, 'e>> = asyncEx {
    match! env.Http.Send signed with
    | Error failure -> return Error('e.TransportFailed failure.Message)
    | Ok response when isSuccess response.Status ->
      return
        decode response.Body
        |> Result.mapError(fun failure -> 'e.DecodeFailed failure)
    | Ok response ->
      let fallback: Failure = {
        Message = response.Body.Trim()
        Code = None
      }

      let failure =
        ApiTypes.Failure.decode response.Body |> Result.defaultValue fallback

      return Error('e.Refused(response.Status, failure.Code, failure.Message))
  }

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
    | Ok signed -> return! deliver(env, signed, call.Decode)
  }

  let sendSignedFiles<'env, 'e, 'value
    when SendSignedDeps<'env>
    and NonceError<'e>
    and SigningError<'e>
    and TransportError<'e>
    and Rejected<'e>
    and DecodeError<'e>>
    (env: 'env, call: FileCall<'value>)
    : Async<Result<'value, 'e>> = asyncEx {
    match!
      Signing.signedFilesRequest(
        env,
        {
          KeyFile = call.KeyFile
          Method = call.Method
          Target = call.Target
          Body = call.Body
          PayloadName = call.PayloadName
          Files = call.Files
        }
      )
    with
    | Error error -> return Error error
    | Ok signed -> return! deliver(env, signed, call.Decode)
  }

  let sendSignedImage<'env, 'e, 'value
    when SendSignedDeps<'env>
    and NonceError<'e>
    and SigningError<'e>
    and TransportError<'e>
    and Rejected<'e>
    and DecodeError<'e>>
    (env: 'env, call: ImageCall<'value>)
    : Async<Result<'value, 'e>> =
    sendSignedFiles(
      env,
      {
        KeyFile = call.KeyFile
        Method = call.Method
        Target = call.Target
        Body = call.Body
        PayloadName = "commit.json"
        Files = call.Images |> List.map(fun image -> "images[]", image)
        Decode = call.Decode
      }
    )

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
    value
    |> Option.map(fun value -> List.singleton(name, value))
    |> Option.defaultValue []

  let flagParameter(name: string, value: bool) =
    if value then List.singleton(name, "1") else []

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

  /// The terms version the app showed the person, as the server takes
  /// it back.
  type TermsRequest = { KeyFile: string; Version: string }

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
      Json.object(List.singleton("enabled", Encode.boolean request.Enabled))
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
        | Automatic -> Json.object(List.singleton("lane", Encode.Null()))
        | Palette index -> Json.object(List.singleton("lane", Encode.int index))
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

  let acceptTerms(env, request: TermsRequest) =
    let body =
      Json.object(List.singleton("version", Encode.string request.Version))
      |> _.ToJsonString()

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "PUT"
        Target = "/api/v1/me/terms"
        Body = body
        Decode = Me.decode
      }
    )

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
    Images: ImageAttachment list
    Poll: PollSpec option
  }

  type VoteRequest = {
    KeyFile: string
    CommitId: string
    OptionId: string
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

  type PreviewRequest = { KeyFile: string; Body: string }

  let validateDetails<'e when ReportDetailsError<'e>>
    (details: string option)
    : Result<string option, 'e> =
    match details with
    | Some details when details.Length > 500 ->
      Error('e.ReportDetailsTooLong details.Length)
    | details -> Ok details

  /// The server refuses a poll on a reply, a quote, or a commit with
  /// images; the boundary states the rule before the post.
  let checkPoll<'e when PollError<'e>>
    (poll: PollSpec option, target: Target, images: ImageAttachment list)
    : Result<PollSpec option, 'e> =
    match poll, target, images with
    | None, _, _ -> Ok None
    | Some _, New, [] -> Ok poll
    | Some _, ReplyTo _, _ -> Error('e.PollProhibited PollOnReply)
    | Some _, Quote _, _ -> Error('e.PollProhibited PollOnQuote)
    | Some _, New, _ -> Error('e.PollProhibited PollWithImages)

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

    let! poll = checkPoll(request.Poll, request.Target, request.Images)

    let! images = DevNull.Client.Core.Commit.CommitImages.parse request.Images

    let pollJson =
      poll
      |> Option.map(fun spec ->
        Json.object [
          "options", Json.sequence(PollSpecs.options spec, Encode.string)
          "duration", Encode.int(PollDurations.wire(PollSpecs.duration spec))
        ])

    let json =
      Json.object [
        "body", Encode.string(CommitBodies.value body)
        match target with
        | Some(name, value) -> name, value
        | None -> ()
        match pollJson |> Option.map(fun poll -> "poll", poll) with
        | Some(name, value) -> name, value
        | None -> ()
      ]
      |> _.ToJsonString()

    let baseCall: Internal.SignedCall<Commit> = {
      KeyFile = request.KeyFile
      Method = "POST"
      Target = "/api/v1/commits"
      Body = json
      Decode = Commit.decodeCreated
    }

    return!
      match images with
      | [] -> Internal.sendSigned(env, baseCall)
      | staged ->
        Internal.sendSignedImage(
          env,
          {
            KeyFile = baseCall.KeyFile
            Method = baseCall.Method
            Target = baseCall.Target
            Body = baseCall.Body
            Images = staged
            Decode = baseCall.Decode
          }
        )
  }

  /// Votes one poll option; the answer is the commit as its page shows
  /// it, poll results included.
  let vote(env, request: VoteRequest) = asyncResult {
    let! path = Internal.commitPath("/poll/votes", request.CommitId)

    let json =
      Json.object(List.singleton("option", Encode.string request.OptionId))
      |> _.ToJsonString()

    let call: Internal.SignedCall<Commit> = {
      KeyFile = request.KeyFile
      Method = "POST"
      Target = path
      Body = json
      Decode = Commit.decodeCreated
    }

    return! Internal.sendSigned(env, call)
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
      Json.object [
        "reason", Encode.string(ReportReasons.wire request.Reason)
        match details with
        | Some details -> "details", Encode.string details
        | None -> ()
      ]
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

  let preview(env, request: PreviewRequest) = asyncResult {
    let! body = CommitBodies.parse request.Body

    let json =
      Json.object(
        List.singleton("body", Encode.string(CommitBodies.value body))
      )
      |> _.ToJsonString()

    return!
      Internal.sendSigned(
        env,
        {
          KeyFile = request.KeyFile
          Method = "POST"
          Target = "/api/v1/commits/preview"
          Body = json
          Decode = CommitPreview.decode
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
        [
          yield! Internal.optionalParameter("type", request.Filter)

          yield!
            request.Through
            |> Option.map(fun through ->
              List.singleton(
                "through",
                through.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")
              ))
            |> Option.defaultValue []

          yield! Internal.optionalParameter("cursor", request.Cursor)
        ]
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

module Remotes =

  type ThreadTarget =
    | Partner of username: string
    | Org of orgId: string

  type TargetRequest = {
    KeyFile: string
    Target: ThreadTarget
  }

  type ReadRequest = {
    KeyFile: string
    Target: ThreadTarget
    Through: int
  }

  type MuteRequest = {
    KeyFile: string
    Target: ThreadTarget
    Duration: MuteDuration
  }

  type PinRequest = {
    KeyFile: string
    Target: ThreadTarget
    Pinned: bool
  }

  type InboxRequest = {
    KeyFile: string
    Tab: InboxTab
    Search: string option
    Before: string option
  }

  type ThreadRequest = {
    KeyFile: string
    Partner: string
    After: int option
    Before: int option
    ReactionsAfter: int option
  }

  type OrgThreadRequest = {
    KeyFile: string
    OrgId: string
    After: int option
    Before: int option
    ReactionsAfter: int option
  }

  type LinkCard =
    | FirstLink
    | NoCard
    | CardOf of url: string

  type PushRequest = {
    KeyFile: string
    Target: ThreadTarget
    Body: string option
    ClientId: string<ClientId>
    ReplyTo: int option
    Card: LinkCard
    Images: ImageAttachment list
    ImageDescriptions: string option list
    Voice: (ImageAttachment * Waveform) option
  }

  type MergeRequest = { KeyFile: string; Partner: string }

  type ReactionRequest = {
    KeyFile: string
    MessageId: int
    Emoji: string
  }

  type LinkPreviewRequest = { KeyFile: string; MessageId: int }

  type UsageRequest = { KeyFile: string }

  type SettingsRequest = { KeyFile: string; Enabled: bool }

  type PreferencesRequest = {
    KeyFile: string
    From: RemotesFrom
    PullReceipts: bool
  }

  type CandidateFor =
    | ForRemote
    | ForOrg

  type CandidatesRequest = {
    KeyFile: string
    Search: string option
    Org: string option
    For: CandidateFor option
  }

  type CreateOrgRequest = {
    KeyFile: string
    Name: string option
    Members: string list
  }

  type RenameOrgRequest = {
    KeyFile: string
    OrgId: string
    Name: string option
  }

  type AddMembersRequest = {
    KeyFile: string
    OrgId: string
    Members: string list
    ShareHistory: bool
  }

  type MemberRoleRequest = {
    KeyFile: string
    OrgId: string
    Member: string
    Role: OrgRole
  }

  type RemoveMemberRequest = {
    KeyFile: string
    OrgId: string
    Member: string
  }

  let path(target: ThreadTarget, suffix: string) =
    match target with
    | Partner username ->
      "/api/v1/remotes/@" + Uri.EscapeDataString username + suffix
    | Org orgId -> "/api/v1/remotes/orgs/" + Uri.EscapeDataString orgId + suffix

  let inbox(env, request: InboxRequest) =
    let target =
      Internal.withQuery(
        "/api/v1/remotes",
        List.concat [
          List.singleton("tab", InboxTabs.wire request.Tab)
          Internal.optionalParameter("q", request.Search)
          Internal.optionalParameter("before", request.Before)
        ]
      )

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = target
        Body = ""
        Decode = Inbox.decode
      }
    )

  let thread(env, request: ThreadRequest) =
    let target =
      Internal.withQuery(
        path(Partner request.Partner, "/messages"),
        List.concat [
          Internal.optionalParameter(
            "after",
            request.After |> Option.map string
          )
          Internal.optionalParameter(
            "before",
            request.Before |> Option.map string
          )
          Internal.optionalParameter(
            "reactions_after",
            request.ReactionsAfter |> Option.map string
          )
        ]
      )

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = target
        Body = ""
        Decode = Thread.decode
      }
    )

  let orgThread(env, request: OrgThreadRequest) =
    let target =
      Internal.withQuery(
        path(Org request.OrgId, "/messages"),
        List.concat [
          Internal.optionalParameter(
            "after",
            request.After |> Option.map string
          )
          Internal.optionalParameter(
            "before",
            request.Before |> Option.map string
          )
          Internal.optionalParameter(
            "reactions_after",
            request.ReactionsAfter |> Option.map string
          )
        ]
      )

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = target
        Body = ""
        Decode = OrgThread.decode
      }
    )

  /// The JSON body one pushed message rides in its payload part: the
  /// client id, the body when there is one, the reply, the chosen link
  /// card, and the descriptions or the waveform its files brought.
  let pushBody(clientId: string<ClientId>, request: PushRequest) =
    let descriptions =
      request.ImageDescriptions
      |> List.map(fun description ->
        description
        |> Option.map Encode.string
        |> Option.defaultValue(Encode.Null()))

    let waveform =
      request.Voice
      |> Option.map(fun (_, waveform) ->
        Json.sequence(Commit.Voices.bars waveform, Encode.int))

    Json.object(
      List.concat [
        List.singleton("client_id", Encode.string(ClientIds.value clientId))

        request.Body
        |> Option.map(fun text -> List.singleton("body", Encode.string text))
        |> Option.defaultValue []

        request.ReplyTo
        |> Option.map(fun reply ->
          List.singleton("reply_to_id", Encode.int reply))
        |> Option.defaultValue []

        match request.Card with
        | FirstLink -> []
        | NoCard -> List.singleton("link_preview_url", Encode.Null())
        | CardOf url -> List.singleton("link_preview_url", Encode.string url)

        match request.Images with
        | [] -> []
        | _ ->
          List.singleton("image_descriptions", Json.sequence(descriptions, id))

        waveform
        |> Option.map(fun bars -> List.singleton("voice_waveform", bars))
        |> Option.defaultValue []
      ]
    )
    |> _.ToJsonString()

  let push(env, request: PushRequest) = asyncResult {
    let! body = request.Body |> Option.traverseResult MessageBodies.parse

    let! images = DevNull.Client.Core.Commit.CommitImages.parse request.Images

    let! voice =
      request.Voice
      |> Option.traverseResult(fun (voice, waveform) ->
        Commit.Voices.parse(voice.FileName, voice.Bytes)
        |> Result.map(fun staged -> staged, waveform))

    do!
      Commit.Voices.check(
        voice |> Option.map fst,
        body.IsSome,
        not images.IsEmpty
      )

    let json = pushBody(request.ClientId, request)

    let baseCall: Internal.SignedCall<Message> = {
      KeyFile = request.KeyFile
      Method = "POST"
      Target = path(request.Target, "/messages")
      Body = json
      Decode = Message.decode
    }

    let files =
      List.concat [
        [ for image in images -> "images[]", image ]

        voice
        |> Option.map(fun (staged, _) -> List.singleton("voice", staged))
        |> Option.defaultValue []
      ]

    return!
      match files with
      | [] -> Internal.sendSigned(env, baseCall)
      | files ->
        Internal.sendSignedFiles(
          env,
          {
            KeyFile = baseCall.KeyFile
            Method = baseCall.Method
            Target = baseCall.Target
            Body = baseCall.Body
            PayloadName = "message.json"
            Files = files
            Decode = baseCall.Decode
          }
        )
  }

  let markRead(env, request: ReadRequest) =
    let body =
      Json.object(List.singleton("through", Encode.int request.Through))
      |> _.ToJsonString()

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "PUT"
        Target = path(request.Target, "/read")
        Body = body
        Decode = Internal.decodeUnit
      }
    )

  let typing(env, request: TargetRequest) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "PUT"
        Target = path(request.Target, "/typing")
        Body = ""
        Decode = Internal.decodeUnit
      }
    )

  /// Nudges the partner or the org: a small attention ping the other
  /// side sees the way they see typing.
  let ping(env, request: TargetRequest) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "POST"
        Target = path(request.Target, "/ping")
        Body = ""
        Decode = Internal.decodeUnit
      }
    )

  let stash(env, request: TargetRequest) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "POST"
        Target = path(request.Target, "/archive")
        Body = ""
        Decode = Internal.decodeUnit
      }
    )

  let unstash(env, request: TargetRequest) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "DELETE"
        Target = path(request.Target, "/archive")
        Body = ""
        Decode = Internal.decodeUnit
      }
    )

  let mute(env, request: MuteRequest) =
    let body =
      Json.object(
        List.singleton(
          "duration",
          Encode.string(MuteDurations.wire request.Duration)
        )
      )
      |> _.ToJsonString()

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "PUT"
        Target = path(request.Target, "/mute")
        Body = body
        Decode = ThreadMuteResult.decode
      }
    )

  let pin(env, request: PinRequest) =
    let body =
      Json.object(List.singleton("pinned", Encode.boolean request.Pinned))
      |> _.ToJsonString()

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "PUT"
        Target = path(request.Target, "/pin")
        Body = body
        Decode = PinResult.decode
      }
    )

  let merge(env, request: MergeRequest) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "POST"
        Target = path(Partner request.Partner, "/merge")
        Body = ""
        Decode = Internal.decodeUnit
      }
    )

  let drop(env, request: MergeRequest) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "POST"
        Target = path(Partner request.Partner, "/drop")
        Body = ""
        Decode = Internal.decodeUnit
      }
    )

  let reactionUsage(env, request: UsageRequest) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = "/api/v1/remotes/reactions"
        Body = ""
        Decode = ReactionUses.decode
      }
    )

  let react(env, request: ReactionRequest) =
    let body =
      Json.object(List.singleton("emoji", Encode.string request.Emoji))
      |> _.ToJsonString()

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "POST"
        Target =
          "/api/v1/remotes/messages/" + string request.MessageId + "/reactions"
        Body = body
        Decode = Reactions.decode
      }
    )

  let unreact(env, request: ReactionRequest) =
    let body =
      Json.object(List.singleton("emoji", Encode.string request.Emoji))
      |> _.ToJsonString()

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "DELETE"
        Target =
          "/api/v1/remotes/messages/" + string request.MessageId + "/reactions"
        Body = body
        Decode = Reactions.decode
      }
    )

  let linkPreview(env, request: LinkPreviewRequest) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target =
          "/api/v1/remotes/messages/"
          + string request.MessageId
          + "/link-preview"
        Body = ""
        Decode = MessageLinkPreview.decode
      }
    )

  let candidates(env, request: CandidatesRequest) =
    let target =
      Internal.withQuery(
        "/api/v1/remotes/candidates",
        List.concat [
          Internal.optionalParameter("q", request.Search)
          Internal.optionalParameter("org", request.Org)

          request.For
          |> Option.map(fun forWhat ->
            List.singleton(
              "for",
              match forWhat with
              | ForRemote -> "remote"
              | ForOrg -> "org"
            ))
          |> Option.defaultValue []
        ]
      )

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "GET"
        Target = target
        Body = ""
        Decode = RemoteCandidates.decode
      }
    )

  let createOrg(env, request: CreateOrgRequest) =
    let body =
      Json.object(
        List.concat [
          List.singleton(
            "members",
            Json.sequence(request.Members, Encode.string)
          )

          request.Name
          |> Option.map(fun name -> List.singleton("name", Encode.string name))
          |> Option.defaultValue []
        ]
      )
      |> _.ToJsonString()

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "POST"
        Target = "/api/v1/remotes/orgs"
        Body = body
        Decode = CreatedOrg.decode
      }
    )

  let renameOrg(env, request: RenameOrgRequest) =
    let body =
      Json.object(
        List.singleton(
          "name",
          request.Name
          |> Option.map Encode.string
          |> Option.defaultValue(Encode.Null())
        )
      )
      |> _.ToJsonString()

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "PUT"
        Target = "/api/v1/remotes/orgs/" + Uri.EscapeDataString request.OrgId
        Body = body
        Decode = Internal.decodeUnit
      }
    )

  let addMembers(env, request: AddMembersRequest) =
    let body =
      Json.object [
        "members", Json.sequence(request.Members, Encode.string)
        "share_history", Encode.boolean request.ShareHistory
      ]
      |> _.ToJsonString()

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "POST"
        Target =
          "/api/v1/remotes/orgs/"
          + Uri.EscapeDataString request.OrgId
          + "/members"
        Body = body
        Decode = Internal.decodeUnit
      }
    )

  let setMemberRole(env, request: MemberRoleRequest) =
    let body =
      Json.object(
        List.singleton("role", Encode.string(OrgRoles.wire request.Role))
      )
      |> _.ToJsonString()

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "PUT"
        Target =
          "/api/v1/remotes/orgs/"
          + Uri.EscapeDataString request.OrgId
          + "/members/"
          + Uri.EscapeDataString request.Member
        Body = body
        Decode = Internal.decodeUnit
      }
    )

  let removeMember(env, request: RemoveMemberRequest) =
    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "DELETE"
        Target =
          "/api/v1/remotes/orgs/"
          + Uri.EscapeDataString request.OrgId
          + "/members/"
          + Uri.EscapeDataString request.Member
        Body = ""
        Decode = Internal.decodeUnit
      }
    )

  let setEnabled(env, request: SettingsRequest) =
    let body =
      Json.object(List.singleton("enabled", Encode.boolean request.Enabled))
      |> _.ToJsonString()

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "PUT"
        Target = "/api/v1/me/remotes"
        Body = body
        Decode = Me.decode
      }
    )

  let setPreferences(env, request: PreferencesRequest) =
    let body =
      Json.object [
        "from", Encode.string(RemotesFroms.wire request.From)
        "pull_receipts", Encode.boolean request.PullReceipts
      ]
      |> _.ToJsonString()

    Internal.sendSigned(
      env,
      {
        KeyFile = request.KeyFile
        Method = "PUT"
        Target = "/api/v1/me/remote-preferences"
        Body = body
        Decode = Me.decode
      }
    )
