module DevNull.Client.Core.Pkce

open System
open System.Security.Cryptography
open System.Text

let inline challenge(verifier: string) =
  let hash = SHA256.HashData(Encoding.ASCII.GetBytes verifier)

  Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')
