#nowarn "3535"

type Sku = Sku of string

type StockError<'e> =
    static abstract OutOfStock : sku: Sku -> 'e
    static abstract Discontinued : sku: Sku -> 'e
    static abstract Backordered : sku: Sku -> 'e

type FlatError =
    | OutOfStock of Sku
    | Discontinued of Sku

    interface StockError<FlatError> with
        static member OutOfStock sku = OutOfStock sku
        static member Discontinued sku = Discontinued sku
