module DevNull.Client.Startup

open Avalonia
open Microsoft.Extensions.Logging
open DevNull.Client.Natives

/// The application root: the Fluent theme first, then the app styles.
/// Parameterless: the headless test harness starts the same type.
type App =
  inherit Application
  new: unit -> App

/// The app log and the factory every logger comes from: the console
/// plus one file per day under the app's data directory,
/// debug-verbose in development builds. A `DEVNULL_LOG_LEVEL` value
/// overrides the build's default, so a release install can trace. The
/// heads call this before they build their native services, so every
/// native worker logs through the same sink, then hand the factory to
/// `start`.
val createLogging: unit -> ILoggerFactory

/// Boots the shared client with the native services one platform head
/// built. The head's entry point is everything platform-specific; this
/// is everything else. The factory comes from `createLogging`.
val start:
  logging: ILoggerFactory * natives: NativeServices * argv: string[] -> int
