#load "../Domain.fsx"
#nowarn "3535"

open Domain
open Domain.Fixtures

type PaymentOnly =
    | Declined of amount: decimal
    | CardExpired

    interface PaymentError<PaymentOnly> with
        static member Declined amount = Declined amount
        static member CardExpired = CardExpired

let outcome: Result<unit, PaymentOnly> = placeOrder onHand goodCard goodAddress ({ Sku = sku "A"; Quantity = 1 }: OrderLine)
