#r "nuget: FsToolkit.ErrorHandling, 5.2.0"
#nowarn "3535"

open System
open FsToolkit.ErrorHandling

type SkuError<'e> =
    static abstract InvalidSku : raw: string -> 'e

type PostcodeError<'e> =
    static abstract UnparsedPostcode : raw: string -> 'e

type EmailError<'e> =
    static abstract UnparsedEmail : raw: string -> 'e

type InputError<'e> =
    inherit SkuError<'e>
    inherit PostcodeError<'e>
    inherit EmailError<'e>

type Sku = private Sku of string

module Sku =
    let value (Sku s) = s

    let parse<'e when SkuError<'e>> (raw: string) : Result<Sku, 'e> =
        if raw.Length >= 1 && raw.Length <= 8 && raw |> Seq.forall Char.IsAsciiLetterOrDigit then
            Ok(Sku raw)
        else
            Error('e.InvalidSku raw)

type Postcode = private Postcode of string

module Postcode =
    let value (Postcode s) = s

    let parse<'e when PostcodeError<'e>> (raw: string) : Result<Postcode, 'e> =
        let trimmed = raw.Trim()

        if trimmed.Length >= 5 && trimmed.Length <= 8 && trimmed.Contains ' ' then
            Ok(Postcode trimmed)
        else
            Error('e.UnparsedPostcode raw)

type EmailAddress = private EmailAddress of string

module EmailAddress =
    let value (EmailAddress s) = s

    let parse<'e when EmailError<'e>> (raw: string) : Result<EmailAddress, 'e> =
        match raw.Split '@' with
        | [| local; domain |] when local.Length > 0 && domain.Contains '.' -> Ok(EmailAddress raw)
        | _ -> Error('e.UnparsedEmail raw)

type StockError<'e> =
    static abstract OutOfStock : sku: Sku -> 'e
    static abstract Discontinued : sku: Sku -> 'e

type PaymentError<'e> =
    static abstract Declined : amount: decimal -> 'e
    static abstract CardExpired : 'e

type ShippingError<'e> =
    static abstract NoCarrier : postcode: Postcode -> 'e

type NotificationError<'e> =
    static abstract EmailBounced : address: EmailAddress -> 'e

type CheckoutError<'e> =
    inherit StockError<'e>
    inherit PaymentError<'e>
    inherit ShippingError<'e>
    inherit NotificationError<'e>

type OrderLine = { Sku: Sku; Quantity: int }
type Card = { Last4: string; ExpiryYear: int }
type Address = { Postcode: Postcode }
type Basket = { Lines: OrderLine list; Address: Address; Email: EmailAddress }

type RawLine = { Sku: string; Quantity: int }
type RawBasket = { Lines: RawLine list; Postcode: string; Email: string }

let unitPrice = 10m

let parseLine (raw: RawLine) = result {
    let! sku = Sku.parse raw.Sku
    return ({ Sku = sku; Quantity = raw.Quantity }: OrderLine)
}

let parseBasket (raw: RawBasket) = result {
    let! lines = raw.Lines |> List.traverseResultM parseLine
    let! postcode = Postcode.parse raw.Postcode
    let! email = EmailAddress.parse raw.Email
    return { Lines = lines; Address = { Postcode = postcode }; Email = email }
}

let reserveStock<'e when StockError<'e>> (onHand: Map<Sku, int>) (line: OrderLine) : Result<unit, 'e> =
    match Map.tryFind line.Sku onHand with
    | None -> Error('e.Discontinued line.Sku)
    | Some n when n < line.Quantity -> Error('e.OutOfStock line.Sku)
    | Some _ -> Ok()

let chargeCard<'e when PaymentError<'e>> (card: Card) (amount: decimal) : Result<unit, 'e> =
    if card.ExpiryYear < 2026 then Error 'e.CardExpired
    elif amount > 500m then Error('e.Declined amount)
    else Ok()

let scheduleShipment<'e when ShippingError<'e>> (address: Address) : Result<unit, 'e> =
    if (Postcode.value address.Postcode).StartsWith "ZZ" then Error('e.NoCarrier address.Postcode) else Ok()

let notifyCustomer<'e when NotificationError<'e>> (email: EmailAddress) : Result<unit, 'e> =
    if (EmailAddress.value email).EndsWith ".invalid" then Error('e.EmailBounced email) else Ok()

let placeOrder onHand card address (line: OrderLine) = result {
    do! reserveStock onHand line
    do! chargeCard card (decimal line.Quantity * unitPrice)
    do! scheduleShipment address
}

let fulfillBasket onHand card (basket: Basket) = result {
    for line in basket.Lines do
        do! placeOrder onHand card basket.Address line

    do! notifyCustomer basket.Email
}

let checkout onHand card (raw: RawBasket) = result {
    let! basket = parseBasket raw
    do! fulfillBasket onHand card basket
}

module Fixtures =
    type FixtureError =
        | BadSku of string
        | BadPostcode of string
        | BadEmail of string

        interface InputError<FixtureError> with
            static member InvalidSku raw = BadSku raw
            static member UnparsedPostcode raw = BadPostcode raw
            static member UnparsedEmail raw = BadEmail raw

    let private orFail (r: Result<'a, FixtureError>) =
        match r with
        | Ok v -> v
        | Error e -> failwithf "fixture is not valid: %A" e

    let sku raw = Sku.parse raw |> orFail
    let postcode raw = Postcode.parse raw |> orFail
    let email raw = EmailAddress.parse raw |> orFail

    let onHand = Map [ sku "A", 100; sku "B", 1 ]
    let goodCard = { Last4 = "4242"; ExpiryYear = 2030 }
    let expiredCard = { Last4 = "0001"; ExpiryYear = 2020 }
    let goodAddress = { Postcode = postcode "SW1A 1AA" }
    let remoteAddress = { Postcode = postcode "ZZ9 9ZZ" }
    let goodEmail = email "ann@example.com"

    let basket skuRaw quantity postcodeRaw emailRaw =
        { Lines = [ { Sku = skuRaw; Quantity = quantity } ]
          Postcode = postcodeRaw
          Email = emailRaw }

    let scenarios =
        [ "all ok", goodCard, basket "A" 1 "SW1A 1AA" "ann@example.com"
          "invalid sku", goodCard, basket "a-1" 1 "SW1A 1AA" "ann@example.com"
          "unparsed postcode", goodCard, basket "A" 1 "SW1" "ann@example.com"
          "unparsed email", goodCard, basket "A" 1 "SW1A 1AA" "ann"
          "out of stock", goodCard, basket "B" 2 "SW1A 1AA" "ann@example.com"
          "discontinued", goodCard, basket "Z" 1 "SW1A 1AA" "ann@example.com"
          "card expired", expiredCard, basket "A" 1 "SW1A 1AA" "ann@example.com"
          "declined", goodCard, basket "A" 60 "SW1A 1AA" "ann@example.com"
          "no carrier", goodCard, basket "A" 1 "ZZ9 9ZZ" "ann@example.com"
          "email bounced", goodCard, basket "A" 1 "SW1A 1AA" "ann@example.invalid" ]

    let runAll (title: string) (describe: Result<unit, 'e> -> string) =
        printfn "== %s ==" title

        for name, card, raw in scenarios do
            let outcome = checkout onHand card raw
            printfn "%-18s -> %s" name (describe outcome)

        printfn ""
