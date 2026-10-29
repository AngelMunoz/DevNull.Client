module DevNull.Client.UI.Tests.Natives

// The media sniff the heads route by: the site's voice notes are only
// ever WAV or WebM, so the answer is binary — a WAV carries its
// samples, everything else rides the platform's own player.

open Expecto
open DevNull.Client.Natives
open DevNull.Client.UI.Tests.Fixtures

let tests =
  testList "natives" [
    test "a web note sniffs as WebM and carries no samples" {
      let webm: byte[] = [| 0x1Auy; 0x45uy; 0xDFuy; 0xA3uy; 0x00uy |]

      Expect.equal "audio/webm" (VoiceMedia.contentType webm) "content type"

      Expect.isNone (VoiceMedia.samples webm) "samples"
    }

    test "a recorded note sniffs as WAV and carries its samples" {
      Expect.equal "audio/wav" (VoiceMedia.contentType cannedWav) "content type"

      Expect.isSome (VoiceMedia.samples cannedWav) "samples"
    }

    test "anything that is not a WAV rides the platform player" {
      let bytes: byte[] = [| 0x00uy; 0x01uy; 0x02uy; 0x03uy |]

      Expect.equal "audio/webm" (VoiceMedia.contentType bytes) "content type"

      Expect.isNone (VoiceMedia.samples bytes) "samples"
    }
  ]
