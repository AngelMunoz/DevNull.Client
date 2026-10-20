module DevNull.Client.Core.Payload

open FSharp.UMX
open DevNull.Client.Core
open DevNull.Client.Core.Types

/// The values a signed commit payload needs, together.
[<Struct>]
type CommitPayload = {
  Timestamp: int64
  Nonce: string<Nonce>
  ReplyTo: string<CommitReference> option
  ImageHashes: string list
  Body: string<CommitBody>
}

/// Renders the payload: timestamp, nonce, reply reference or an empty line, the image hashes
/// when there are images, and the body, joined by LF, no trailing newline.
val render: payload: CommitPayload -> string
