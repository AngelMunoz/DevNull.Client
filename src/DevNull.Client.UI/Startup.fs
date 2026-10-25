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

/// The app log: the console plus one file per day under the app's data
/// directory, debug-verbose in development builds.
let configureLogging() =
  let folder =
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DevNull",
      "logs"
    )

  Directory.CreateDirectory(folder) |> ignore

  Log.Logger <-
    LoggerConfiguration()
#if DEBUG
      .MinimumLevel
      .Debug()
#else
      .MinimumLevel
      .Information()
#endif
      .WriteTo.Console()
      .WriteTo.File(
        Path.Combine(folder, "devnull-.log"),
        rollingInterval = RollingInterval.Day
      )
      .CreateLogger()

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
/// the session file.
let completeCallback (env: AppEnv) (uri: string) =
  match ensureKey() with
  | Error message -> saveLastError message
  | Ok key ->
    match complete(env, key, uri) |> Async.RunSynchronously with
    | Ok _ -> ()
    | Error error -> saveLastError(describe error)

/// Boots the shared client with the native services one platform head
/// built. The head's entry point is everything platform-specific; this
/// is everything else.
let start(natives: NativeServices, argv: string[]) =
  configureLogging()

  let logging = (new LoggerFactory()).AddSerilog(Log.Logger)

  let store = SessionStore(loadSession(), devicePrivateKeyPath, clearSession)

  let env = AppEnv(baseAddress, store, natives, logging)

  // The theme choice follows the store at app level, not window level.
  // The store replays its current value here, but no application
  // exists yet, so that first replay cannot land; the AfterSetup hook
  // below applies the stored choice once the app is up.
  (env :> IProvideSettings).Settings.Observe
  |> Observable.add(fun value -> Theme.apply value.Theme)

  match natives.Registrar.Register() with
  | Ok() -> ()
  | Error message ->
    Log.Warning("The devnull:// scheme is not registered: {Error}", message)

  let callback = callbackUri argv

  if hasRunningInstance() then
    match callback with
    | Some uri -> completeCallback env uri
    | None -> ()

    0
  else
    use mutex = new Mutex(true, mutexName)

    AppBuilder
      .Configure(fun () -> DesktopApp(env, callback))
      .UsePlatformDetect()
      .AfterSetup(fun _ ->
        Theme.apply((env :> IProvideSettings).Settings.Current.Theme))
      .StartWithClassicDesktopLifetime(argv)
