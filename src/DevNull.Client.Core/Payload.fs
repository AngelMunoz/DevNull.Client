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

let render(payload: CommitPayload) =
  let replyTo =
    payload.ReplyTo
    |> Option.map CommitReferences.value
    |> Option.defaultValue ""

  let body = CommitBodies.value payload.Body

  match payload.ImageHashes with
  | [] ->
    $"""{payload.Timestamp}
{Nonces.value payload.Nonce}
{replyTo}
{body}"""
  | hashes ->
    $"""{payload.Timestamp}
{Nonces.value payload.Nonce}
{replyTo}
{String.concat "," hashes}
{body}"""
