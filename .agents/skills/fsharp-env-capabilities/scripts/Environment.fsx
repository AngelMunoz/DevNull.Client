#r "nuget: FsToolkit.ErrorHandling, 5.2.0"
#r "nuget: Microsoft.Extensions.DependencyInjection, 10.0.0"
#nowarn "3535"

open System
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Microsoft.Extensions.DependencyInjection

type ILogger =
    abstract Info: string -> unit
    abstract Error: string -> unit

type IProvideLogger =
    abstract Logger: ILogger

type IDatabase =
    abstract Query: sql: string * param: obj -> Task<obj>
    abstract Execute: sql: string * param: obj -> Task

type IProvideDatabase =
    abstract Database: IDatabase

type IRandom =
    abstract NextBytes: length: int -> byte[]

type IProvideRandom =
    abstract Random: IRandom

type IProvideClock =
    abstract UtcNow: DateTimeOffset

module Log =
    let info (env: #IProvideLogger) fmt = Printf.kprintf env.Logger.Info fmt
    let error (env: #IProvideLogger) fmt = Printf.kprintf env.Logger.Error fmt

module Random =
    let salt (env: #IProvideRandom) (length: int) =
        Convert.ToBase64String(env.Random.NextBytes length)

module Clock =
    let utcNow (env: #IProvideClock) = env.UtcNow

type User = { Id: int; Hash: string; Salt: string }

let hash salt password = $"{salt}:{password}"

type UserError<'e> =
    static abstract UserNotFound: id: int -> 'e

type PasswordError<'e> =
    static abstract OldPasswordInvalid: 'e

module Db =
    let fetchUser<'env, 'e when 'env :> IProvideDatabase and UserError<'e>> (env: 'env) (id: int) : Task<Result<User, 'e>> = task {
        let! row = env.Database.Query("select * from users where id = @id", {| id = id |})

        match row with
        | :? User as user -> return Ok user
        | _ -> return Error('e.UserNotFound id)
    }

    let updateUser (env: #IProvideDatabase) (user: User) : Task<unit> = task {
        do! env.Database.Execute("update users set hash = @Hash, salt = @Salt where id = @Id", user)
    }

module Password =
    let verify<'e when PasswordError<'e>> (user: User) (oldPassword: string) : Result<unit, 'e> =
        if user.Hash = hash user.Salt oldPassword then Ok() else Error 'e.OldPasswordInvalid

type ChangePasswordRequest = { UserId: int; OldPassword: string; NewPassword: string }

let changePassword env (request: ChangePasswordRequest) = taskResult {
    let! user = Db.fetchUser env request.UserId
    do! Password.verify user request.OldPassword |> Result.teeError (fun _ -> Log.error env "old password rejected for %d" user.Id)
    let salt = Random.salt env 16
    do! Db.updateUser env { user with Salt = salt; Hash = hash salt request.NewPassword }
    Log.info env "password changed for %d" user.Id
}

let audit env (request: ChangePasswordRequest) = task {
    Log.info env "audit at %O for %d" (Clock.utcNow env) request.UserId
}

type ChangePasswordError =
    | UserNotFound of id: int
    | OldPasswordInvalid

    interface UserError<ChangePasswordError> with
        static member UserNotFound id = UserNotFound id

    interface PasswordError<ChangePasswordError> with
        static member OldPasswordInvalid = OldPasswordInvalid

type FakeDatabase(user: User option) =
    member val Executed = ResizeArray<obj>()

    interface IDatabase with
        member _.Query(_, _) =
            Task.FromResult(
                match user with
                | Some u -> box u
                | None -> null
            )

        member this.Execute(_, param) =
            this.Executed.Add param
            Task.CompletedTask

type SystemRandom() =
    let random = Random()

    interface IRandom with
        member _.NextBytes length =
            let buffer = Array.zeroCreate<byte> length
            random.NextBytes buffer
            buffer

type FixedRandom(fill: byte) =
    interface IRandom with
        member _.NextBytes length = Array.create length fill

type RecordingLogger() =
    member val Lines = ResizeArray<string>()

    interface ILogger with
        member this.Info s = this.Lines.Add $"info {s}"
        member this.Error s = this.Lines.Add $"error {s}"

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

let stored = { Id = 7; Hash = hash "s1" "old"; Salt = "s1" }

let expectedSalt = Convert.ToBase64String(Array.create 16 7uy)

let run (name: string) (env: #IChangePasswordRequirements) (logger: RecordingLogger) (database: FakeDatabase) request =
    let outcome: Result<unit, ChangePasswordError> = (changePassword env request).Result

    let written =
        database.Executed
        |> Seq.map (fun p -> (p :?> User).Salt = expectedSalt)
        |> List.ofSeq

    printfn "%-14s -> %A | logs %A | salt matches %A" name outcome (List.ofSeq logger.Lines) written

printfn "== Test env from an object expression =="

let scenario name user request =
    let logger = RecordingLogger()
    let database = FakeDatabase user
    let env = IChangePasswordRequirements.create logger database (FixedRandom 7uy)
    run name env logger database request

scenario "ok" (Some stored) { UserId = 7; OldPassword = "old"; NewPassword = "new" }
scenario "wrong old" (Some stored) { UserId = 7; OldPassword = "wrong"; NewPassword = "new" }
scenario "missing user" None { UserId = 9; OldPassword = "x"; NewPassword = "y" }
printfn ""

printfn "== Production env from a container =="

let provider =
    ServiceCollection()
        .AddSingleton<ILogger>(
            { new ILogger with
                member _.Info s = printfn "[info] %s" s
                member _.Error s = printfn "[error] %s" s }
        )
        .AddSingleton<IDatabase>(FakeDatabase(Some stored))
        .AddSingleton<IRandom>(SystemRandom())
        .AddTransient<IAppEnvironment, AppEnvironment>()
        .BuildServiceProvider()

let appEnv = provider.GetRequiredService<IAppEnvironment>()
let viaContainer: Result<unit, ChangePasswordError> = (changePassword appEnv { UserId = 7; OldPassword = "old"; NewPassword = "new" }).Result
printfn "container      -> %A" viaContainer
(audit appEnv { UserId = 7; OldPassword = ""; NewPassword = "" }).Wait()
printfn ""
