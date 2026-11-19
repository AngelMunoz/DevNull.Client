module DevNull.Client.Mac.Registrar

open Microsoft.Extensions.Logging
open DevNull.Client.Natives

/// The devnull:// scheme handler for macOS. The scheme itself is
/// declared by the app bundle's Info.plist (CFBundleURLTypes); this
/// registrar forces LaunchServices to re-read the bundle the running
/// executable lives in, so the claim follows the binary even when the
/// bundle moves. Outside a bundle there is nothing to claim, and the
/// registration reports why.
val registrar: logging: ILoggerFactory -> ISchemeRegistrar
