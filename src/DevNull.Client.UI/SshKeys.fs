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

  let firstLineOf(path: string) : string option =
    try
      File.ReadLines path |> Seq.tryHead
    with _ ->
      None

  let candidates() : string array =
    try
      Directory.GetFiles(sshDirectory, "*.pub")
      |> Array.choose firstLineOf
      |> Array.filter(fun line ->
        line.StartsWith "ssh-ed25519 " || line.StartsWith "ssh-rsa ")
      |> Array.map(fun line ->
        let parts = line.Split(' ')
        parts[0] + " " + parts[1])
      |> Array.distinct
    with _ -> [||]

  let privatePathOf(keyId: string) =
    try
      Directory.GetFiles(sshDirectory, "*.pub")
      |> Array.tryFind(fun pub ->
        firstLineOf pub |> Option.exists(fun line -> line.StartsWith keyId))
      |> Option.map(fun pub -> pub.Substring(0, pub.Length - 4))
    with _ ->
      None

  let sources() : ISshKeys = {
    new ISshKeys with
      member _.Candidates() = candidates()
      member _.PrivateKeyOf keyId = privatePathOf keyId
  }
