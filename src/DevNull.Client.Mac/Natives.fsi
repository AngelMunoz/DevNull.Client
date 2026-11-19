module DevNull.Client.Mac.Natives

open Microsoft.Extensions.Logging
open DevNull.Client.Natives

/// The native services this head provides: the LaunchServices scheme
/// registrar for the app bundle. The audio engine and the system sounds
/// are not wired yet, so both stay `ValueNone`: the voice UI hides and
/// alerts stay silent until an AVFoundation engine lands. The factory
/// takes the logging factory, so every worker logs through the app's
/// own sink.
val services: logging: ILoggerFactory -> NativeServices
