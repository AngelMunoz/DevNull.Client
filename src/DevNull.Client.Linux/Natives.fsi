module DevNull.Client.Linux.Natives

open Microsoft.Extensions.Logging
open DevNull.Client.Natives

/// The native services this head provides: the .desktop scheme
/// registrar, the audio engine behind the PulseAudio client library,
/// and the desktop's own sounds through libcanberra. A library this
/// machine lacks hides its service; the voice UI follows.
val services: logging: ILoggerFactory -> NativeServices
