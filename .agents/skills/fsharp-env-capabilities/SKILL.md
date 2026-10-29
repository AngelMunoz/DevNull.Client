---
name: fsharp-env-capabilities
description: Pass dependencies in F# as one generic env value whose capabilities are small IProvideX interfaces, so the compiler infers each function's dependency set and tests build only what a function needs. Use when an F# function list of dependency parameters grows, when wiring IServiceProvider or ASP.NET Core DI into F# functions, when a test must substitute one dependency, or when the user says env, capability, IProvide, or environment pattern.
compatibility: Requires the .NET 10 SDK and PowerShell 7.2 or later. scripts/global.json selects .NET 10 because F# 8 can report FS0192 on IWSAM edge types. Scripts pull FsToolkit.ErrorHandling and Microsoft.Extensions.DependencyInjection from NuGet.
---

# F# environment capabilities

Each function takes `env` as its first parameter. A capability is an interface with one property
that exposes a worker interface. A function names only the capabilities it uses, through a
flexible type `#IProvideX`. A composite that calls several functions gets the union of their
capabilities by inference, with no new parameter. The caller passes one object that implements
every capability the call tree names. Sources: Bartosz Sypytkowski,
[Dealing with complex dependency injection in F#](https://www.bartoszsypytkowski.com/dealing-with-complex-dependency-injection-in-f/),
and the bridge to ASP.NET Core DI, [Fsharp-DI-Bridge](https://github.com/TheAngryByrd/Fsharp-DI-Bridge/).
A runnable proof with a test env, a container env, and two compile-time break tests lives in
[scripts/](scripts/). Read [references/FINDINGS.md](references/FINDINGS.md) for output you can
compare against.

## Rules

- Two interfaces per capability. The worker holds the operations, for example `ILogger`. The accessor holds one property, for example `IProvideLogger` with `abstract Logger: ILogger`. Name accessors `IProvideX`. The env implements accessors only. [references/ENV-PATTERNS.md](references/ENV-PATTERNS.md) gives the reasons.
- The accessor exposes a worker interface or a plain value, never a concrete class such as `Random` or `HttpClient`. A test must control what the worker returns. Wrap the class: `IRandom` with `NextBytes: length: int -> byte[]`, implemented once by `SystemRandom` and in tests by `FixedRandom`.
- One module per capability. Each function takes `(env: #IProvideX)` first and reads `env.X`.
- A composite leaves `env` unannotated. Inference supplies the union.
- A function that declares explicit type parameters must name `'env` among them. See `FS0064` below.
- A bundle interface that inherits several `IProvideX` is for implementers: the production env, a test requirements type. Inference never names it.
- Keep `task` and `taskResult` blocks. The Reader-monad variant in the article drops async composition. The article itself rates it less useful.

## Steps

1. List the external things a function touches: logging, database, clock, random, configuration, cache. Each is one capability.
   Done when every I/O call in the function maps to one capability and each worker is an interface a test can fake.
2. Declare the worker and accessor pair and the module:

   ```fsharp
   type ILogger =
       abstract Info: string -> unit

   type IProvideLogger =
       abstract Logger: ILogger

   module Log =
       let info (env: #IProvideLogger) fmt = Printf.kprintf env.Logger.Info fmt
   ```

   Done when `dotnet fsi` prints `val info: env: #IProvideLogger -> ...`.
3. Write leaves with `(env: #IProvideX)`. When a leaf also declares `'e` for an IWSAM error, declare `'env` explicitly:

   ```fsharp
   let fetchUser<'env, 'e when 'env :> IProvideDatabase and UserError<'e>> (env: 'env) (id: int) : Task<Result<User, 'e>> =
   ```

   Done when `dotnet fsi` prints no `warning FS0064` for the leaf.
4. Write composites with `env` unannotated and pass `env` to each call:

   ```fsharp
   let changePassword env (request: ChangePasswordRequest) = taskResult {
       let! user = Db.fetchUser env request.UserId
       do! Db.updateUser env { user with Salt = Random.salt env 16 }
       Log.info env "password changed for %d" user.Id
   }
   ```

   Done when the printed signature shows the union, for example `when 'a :> IProvideDatabase and 'a :> IProvideLogger and 'a :> IProvideRandom`.
5. Build the production env and the test env from [references/ENV-PATTERNS.md](references/ENV-PATTERNS.md).
   Done when the same composite runs with both and the test env implements only the capabilities the composite names.
6. Add a `breaks/` script that passes an env with one capability missing.
   Done when it fails with `FS0193`.

## Compiler signals

| Signal | Meaning | Action |
| --- | --- | --- |
| `FS0193: The type 'LogOnly' is not compatible with the type 'IProvideClock'` | The env misses a capability the call tree names. | Implement `IProvideClock` on the env. |
| `warning FS0064: ... The type variable implied by the use of a '#' ... has been constrained to be type 'FirstEnv'` | A function with explicit type parameters used `#IProvideX`. The compiler did not generalize `env`. The first caller fixed it. A second caller then fails with `FS0193: The type 'SecondEnv' is not compatible with the type 'FirstEnv'`. | Name `'env` in the explicit list: `<'env, 'e when 'env :> IProvideX and ...>`. |
| Signature prints `IProvideDatabase -> ...` instead of `'a -> ... when 'a :> IProvideDatabase` | The function uses one capability. The compiler fixed `env` to that interface. | No action. Any env that implements it still passes. A composite that calls it still infers the full union. |
| `FS0039: The type parameter 'e is not defined` | A composite named an IWSAM member. | Move the check into a leaf. See the `fsharp-iwsam-errors` skill. |

## Verify

Run [scripts/run.ps1](scripts/run.ps1). It runs `Environment.fsx` and `FlatEnv.fsx`, prints the
inferred signatures, and confirms that each script under `scripts/breaks/` fails with the error
quoted above. In your own project, print the signature of every composite with `dotnet fsi` and
read the constraint list. A composite whose signature names a concrete env type has lost its
genericity. Find the leaf that pinned it.
