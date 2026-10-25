module DevNull.Client.Core.Commit

open System
open System.Security.Cryptography
open System.Text
open FSharp.UMX
open DevNull.Client.Core
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types
open FsToolkit.ErrorHandling
open IcedTasks

type ImageAttachment = { FileName: string; Bytes: byte array }

type Image = {
  FileName: string
  ContentType: string
  Bytes: byte array
  Hash: string
}

/// A staged voice note rides the same multipart shape an image does.
type Voice = Image

type Waveform = private { Bars: int array }

type CommitInput = {
  Body: string
  ReplyTo: string option
  KeyFile: string
  Images: ImageAttachment list
}

type SignInput = {
  KeyFile: string
  Namespace: string
  Payload: string
}

type PostInput = {
  Payload: string
  Signature: string
  Images: Image list
}

module CommitImages =

  [<Literal>]
  let MaxImages = 3

  [<Literal>]
  let MaxBytes = 10240000

  let hasPrefixAt(offset: int, prefix: byte array, bytes: byte array) =
    bytes.Length >= offset + prefix.Length
    && (prefix
        |> Array.indexed
        |> Array.forall(fun (index, value) -> bytes.[offset + index] = value))

  let hasPrefix(prefix: byte array, bytes: byte array) =
    hasPrefixAt(0, prefix, bytes)

  let ascii(text: string) = Encoding.ASCII.GetBytes text

  let detect(bytes: byte array) =
    if
      hasPrefix(
        [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |],
        bytes
      )
    then
      Some "image/png"
    elif hasPrefix([| 0xFFuy; 0xD8uy; 0xFFuy |], bytes) then
      Some "image/jpeg"
    elif
      bytes.Length >= 12
      && hasPrefix(ascii "RIFF", bytes)
      && hasPrefixAt(8, ascii "WEBP", bytes)
    then
      Some "image/webp"
    else
      None

  let sha256(bytes: byte array) =
    SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant()

  /// Checks the image count, size, and format, and computes each SHA-256.
  let parse<'e when ImageError<'e>>
    (images: ImageAttachment list)
    : Result<Image list, 'e> =
    if List.length images > MaxImages then
      Error('e.TooManyImages(List.length images))
    else
      images
      |> List.traverseResultM(fun image ->
        if image.Bytes.Length > MaxBytes then
          Error('e.ImageTooLarge(image.FileName, image.Bytes.Length))
        else
          match detect image.Bytes with
          | Some contentType ->
            Ok {
              FileName = image.FileName
              ContentType = contentType
              Bytes = image.Bytes
              Hash = sha256 image.Bytes
            }
          | None -> Error('e.UnsupportedImage image.FileName))

module Voices =

  /// How many loudness bars one voice note carries.
  let Bars = 48

  /// The upload cap, as the server bounds it, in bytes.
  let MaxBytes = 1950 * 1024

  let asciiAt(offset: int, text: string, bytes: byte array) =
    bytes.Length >= offset + text.Length
    && text
       |> Seq.indexed
       |> Seq.forall(fun (index, value) -> bytes.[offset + index] = byte value)

  /// Reads the bars a recording produced. Accepts exactly 48 values,
  /// each between 0 and 100.
  let parseWaveform<'e when VoiceError<'e>>
    (values: int list)
    : Result<Waveform, 'e> =
    match values |> List.tryFind(fun bar -> bar < 0 || bar > 100) with
    | Some bar -> Error('e.WaveformBarOutOfRange bar)
    | None when List.length values <> Bars ->
      Error('e.WrongWaveformCount(List.length values))
    | None -> Ok { Bars = Array.ofList values }

  let bars(waveform: Waveform) : int list = List.ofArray waveform.Bars

  let isWebM(bytes: byte array) =
    bytes.Length >= 4
    && bytes.[0] = 0x1Auy
    && bytes.[1] = 0x45uy
    && bytes.[2] = 0xDFuy
    && bytes.[3] = 0xA3uy

  let isWav(bytes: byte array) =
    asciiAt(0, "RIFF", bytes) && asciiAt(8, "WAVE", bytes)

  /// Checks one voice note's size and format, and computes the SHA-256
  /// the signed payload needs. The recording's length and encoding stay
  /// the server's to judge.
  let parse<'e when VoiceError<'e>>
    (fileName: string, bytes: byte array)
    : Result<Voice, 'e> =
    if bytes.Length > MaxBytes then
      Error('e.VoiceTooLarge(fileName, bytes.Length))
    else
      let contentType =
        if isWebM bytes then "audio/webm"
        elif isWav bytes then "audio/wav"
        else ""

      match contentType with
      | "" -> Error('e.UnsupportedVoice fileName)
      | contentType ->
        Ok {
          FileName = fileName
          ContentType = contentType
          Bytes = bytes
          Hash = CommitImages.sha256 bytes
        }

  /// The server takes a voice note alone, or with a reply: never beside
  /// a body or images.
  let check<'e when VoiceError<'e>>
    (voice: Voice option, hasBody: bool, hasImages: bool)
    : Result<unit, 'e> =
    match voice, hasBody, hasImages with
    | None, _, _ -> Ok()
    | Some _, true, _ -> Error('e.VoiceProhibited VoiceWithBody)
    | Some _, false, true -> Error('e.VoiceProhibited VoiceWithImages)
    | Some _, false, false -> Ok()

module Commit =

  [<Literal>]
  let CommitNamespaceName = "devnull-commit"

  [<Literal>]
  let CommitImagesNamespaceName = "devnull-commit-images"

  let findKey<'env, 'e when 'env :> IProvideHttp and KeyError<'e>>
    (env: 'env, candidates: string)
    : Async<Result<string<KeyId>, 'e>> =

    asyncEx {
      let offered =
        candidates.Split('\n')
        |> Array.map _.Trim()
        |> Array.filter(fun line -> line <> "")

      let request = {
        Method = "POST"
        Target = "/api/cli/key-lookups"
        Headers = [
          "User-Agent", userAgent
          "Content-Type", "text/plain; charset=utf-8"
        ]
        Body = Text candidates
      }

      let! outcome = env.Http.Send(request)

      return
        match outcome with
        | Error failure -> Error('e.LookupUnreachable failure.Message)
        | Ok response when isSuccess response.Status ->
          KeyIds.fromLookup(response.Body, offered)
        | Ok response -> Error('e.LookupRefused(response.Status, response.Body))
    }

  let sign<'env, 'e when 'env :> IProvideSigner and SigningError<'e>>
    (env: 'env, input: SignInput)
    : Async<Result<string, 'e>> = asyncEx {
    let! outcome =
      env.Signer.Sign {
        KeyFile = input.KeyFile
        Namespace = input.Namespace
        Payload = input.Payload
      }

    return
      outcome
      |> Result.mapError(fun failure -> 'e.SigningFailed failure.Message)
  }

  let post<'env, 'e when 'env :> IProvideHttp and PostingError<'e>>
    (env: 'env, input: PostInput)
    : Async<Result<CommitPosted, 'e>> =
    let request =
      match input.Images with
      | [] -> {
          Method = "POST"
          Target = "/api/cli/commits"
          Headers = [
            "User-Agent", userAgent
            "X-SSH-Signature", input.Signature
            "Content-Type", "text/plain; charset=utf-8"
          ]
          Body = Text input.Payload
        }
      | images ->
        let payload = {
          Name = "payload"
          FileName = "commit"
          ContentType = "text/plain; charset=utf-8"
          Content = Encoding.UTF8.GetBytes input.Payload
        }

        let imageFiles =
          images
          |> List.map(fun image -> {
            Name = "images[]"
            FileName = image.FileName
            ContentType = image.ContentType
            Content = image.Bytes
          })

        {
          Method = "POST"
          Target = "/api/cli/commits"
          Headers = [
            "User-Agent", userAgent
            "X-SSH-Signature", input.Signature
          ]
          Body =
            Multipart {
              Fields = []
              Files = payload :: imageFiles
            }
        }

    asyncEx {
      let! outcome = env.Http.Send(request)

      return
        match outcome with
        | Error failure -> Error('e.PostUnreachable failure.Message)
        | Ok response when isSuccess response.Status ->
          CommitPosted.fromResponse response.Body
        | Ok response -> Error('e.PostRefused(response.Status, response.Body))
    }

  let commit(env, input: CommitInput) = asyncResult {
    let! body = CommitBodies.parse input.Body
    let! replyTo = input.ReplyTo |> Option.traverseResult CommitReferences.parse
    let! images = CommitImages.parse input.Images
    let! nonce = Nonces.parse(NonceSource.newHex env)

    let payload =
      Payload.render {
        Timestamp = Clock.unixNow env
        Nonce = nonce
        ReplyTo = replyTo
        ImageHashes = images |> List.map _.Hash
        Body = body
      }

    let signingNamespace =
      match images with
      | [] -> CommitNamespaceName
      | _ -> CommitImagesNamespaceName

    let! signature =
      sign(
        env,
        {
          KeyFile = input.KeyFile
          Namespace = signingNamespace
          Payload = payload
        }
      )

    return!
      post(
        env,
        {
          Payload = payload
          Signature = signature
          Images = images
        }
      )
  }
