# Findings: environment capabilities

All scripts run on the .NET 10 SDK with `dotnet fsi`. Run the runner from the skill root:

```powershell
pwsh -NoProfile -File ./scripts/run.ps1
```

| File | Purpose |
| --- | --- |
| `Environment.fsx` | The capability-environment pattern for dependencies, with a test env, a container env, and IWSAM errors in one composite. |
| `FlatEnv.fsx` | The flat alternative, where the env implements the workers directly. Shows the silent member capture. |
| `breaks/MissingCapability.fsx` | Must fail. Shows the error when an env misses a capability the call tree needs. |
| `breaks/PinnedLeaf.fsx` | Must fail. Shows a leaf with explicit `'e` and `#IProvideX` fixed to its first caller. |
| `run.ps1` | Runs both scripts, prints inferred signatures, and checks that each `breaks` script fails. |

`scripts/Environment.fsx` applies the pattern from
[Dealing with complex dependency injection in F#](https://www.bartoszsypytkowski.com/dealing-with-complex-dependency-injection-in-f/)
and the bridge to ASP.NET Core DI in [Fsharp-DI-Bridge](https://github.com/TheAngryByrd/Fsharp-DI-Bridge/).
Each function takes `env` first and names its capabilities through `#IProvideX`. The composite
`changePassword` infers the capability union and the IWSAM error union in one signature:

```text
val it:
  ('a ->
     Environment.ChangePasswordRequest ->
     FsToolkit.ErrorHandling.TaskResult<unit,'b>)
    when 'a :> Environment.IProvideDatabase and
         'a :> Environment.IProvideLogger and 'a :> Environment.IProvideRandom and
         'b :> Environment.PasswordError<'b> and 'b :> Environment.UserError<'b>
```

Each accessor exposes a worker interface, not a concrete class. `IProvideRandom` exposes `IRandom`,
so the example returns fixed bytes and prints whether each written salt matches the expected value.
Compare the output below manually; the script does not assert these runtime results. The same
composite runs with an object-expression test env that implements three capabilities and with a
container env that implements four:

```text
ok             -> Ok () | logs ["info password changed for 7"] | salt matches [true]
wrong old      -> Error OldPasswordInvalid | logs ["error old password rejected for 7"] | salt matches []
missing user   -> Error (UserNotFound 9) | logs [] | salt matches []
container      -> Ok ()
```

## Why the env implements accessors, not workers

`scripts/FlatEnv.fsx` implements `ILogger`, `IDatabase`, and `ICache` directly on one env. Inference and
the `FS0193` check are the same as with accessors:

```text
val it:
  ('a -> string -> string)
    when 'a :> FlatEnv.ILogger and 'a :> FlatEnv.IDatabase
```

The flat form costs more when you build the env. It forwards every worker member by hand, it
cannot hand an existing instance to the env in one line, and it resolves every worker up front.
It also captures a shared member name silently. `cacheThenDirect` calls `env.Get key` on an env
that has both `IDatabase.Get` and `ICache.Get`. It compiles with no warning and binds to one:

```text
cacheThenDirect -> cache:k (env.Get bound to one Get with no warning)
```

With accessors, `env.Database.Get` and `env.Cache.Get` cannot be confused. [ENV-PATTERNS.md](ENV-PATTERNS.md) lists the full reasoning.

## Finding 1: a missing capability fails at compile time

`breaks/MissingCapability.fsx` passes an env with a logger but no clock. The compiler reports:

```text
error FS0193: The type 'LogOnly' is not compatible with the type 'IProvideClock'
```

## Limit 1: explicit type parameters pin a flexible env

`breaks/PinnedLeaf.fsx` declares `fetchUser<'e when UserError<'e>> (env: #IProvideDatabase)`.
The explicit `'e` stops the compiler from generalizing the `#IProvideDatabase` variable. The first
caller fixes it. The second caller fails:

```text
warning FS0064: This construct causes code to be less generic than indicated by its type annotations. The type variable implied by the use of a '#', '_' or other type annotation at or near '...breaks/PinnedLeaf.fsx(12,43)-(12,60)' has been constrained to be type 'FirstEnv'.
error FS0193: The type 'SecondEnv' is not compatible with the type 'FirstEnv'
```

`Environment.fsx` names `'env` in the explicit list instead:
`fetchUser<'env, 'e when 'env :> IProvideDatabase and UserError<'e>> (env: 'env)`.

## Note: a single capability prints as the interface

`Db.updateUser` uses one capability. Its signature prints as
`IProvideDatabase -> User -> Task<unit>` with no type variable. Any env that implements
`IProvideDatabase` still passes, and `changePassword` still infers the full union. No fix is needed.

## Open question: struct env and boxing

The article uses a struct env to avoid allocation. A call to a single-capability function such as
`Db.updateUser` upcasts the env to the interface. A struct env boxes there. These scripts do not
measure it.
