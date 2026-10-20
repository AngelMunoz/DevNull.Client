module DevNull.Client.Errors

open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Types

/// Every failure the commit protocol can produce, one case per leaf member.
type AppError =
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
  | Refused of status: int * code: ErrorCode * message: string
  | DecodeFailed of failure: DecodeFailure
  | ReportDetailsTooLong of length: int
  | InvalidLaneIndex of index: int
  | InvalidLaneColor of color: string
  | SignInFailed of message: string

  interface CommitBodyError<AppError> with
    static member BodyTooShort(raw, length) = BodyTooShort(raw, length)
    static member BodyTooLong(raw, length) = BodyTooLong(raw, length)

  interface ShortHashError<AppError> with
    static member InvalidShortHash raw = InvalidShortHash raw

  interface CommitReferenceError<AppError> with
    static member InvalidCommitReference raw = InvalidCommitReference raw

  interface ImageError<AppError> with
    static member TooManyImages count = TooManyImages count
    static member ImageTooLarge(fileName, size) = ImageTooLarge(fileName, size)
    static member UnsupportedImage fileName = UnsupportedImage fileName

  interface CommitIdError<AppError> with
    static member InvalidCommitId raw = InvalidCommitId raw

  interface NonceError<AppError> with
    static member MalformedNonce raw = MalformedNonce raw

  interface KeyError<AppError> with
    static member LookupUnreachable message = LookupUnreachable message
    static member LookupRefused(status, body) = LookupRefused(status, body)
    static member UnexpectedKey key = UnexpectedKey key

  interface SigningError<AppError> with
    static member SigningFailed message = SigningFailed message

  interface PostingError<AppError> with
    static member PostUnreachable message = PostUnreachable message
    static member PostRefused(status, body) = PostRefused(status, body)
    static member UnexpectedResponse body = UnexpectedResponse body

  interface TransportError<AppError> with
    static member TransportFailed message = TransportFailed message

  interface Rejected<AppError> with
    static member Refused(status, code, message) =
      Refused(status, code, message)

  interface DecodeError<AppError> with
    static member DecodeFailed failure = DecodeFailed failure

  interface ReportDetailsError<AppError> with
    static member ReportDetailsTooLong length = ReportDetailsTooLong length

  interface LaneError<AppError> with
    static member InvalidLaneIndex index = InvalidLaneIndex index
    static member InvalidLaneColor color = InvalidLaneColor color

/// One line of plain text per failure, for the app.
let describe error =
  let refused (what: string) (status: int, body: string) =
    $"dev-null.sh refused the {what} (HTTP {status}): {body.Trim()}"

  match error with
  | BodyTooShort(_, length) ->
    $"The commit body needs at least 1 character, got {length}."
  | BodyTooLong(_, length) ->
    $"The commit body allows at most 4000 characters, got {length}."
  | InvalidShortHash raw -> $"{raw} is not a commit hash."
  | InvalidCommitReference raw -> $"{raw} is not a commit hash, id, or link."
  | InvalidCommitId raw -> $"{raw} is not a commit id."
  | TooManyImages count -> $"A commit allows at most 3 images, got {count}."
  | ImageTooLarge(fileName, size) ->
    $"{fileName} is larger than 10 MB, got {size} bytes."
  | UnsupportedImage fileName ->
    $"{fileName} is not a JPEG, PNG, or WebP image."
  | MalformedNonce _ -> "The generated nonce is malformed."
  | LookupUnreachable message ->
    $"Could not reach dev-null.sh for the key lookup: {message.Trim()}"
  | LookupRefused(status, body) -> refused "key lookup" (status, body)
  | UnexpectedKey key -> $"Unexpected answer from the key lookup: {key}"
  | SigningFailed message -> $"ssh-keygen failed: {message.Trim()}"
  | PostUnreachable message ->
    $"Could not reach dev-null.sh for the commit: {message.Trim()}"
  | PostRefused(status, body) -> refused "commit" (status, body)
  | UnexpectedResponse body ->
    $"Unexpected answer from dev-null.sh: {body.Trim()}"
  | TransportFailed message -> $"Could not reach dev-null.sh: {message.Trim()}"
  | Refused(status, Unknown "", message) ->
    $"dev-null.sh refused the request (HTTP {status}): {message.Trim()}"
  | Refused(status, code, message) ->
    $"dev-null.sh refused the request (HTTP {status}, {ErrorCodes.wire code}): {message.Trim()}"
  | DecodeFailed failure ->
    $"Unexpected answer from dev-null.sh at {failure.Path}: {failure.Message}"
  | ReportDetailsTooLong length ->
    $"Report details allow at most 500 characters, got {length}."
  | InvalidLaneIndex index -> $"Lane index {index} is not between 0 and 5."
  | InvalidLaneColor color -> $"{color} is not a lowercase six-digit hex color."
  | SignInFailed message -> message
