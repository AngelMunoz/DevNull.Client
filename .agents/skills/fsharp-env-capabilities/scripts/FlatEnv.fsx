type ILogger =
    abstract Info: string -> unit
    abstract Error: string -> unit

type IDatabase =
    abstract Get: key: string -> string

type ICache =
    abstract Get: key: string -> string

module Log =
    let info (env: #ILogger) (message: string) = env.Info message

module Db =
    let read (env: #IDatabase) key = env.Get key

module Cache =
    let read (env: #ICache) key = env.Get key

let logThenRead env key =
    Log.info env "reading"
    Db.read env key

let cacheThenDirect env key =
    Cache.read env key |> ignore
    env.Get key

let existingLogger =
    { new ILogger with
        member _.Info _ = ()
        member _.Error _ = () }

type FlatEnv() =
    interface ILogger with
        member _.Info s = existingLogger.Info s
        member _.Error s = existingLogger.Error s

    interface IDatabase with
        member _.Get k = "db:" + k

    interface ICache with
        member _.Get k = "cache:" + k

printfn "logThenRead     -> %s" (logThenRead (FlatEnv()) "k")
printfn "cacheThenDirect -> %s (env.Get bound to one Get with no warning)" (cacheThenDirect (FlatEnv()) "k")
