module Tests.Types

open Expecto
open FSharp.UMX
open DevNull.Client.Core
open DevNull.Client.Core.Types
open Tests.TestSupport

let tests =
  testList "parse" [
    test "an empty body fails with its length" {
      match CommitBodies.parse "" with
      | Error(BodyTooShort(raw, length)) ->
        Expect.equal raw "" "the raw input travels"
        Expect.equal length 0 "the length is exact"
      | _ -> failtest "expected BodyTooShort"
    }

    test "a one-character body is accepted" {
      let parsed: Result<string<CommitBody>, TestError> = CommitBodies.parse "x"

      match parsed with
      | Ok body -> Expect.equal "x" (CommitBodies.value body) "round trip"
      | Error _ -> failtest "expected Ok"
    }

    test "a body over 4000 characters fails" {
      let long = String.replicate 4001 "x"

      match CommitBodies.parse long with
      | Error(BodyTooLong(_, length)) ->
        Expect.equal length 4001 "the length is exact"
      | _ -> failtest "expected BodyTooLong"
    }

    test "a long link counts as 23 characters" {
      // 3970 characters of text plus a 60-character link: 4030 raw,
      // 3993 counted, so the server accepts what the old raw cap
      // refused.
      let padded =
        String.replicate 3970 "x"
        + " https://dev-null.sh/aaaaaaaaaaaaaaaaaaaaaaaaaa"

      let parsed: Result<string<CommitBody>, TestError> =
        CommitBodies.parse padded

      match parsed with
      | Ok _ -> ()
      | Error _ -> failtest "a body with one link was refused"
    }

    test "a run of blank lines collapses before the count" {
      // 3997 characters plus four newlines plus one more: 4002 raw,
      // 4000 after the collapse of the blank run.
      let padded = String.replicate 3997 "x" + "\n\n\n\n" + "y"

      let parsed: Result<string<CommitBody>, TestError> =
        CommitBodies.parse padded

      match parsed with
      | Ok body ->
        Expect.stringContains
          (CommitBodies.value body)
          "\n\n"
          "the collapse kept one blank"
      | Error _ -> failtest "a padded body was refused"
    }

    test "a body over the raw cap fails before any counting" {
      let long = String.replicate 40001 "x"

      match CommitBodies.parse long with
      | Error(BodyTooLong(_, length)) ->
        Expect.equal length 40001 "the length is exact"
      | _ -> failtest "expected BodyTooLong"
    }

    test "carriage returns normalize to single line feeds" {
      let parsed: Result<string<CommitBody>, TestError> =
        CommitBodies.parse "a\r\nb\rc"

      match parsed with
      | Ok body ->
        Expect.equal "a\nb\nc" (CommitBodies.value body) "the endings collapse"
      | Error _ -> failtest "expected Ok"
    }

    test "a valid body keeps its text" {
      let parsed: Result<string<CommitBody>, TestError> =
        CommitBodies.parse "It was DNS."

      match parsed with
      | Ok body ->
        Expect.equal "It was DNS." (CommitBodies.value body) "round trip"
      | Error _ -> failtest "expected Ok"
    }

    test "a hash with characters outside hex fails" {
      match ShortHashes.parse "0be013z" with
      | Error(InvalidShortHash raw) ->
        Expect.equal raw "0be013z" "the raw input travels"
      | _ -> failtest "expected InvalidShortHash"
    }

    test "a hash of 6 characters fails" {
      match ShortHashes.parse "0be013" with
      | Error(InvalidShortHash _) -> ()
      | _ -> failtest "expected InvalidShortHash"
    }

    test "an uppercase hash is accepted and lowercased" {
      let parsed: Result<string<ShortHash>, TestError> =
        ShortHashes.parse "0BE0130"

      match parsed with
      | Ok hash -> Expect.equal "0be0130" (ShortHashes.value hash) "normalized"
      | Error _ -> failtest "expected Ok"
    }

    test "a commit id is canonical lowercase" {
      let parsed: Result<string<CommitId>, TestError> =
        CommitIds.parse "019A2B3C-4D5E-7F60-8A9B-0C1D2E3F4A5B"

      match parsed with
      | Ok id ->
        Expect.equal
          "019a2b3c-4d5e-7f60-8a9b-0c1d2e3f4a5b"
          (CommitIds.value id)
          "normalized"
      | Error _ -> failtest "expected Ok"
    }

    test "a commit id that is not a UUID fails" {
      let parsed: Result<string<CommitId>, TestError> =
        CommitIds.parse "0be0130"

      match parsed with
      | Error(InvalidCommitId raw) ->
        Expect.equal "0be0130" raw "the raw input travels"
      | _ -> failtest "expected InvalidCommitId"
    }

    test "a nonce without exactly 32 hex characters fails" {
      match Nonces.parse "00112233445566778899aabbccddeef" with
      | Error(MalformedNonce _) -> ()
      | _ -> failtest "expected MalformedNonce"
    }

    test "a reply reference accepts a short hash" {
      let parsed: Result<string<CommitReference>, TestError> =
        CommitReferences.parse "0BE0130"

      match parsed with
      | Ok reference ->
        Expect.equal "0BE0130" (CommitReferences.value reference) "kept"
      | Error _ -> failtest "expected Ok"
    }

    test "a reply reference accepts a commit id" {
      let parsed: Result<string<CommitReference>, TestError> =
        CommitReferences.parse "019a2b3c-4d5e-7f60-8a9b-0c1d2e3f4a5b"

      match parsed with
      | Ok _ -> ()
      | Error _ -> failtest "expected Ok"
    }

    test "a reply reference accepts a link that contains a commit id" {
      let parsed: Result<string<CommitReference>, TestError> =
        CommitReferences.parse
          "https://dev-null.sh/@someone/commits/019a2b3c-4d5e-7f60-8a9b-0c1d2e3f4a5b"

      match parsed with
      | Ok _ -> ()
      | Error _ -> failtest "expected Ok"
    }

    test "a reply reference that is not a hash, id, or link fails" {
      match CommitReferences.parse "not-a-reference" with
      | Error(InvalidCommitReference raw) ->
        Expect.equal "not-a-reference" raw "raw input travels"
      | _ -> failtest "expected InvalidCommitReference"
    }

    test "a lookup answer that was not offered fails" {
      match KeyIds.fromLookup("ssh-ed25519 AAAA", [| "ssh-ed25519 BBBB" |]) with
      | Error(UnexpectedKey key) ->
        Expect.equal key "ssh-ed25519 AAAA" "the offered key travels"
      | _ -> failtest "expected UnexpectedKey"
    }

    test "a lookup answer that was offered becomes a key id" {
      let parsed: Result<string<KeyId>, TestError> =
        KeyIds.fromLookup(
          "ssh-ed25519 BBBB\n",
          [| "ssh-ed25519 AAAA"; "ssh-ed25519 BBBB" |]
        )

      match parsed with
      | Ok keyId ->
        Expect.equal "ssh-ed25519 BBBB" (KeyIds.value keyId) "matched"
      | Error _ -> failtest "expected Ok"
    }

    test "an answer that is not a commit line fails" {
      match CommitPosted.fromResponse "Slow down: 10 commits a minute." with
      | Error(UnexpectedResponse body) ->
        Expect.equal body "Slow down: 10 commits a minute." "the body travels"
      | _ -> failtest "expected UnexpectedResponse"
    }

    test "an approved commit answer reports the url" {
      let answer =
        "Committed: https://dev-null.sh/@someone/commits/01a15610-f3b7-73ff-a344-4c7775632a89\n"

      let parsed: Result<CommitPosted, TestError> =
        CommitPosted.fromResponse answer

      match parsed with
      | Ok posted ->
        Expect.equal
          "https://dev-null.sh/@someone/commits/01a15610-f3b7-73ff-a344-4c7775632a89"
          (CommitPosted.url posted)
          "url"

        Expect.isFalse (CommitPosted.pendingReview posted) "no review pending"
      | Error _ -> failtest "expected Ok"
    }

    test "a pending review answer sets the flag" {
      let answer =
        "Committed: https://dev-null.sh/@someone/commits/01a15610-f3b7-73ff-a344-4c7775632a89 (pending review, only you can see it until it is approved)"

      let parsed: Result<CommitPosted, TestError> =
        CommitPosted.fromResponse answer

      match parsed with
      | Ok posted ->
        Expect.isTrue (CommitPosted.pendingReview posted) "review pending"
      | Error _ -> failtest "expected Ok"
    }
  ]
