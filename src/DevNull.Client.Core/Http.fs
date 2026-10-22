module DevNull.Client.Core.Http

open System.Reflection

type MultipartFile = {
  Name: string
  FileName: string
  ContentType: string
  Content: byte array
}

type MultipartForm = {
  Fields: (string * string) list
  Files: MultipartFile list
}

type HttpBody =
  | Empty
  | Text of string
  | Multipart of MultipartForm

type HttpRequest = {
  Method: string
  Target: string
  Headers: (string * string) list
  Body: HttpBody
}

type HttpResponse = {
  Status: int
  Headers: (string * string) list
  Body: string
}

type TransportFailure = { Message: string }

type IHttp =
  abstract Send:
    request: HttpRequest -> Async<Result<HttpResponse, TransportFailure>>

  /// Downloads one resource as raw bytes. Image payloads cannot travel
  /// through the string body of `Send`.
  abstract GetBytes:
    target: string -> Async<Result<byte array, TransportFailure>>

type IProvideHttp =
  abstract Http: IHttp

/// The agent every request of this client wears, so the server tells
/// our traffic from browsers and scripts. The version follows this
/// assembly's version, which the changelog stamps at build time.
let userAgent =
  let version = Assembly.GetExecutingAssembly().GetName().Version

  // A loaded assembly always carries a version; `GetName` types it as
  // nullable, so the null arm only satisfies the compiler.
  let text =
    match version with
    | null -> "unknown"
    | version -> string version

  $"DevNull.Client/{text} (+https://github.com/AngelMunoz/DevNull.Client)"

let inline isSuccess status = status >= 200 && status <= 299
