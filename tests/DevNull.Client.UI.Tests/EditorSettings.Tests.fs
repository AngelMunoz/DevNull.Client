module DevNull.Client.UI.Tests.EditorSettings

open Expecto
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Editor

let tests =
  testList "editor settings" [
    testCase "an unknown stored string answers Following" (fun () ->
      Expect.equal
        (EditorSettings.parse "archive" |> StoredState.selectedRef)
        Following
        "the bad string took the default")

    testCase "a null stored string answers Following" (fun () ->
      Expect.equal
        (EditorSettings.parse null |> StoredState.selectedRef)
        Following
        "the null string took the default")

    testCase "a blank stored string answers Following" (fun () ->
      Expect.equal
        (EditorSettings.parse "" |> StoredState.selectedRef)
        Following
        "the blank string took the default")

    testCase "a fresh install reads Following" (fun () ->
      Expect.equal
        (EditorSettings.initial() |> StoredState.selectedRef)
        Following
        "the fresh state took another ref")

    testCase "each ref round trips through the one string" (fun () ->
      let refs = [ Following; Latest; Trending ]

      let read =
        refs |> List.map(fun log -> EditorSettings.parse(LogKinds.wire log))

      Expect.equal
        (read |> List.map StoredState.selectedRef)
        refs
        "a ref changed on the way in"

      Expect.equal
        (read |> List.map EditorSettings.store)
        (refs |> List.map LogKinds.wire)
        "a ref changed on the way out")
  ]
