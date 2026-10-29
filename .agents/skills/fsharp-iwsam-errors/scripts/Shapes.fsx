#load "Domain.fsx"
#nowarn "3535"

open FsToolkit.ErrorHandling
open Domain
open Domain.Fixtures

module Flat =
    type FlatError =
        | InvalidSku of raw: string
        | UnparsedPostcode of raw: string
        | UnparsedEmail of raw: string
        | OutOfStock of Sku
        | Discontinued of Sku
        | Declined of amount: decimal
        | CardExpired
        | NoCarrier of Postcode
        | EmailBounced of EmailAddress

        interface InputError<FlatError> with
            static member InvalidSku raw = InvalidSku raw
            static member UnparsedPostcode raw = UnparsedPostcode raw
            static member UnparsedEmail raw = UnparsedEmail raw

        interface CheckoutError<FlatError> with
            static member OutOfStock sku = OutOfStock sku
            static member Discontinued sku = Discontinued sku
            static member Declined amount = Declined amount
            static member CardExpired = CardExpired
            static member NoCarrier postcode = NoCarrier postcode
            static member EmailBounced address = EmailBounced address

    let run () =
        runAll "Flat: one case per leaf" (fun (r: Result<unit, FlatError>) -> sprintf "%A" r)

module Nested =
    type InputFailure =
        | InvalidSku of raw: string
        | UnparsedPostcode of raw: string
        | UnparsedEmail of raw: string

    type StockFailure =
        | OutOfStock of Sku
        | Discontinued of Sku

    type PaymentFailure =
        | Declined of amount: decimal
        | CardExpired

    type NestedError =
        | Input of InputFailure
        | Stock of StockFailure
        | Payment of PaymentFailure
        | NoCarrier of Postcode
        | EmailBounced of EmailAddress

        interface InputError<NestedError> with
            static member InvalidSku raw = Input(InvalidSku raw)
            static member UnparsedPostcode raw = Input(UnparsedPostcode raw)
            static member UnparsedEmail raw = Input(UnparsedEmail raw)

        interface CheckoutError<NestedError> with
            static member OutOfStock sku = Stock(OutOfStock sku)
            static member Discontinued sku = Stock(Discontinued sku)
            static member Declined amount = Payment(Declined amount)
            static member CardExpired = Payment CardExpired
            static member NoCarrier postcode = NoCarrier postcode
            static member EmailBounced address = EmailBounced address

    let run () =
        runAll "Nested: one branch per interface" (fun (r: Result<unit, NestedError>) -> sprintf "%A" r)

module Collapsed =
    type FailedStage =
        | Parsing
        | Stock
        | Payment
        | Shipping
        | Notification

        interface InputError<FailedStage> with
            static member InvalidSku _ = Parsing
            static member UnparsedPostcode _ = Parsing
            static member UnparsedEmail _ = Parsing

        interface CheckoutError<FailedStage> with
            static member OutOfStock _ = Stock
            static member Discontinued _ = Stock
            static member Declined _ = Payment
            static member CardExpired = Payment
            static member NoCarrier _ = Shipping
            static member EmailBounced _ = Notification

    let run () =
        runAll "Collapsed: only the failed stage survives" (fun (r: Result<unit, FailedStage>) -> sprintf "%A" r)

module Policy =
    type RetryReason =
        | Restock of Sku
        | AwaitCarrier of Postcode
        | ResendEmail of EmailAddress

    type RejectReason =
        | InvalidSku of raw: string
        | UnparsedPostcode of raw: string
        | UnparsedEmail of raw: string
        | Discontinued of Sku
        | Declined of amount: decimal
        | CardExpired

    type Policy =
        | Retry of RetryReason
        | Reject of RejectReason

        interface InputError<Policy> with
            static member InvalidSku raw = Reject(InvalidSku raw)
            static member UnparsedPostcode raw = Reject(UnparsedPostcode raw)
            static member UnparsedEmail raw = Reject(UnparsedEmail raw)

        interface CheckoutError<Policy> with
            static member OutOfStock sku = Retry(Restock sku)
            static member Discontinued sku = Reject(Discontinued sku)
            static member Declined amount = Reject(Declined amount)
            static member CardExpired = Reject CardExpired
            static member NoCarrier postcode = Retry(AwaitCarrier postcode)
            static member EmailBounced address = Retry(ResendEmail address)

    let run () =
        runAll "Policy: regrouped by what the caller does next" (fun (r: Result<unit, Policy>) -> sprintf "%A" r)

module Narrow =
    type PaymentOnly =
        | Declined of amount: decimal
        | CardExpired

        interface PaymentError<PaymentOnly> with
            static member Declined amount = Declined amount
            static member CardExpired = CardExpired

    let run () =
        printfn "== Narrow: a caller of chargeCard alone needs only two cases =="
        let outcome: Result<unit, PaymentOnly> = chargeCard expiredCard 20m
        printfn "%A" outcome
        printfn ""

module Record =
    type HttpStatus =
        | BadRequest
        | PaymentRequired
        | Conflict
        | Gone
        | BadGateway
        | ServiceUnavailable

    type Problem =
        { Status: HttpStatus
          Cause: Flat.FlatError }

        interface InputError<Problem> with
            static member InvalidSku raw = { Status = BadRequest; Cause = Flat.InvalidSku raw }
            static member UnparsedPostcode raw = { Status = BadRequest; Cause = Flat.UnparsedPostcode raw }
            static member UnparsedEmail raw = { Status = BadRequest; Cause = Flat.UnparsedEmail raw }

        interface CheckoutError<Problem> with
            static member OutOfStock sku = { Status = Conflict; Cause = Flat.OutOfStock sku }
            static member Discontinued sku = { Status = Gone; Cause = Flat.Discontinued sku }
            static member Declined amount = { Status = PaymentRequired; Cause = Flat.Declined amount }
            static member CardExpired = { Status = PaymentRequired; Cause = Flat.CardExpired }
            static member NoCarrier postcode = { Status = ServiceUnavailable; Cause = Flat.NoCarrier postcode }
            static member EmailBounced address = { Status = BadGateway; Cause = Flat.EmailBounced address }

    let run () =
        runAll "Record: a typed HTTP status beside the leaf" (fun (r: Result<unit, Problem>) ->
            match r with
            | Ok() -> "Ok"
            | Error p -> sprintf "%A %A" p.Status p.Cause)

module Exception =
    type CheckoutException(cause: Flat.FlatError) =
        inherit System.Exception(sprintf "%A" cause)
        member _.Cause = cause

        interface InputError<CheckoutException> with
            static member InvalidSku raw = CheckoutException(Flat.InvalidSku raw)
            static member UnparsedPostcode raw = CheckoutException(Flat.UnparsedPostcode raw)
            static member UnparsedEmail raw = CheckoutException(Flat.UnparsedEmail raw)

        interface CheckoutError<CheckoutException> with
            static member OutOfStock sku = CheckoutException(Flat.OutOfStock sku)
            static member Discontinued sku = CheckoutException(Flat.Discontinued sku)
            static member Declined amount = CheckoutException(Flat.Declined amount)
            static member CardExpired = CheckoutException Flat.CardExpired
            static member NoCarrier postcode = CheckoutException(Flat.NoCarrier postcode)
            static member EmailBounced address = CheckoutException(Flat.EmailBounced address)

    let run () =
        runAll "Exception: a class edge for throwing or C# interop" (fun (r: Result<unit, CheckoutException>) ->
            match r with
            | Ok() -> "Ok"
            | Error e -> sprintf "%s %A" (e.GetType().Name) e.Cause)

module SameTreeTwoEdges =
    let run () =
        printfn "== Same checkout call, two edge types chosen by the caller =="
        let raw = basket "B" 2 "SW1A 1AA" "ann@example.com"
        let flat: Result<unit, Flat.FlatError> = checkout onHand goodCard raw
        let policy: Result<unit, Policy.Policy> = checkout onHand goodCard raw
        printfn "flat   -> %A" flat
        printfn "policy -> %A" policy
        printfn ""

module PerLeafNotPerPath =
    type ShippingFailure =
        | NoCarrier of Postcode

        interface ShippingError<ShippingFailure> with
            static member NoCarrier postcode = NoCarrier postcode

    let shipBothLegs (first: Address) (second: Address) = result {
        do! scheduleShipment first
        do! scheduleShipment second
    }

    type Leg =
        | FirstLeg of ShippingFailure
        | SecondLeg of ShippingFailure

    let shipBothLegsTagged (first: Address) (second: Address) = result {
        do! scheduleShipment first |> Result.mapError FirstLeg
        do! scheduleShipment second |> Result.mapError SecondLeg
    }

    let run () =
        printfn "== Limit: the edge sees the leaf, not the call site =="
        let untagged: Result<unit, ShippingFailure> = shipBothLegs remoteAddress remoteAddress
        printfn "untagged -> %A (which leg?)" untagged
        printfn "tagged   -> %A" (shipBothLegsTagged remoteAddress remoteAddress)
        printfn ""

Flat.run ()
Nested.run ()
Collapsed.run ()
Policy.run ()
Narrow.run ()
Record.run ()
Exception.run ()
SameTreeTwoEdges.run ()
PerLeafNotPerPath.run ()
