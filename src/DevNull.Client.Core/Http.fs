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

let inline isSuccess status = status >= 200 && status <= 299
