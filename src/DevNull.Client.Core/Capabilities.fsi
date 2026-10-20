module DevNull.Client.Core.Capabilities

/// Worker: the clock the payload timestamp comes from.
type IClock =
  /// Seconds since the unix epoch.
  abstract UnixNow: unit -> int64

/// Accessor for the clock capability.
type IProvideClock =
  abstract Clock: IClock

/// Worker: a source of 16 random bytes, hex encoded.
type INonceSource =
  abstract NewHex: unit -> string

/// Accessor for the nonce source capability.
type IProvideNonceSource =
  abstract NonceSource: INonceSource

/// Worker: a source of PKCE code verifiers, 43 to 128 characters from the allowed set.
type IPkceSource =
  abstract NewVerifier: unit -> string

/// Accessor for the PKCE verifier source capability.
type IProvidePkceSource =
  abstract PkceSource: IPkceSource

/// A signing request, all values it needs, together.
type SignRequest = {
  KeyFile: string
  Namespace: string
  Payload: string
}

/// ssh-keygen's stderr, when it fails.
type SignFailure = { Message: string }

/// Worker: signs a payload the way `ssh-keygen -Y sign` does.
type ISigner =
  /// Returns the signature, base64 on one line, without the armor.
  abstract Sign: request: SignRequest -> Async<Result<string, SignFailure>>

/// Accessor for the signer capability.
type IProvideSigner =
  abstract Signer: ISigner

module Clock =
  /// The current unix timestamp, in seconds.
  val inline unixNow: env: #IProvideClock -> int64

module NonceSource =
  /// A fresh nonce candidate, 16 random bytes as hex.
  val inline newHex: env: #IProvideNonceSource -> string

module PkceSource =
  /// A fresh PKCE code verifier.
  val inline newVerifier: env: #IProvidePkceSource -> string
