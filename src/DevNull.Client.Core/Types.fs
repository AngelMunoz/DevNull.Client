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

  /// The raw cap before any counting, as the server bounds the input.
  let maxRawLength = 40000

  /// The effective cap after normalization, as the server counts it.
  let maxLength = 4000

  /// The longest link that still counts, as the server takes it.
  let maxLinkLength = 2048

  /// How many characters one prose link counts as.
  let linkCountsAs = 23

  // The server collapses runs of blank lines in prose only; this leaf
  // collapses the whole body, so a code-heavy body counts a little
  // lower here than there. The server stays the authority, and a
  // false accept fails loudly at the post, not silently here.
  let normalize(body: string) =
    let endings = body.Replace("\r\n", "\n").Replace("\r", "\n")

    Regex.Replace(endings, "(?:[ \t]*\n){3,}", "\n\n")

  let proseLink = Regex(@"https?://[^\s<>""]+", RegexOptions.IgnoreCase)

  /// The server's count: every prose link counts as 23 characters.
  let characterCount(body: string) =
    let links =
      proseLink.Matches body
      |> Seq.sumBy(fun matched ->
        let length = matched.Value.Length

        if length <= maxLinkLength then
          max 0 (length - linkCountsAs)
        else
          0)

    body.Length - links

  let parse<'e when CommitBodyError<'e>>
    (raw: string)
    : Result<string<CommitBody>, 'e> =
    let length = raw.Length

    if length < 1 then
      Error('e.BodyTooShort(raw, length))
    elif length > maxRawLength then
      Error('e.BodyTooLong(raw, length))
    else
      let normalized = normalize raw
      let counted = characterCount normalized

      if counted > maxLength then
        Error('e.BodyTooLong(raw, length))
      else
        Ok(UMX.tag<CommitBody> normalized)

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
    (answer: string, candidates: string array)
    : Result<string<KeyId>, 'e> =
    let offered = answer.Trim()

    if candidates |> Array.contains offered then
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

  // Both accessors stay plain: the Release optimizer cannot expand them
  // inside closures (FS1118 in the composer and the Expecto CE).
  let url posted = posted.Url

  let pendingReview posted = posted.PendingReview

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

type MessageBodyError<'e> =
  static abstract MessageBodyTooLong: raw: string * length: int -> 'e

module MessageBodies =

  let inline value(body: string<MessageBody>) = UMX.untag body

  /// The cap, as the server counts it: the raw length, no link rules.
  let maxLength = 2000

  /// Reads one message body. Unlike a commit body, any length from zero
  /// up is content; the images or a voice note beside it may replace it.
  let parse<'e when MessageBodyError<'e>>
    (raw: string)
    : Result<string<MessageBody>, 'e> =
    if raw.Length > maxLength then
      Error('e.MessageBodyTooLong(raw, raw.Length))
    else
      Ok(UMX.tag<MessageBody> raw)

module ClientIds =

  let inline value(id: string<ClientId>) = UMX.untag id

  /// A fresh client id: a uuid, so a retried push answers with the
  /// message it already created.
  let fresh() : string<ClientId> =
    UMX.tag<ClientId>(Guid.NewGuid().ToString "D")

/// What a voice note may not ride with, as the server refuses it.
type VoiceConflict =
  | VoiceWithBody
  | VoiceWithImages

type VoiceError<'e> =
  static abstract VoiceTooLarge: fileName: string * size: int -> 'e
  static abstract UnsupportedVoice: fileName: string -> 'e
  static abstract WrongWaveformCount: count: int -> 'e
  static abstract WaveformBarOutOfRange: bar: int -> 'e
  static abstract VoiceProhibited: conflict: VoiceConflict -> 'e
