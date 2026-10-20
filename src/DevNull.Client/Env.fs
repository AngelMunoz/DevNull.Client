module DevNull.Client.Env

open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open IcedTasks
open Microsoft.Extensions.Logging
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.SettingsStore
open DevNull.Client.Stores

type SystemClock() =

  interface IClock with
    member _.UnixNow() =
      DateTimeOffset.UtcNow.ToUnixTimeSeconds()

type RandomNonceSource() =

  interface INonceSource with
    member _.NewHex() =
      let bytes = Array.zeroCreate<byte> 16

      RandomNumberGenerator.Fill(bytes)

      Convert.ToHexString(bytes).ToLowerInvariant()

type RandomPkceSource() =

  interface IPkceSource with
    member _.NewVerifier() =
      let bytes = Array.zeroCreate<byte> 32

      RandomNumberGenerator.Fill(bytes)

      Convert
        .ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_')

type SshKeygenSigner() =

  let stripArmor(armored: string) =
    armored.Split('\n')
    |> Array.map _.Trim()
    |> Array.filter(fun line -> not(line.StartsWith "-"))
    |> String.concat ""

  let signSync(request: SignRequest) : Result<string, SignFailure> =
    let directory =
      Path.Combine(Path.GetTempPath(), "devnull." + Path.GetRandomFileName())

    Directory.CreateDirectory(directory) |> ignore

    let payloadFile = Path.Combine(directory, "commit")

    File.WriteAllText(payloadFile, request.Payload, UTF8Encoding(false))

    let start =
      ProcessStartInfo(
        FileName = "ssh-keygen",
        Arguments =
          $"-Y sign -f \"{request.KeyFile}\" -n {request.Namespace} \"{payloadFile}\"",
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      )

    let outcome: Result<string, SignFailure> =
      match Process.Start(start) with
      | null ->
        Error {
          Message = "ssh-keygen did not start."
        }
      | started ->
        use signing = started
        let stderr = signing.StandardError.ReadToEnd()
        signing.WaitForExit()

        match signing.ExitCode with
        | 0 -> File.ReadAllText(payloadFile + ".sig") |> stripArmor |> Ok
        | _ -> Error { Message = stderr }

    Directory.Delete(directory, true)

    outcome

  interface ISigner with
    member _.Sign(request: SignRequest) : Async<Result<string, SignFailure>> =
      // ssh-keygen blocks until it exits, so it runs off the UI thread.
      asyncEx { return! Task.Run(fun () -> signSync request) }

type HttpClientTransport(baseAddress: string) =

  let client = new HttpClient(BaseAddress = Uri(baseAddress))

  let headersOf(response: HttpResponseMessage) =
    Seq.append response.Headers response.Content.Headers
    |> Seq.map(fun header -> header.Key, String.concat ", " header.Value)
    |> Seq.toList

  interface IHttp with
    member _.Send
      (request: HttpRequest)
      : Async<Result<HttpResponse, TransportFailure>> = asyncEx {
      try
        use message =
          new HttpRequestMessage(HttpMethod(request.Method), request.Target)

        for name, value in request.Headers do
          if
            not(name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
          then
            message.Headers.TryAddWithoutValidation(name, value) |> ignore

        match request.Body with
        | Empty -> ()
        | Text body ->
          if body <> "" then
            let content = new ByteArrayContent(Encoding.UTF8.GetBytes body)

            request.Headers
            |> List.tryFind(fun (name, _) ->
              name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            |> Option.iter(fun (_, value) ->
              content.Headers.ContentType <- MediaTypeHeaderValue.Parse value)

            message.Content <- content
        | Multipart form ->
          let content = new MultipartFormDataContent()

          for name, value in form.Fields do
            content.Add(new StringContent(value, Encoding.UTF8), name)

          for file in form.Files do
            let part = new ByteArrayContent(file.Content)

            part.Headers.ContentType <-
              MediaTypeHeaderValue.Parse file.ContentType

            content.Add(part, file.Name, file.FileName)

          message.Content <- content

        let! response = client.SendAsync(message)
        use response = response
        let! body = response.Content.ReadAsStringAsync()

        return
          Ok {
            Status = int response.StatusCode
            Headers = headersOf response
            Body = body
          }
      with ex ->
        return Error({ Message = ex.Message }: TransportFailure)
    }

type IProvideLogging =
  abstract LoggerFactory: ILoggerFactory

type AppEnv(baseAddress: string, session: SessionStore, logging: ILoggerFactory)
  =

  let clock = SystemClock()
  let nonceSource = RandomNonceSource()
  let pkceSource = RandomPkceSource()
  let signer = SshKeygenSigner()
  let http = HttpClientTransport(baseAddress)
  let logger = logging.CreateLogger "DevNull.Client.SettingsStore"

  let settings =
    AppSettingsStore(
      SettingsStore.load(),
      fun value ->
        match SettingsStore.save value with
        | Ok() -> ()
        | Error message ->
          logger.LogError("The settings were not saved: {Error}", message)
    )

  interface IProvideClock with
    member _.Clock = upcast clock

  interface IProvideNonceSource with
    member _.NonceSource = upcast nonceSource

  interface IProvidePkceSource with
    member _.PkceSource = upcast pkceSource

  interface IProvideSigner with
    member _.Signer = upcast signer

  interface IProvideHttp with
    member _.Http = upcast http

  interface IProvideSession with
    member _.Session = session

  interface IProvideSettings with
    member _.Settings = settings

  interface IProvideLogging with
    member _.LoggerFactory = logging

module KeyFiles =

  let sshDirectory =
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
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
