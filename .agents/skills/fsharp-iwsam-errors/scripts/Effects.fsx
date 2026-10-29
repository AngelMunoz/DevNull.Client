#r "nuget: FsToolkit.ErrorHandling, 5.2.0"
#load "Domain.fsx"
#nowarn "3535"

open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Domain
open Domain.Fixtures

type FlatError =
    | OutOfStock of Sku
    | Discontinued of Sku
    | Declined of amount: decimal
    | CardExpired
    | NoCarrier of Postcode

    interface StockError<FlatError> with
        static member OutOfStock sku = OutOfStock sku
        static member Discontinued sku = Discontinued sku

    interface PaymentError<FlatError> with
        static member Declined amount = Declined amount
        static member CardExpired = CardExpired

    interface ShippingError<FlatError> with
        static member NoCarrier postcode = NoCarrier postcode

let reserveStockAsync<'e when StockError<'e>> onHand line : Task<Result<unit, 'e>> =
    task { return reserveStock onHand line }

let chargeCardAsync<'e when PaymentError<'e>> card amount : Task<Result<unit, 'e>> =
    task { return chargeCard card amount }

let scheduleShipmentAsync<'e when ShippingError<'e>> address : Task<Result<unit, 'e>> =
    task { return scheduleShipment address }

let placeOrderAsync onHand card address (line: OrderLine) = taskResult {
    do! reserveStockAsync onHand line
    do! chargeCardAsync card (decimal line.Quantity * unitPrice)
    do! scheduleShipmentAsync address
}

let validateOrder onHand card address (line: OrderLine) = validation {
    let! () = reserveStock onHand line |> Result.mapError List.singleton
    and! () = chargeCard card (decimal line.Quantity * unitPrice) |> Result.mapError List.singleton
    and! () = scheduleShipment address |> Result.mapError List.singleton
    return ()
}

let line: OrderLine = { Sku = sku "B"; Quantity = 2 }

printfn "== taskResult =="
let viaTask: Result<unit, FlatError> = (placeOrderAsync onHand expiredCard remoteAddress line).Result
printfn "%A" viaTask
printfn ""

printfn "== validation: all leaves collected into one edge list =="
let viaValidation: Result<unit, FlatError list> = validateOrder onHand expiredCard remoteAddress line
printfn "%A" viaValidation
printfn ""
