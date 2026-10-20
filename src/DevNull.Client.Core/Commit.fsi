module DevNull.Client.Core.Commit

open FSharp.UMX
open DevNull.Client.Core
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types

/// One image the caller read from disk.
type ImageAttachment = { FileName: string; Bytes: byte array }

/// A validated image, with the content type and SHA-256 the signed payload needs.
type Image = {
  FileName: string
  ContentType: string
  Bytes: byte array
  Hash: string
}

/// Raw input of one commit, parsed at the boundary of `commit`.
type CommitInput = {
  Body: string
  ReplyTo: string option
  KeyFile: string
  Images: ImageAttachment list
}

/// The values one signature needs.
type SignInput = {
  KeyFile: string
  Namespace: string
  Payload: string
}

/// The values one POST /api/cli/commits needs.
type PostInput = {
  Payload: string
  Signature: string
  Images: Image list
}

module CommitImages =

  /// Checks the image count, size, and format, and computes each SHA-256.
  val parse:
    images: ImageAttachment list -> Result<Image list, 'e> when ImageError<'e>

module Commit =

  /// <summary>
  /// Finds, among the offered "type base64" lines, the key that belongs to a registered devnull user.
  /// </summary>
  val findKey:
    env: 'env * candidates: string -> Async<Result<string<KeyId>, 'e>>
      when 'env :> IProvideHttp and KeyError<'e>

  /// <summary>
  /// Signs the payload with the given key file, in the given namespace, armor stripped.
  /// </summary>
  val sign:
    env: 'env * input: SignInput -> Async<Result<string, 'e>>
      when 'env :> IProvideSigner and SigningError<'e>

  /// <summary>
  /// Posts the signed payload and reads the plain-text answer. Images use the multipart form.
  /// </summary>
  val post:
    env: 'env * input: PostInput -> Async<Result<CommitPosted, 'e>>
      when 'env :> IProvideHttp and PostingError<'e>

  /// <summary>
  /// Parses, signs and posts one commit: the boundary of the whole protocol. The clock, the
  /// nonce source, the signer and the http client come from the env.
  /// </summary>
  val commit:
    env: 'env * input: CommitInput -> Async<Result<CommitPosted, 'e>>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and CommitBodyError<'e>
      and CommitReferenceError<'e>
      and ImageError<'e>
      and NonceError<'e>
      and SigningError<'e>
      and PostingError<'e>
