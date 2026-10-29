module DevNull.Client.SshKeys

open System
open System.IO

type ISshKeys =
  abstract Candidates: unit -> string array
  abstract PrivateKeyOf: keyId: string -> string option

type IProvideSshKeys =
  abstract SshKeys: ISshKeys

module Desktop =

  let sshDirectory =
    Path.Combine(
      Environment.GetFolderPath Environment.SpecialFolder.UserProfile,
      ".ssh"
    )

  // The probes answer empty on the IO failures a desktop can produce —
  // a folder it cannot read, a file that vanished mid-listing — and
  // let anything louder surface.
  let private firstLineOf(path: string) : string option =
    try
      File.ReadLines path |> Seq.tryHead
    with
    | :? IOException
    | :? UnauthorizedAccessException -> None

  let candidates() : string array =
    try
      Directory.GetFiles(sshDirectory, "*.pub")
      |> Array.choose firstLineOf
      |> Array.filter(fun line ->
        line.StartsWith "ssh-ed25519 " || line.StartsWith "ssh-rsa ")
      |> Array.choose(fun line ->
        match line.Split(' ', StringSplitOptions.RemoveEmptyEntries) with
        | [| kind; key; _ |] -> Some $"{kind} {key}"
        | _ -> None)
      |> Array.distinct
    with
    | :? IOException
    | :? UnauthorizedAccessException -> [||]

  let privatePathOf(keyId: string) =
    try
      Directory.GetFiles(sshDirectory, "*.pub")
      |> Array.tryFind(fun pub ->
        firstLineOf pub |> Option.exists(fun line -> line.StartsWith keyId))
      |> Option.map(fun pub -> pub.Substring(0, pub.Length - 4))
    with
    | :? IOException
    | :? UnauthorizedAccessException -> None

  let sources() : ISshKeys = {
    new ISshKeys with
      member _.Candidates() = candidates()
      member _.PrivateKeyOf keyId = privatePathOf keyId
  }
