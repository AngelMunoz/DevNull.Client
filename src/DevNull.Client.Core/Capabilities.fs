module DevNull.Client.Core.Capabilities

type IClock =
  abstract UnixNow: unit -> int64

type IProvideClock =
  abstract Clock: IClock

type INonceSource =
  abstract NewHex: unit -> string

type IProvideNonceSource =
  abstract NonceSource: INonceSource

type IPkceSource =
  abstract NewVerifier: unit -> string

type IProvidePkceSource =
  abstract PkceSource: IPkceSource

type SignRequest = {
  KeyFile: string
  Namespace: string
  Payload: string
}

type SignFailure = { Message: string }

type ISigner =
  abstract Sign: request: SignRequest -> Async<Result<string, SignFailure>>

type IProvideSigner =
  abstract Signer: ISigner

module Clock =

  let inline unixNow(env: #IProvideClock) = env.Clock.UnixNow()

module NonceSource =

  let inline newHex(env: #IProvideNonceSource) = env.NonceSource.NewHex()

module PkceSource =

  let inline newVerifier(env: #IProvidePkceSource) =
    env.PkceSource.NewVerifier()
