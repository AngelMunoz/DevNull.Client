module DevNull.Client.Env

open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http

/// The production environment: system clock, crypto nonce source, ssh-keygen signer, http transport.
type AppEnv =
  new: baseAddress: string -> AppEnv
  interface IProvideClock
  interface IProvideNonceSource
  interface IProvideSigner
  interface IProvidePkceSource
  interface IProvideHttp

module KeyFiles =

  /// Public keys from ~/.ssh/*.pub, "type base64" lines, Ed25519 or RSA, distinct.
  val candidates: unit -> string list

  /// The private key file whose public key matches the given key id.
  val privatePathOf: keyId: string -> string option
