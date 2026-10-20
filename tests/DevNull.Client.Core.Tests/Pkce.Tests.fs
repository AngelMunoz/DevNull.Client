module Tests.PkceTests

open Expecto

let tests =
  testList "Pkce.challenge" [
    test "matches the S256 challenge of the server test verifier" {
      Expect.equal
        "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"
        (DevNull.Client.Core.Pkce.challenge
          "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")
        "challenge"
    }
  ]
