# Edge shapes

The call tree does not fix the edge shape. The same composite resolves to any type that implements
its leaf interfaces. Pick by what the handler does with the value. Each shape below is proven in
[scripts/Shapes.fsx](../scripts/Shapes.fsx).

| Shape | Edge type | Pick when |
| --- | --- | --- |
| Flat | One DU case per leaf member. | The handler branches on every leaf. Simplest. |
| Nested | One outer case per interface, each wrapping an inner DU. | Handlers are layered by subsystem, or an inner DU already exists. |
| Collapsed | One case per stage, payloads dropped. | The handler needs only which stage failed. Fewest cases. |
| Policy | Cases named for the next action, such as `Retry of RetryReason` and `Reject of RejectReason`. | The handler is a retry loop or a gate. Grouping crosses interface lines. |
| Narrow | A small DU for one leaf. | The caller uses one leaf, not the composite. A composite still needs every interface. |
| Record | A record with a typed classification beside the cause, for example an HTTP status DU. | The edge feeds a transport layer. |
| Exception | A class inheriting `System.Exception` with a typed `Cause` property. | Code must throw, or a C# caller consumes it. |

## Implementing an edge

```fsharp
type Policy =
    | Retry of RetryReason
    | Reject of RejectReason

    interface StockError<Policy> with
        static member OutOfStock sku = Retry(Restock sku)
        static member Discontinued sku = Reject(Discontinued sku)

    interface PaymentError<Policy> with
        static member Declined amount = Reject(Declined amount)
        static member CardExpired = Reject CardExpired
```

A record or class edge uses the same `interface ... with` block. Each static member returns one
value of the edge type.

## Two edges, one call

```fsharp
let flat: Result<unit, FlatError> = checkout onHand card raw
let policy: Result<unit, Policy> = checkout onHand card raw
```

Only the annotation differs. Use this when one composite serves two handlers.

## Limits

- The edge sees the leaf, not the call site. Two calls to one leaf with the same input yield the same value. Tag the call with `Result.mapError` to recover the site.
- Subset reuse of a composite is not possible. An edge for a composite implements every interface the composite names. Map unwanted leaves to one case instead.
- `validation` with `and!` needs `Result.mapError List.singleton` on each binding. `taskResult` needs nothing extra.
