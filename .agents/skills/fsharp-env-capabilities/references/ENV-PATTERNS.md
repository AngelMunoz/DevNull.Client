# Environment implementations

Every pattern below is proven in [scripts/Environment.fsx](../scripts/Environment.fsx)
except where marked.

## Why the env implements accessors, not workers

A flat env that implements `ILogger` and `IDatabase` directly gets the same inference and the same
`FS0193` check. [scripts/FlatEnv.fsx](../scripts/FlatEnv.fsx) shows that. The accessor form wins on four
points that appear when you build the env, not when you call it.

- **One line per capability.** The env returns a worker from one property. A flat env re-implements
  every member of every worker. The bridge repo's `IWrapDapper` has ten members. An object
  expression cannot delegate them for you.
- **Existing instances plug in.** `member _.Logger = services.GetRequiredService<ILogger>()` hands
  over Microsoft's logger, a `NullLogger`, or a test recorder unchanged. A flat env must forward
  each member to that instance by hand.
- **Lazy resolution.** The property body runs on access. A composite that never reads
  `env.Database` never resolves `IDatabase`. A flat env resolves every worker up front or inside
  every member.
- **No silent member capture.** Two workers can share a member name, such as `IDatabase.Get` and
  `ICache.Get`. On a flat env, `env.Get key` compiles and binds to whichever interface the
  compiler met first, with no warning. `FlatEnv.fsx` shows `cacheThenDirect` pinned to
  `ICache` this way. With accessors, `env.Database.Get` and `env.Cache.Get` cannot be confused.

The article adds a fifth reason. Each module ships its own `live` worker, such as `Log.live`,
before any env exists. The accessor is the slot the env uses to hand that value back.

Use the flat form only for a bespoke worker with one or two members, a unique member name, and
no third-party interface behind it.

## Test env from an object expression

Declare a requirements interface that inherits the capabilities one composite needs. Build it
with a `create` function and an object expression. The test passes fakes as plain values.

```fsharp
type IChangePasswordRequirements =
    inherit IProvideLogger
    inherit IProvideDatabase
    inherit IProvideRandom

module IChangePasswordRequirements =
    let create (logger: ILogger) (database: IDatabase) (random: IRandom) =
        { new IChangePasswordRequirements with
            member _.Logger = logger
            member _.Database = database
            member _.Random = random }
```

A class with `member val` recorders works the same way and lets the test read what happened.
Pick the object expression when the test needs no state. Pick the class when it does.

Each fake controls its output. `FixedRandom 7uy` returns a buffer of sevens.
The example prints whether each written salt matches that fixed output. Compare these results manually.

## Production env over IServiceProvider

Declare one application interface that inherits every capability. Implement it with a class that
holds the provider and resolves each service inside the property. Resolution is lazy. A composite
that never reads `env.Database` never resolves `IDatabase`. Register the class as transient.

```fsharp
type IAppEnvironment =
    inherit IProvideLogger
    inherit IProvideDatabase
    inherit IProvideRandom
    inherit IProvideClock

type AppEnvironment(services: IServiceProvider) =
    interface IAppEnvironment with
        member _.Logger = services.GetRequiredService<ILogger>()
        member _.Database = services.GetRequiredService<IDatabase>()
        member _.Random = services.GetRequiredService<IRandom>()
        member _.UtcNow = DateTimeOffset.UtcNow
```

```fsharp
ServiceCollection()
    .AddSingleton<ILogger>(liveLogger)
    .AddSingleton<IDatabase>(liveDatabase)
    .AddSingleton<IRandom>(SystemRandom())
    .AddTransient<IAppEnvironment, AppEnvironment>()
```

A scoped service such as a database connection stays scoped. The env resolves it from the scope
that created the env.

## Handing env to a web handler

Not compiled here. The bridge repo resolves the env from the request and passes it to a Giraffe
handler that takes `env` first. Its `RequireEnv.services` static member wraps a function of shape
`IEnvironment -> HttpFunc -> HttpContext -> Task<HttpContext option>` and calls
`ctx.GetService<IEnvironment>()` on each request. Apply the same shape for any framework: resolve
the env once at the edge, pass it down, and let inference check the rest.

## With IWSAM errors

A composite that uses capabilities and IWSAM error leaves infers both unions in one signature:

```text
val changePassword:
  env: 'a -> request: ChangePasswordRequest -> TaskResult<unit,'b>
    when 'a :> IProvideDatabase and 'a :> IProvideLogger and 'a :> IProvideRandom
     and 'b :> PasswordError<'b> and 'b :> UserError<'b>
```

The caller fixes `'a` by passing an env and `'b` by annotating the result.

## Open question

The article uses a struct env to avoid allocation. A function that uses one capability is fixed
to that interface type. Passing a struct env to it boxes at the call. Measure before choosing a
struct.
