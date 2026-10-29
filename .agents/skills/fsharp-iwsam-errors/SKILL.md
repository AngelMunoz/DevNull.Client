---
name: fsharp-iwsam-errors
description: Design F# error types as IWSAM leaves (interfaces with static abstract members) so each function declares only its own errors and the caller picks one edge type. Use when adding or composing Result error types across F# functions, when a nested error DU needs Result.mapError at every call, when a new error case must reach every handler, or when the user says IWSAM, error tree, or edge type.
compatibility: Requires the .NET 10 SDK and PowerShell 7.2 or later. scripts/global.json selects .NET 10 because F# 8 can report FS0192 on IWSAM edge types. Scripts pull FsToolkit.ErrorHandling from NuGet.
---

# F# IWSAM error trees

A leaf function declares its errors as an interface with static abstract members and returns
`Result<_, 'e> when 'e :> LeafError<'e>`. Composites in a `result` block pick up the union of
their leaves' constraints with no `Result.mapError`. The caller at the edge picks one concrete
type that implements every interface the tree needs. The gist that started this:
https://gist.github.com/TheAngryByrd/ceb18e5b219458084f6c156b538ba579. A runnable proof with
seven edge shapes and four compile-time break tests lives in [scripts/](scripts/). Read
[references/FINDINGS.md](references/FINDINGS.md) for output you can compare against.

## Rules

- A payload is a typed value. A `string` appears in an error only when it comes from outside the domain: the raw input that failed to parse, named `raw`, or a message an external library or call returned, named for its source.
- A domain value with a format rule gets a private constructor and a `parse` function. That `parse` is itself a leaf.
- One interface per source of failure. Name the parse leaf after the failure: `InvalidSku`, `UnparsedEmail`.
- A bundle interface that inherits several leaves is implementer convenience. Inference never names it. Add one only when several edge types implement the same set.
- Put `#nowarn "3535"` at file top, or pass `--nowarn:3535` to `dotnet fsi`. The in-file directive does not silence the main script under `dotnet fsi`.
- A leaf that also takes a capability env as `#IProvideX` must name `'env` in its explicit type parameters: `<'env, 'e when 'env :> IProvideX and X<'e>>`. The `fsharp-env-capabilities` skill records the `FS0064` failure.

## Steps

1. List every function that returns `Result`. For each, write the cases it alone can produce, with typed payloads.
   Done when no case carries a `string` except a `raw` input or an external library's message.
2. Declare one IWSAM per function or per domain value parser:

   ```fsharp
   type StockError<'e> =
       static abstract OutOfStock : sku: Sku -> 'e
       static abstract Discontinued : sku: Sku -> 'e

   let reserveStock<'e when StockError<'e>> onHand (line: OrderLine) : Result<unit, 'e> =
       match Map.tryFind line.Sku onHand with
       | None -> Error('e.Discontinued line.Sku)
       | Some n when n < line.Quantity -> Error('e.OutOfStock line.Sku)
       | Some _ -> Ok()
   ```

   Done when every leaf has the explicit `<'e when X<'e>>` and return annotation. Without it, `'e.Member` fails with `FS0039: The type parameter 'e is not defined`.
3. Give each parsed value a private case and a generic `parse`:

   ```fsharp
   type Sku = private Sku of string

   module Sku =
       let value (Sku s) = s
       let parse<'e when SkuError<'e>> (raw: string) : Result<Sku, 'e> =
           if isValid raw then Ok(Sku raw) else Error('e.InvalidSku raw)
   ```

   Done when `Sku "x"` outside the module fails with `FS1093`.
4. Write composites as plain `result` blocks with `do!` and `let!`. Add no annotation and no `mapError`.
   Done when `dotnet fsi` prints the composite's signature with the union of leaf constraints and nothing else.
5. Pick the edge shape from [references/SHAPES.md](references/SHAPES.md) and implement every interface the composite's signature names.
   Done when the edge compiles and the handler matches exhaustively on it.
6. Fix the edge at each call site with a result annotation or a concrete-case match.
   Done when no call site reports `FS0331`.
7. Where two calls to one leaf must be told apart, wrap that call alone with `Result.mapError`.
   Done when the handler can name the call site for every such pair.

## Compiler signals

| Error | Meaning | Action |
| --- | --- | --- |
| `FS0366: No implementation was given for 'static abstract ...'` | A leaf gained a case and this edge misses it. | Add the member. This is the safety the approach buys. |
| `FS0331: The implicit instantiation of a generic construct ... could not be resolved` | Nothing fixes `'e` at this call. | Annotate the result or match a concrete case. |
| `FS0001: The type 'X' is not compatible with the type 'Y<X>'` | The edge misses interface `Y`. The message names the first missing one only. | Implement `Y`, compile again. |
| `FS1093: The union cases or fields of the type 'X' are not accessible` | Code bypassed `parse`. | Call `X.parse`. |
| `FS0039: The type parameter 'e is not defined` | A leaf uses `'e.Member` without the explicit constraint. | Add `<'e when X<'e>>` to the function. |
| Two records share a field name and the wrong one is inferred | F# picks the last declared record. | Annotate the record expression. |

## Verify

Run [scripts/run.ps1](scripts/run.ps1). It runs `Domain.fsx`, `Shapes.fsx`, and `Effects.fsx`,
prints the inferred signatures, and confirms that each script under `scripts/breaks/` fails with
the error quoted above. Keep a `breaks/` folder in your own project for the same reason. A break
test is the only evidence that a new leaf cannot reach a handler unseen.
