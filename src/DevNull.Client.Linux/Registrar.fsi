module DevNull.Client.Linux.Registrar

open Microsoft.Extensions.Logging
open DevNull.Client.Natives

/// The devnull:// scheme handler for Linux: a NoDisplay .desktop entry
/// in the user's applications directory, claimed for the scheme
/// through xdg-mime. Inside a flatpak sandbox the exported desktop
/// entry already owns the scheme, and registration reports success
/// without writing anything.
val registrar: logging: ILoggerFactory -> ISchemeRegistrar
