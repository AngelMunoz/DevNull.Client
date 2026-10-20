module Tests.Commit

open System
open System.Security.Cryptography
open Expecto
open FSharp.UMX
open DevNull.Client.Core
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Commit
open DevNull.Client.Core.Types
open Tests.TestSupport

let committedLine =
  "Committed: https://dev-null.sh/@someone/commits/01a15610-f3b7-73ff-a344-4c7775632a89"

let envWith signer http =
  createEnv {
    Clock = FixedClock(1760000000L)
    NonceSource = FixedNonceSource("00112233445566778899aabbccddeeff")
    Signer = signer
    Http = http
  }

let commit input env : Async<Result<CommitPosted, TestError>> =
  Commit.commit(env, input)

let input body replyTo = {
  Body = body
  ReplyTo = replyTo
  KeyFile = "unused"
  Images = []
}

let pngBytes = [|
  0x89uy
  0x50uy
  0x4Euy
  0x47uy
  0x0Duy
  0x0Auy
  0x1Auy
  0x0Auy
  0x00uy
|]

let image name bytes = { FileName = name; Bytes = bytes }

let sha256(bytes: byte array) =
  SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant()

let tests =
  testList "Commit.commit" [
    testAsync "signs the rendered payload and posts it" {
      let env =
        envWith
          (FixedSigner(Ok "c2lnbmF0dXJl"))
          (FakeHttp(ok "", ok committedLine))

      let! posted = commit (input "It was DNS." None) env

      match posted with
      | Ok posted ->
        Expect.equal
          "https://dev-null.sh/@someone/commits/01a15610-f3b7-73ff-a344-4c7775632a89"
          (CommitPosted.url posted)
          "url"

        Expect.isFalse (CommitPosted.pendingReview posted) "approved on arrival"
      | Error error -> failtestf "%A" error
    }

    testAsync "a pending review answer sets the flag" {
      let pendingLine =
        committedLine
        + " (pending review, only you can see it until it is approved)"

      let env =
        envWith
          (FixedSigner(Ok "c2lnbmF0dXJl"))
          (FakeHttp(ok "", ok pendingLine))

      let! posted = commit (input "It was DNS." None) env

      match posted with
      | Ok posted ->
        Expect.isTrue (CommitPosted.pendingReview posted) "review pending"
      | Error error -> failtestf "%A" error
    }

    testAsync "a signing failure surfaces the ssh-keygen message" {
      let env =
        envWith
          (FixedSigner(
            Error {
              Message = "load pubkey: invalid format"
            }
          ))
          (FakeHttp(ok "", ok committedLine))

      let! posted = commit (input "It was DNS." None) env

      match posted with
      | Error(SigningFailed message) ->
        Expect.equal "load pubkey: invalid format" message "stderr travels"
      | _ -> failtest "expected SigningFailed"
    }

    testAsync "a refused post surfaces status and body" {
      let env =
        envWith
          (FixedSigner(Ok "c2lnbmF0dXJl"))
          (FakeHttp(
            ok "",
            refused 429 "Slow down: 10 commits a minute. Even CI takes breaks."
          ))

      let! posted = commit (input "It was DNS." None) env

      match posted with
      | Error(PostRefused(status, body)) ->
        Expect.equal 429 status "status travels"

        Expect.equal
          "Slow down: 10 commits a minute. Even CI takes breaks."
          body
          "body travels"
      | _ -> failtest "expected PostRefused"
    }

    testAsync "a refused key lookup surfaces status and body" {
      let env =
        envWith
          (FixedSigner(Ok "c2lnbmF0dXJl"))
          (FakeHttp(
            refused
              404
              "This SSH key is not on the GitHub account of a /dev/null user.",
            ok committedLine
          ))

      let! (found: Result<string<KeyId>, TestError>) =
        Commit.findKey(env, "ssh-ed25519 AAAA")

      match found with
      | Error(LookupRefused(status, body)) ->
        Expect.equal 404 status "status travels"

        Expect.equal
          "This SSH key is not on the GitHub account of a /dev/null user."
          body
          "body travels"
      | _ -> failtest "expected LookupRefused"
    }

    testAsync "a transport failure during key lookup is not an HTTP refusal" {
      let env =
        envWith
          (FixedSigner(Ok "c2lnbmF0dXJl"))
          (FakeHttp(Error { Message = "connection refused" }, ok committedLine))

      let! (found: Result<string<KeyId>, TestError>) =
        Commit.findKey(env, "ssh-ed25519 AAAA")

      match found with
      | Error(LookupUnreachable "connection refused") -> ()
      | _ -> failtest "expected LookupUnreachable"
    }

    testAsync "a transport failure during post is not an HTTP refusal" {
      let env =
        envWith
          (FixedSigner(Ok "c2lnbmF0dXJl"))
          (FakeHttp(ok "", Error { Message = "connection refused" }))

      let! posted = commit (input "It was DNS." None) env

      match posted with
      | Error(PostUnreachable "connection refused") -> ()
      | _ -> failtest "expected PostUnreachable"
    }

    testAsync "an invalid reply reference fails before signing" {
      let env =
        envWith
          (FixedSigner(Error { Message = "must not sign" }))
          (FakeHttp(ok "", ok committedLine))

      let! posted = commit (input "It was DNS." (Some "not-a-hash")) env

      match posted with
      | Error(InvalidCommitReference raw) ->
        Expect.equal "not-a-hash" raw "raw input travels"
      | _ -> failtest "expected InvalidCommitReference"
    }

    testAsync "a reply link is rendered on the third line" {
      let link =
        "https://dev-null.sh/@someone/commits/019a2b3c-4d5e-7f60-8a9b-0c1d2e3f4a5b"

      let signer =
        signerWith(fun signed ->
          let lines = signed.Payload.Split('\n')
          Expect.equal link lines.[2] "reply line"
          Ok "signature")

      let env = envWith signer (FakeHttp(ok "", ok committedLine))

      let! posted = commit (input "Agreed!" (Some link)) env

      match posted with
      | Ok _ -> ()
      | Error error -> failtestf "%A" error
    }

    test "an image is validated and hashed" {
      let parsed: Result<Image list, TestError> =
        CommitImages.parse [ image "diagram.png" pngBytes ]

      match parsed with
      | Ok images ->
        let parsedImage = images |> List.exactlyOne
        Expect.equal "image/png" parsedImage.ContentType "content type"
        Expect.equal (sha256 pngBytes) parsedImage.Hash "hash"
      | Error error -> failtestf "%A" error
    }

    test "more than three images fail" {
      let images = List.replicate 4 (image "diagram.png" pngBytes)

      match CommitImages.parse images with
      | Error(TooManyImages 4) -> ()
      | _ -> failtest "expected TooManyImages"
    }

    test "an image over ten megabytes fails" {
      let bytes = Array.append pngBytes (Array.zeroCreate 10240001)

      match CommitImages.parse [ image "huge.png" bytes ] with
      | Error(ImageTooLarge("huge.png", size)) ->
        Expect.equal (bytes.Length) size "size travels"
      | _ -> failtest "expected ImageTooLarge"
    }

    test "an unsupported image fails" {
      match CommitImages.parse [ image "notes.txt" "hello"B ] with
      | Error(UnsupportedImage "notes.txt") -> ()
      | _ -> failtest "expected UnsupportedImage"
    }

    testAsync
      "an image commit signs the image payload and posts a multipart form" {
      let http = CapturingHttp(ok committedLine)

      let signer =
        signerWith(fun signed ->
          Expect.equal "devnull-commit-images" signed.Namespace "namespace"
          let lines = signed.Payload.Split('\n')
          Expect.equal 5 lines.Length "five payload lines"
          Expect.equal (sha256 pngBytes) lines.[3] "hashes"
          Expect.equal "Shipped!" lines.[4] "body"
          Ok "signature")

      let env = envWith signer http

      let request = {
        input "Shipped!" None with
            Images = [ image "diagram.png" pngBytes ]
      }

      let! posted = commit request env

      match posted with
      | Ok _ ->
        let sent = http.Requests |> List.exactlyOne
        let form = multipartForm sent

        Expect.equal
          [ "payload"; "images[]" ]
          (form.Files |> List.map _.Name)
          "part names"

        Expect.equal "image/png" (form.Files.[1].ContentType) "image type"
      | Error error -> failtestf "%A" error
    }
  ]
