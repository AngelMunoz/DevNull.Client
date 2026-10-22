module DevNull.Client.Core.Http

/// One part of a multipart upload.
type MultipartFile = {
  Name: string
  FileName: string
  ContentType: string
  Content: byte array
}

/// A multipart form: text fields and files.
type MultipartForm = {
  Fields: (string * string) list
  Files: MultipartFile list
}

/// The body of one request: empty, text, or a multipart form.
type HttpBody =
  | Empty
  | Text of string
  | Multipart of MultipartForm

/// One HTTP request. Target is the path and query exactly as the server receives it.
type HttpRequest = {
  Method: string
  Target: string
  Headers: (string * string) list
  Body: HttpBody
}

/// One HTTP response. The transport returns non-2xx responses too.
type HttpResponse = {
  Status: int
  Headers: (string * string) list
  Body: string
}

/// The transport could not reach the server.
type TransportFailure = { Message: string }

/// Worker: sends one request.
type IHttp =
  abstract Send:
    request: HttpRequest -> Async<Result<HttpResponse, TransportFailure>>

  /// Downloads one resource as raw bytes. Image payloads cannot travel
  /// through the string body of `Send`.
  abstract GetBytes:
    target: string -> Async<Result<byte array, TransportFailure>>

/// Accessor for the http capability.
type IProvideHttp =
  abstract Http: IHttp

/// The agent every request of this client wears, so the server tells
/// our traffic from browsers and scripts.
val userAgent: string

/// True when the status is 2xx.
val inline isSuccess: status: int -> bool
