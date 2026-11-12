module DevNull.Client.UI.Tests.WorkbenchSettingsTests

open Expecto

open DevNull.Client.Core.ApiTypes
open DevNull.Client.Layouts.Workbench

let private commitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

let private stateOf(raw: string) = WorkbenchSettings.parse raw

let private refOf(raw: string) = stateOf raw |> WorkbenchState.refOf

let tests =
  testList "workbench settings" [
    testCase "the default state is Following with no checkout"
    <| fun () ->
      Expect.equal (stateOf "following") WorkbenchSettings.initial "following"

      Expect.equal
        (WorkbenchState.checkoutId WorkbenchSettings.initial)
        None
        "the default holds no checkout"

      Expect.equal
        (WorkbenchSettings.store WorkbenchSettings.initial)
        "following"
        "the stored default"

    testCase "a bad stored string loads the default state whole"
    <| fun () ->
      let bad = [
        ""
        "   "
        "nope"
        "repository:"
        "repository:two words"
        "following|"
        "following|not-a-guid"
        "following|" + commitId + "|extra"
        "following|" + commitId + "|"
      ]

      for raw in bad do
        Expect.equal
          (stateOf raw)
          WorkbenchSettings.initial
          $"the bad string {raw}"

    testCase "every ref round-trips through the stored string"
    <| fun () ->
      let tokens = [
        "latest"
        "following"
        "trending"
        "saved"
        "scheduled"
        "notifications"
        "repository:ana"
      ]

      for token in tokens do
        let state = stateOf token

        Expect.equal
          (WorkbenchSettings.store state)
          token
          $"the stored token {token}"

        Expect.equal
          (stateOf(WorkbenchSettings.store state))
          state
          $"the round trip {token}"

    testCase "a checkout id survives as its normalized id"
    <| fun () ->
      let state = stateOf("trending|" + commitId.ToUpperInvariant())

      Expect.equal (Ref.log(refOf "trending")) (Some Trending) "the log"

      Expect.equal
        (WorkbenchState.checkoutId state)
        (Some commitId)
        "the checkout id"

      Expect.equal
        (WorkbenchSettings.store state)
        ("trending|" + commitId)
        "the stored checkout"

    testCase "a repository ref keeps its parsed handle"
    <| fun () ->
      let state = stateOf("repository:@ana|" + commitId)

      Expect.equal (Ref.label(refOf "repository:ana")) "@ana" "the label"

      Expect.equal
        (WorkbenchSettings.store state)
        ("repository:ana|" + commitId)
        "the stored handle"

    testCase "a stored ref is trimmed before it is read"
    <| fun () ->
      Expect.equal
        (WorkbenchSettings.store(stateOf "  saved  "))
        "saved"
        "the trimmed ref"
  ]
