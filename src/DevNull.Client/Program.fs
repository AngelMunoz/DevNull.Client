module DevNull.Client.Program

open System
open System.Threading
open Avalonia
open Avalonia.Controls
open Avalonia.Themes.Fluent
open NXUI.Extensions
open DevNull.Client.CommitView
open DevNull.Client.DeviceAuth
open DevNull.Client.Env
open DevNull.Client.Errors

/// The application root: the Fluent theme as shipped.
type App() =
  inherit Application()

  override this.Initialize() = this.Styles.Add(FluentTheme())

let mutexName = "DevNull.Client.SingleInstance"

let hasRunningInstance() =
  try
    use existing = Mutex.OpenExisting mutexName
    true
  with
  | :? WaitHandleCannotBeOpenedException -> false
  | :? UnauthorizedAccessException -> true

let completeCallback (env: AppEnv) (uri: string) =
  match ensureKey() with
  | Error message -> saveLastError message
  | Ok key ->
    match complete(env, key, uri) |> Async.RunSynchronously with
    | Ok _ -> ()
    | Error error -> saveLastError(describe error)

let start argv =
  let env = AppEnv baseAddress
  registerScheme() |> ignore
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
        (fun () -> buildWindow(env, callback)),
        argv
      )

[<EntryPoint>]
let main argv = start argv
