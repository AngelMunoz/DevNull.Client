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
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Microsoft.Extensions.Logging
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.RecentSearchStore
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.SessionFiles
open DevNull.Client.SettingsStore
open DevNull.Client.SshKeygen
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.UiClock
open DevNull.Client.Timeline
open DevNull.Client.RouteStore
open DevNull.Client.Unread
open DevNull.Client.Remotes

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

type SshKeygenSigner(log: ILogger) =

  let stripArmor(armored: string) =
    armored.Split('\n')
    |> Array.map _.Trim()
    |> Array.filter(fun line -> not(line.StartsWith "-"))
    |> String.concat ""

  let sign(request: SignRequest) : Async<Result<string, SignFailure>> = asyncEx {
    let directory =
      Path.Combine(Path.GetTempPath(), "devnull." + Path.GetRandomFileName())

    let started = Stopwatch.StartNew()

    try
      try
        Directory.CreateDirectory(directory) |> ignore

        let payloadFile = Path.Combine(directory, "commit")

        log.LogTrace("The payload signs under {Namespace}.", request.Namespace)

        File.WriteAllText(payloadFile, request.Payload, UTF8Encoding(false))

        let! signed =
          SshKeygen.run(
            $"-Y sign -f \"{request.KeyFile}\" -n {request.Namespace} \"{payloadFile}\""
          )

        let outcome: Result<string, SignFailure> =
          match signed with
          | Error message ->
            log.LogWarning("ssh-keygen did not sign: {Error}", message.Trim())

            Error { Message = message }
          | Ok _ ->
            try
              File.ReadAllText(payloadFile + ".sig") |> stripArmor |> Ok
            with ex ->
              log.LogWarning(ex, "ssh-keygen left no signature file.")

              Error { Message = ex.Message }

        log.LogDebug(
          "The payload signed in {Milliseconds} ms.",
          started.ElapsedMilliseconds
        )

        return outcome
      with ex ->
        log.LogWarning(ex, "The signature did not happen.")

        return Error { Message = ex.Message }
    finally
      try
        Directory.Delete(directory, true)
      with :? IOException ->
        ()
  }

  interface ISigner with
    member _.Sign(request: SignRequest) : Async<Result<string, SignFailure>> =
      sign request

type HttpClientTransport(baseAddress: string, log: ILogger) =

  let client =
    new HttpClient(
      BaseAddress = Uri(baseAddress),
      Timeout = TimeSpan.FromSeconds 30.0
    )

  let headersOf(response: HttpResponseMessage) =
    Seq.append response.Headers response.Content.Headers
    |> Seq.map(fun header -> header.Key, String.concat ", " header.Value)
    |> Seq.toList

  interface IHttp with
    member _.Send
      (request: HttpRequest)
      : Async<Result<HttpResponse, TransportFailure>> = asyncEx {
      // The caller's token reaches the socket, so a disposed page stops
      // its in-flight request instead of only its sleep.
      let! ct = Async.CancellationToken

      let started = Stopwatch.StartNew()

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

        let! response = client.SendAsync(message, ct)
        use response = response
        let! body = response.Content.ReadAsStringAsync(ct)

        log.LogDebug(
          "{Method} {Target} answered {Status} in {Milliseconds} ms.",
          request.Method,
          request.Target,
          int response.StatusCode,
          started.ElapsedMilliseconds
        )

        return
          Ok {
            Status = int response.StatusCode
            Headers = headersOf response
            Body = body
          }
      with ex ->
        log.LogWarning(
          ex,
          "{Method} {Target} did not answer.",
          request.Method,
          request.Target
        )

        return Error({ Message = ex.Message }: TransportFailure)
    }

    member _.GetBytes
      (target: string)
      : Async<Result<byte array, TransportFailure>> = asyncEx {
      let! ct = Async.CancellationToken
      let started = Stopwatch.StartNew()

      // The path alone goes to the log: a media URL's query carries the
      // signature that grants the download.
      let path =
        match target.IndexOf '?' with
        | -1 -> target
        | i -> target[.. i - 1]

      try
        use message = new HttpRequestMessage(HttpMethod.Get, target)

        message.Headers.TryAddWithoutValidation("User-Agent", userAgent)
        |> ignore

        // Image URLs are absolute, so the base address never applies.
        let! response = client.SendAsync(message, ct)

        use response = response
        let! bytes = response.Content.ReadAsByteArrayAsync(ct)

        log.LogDebug(
          "GET {Path} answered {Status} with {Bytes} bytes in {Milliseconds} ms.",
          path,
          int response.StatusCode,
          bytes.Length,
          started.ElapsedMilliseconds
        )

        return Ok bytes
      with ex ->
        log.LogWarning(ex, "GET {Path} did not answer.", path)

        return Error({ Message = ex.Message }: TransportFailure)
    }

type IProvideLogging =
  abstract LoggerFactory: ILoggerFactory

type AppEnv
  (
    baseAddress: string,
    session: SessionStore,
    natives: Natives.NativeServices,
    logging: ILoggerFactory
  ) as this =

  let clock = SystemClock()
  let nonceSource = RandomNonceSource()
  let pkceSource = RandomPkceSource()
  let signer = SshKeygenSigner(logging.CreateLogger "DevNull.Client.Signer")

  let http =
    HttpClientTransport(baseAddress, logging.CreateLogger "DevNull.Client.Http")

  let imageCache =
    ImageCacheService(
      Path.Combine(
        Environment.GetFolderPath(
          Environment.SpecialFolder.LocalApplicationData
        ),
        "DevNull",
        "cache",
        "images"
      ),
      (http :> IHttp).GetBytes,
      logging.CreateLogger "DevNull.Client.ImageCache"
    )

  // The attachment sources resolve the main window per call: the shell
  // owns the window, the env outlives it.
  let attachments =
    AttachmentSources.desktop(
      logging.CreateLogger "DevNull.Client.Attachments",
      fun () ->
        match Avalonia.Application.Current with
        | null -> ValueNone
        | app ->
          match app.ApplicationLifetime with
          | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            match desktop.MainWindow with
            | null -> ValueNone
            | window -> ValueSome(window :> TopLevel)
          | _ -> ValueNone
    )

  let logger = logging.CreateLogger "DevNull.Client.SettingsStore"

  let settings =
    AppSettingsStore(
      (match SettingsStore.load() with
       | Ok settings ->
         logger.LogDebug("The settings were read.")
         settings
       | Error message ->
         logger.LogError("The settings were not read: {Error}", message)
         None),
      fun value ->
        match SettingsStore.save value with
        | Ok() -> logger.LogDebug("The settings were saved.")
        | Error message ->
          logger.LogError("The settings were not saved: {Error}", message)
    )

  let uiClock = UiClockService(TimeSpan.FromMinutes 1.0)

  let lastVisit = LastVisitStore()

  let recentSearches = RecentSearchStore()

  let profilesLog = logging.CreateLogger "DevNull.Client.Profiles"

  let profiles = {
    new IProfiles with
      member _.Fetch(username: string) = asyncEx {
        match session.SignedIn with
        | ValueSome entry ->
          let! result: Result<ProfileDetails, AppError> =
            Users.get(
              this,
              {
                KeyFile = entry.KeyFile
                Username = username
              }
            )

          return
            match result with
            | Ok details ->
              profilesLog.LogDebug("The profile of {User} arrived.", username)
              Some details
            | Error error ->
              profilesLog.LogWarning(
                "The profile of {User} did not arrive: {Error}",
                username,
                bounded 300 (describe error)
              )

              None
        | ValueNone -> return None
      }
  }

  let unread =
    UnreadService(
      session,
      (fun () ->
        match session.SignedIn with
        | ValueSome current -> Me.get(this, { KeyFile = current.KeyFile })
        | ValueNone -> async { return Error(SignInFailed "signed out") }),
      logging.CreateLogger "DevNull.Client.Unread"
    )

  let remotes =
    RemotesStore(
      {
        Clock = clock
        NonceSource = nonceSource
        Signer = signer
        Http = http
      },
      session,
      logging.CreateLogger "DevNull.Client.Remotes"
    )

  let timeline = TimelineService()

  let routes = RouteStore()

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

  interface IProvideNatives with
    member _.Natives = natives

  interface IProvideSettings with
    member _.Settings = settings

  interface IProvideLogging with
    member _.LoggerFactory = logging

  interface IProvideUnread with
    member _.Unread = unread

  interface IProvideRemotes with
    member _.Remotes = remotes

  interface IProvideTimeline with
    member _.Timeline = timeline

  interface IProvideUiClock with
    member _.UiClock = uiClock

  interface IProvideRoutes with
    member _.Routes = routes

  interface IProvideImageCache with
    member _.ImageCache = imageCache

  interface IProvideAttachmentSources with
    member _.Attachments = attachments

  interface IProvideLastVisit with
    member _.LastVisit = lastVisit

  interface IProvideRecentSearches with
    member _.RecentSearches = recentSearches

  interface IProvideProfiles with
    member _.Profiles = profiles

  interface IProvideSshKeys with
    member _.SshKeys = Desktop.sources()
