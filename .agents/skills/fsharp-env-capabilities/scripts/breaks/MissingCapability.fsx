type ILogger =
    abstract Info: string -> unit

type IProvideLogger =
    abstract Logger: ILogger

type IProvideClock =
    abstract UtcNow: System.DateTimeOffset

module Log =
    let info (env: #IProvideLogger) (message: string) = env.Logger.Info message

module Clock =
    let utcNow (env: #IProvideClock) = env.UtcNow

let stamp env = Log.info env (string (Clock.utcNow env))

type LogOnly() =
    interface IProvideLogger with
        member _.Logger =
            { new ILogger with
                member _.Info s = printfn "%s" s }

stamp (LogOnly())
