module Tests.Program

open Expecto

[<EntryPoint>]
let main args =
  runTestsWithCLIArgs
    []
    args
    (testList "all" [
      Tests.Types.tests
      Tests.PkceTests.tests
      Tests.Payload.tests
      Tests.Api.tests
      Tests.Api.deviceTests
      Tests.Api.redeemDeviceTests
      Tests.Api.commitApiTests
      Tests.Api.richCommitTests
      Tests.Api.accountApiTests
      Tests.Api.listApiTests
      Tests.Api.actionApiTests
      Tests.Commit.tests
    ])
