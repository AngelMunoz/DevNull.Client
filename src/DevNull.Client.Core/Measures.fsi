namespace DevNull.Client.Core

/// A commit's identifier: the canonical lowercase UUID the JSON API uses.
[<Measure>]
type CommitId

/// A commit body as dev-null.sh accepts it: 1 to 4000 characters.
[<Measure>]
type CommitBody

/// A pushed message's body, as dev-null.sh accepts it: up to 2000 characters.
[<Measure>]
type MessageBody

/// The short hash dev-null.sh shows for a commit: 7 to 12 hex characters.
[<Measure>]
type ShortHash

/// A reply target the CLI accepts: a short hash, a commit id, or a link that contains a commit id.
[<Measure>]
type CommitReference

/// The one-time value every signed commit carries: 16 random bytes, hex encoded.
[<Measure>]
type Nonce

/// An SSH public key id, "type base64", that belongs to a registered devnull user.
[<Measure>]
type KeyId

/// The uuid one pushed message identifies itself by, so a retry does not push it twice.
[<Measure>]
type ClientId
