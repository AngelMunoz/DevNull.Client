---
name: fsharp-failure-paths
description: Make failures in F# code explicit instead of crashing or hiding them. Use when writing or reviewing F# that calls partial functions (List.head, Seq.find, Map.find, .Value), uses failwith, falls back with defaultArg or Option.defaultValue, catches exceptions, or combines several validations into one Result.
compatibility: The proof script requires the .NET 10 SDK and PowerShell 7.2 or later. scripts/global.json selects .NET 10.
---

# F# failure paths

The compiler accepts code that crashes on an empty list or hides a missing value.
Human reviewers find these defects, so the agent must find them first.
Review evidence ranks failure-path handling as a top category of human comments on agent F# code.

Model input boundaries with [parse-dont-validate](../parse-dont-validate/SKILL.md).
This skill covers what happens after a call can fail.

## Rules

- Do not call a partial function on a value that the type lets be empty or absent.
  Use the safe form and handle each case: `List.tryHead`, `Seq.tryFind`, `Map.tryFind`, `Option` pattern match.
  A pattern match such as `first :: _` is safe when the empty case also has a branch.
- Use `failwith` and `invalidArg` only for a broken invariant that the caller cannot cause or recover from.
  When input, configuration, or a dependency can cause the failure, return `Error`.
- Use `defaultArg` and `Option.defaultValue` only when the contract defines the default.
  When the value is required, report the absence. A default for a removed setting hides the removal and keeps stale behavior.
- Do not catch every exception and return empty data. A later step then reports the wrong cause, or reports success.
  Catch the exceptions that you expect, and keep the cause in the error: `with ex -> Error(RatesUnavailable ex)`.
- When several checks validate independent inputs, return every error. `List.head errors` and `Result.bind` chains stop at the first error.
  Use `validation` from FsToolkit.ErrorHandling, or collect the `Error` cases into a list.
- Check each guard helper with a true and a false condition. A reversed condition compiles and rejects valid input.

Read [references/PATTERNS.md](references/PATTERNS.md) for each pattern with its unsafe form and its safe form.

## Steps

1. List every partial function, `failwith`, `defaultArg`, `Option.defaultValue`, `try ... with`, and first-error-only path in the change.
   Done when each item is on the list.
2. For each item, name the input that makes it fail: empty collection, missing key, missing setting, failed dependency, or a second invalid value.
3. Change each item that the input can reach to return `Error` with the cause. Keep each `failwith` only for a broken invariant, and keep each default only when the contract defines it.
4. Run the code with each failing input from step 2. Confirm that each one returns an `Error` that names its cause, and that valid input gives the same result as before.

## Verify

Run the proof script from the skill root:

```powershell
pwsh ./scripts/run.ps1
```

The script shows each unsafe form failing and each safe form reporting the failure.
It fails when an expected result does not occur.
