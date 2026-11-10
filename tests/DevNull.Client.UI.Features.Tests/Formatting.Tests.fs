module DevNull.Client.UI.Tests.Formatting

// The sized avatar URL: only a GitHub host carries the size in its
// `s` query value, so only that host is rebuilt; any other address
// and any address that does not parse rides through unchanged.

open Expecto
open DevNull.Client.Formatting

[<Tests>]
let tests =
  testList "formatting sized avatar urls" [
    testCase "a github host without a query gains the size" (fun () ->
      Expect.equal
        "https://avatars.githubusercontent.com/u/1?s=1024"
        (sizedAvatarUrl("https://avatars.githubusercontent.com/u/1", 1024))
        "gained the size")

    testCase "a github host with a query keeps its values" (fun () ->
      Expect.equal
        "https://avatars.githubusercontent.com/u/1?v=4&s=1024"
        (sizedAvatarUrl("https://avatars.githubusercontent.com/u/1?v=4", 1024))
        "kept its values")

    testCase "a github host replaces its own size value" (fun () ->
      Expect.equal
        "https://avatars.githubusercontent.com/u/1?s=1024&a=1"
        (sizedAvatarUrl(
          "https://avatars.githubusercontent.com/u/1?s=80&a=1",
          1024
        ))
        "replaced the size")

    testCase "another host rides through unchanged" (fun () ->
      Expect.equal
        "https://example.test/u/1?v=4"
        (sizedAvatarUrl("https://example.test/u/1?v=4", 1024))
        "unchanged")

    testCase "an address that does not parse rides through unchanged" (fun () ->
      Expect.equal "not a url" (sizedAvatarUrl("not a url", 1024)) "unchanged")
  ]
