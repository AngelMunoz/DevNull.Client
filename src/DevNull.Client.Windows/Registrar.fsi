module DevNull.Client.Windows.Registrar

open Microsoft.Extensions.Logging
open DevNull.Client.Natives

/// The devnull:// scheme handler for Windows: a per-user registry
/// entry under Software\Classes that points at this executable. The
/// outcome returns to the caller, which logs the refused registration;
/// the steps land in the head's own log.
val registrar: logging: ILoggerFactory -> ISchemeRegistrar
