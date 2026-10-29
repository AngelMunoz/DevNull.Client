#nowarn "3535"

type IDatabase =
    abstract Query: sql: string -> obj

type IProvideDatabase =
    abstract Database: IDatabase

type UserError<'e> =
    static abstract UserNotFound: id: int -> 'e

let fetchUser<'e when UserError<'e>> (env: #IProvideDatabase) (id: int) : Result<obj, 'e> =
    match env.Database.Query $"select {id}" with
    | null -> Error('e.UserNotFound id)
    | row -> Ok row

type UserMissing =
    | UserNotFound of id: int

    interface UserError<UserMissing> with
        static member UserNotFound id = UserNotFound id

let emptyDatabase =
    { new IDatabase with
        member _.Query _ = null }

type FirstEnv() =
    interface IProvideDatabase with
        member _.Database = emptyDatabase

type SecondEnv() =
    interface IProvideDatabase with
        member _.Database = emptyDatabase

let first: Result<obj, UserMissing> = fetchUser (FirstEnv()) 1
let second: Result<obj, UserMissing> = fetchUser (SecondEnv()) 2
