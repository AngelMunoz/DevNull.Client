---
name: fsharp-nullness-interop
description: Handle F# nullness checking (Nullable enable, FS3261, FS3262 in F# 9 and later) at .NET interop boundaries, and fix FS0041 "A unique overload could not be determined" from new BCL overloads such as TimeSpan.FromSeconds(int64). Covers BCL members that return `| null`, correct null handling with match and Option.ofObj, null leaks that the compiler does not report, and when suppression is acceptable. Use when an F# build reports FS3261, FS3262, or FS0041, when the user turns on nullness checks or TreatWarningsAsErrors, or when F# code calls .NET APIs that can return null.
compatibility: Nullness checking needs F# 9 or later (.NET 9 SDK or later). The proof requires the .NET 10 SDK, NuGet access, and PowerShell 7.2 or later. Results were verified with SDK 10.0.401 on win-x64.
---

# F# nullness at interop boundaries

F# nullness checking makes .NET reference types that can be null visible as `T | null`.
The compiler reports most problems. The risk is the wrong fix: a fix that compiles and
hides a real null. Convert each `T | null` to an F# type once, at the boundary. Then the
rest of the code uses `T` or `T option`.

[scripts/run.ps1](scripts/run.ps1) verifies the null results and diagnostics below. [Verify](#verify)
lists the rules that it does not verify.

## Turn on the checks

```xml
<PropertyGroup>
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>
```

`<Nullable>enable</Nullable>` passes `--checknulls+`. Nullness diagnostics are warnings
(FS3261, FS3262). `TreatWarningsAsErrors` makes them fail the build.

## Members that return `| null`

These members return `T | null`. Using the result as `T` gives FS3261.

| Member | Null when |
|---|---|
| `JsonElement.GetString()` | the JSON value is `null` |
| `JsonSerializer.Deserialize<T>` | the document is `null`, also for F# records |
| `Uri.TryCreate(s, kind)` out value | the parse fails, so it is typed `Uri \| null` even on `true` |
| `Environment.GetEnvironmentVariable` | the variable is not set |
| `exn.InnerException` | there is no inner exception |
| `Path.GetDirectoryName` | the path is a root, such as `C:\` or `/`. A bare file name gives `""`, not null. |
| `TextReader.ReadLine()`, `Console.ReadLine()` | end of input |
| `obj.ToString()`, `Type.FullName` | the API allows null |

## Handle null at the boundary

| Need | Use |
|---|---|
| Value or fallback | `match x with \| null -> fallback \| v -> v` |
| Null and empty both mean "none" | `match x with \| null \| "" -> None \| v -> Some v` |
| `option` | `x \|> Option.ofObj` |
| `Result` | `match x with \| null -> Error "..." \| v -> Ok v` |
| Out value of a `Try` method | `\| true, NonNull u -> ...` |
| Two named cases | `match x with \| Null -> ... \| NonNull v -> ...` |

```fsharp
let tryHost (s: string) : string option =
    match Uri.TryCreate(s, UriKind.Absolute) with
    | true, NonNull u -> Some u.Host
    | _ -> None

let rec rootMessage (ex: exn) : string =
    match ex.InnerException with
    | null -> ex.Message
    | inner -> rootMessage inner
```

Rules that the build does not tell you in advance:

- `isNull x` and `String.IsNullOrEmpty x` do not narrow `string | null` to `string`. The
  `else` branch still gives FS3261. Use `match`.
- Do not mix `| null ->` with `| NonNull v ->`. After `| null`, the value is already
  non-null. `NonNull` then gives FS3262 and the match gives FS0025. Use `| v ->`, or use
  `Null` and `NonNull` together.
- Annotate public return types. Without an annotation, the compiler infers `T | null`
  and moves the null to the caller.
- A `string` binding cannot hold `null`. `let mutable x: string = null` gives FS3261.
  Declare `string | null`, or better, use `string option`.
- Passing `ex.InnerException` to a parameter of type `exn` gives FS3261 with the text
  `The types 'exn' and 'exn | null' do not have compatible nullability`. Match on the
  value before the call, or declare the parameter as `exn | null` and match inside.

## Null leaks that compile clean

The compiler does not report these. Each one compiles with nullness and
`TreatWarningsAsErrors` on.

| Code | Result for null |
|---|---|
| `Some(reader.ReadLine())` with no type annotation | `Some null`, not `None`. The type becomes `(string \| null) option`. |
| `string x` on `string \| null` | `""`. A missing value becomes an empty string. |
| `nonNull x` | throws `NullReferenceException` |
| `Unchecked.nonNull x` | passes the null on. It fails later, far from the cause. |
| `#nowarn "3261"`, `<NoWarn>FS3261</NoWarn>` | hides every later null in the file or project |

Use `Option.ofObj` instead of `Some` on a .NET result. Use `string x` only when an empty
string is the correct value for null.

## When suppression is acceptable

Use `nonNull` or `Unchecked.nonNull` only when the API cannot return null in this call,
and the type does not say so. Write the reason in a comment on the same line or the line
above. Example: a value that a `Try` method returned with `true`, when you cannot use
`NonNull` in the pattern. Never suppress FS3261 for a file or a project to get a build
to pass.
`NonNullQuick` and `nullArgCheck` throw on null like `nonNull`. They need the same written reason.

```fsharp
// GetEntryAssembly is null only in unmanaged hosts. This exe always has one.
let entry = Unchecked.nonNull (Assembly.GetEntryAssembly())
```

## New BCL overloads (FS0041)

.NET 9 added `int64` overloads to `TimeSpan.FromSeconds`, `FromMinutes`,
`FromMilliseconds`, and `FromMicroseconds`. An `int` argument used to convert to `float`.
Now it matches no overload exactly, so the compiler reports FS0041. `FromHours` and
`FromDays` have `int` overloads and still compile.

| Code | Fix |
|---|---|
| `TimeSpan.FromSeconds 5` | `TimeSpan.FromSeconds 5.0` |
| `TimeSpan.FromSeconds(pown 2 n)` | `TimeSpan.FromSeconds(float (pown 2 n))` |
| `let toSpan x = TimeSpan.FromSeconds x` | `let toSpan (x: float) = TimeSpan.FromSeconds x` |

Convert the argument at the call. Do not change the type of a public parameter or a
record field to fix FS0041. For other FS0041 errors, read the candidate list in the
message and annotate the argument with one candidate type.

## Verify

1. Build with `Nullable` and `TreatWarningsAsErrors` on. Do not add `NoWarn` for 3261.
2. Search the changed code for `nonNull`, `#nowarn`, `Some`, and `string` used as a function
   (`string x`, `x |> string`). Check each use on a .NET result against the tables above.
3. Run the code with null inputs at each boundary: a JSON `null`, an unset environment
   variable, an exception with no inner exception, and an empty reader.

To check the facts in this skill, run the bundled proof:

```powershell
pwsh ./scripts/run.ps1
```

The runner builds [scripts/Clean](scripts/Clean) with nullness and warnings as errors,
runs it, and compares 19 results. It then builds each file in [scripts/breaks](scripts/breaks)
and checks that it fails with only the diagnostic codes in its `// expect:` lines and with the message text in its `// expect-text:` lines.
The `clean` lines it prints are the results in the tables above.

These rules come from the F# compiler documentation and are not bundled proofs:
`<Nullable>enable</Nullable>` passes `--checknulls+`, `#nowarn "3261"` and `NoWarn`
hide later nullness warnings, and an unannotated function infers `T | null` from its body.
