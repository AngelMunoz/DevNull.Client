module Tests.Payload

open Expecto
open DevNull.Client.Core
open Tests.TestSupport

let tests =
  testList "Payload.render" [
    test "without a reply, the third line is empty" {
      let rendered =
        Payload.render {
          Timestamp = 1760000000L
          Nonce = Values.nonce "00112233445566778899aabbccddeeff"
          ReplyTo = None
          ImageHashes = []
          Body = Values.body "It was DNS."
        }

      Expect.equal
        "1760000000\n00112233445566778899aabbccddeeff\n\nIt was DNS."
        rendered
        "four LF-joined lines"
    }

    test "with a reply, the third line is the hash" {
      let rendered =
        Payload.render {
          Timestamp = 1760000000L
          Nonce = Values.nonce "00112233445566778899aabbccddeeff"
          ReplyTo = Some(Values.reference "0be0130")
          ImageHashes = []
          Body = Values.body "Agreed!"
        }

      Expect.equal
        "1760000000\n00112233445566778899aabbccddeeff\n0be0130\nAgreed!"
        rendered
        "four LF-joined lines"
    }

    test "a body with newlines stays on the last lines" {
      let rendered =
        Payload.render {
          Timestamp = 1760000000L
          Nonce = Values.nonce "00112233445566778899aabbccddeeff"
          ReplyTo = None
          ImageHashes = []
          Body = Values.body "first\nsecond"
        }

      Expect.equal
        "1760000000\n00112233445566778899aabbccddeeff\n\nfirst\nsecond"
        rendered
        "body keeps its own newlines"
    }

    test "with images, the fourth line is the comma-separated hashes" {
      let rendered =
        Payload.render {
          Timestamp = 1760000000L
          Nonce = Values.nonce "00112233445566778899aabbccddeeff"
          ReplyTo = None
          ImageHashes = [ "aaaa"; "bbbb" ]
          Body = Values.body "Shipped!"
        }

      Expect.equal
        "1760000000\n00112233445566778899aabbccddeeff\n\naaaa,bbbb\nShipped!"
        rendered
        "five LF-joined lines"
    }
  ]
