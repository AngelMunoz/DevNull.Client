module DevNull.Client.Env

open Microsoft.Extensions.Logging
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Stores

/// The logging capability: one factory bridged to Serilog, so every
/// service that accepts a logger lands in the same sink and level.
type IProvideLogging =
  abstract LoggerFactory: ILoggerFactory

/// The production environment: system clock, crypto nonce source,
/// ssh-keygen signer, http transport, and the live session store.
type AppEnv =
  new:
    baseAddress: string * session: SessionStore * logging: ILoggerFactory ->
      AppEnv

  interface IProvideClock
  interface IProvideNonceSource
  interface IProvideSigner
  interface IProvidePkceSource
  interface IProvideHttp
  interface IProvideSession
  interface IProvideLogging

module KeyFiles =

  /// Public keys from ~/.ssh/*.pub, "type base64" lines, Ed25519 or RSA, distinct.
  val candidates: unit -> string array

  /// The private key file whose public key matches the given key id.
  val privatePathOf: keyId: string -> string option
