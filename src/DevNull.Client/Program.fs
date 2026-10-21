module DevNull.Client.Program

open System
open System.IO
open System.Threading
open Avalonia
open Avalonia.Controls
open Avalonia.Themes.Fluent
open Microsoft.Extensions.Logging
open NXUI.Extensions
open Serilog
open Serilog.Extensions.Logging
open DevNull.Client.DeviceAuth
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.SchemeRegistration
open DevNull.Client.SessionFiles
open DevNull.Client.SettingsStore
open DevNull.Client.Shell
open DevNull.Client.Stores

/// The application root: the Fluent theme first, then the app styles.
type App() =
  inherit Application()

  override this.Initialize() =
    this.Styles.Add(FluentTheme())
    Theme.install this

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

let start argv =
  configureLogging()

  let logging = (new LoggerFactory()).AddSerilog(Log.Logger)

  let store = SessionStore(loadSession(), devicePrivateKeyPath, clearSession)

  let env = AppEnv(baseAddress, store, logging)

  // The theme choice follows the store at app level, not window level.
  (env :> IProvideSettings).Settings.Observe
  |> Observable.add(fun value -> Theme.apply value.Theme)

  match register() with
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
      .Configure<App>()
      .UsePlatformDetect()
      .StartWithClassicDesktopLifetime(
        (fun () -> Shell(env, callback) :> Window),
        argv
      )

[<EntryPoint; STAThread>]
let main argv = start argv
