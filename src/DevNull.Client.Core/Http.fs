module DevNull.Client.Core.Http

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

type IProvideHttp =
  abstract Http: IHttp

/// The agent every request of this client wears, so the server tells
/// our traffic from browsers and scripts.
let userAgent = "dev-null-fsharp-client/0.0.1-dev"

let inline isSuccess status = status >= 200 && status <= 299
