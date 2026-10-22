module DevNull.Client.SshKeys

/// The user's ssh keys as the CLI commit flow needs them: the public
/// lines the key lookup offers, and the private half a signature uses.
type ISshKeys =
  abstract Candidates: unit -> string array
  abstract PrivateKeyOf: keyId: string -> string option

type IProvideSshKeys =
  abstract SshKeys: ISshKeys

module Desktop =

  /// The ssh key worker over the user's ~/.ssh directory.
  val sources: unit -> ISshKeys
