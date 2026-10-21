module Tests.TestSupport

open Expecto
open FSharp.UMX
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types

/// Every failure the commit protocol can produce, one case per leaf member.
type TestError =
  | BodyTooShort of raw: string * length: int
  | BodyTooLong of raw: string * length: int
  | InvalidShortHash of raw: string
  | InvalidCommitReference of raw: string
  | InvalidCommitId of raw: string
  | TooManyImages of count: int
  | ImageTooLarge of fileName: string * size: int
  | UnsupportedImage of fileName: string
  | MalformedNonce of raw: string
  | LookupUnreachable of message: string
  | LookupRefused of status: int * body: string
  | UnexpectedKey of key: string
  | SigningFailed of message: string
  | PostUnreachable of message: string
  | PostRefused of status: int * body: string
  | UnexpectedResponse of body: string
  | TransportFailed of message: string
  | Refused of status: int * code: ErrorCode option * message: string
  | DecodeFailed of failure: DecodeFailure
  | ReportDetailsTooLong of length: int
  | InvalidLaneIndex of index: int
  | InvalidLaneColor of color: string

  interface CommitBodyError<TestError> with
    static member BodyTooShort(raw, length) = BodyTooShort(raw, length)
    static member BodyTooLong(raw, length) = BodyTooLong(raw, length)

  interface ShortHashError<TestError> with
    static member InvalidShortHash raw = InvalidShortHash raw

  interface CommitReferenceError<TestError> with
    static member InvalidCommitReference raw = InvalidCommitReference raw

  interface ImageError<TestError> with
    static member TooManyImages count = TooManyImages count
    static member ImageTooLarge(fileName, size) = ImageTooLarge(fileName, size)
    static member UnsupportedImage fileName = UnsupportedImage fileName

  interface CommitIdError<TestError> with
    static member InvalidCommitId raw = InvalidCommitId raw

  interface NonceError<TestError> with
    static member MalformedNonce raw = MalformedNonce raw

  interface KeyError<TestError> with
    static member LookupUnreachable message = LookupUnreachable message
    static member LookupRefused(status, body) = LookupRefused(status, body)
    static member UnexpectedKey key = UnexpectedKey key

  interface SigningError<TestError> with
    static member SigningFailed message = SigningFailed message

  interface PostingError<TestError> with
    static member PostUnreachable message = PostUnreachable message
    static member PostRefused(status, body) = PostRefused(status, body)
    static member UnexpectedResponse body = UnexpectedResponse body

  interface TransportError<TestError> with
    static member TransportFailed message = TransportFailed message

  interface Rejected<TestError> with
    static member Refused(status, code, message) =
      Refused(status, code, message)

  interface DecodeError<TestError> with
    static member DecodeFailed failure = DecodeFailed failure

  interface ReportDetailsError<TestError> with
    static member ReportDetailsTooLong length = ReportDetailsTooLong length

  interface LaneError<TestError> with
    static member InvalidLaneIndex index = InvalidLaneIndex index
    static member InvalidLaneColor color = InvalidLaneColor color

type FixedClock(timestamp: int64) =

  interface IClock with
    member _.UnixNow() = timestamp

type FixedNonceSource(hex: string) =

  interface INonceSource with
    member _.NewHex() = hex

type FixedPkceSource(verifier: string) =

  interface IPkceSource with
    member _.NewVerifier() = verifier

type FixedSigner(outcome: Result<string, SignFailure>) =

  interface ISigner with
    member _.Sign _ = async { return outcome }

let signerWith(onSign: SignRequest -> Result<string, SignFailure>) : ISigner = {
  new ISigner with
    member _.Sign request = async { return onSign request }
}

let bodyText(request: HttpRequest) =
  match request.Body with
  | Empty -> ""
  | Text text -> text
  | Multipart _ -> failtest "expected a text body"

let multipartForm(request: HttpRequest) =
  match request.Body with
  | Multipart form -> form
  | _ -> failtest "expected a multipart body"

/// One successful HTTP response.
let ok body : Result<HttpResponse, TransportFailure> =
  Ok {
    Status = 200
    Headers = []
    Body = body
  }

/// One HTTP response with a non-success status.
let refused status body : Result<HttpResponse, TransportFailure> =
  Ok {
    Status = status
    Headers = []
    Body = body
  }

type FakeHttp
  (
    lookup: Result<HttpResponse, TransportFailure>,
    post: Result<HttpResponse, TransportFailure>
  ) =

  interface IHttp with
    member _.Send(request) = async {
      match request.Target with
      | "/api/cli/key-lookups" -> return lookup
      | "/api/cli/commits" -> return post
      | target ->
        return
          Error(
            {
              Message = $"unexpected target {target}"
            }
            : TransportFailure
          )
    }

/// One HTTP worker that records every request and returns a canned response.
type CapturingHttp(response: Result<HttpResponse, TransportFailure>) =
  let requests = ResizeArray<HttpRequest>()

  member _.Requests = requests |> Seq.toList

  interface IHttp with
    member _.Send(request) = async {
      requests.Add request
      return response
    }

/// The capabilities the signed-request leaves name.
type ISignRequirements =
  inherit IProvideClock
  inherit IProvideNonceSource
  inherit IProvideSigner

/// The capabilities the `Commit.commit` composite names, for tests.
type ICommitRequirements =
  inherit ISignRequirements
  inherit IProvideHttp

/// The capabilities one device authorization needs.
type IAuthorizationRequirements =
  inherit IProvidePkceSource
  inherit IProvideNonceSource

type TestWorkers = {
  Clock: IClock
  NonceSource: INonceSource
  Signer: ISigner
  Http: IHttp
}

let createSignEnv
  (clock: IClock)
  (nonceSource: INonceSource)
  (signer: ISigner)
  = {
  new ISignRequirements with
    member _.Clock = clock
    member _.NonceSource = nonceSource
    member _.Signer = signer
}

let createEnv(workers: TestWorkers) = {
  new ICommitRequirements with
    member _.Clock = workers.Clock
    member _.NonceSource = workers.NonceSource
    member _.Signer = workers.Signer
    member _.Http = workers.Http
}

let createAuthorizationEnv
  (nonceSource: INonceSource)
  (pkceSource: IPkceSource)
  = {
  new IAuthorizationRequirements with
    member _.NonceSource = nonceSource
    member _.PkceSource = pkceSource
}

/// Domain values for tests, from input that parses.
module Values =

  let body(raw: string) : string<CommitBody> =
    let parsed: Result<string<CommitBody>, TestError> = CommitBodies.parse raw

    match parsed with
    | Ok value -> value
    | Error error -> failtestf "unexpected %A" error

  let hash(raw: string) : string<ShortHash> =
    let parsed: Result<string<ShortHash>, TestError> = ShortHashes.parse raw

    match parsed with
    | Ok value -> value
    | Error error -> failtestf "unexpected %A" error

  let reference(raw: string) : string<CommitReference> =
    let parsed: Result<string<CommitReference>, TestError> =
      CommitReferences.parse raw

    match parsed with
    | Ok value -> value
    | Error error -> failtestf "unexpected %A" error

  let nonce(raw: string) : string<Nonce> =
    let parsed: Result<string<Nonce>, TestError> = Nonces.parse raw

    match parsed with
    | Ok value -> value
    | Error error -> failtestf "unexpected %A" error
