module DevNull.Client.Core.Pkce

/// The S256 challenge for a PKCE verifier: base64url of SHA-256, no padding.
val inline challenge: verifier: string -> string
