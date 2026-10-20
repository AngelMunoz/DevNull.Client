module DevNull.Client.Core.Types

#nowarn "3535"

open System
open System.Text.RegularExpressions
open FSharp.UMX
open DevNull.Client.Core

type CommitBodyError<'e> =
  static abstract BodyTooShort: raw: string * length: int -> 'e
  static abstract BodyTooLong: raw: string * length: int -> 'e

module CommitBodies =

  let inline value(body: string<CommitBody>) = UMX.untag body

  let parse<'e when CommitBodyError<'e>>
    (raw: string)
    : Result<string<CommitBody>, 'e> =
    let length = raw.Length

    if length < 1 then Error('e.BodyTooShort(raw, length))
    elif length > 4000 then Error('e.BodyTooLong(raw, length))
    else Ok(UMX.tag<CommitBody> raw)

type ShortHashError<'e> =
  static abstract InvalidShortHash: raw: string -> 'e

module ShortHashes =

  let inline value(hash: string<ShortHash>) = UMX.untag hash

  let parse<'e when ShortHashError<'e>>
    (raw: string)
    : Result<string<ShortHash>, 'e> =
    let normalized = raw.Trim().ToLowerInvariant()

    if Regex.IsMatch(normalized, "^[0-9a-f]{7,12}$") then
      Ok(UMX.tag<ShortHash> normalized)
    else
      Error('e.InvalidShortHash raw)

type CommitReferenceError<'e> =
  static abstract InvalidCommitReference: raw: string -> 'e

module CommitReferences =

  let inline value(reference: string<CommitReference>) = UMX.untag reference

  let uuidPattern =
    "(?<![a-z0-9-])[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?![a-z0-9-])"

  /// Accepts a short hash, a commit id, or a link that contains a commit id, as the CLI does.
  let parse<'e when CommitReferenceError<'e>>
    (raw: string)
    : Result<string<CommitReference>, 'e> =
    let reference = raw.Trim()

    if
      Regex.IsMatch(reference, uuidPattern, RegexOptions.IgnoreCase)
      || Regex.IsMatch(reference, "^[0-9a-f]{7,12}$", RegexOptions.IgnoreCase)
    then
      Ok(UMX.tag<CommitReference> reference)
    else
      Error('e.InvalidCommitReference raw)

type CommitIdError<'e> =
  static abstract InvalidCommitId: raw: string -> 'e

module CommitIds =

  let inline value(id: string<CommitId>) = UMX.untag id

  let parse<'e when CommitIdError<'e>>
    (raw: string)
    : Result<string<CommitId>, 'e> =
    match Guid.TryParse(raw.Trim()) with
    | true, id -> Ok(UMX.tag<CommitId>(id.ToString "D"))
    | false, _ -> Error('e.InvalidCommitId raw)

type NonceError<'e> =
  static abstract MalformedNonce: raw: string -> 'e

module Nonces =

  let inline value(hex: string<Nonce>) = UMX.untag hex

  let parse<'e when NonceError<'e>>(raw: string) : Result<string<Nonce>, 'e> =
    if Regex.IsMatch(raw, "^[0-9a-f]{32}$") then
      Ok(UMX.tag<Nonce> raw)
    else
      Error('e.MalformedNonce raw)

type KeyError<'e> =
  static abstract LookupUnreachable: message: string -> 'e
  static abstract LookupRefused: status: int * body: string -> 'e
  static abstract UnexpectedKey: key: string -> 'e

module KeyIds =

  let inline value(key: string<KeyId>) = UMX.untag key

  /// Accepts the answer only when it names one of the candidates the caller offered.
  let fromLookup<'e when KeyError<'e>>
    (answer: string)
    (candidates: string list)
    : Result<string<KeyId>, 'e> =
    let offered = answer.Trim()

    if candidates |> List.contains offered then
      Ok(UMX.tag<KeyId> offered)
    else
      Error('e.UnexpectedKey offered)

/// What a successful POST /api/cli/commits reports: the commit url, and whether it waits for review.
type CommitPosted = private {
  Url: string
  PendingReview: bool
}

type PostingError<'e> =
  static abstract PostUnreachable: message: string -> 'e
  static abstract PostRefused: status: int * body: string -> 'e
  static abstract UnexpectedResponse: body: string -> 'e

module CommitPosted =

  let inline url posted = posted.Url

  let inline pendingReview posted = posted.PendingReview

  /// Reads the plain-text answer of POST /api/cli/commits.
  let fromResponse<'e when PostingError<'e>>
    (body: string)
    : Result<CommitPosted, 'e> =
    let text = body.Trim()

    if not(text.StartsWith "Committed: ") then
      Error('e.UnexpectedResponse body)
    else
      let rest = text.Substring("Committed: ".Length)
      let url = rest.Split('(').[0].Trim()

      if Uri.IsWellFormedUriString(url, UriKind.Absolute) then
        Ok {
          Url = url
          PendingReview = rest.Contains "pending review"
        }
      else
        Error('e.UnexpectedResponse body)

type ImageError<'e> =
  static abstract TooManyImages: count: int -> 'e
  static abstract ImageTooLarge: fileName: string * size: int -> 'e
  static abstract UnsupportedImage: fileName: string -> 'e

type ReportDetailsError<'e> =
  static abstract ReportDetailsTooLong: length: int -> 'e

/// ssh-keygen itself failed: the message is its stderr.
type SigningError<'e> =
  static abstract SigningFailed: message: string -> 'e
