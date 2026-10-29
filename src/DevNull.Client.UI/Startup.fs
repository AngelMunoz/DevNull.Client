module DevNull.Client.Startup

open System
open System.IO
open System.Threading
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Microsoft.Extensions.Logging
open NXUI.Extensions
open Serilog
open Serilog.Events
open Serilog.Extensions.Logging
open DevNull.Client.DeviceAuth
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Natives
open DevNull.Client.SessionFiles
open DevNull.Client.SettingsStore
open DevNull.Client.Shell
open DevNull.Client.Stores

/// The application root: the Fluent theme first, then the app styles.
/// Parameterless: the headless test harness starts the same type.
type App() =
  inherit Application()

  override this.Initialize() =
    this.Styles.Add(FluentTheme())
    Theme.install this

/// The desktop app: the shared theme carrier plus the main window,
/// wired the way Avalonia intends — the window belongs to the classic
/// desktop lifetime, set once initialization completes.
type DesktopApp(env: AppEnv, callback: string option) =
  inherit App()

  override this.OnFrameworkInitializationCompleted() =
    match this.ApplicationLifetime with
    | :? IClassicDesktopStyleApplicationLifetime as desktop ->
      desktop.MainWindow <- Shell(env, callback)
    | _ -> ()

    base.OnFrameworkInitializationCompleted()

/// The level a `DEVNULL_LOG_LEVEL` value names, when it names one.
let requestedLevel() : LogEventLevel option =
  match Environment.GetEnvironmentVariable "DEVNULL_LOG_LEVEL" with
  | null -> None
  | raw ->
    match raw.Trim().ToUpperInvariant() with
    | "TRACE" -> Some LogEventLevel.Verbose
    | "DEBUG" -> Some LogEventLevel.Debug
    | "INFO"
    | "INFORMATION" -> Some LogEventLevel.Information
    | "WARN"
    | "WARNING" -> Some LogEventLevel.Warning
    | "ERROR" -> Some LogEventLevel.Error
    | _ -> None

/// The app log: the console plus one file per day under the app's data
/// directory, debug-verbose in development builds. A `DEVNULL_LOG_LEVEL`
/// value overrides the build's default, so a release install can trace.
let configureLogging() =
  let folder =
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DevNull",
      "logs"
    )

  Directory.CreateDirectory(folder) |> ignore

#if DEBUG
  let defaultLevel = LogEventLevel.Debug
#else
  let defaultLevel = LogEventLevel.Information
#endif

  let minimum = requestedLevel() |> Option.defaultValue defaultLevel

  Log.Logger <-
    LoggerConfiguration()
      .MinimumLevel.Is(minimum)
      .WriteTo.Console()
      .WriteTo.File(
        Path.Combine(folder, "devnull-.log"),
        rollingInterval = RollingInterval.Day
      )
      .CreateLogger()

  minimum

/// The app log and the factory every logger comes from: the console
/// plus one file per day under the app's data directory,
/// debug-verbose in development builds. A `DEVNULL_LOG_LEVEL` value
/// overrides the build's default, so a release install can trace. The
/// heads call this before they build their native services, so every
/// native worker logs through the same sink, then hand the factory to
/// `start`.
let createLogging() : ILoggerFactory =
  let minimum = configureLogging()

  let logging = (new LoggerFactory()).AddSerilog(Log.Logger)

  logging
    .CreateLogger("DevNull.Client.Startup")
    .LogInformation("The client starts (log level {Level}).", minimum)

  logging

let mutexName = "DevNull.Client.SingleInstance"

let hasRunningInstance() =
  try
    use existing = Mutex.OpenExisting mutexName
    true
  with
  | :? WaitHandleCannotBeOpenedException -> false
  | :? UnauthorizedAccessException -> true

/// The second instance has no UI: it completes the sign-in callback
/// headlessly, stores the outcome, and lets the first instance poll for
/// the session file. The sync wait is the one sanctioned bridge — this
/// process must finish before it exits.
let completeCallback
  (env: AppEnv, log: Microsoft.Extensions.Logging.ILogger, uri: string)
  : unit =
  DeviceAuth.finish(env, uri, log) |> Async.Ignore |> Async.RunSynchronously

/// Boots the shared client with the native services one platform head
/// built. The head's entry point is everything platform-specific; this
/// is everything else. The factory comes from `createLogging`.
let start(logging: ILoggerFactory, natives: NativeServices, argv: string[]) =
  let log = logging.CreateLogger "DevNull.Client.Startup"

  log.LogDebug("The client runs with {Args} arguments.", argv.Length)

  let sessionLog = logging.CreateLogger "DevNull.Client.Session"

  // The boot read runs once, before any window exists: this is the
  // one place the async reader is waited on, because the store the
  // window boots from must exist first.
  let restored =
    match loadSession sessionLog |> Async.RunSynchronously with
    | Some session ->
      log.LogInformation(
        "The stored session of {Handle} was restored.",
        session.User.Handle
      )

      Some session
    | None ->
      log.LogDebug("No stored session; the client starts signed out.")
      None

  let store =
    SessionStore(
      restored,
      devicePrivateKeyPath,
      fun () -> clearSession sessionLog
    )

  let env = AppEnv(baseAddress, store, natives, logging)

  // The theme choice follows the store at app level, not window level.
  // The store replays its current value here, but no application
  // exists yet, so that first replay cannot land; the AfterSetup hook
  // below applies the stored choice once the app is up.
  (env :> IProvideSettings).Settings.Observe
  |> Observable.add(fun value -> Theme.apply value.Theme)

  match natives.Registrar.Register() with
  | Ok() -> log.LogDebug("The devnull:// scheme is registered.")
  | Error message ->
    log.LogWarning("The devnull:// scheme is not registered: {Error}", message)

  // A head that found no engine or no sound service keeps its silence;
  // the log names what the session runs without.
  match natives.Audio with
  | ValueSome _ -> log.LogDebug("The audio engine is present.")
  | ValueNone -> log.LogWarning("No audio engine: voice notes stay hidden.")

  match natives.Sounds with
  | ValueSome _ -> log.LogDebug("The system sounds are present.")
  | ValueNone ->
    log.LogWarning("No system sounds: the client stays silent on alerts.")

  let callback = callbackUri argv

  if hasRunningInstance() then
    log.LogDebug(
      "Another instance owns the window; this one serves the callback only."
    )

    match callback with
    | Some uri ->
      log.LogInformation("The sign-in callback arrived at the second instance.")
      completeCallback(env, log, uri)
    | None -> log.LogDebug("The second instance carries no callback and exits.")

    0
  else
    use mutex = new Mutex(true, mutexName)

    log.LogDebug("This instance owns the window.")

    AppBuilder
      .Configure(fun () -> DesktopApp(env, callback))
      .UsePlatformDetect()
      .AfterSetup(fun _ ->
        Theme.apply((env :> IProvideSettings).Settings.Current.Theme))
      .StartWithClassicDesktopLifetime(argv)
