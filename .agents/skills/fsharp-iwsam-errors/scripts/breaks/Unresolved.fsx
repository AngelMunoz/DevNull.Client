#load "../Domain.fsx"
#nowarn "3535"

open Domain
open Domain.Fixtures

match checkout onHand goodCard (basket "B" 2 "SW1A 1AA" "ann@example.com") with
| Ok() -> printfn "ok"
| Error _ -> printfn "failed"
