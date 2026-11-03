module DevNull.Client.Linux.Natives

open Microsoft.Extensions.Logging
open DevNull.Client.Natives

/// The native services this head provides: the .desktop scheme
/// registrar, the audio engine behind the PulseAudio client library
/// (every PipeWire and PulseAudio desktop serves it), and the desktop's
/// own sounds through libcanberra. A library this machine lacks hides
/// its service; the voice UI follows. The factory takes the logging
/// factory, so every worker logs through the app's own sink.
val services: logging: ILoggerFactory -> NativeServices
