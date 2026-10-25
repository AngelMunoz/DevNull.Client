module DevNull.Client.Windows.Registrar

open DevNull.Client.Natives

/// The devnull:// scheme handler for Windows: a per-user registry
/// entry under Software\Classes that points at this executable.
val registrar: ISchemeRegistrar
