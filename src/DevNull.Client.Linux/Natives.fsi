module DevNull.Client.Linux.Natives

open DevNull.Client.Natives

/// The native services this head provides: the .desktop scheme
/// registrar. The audio engine and system sounds come later; their
/// absence hides the voice UI and keeps callers silent.
val services: NativeServices
