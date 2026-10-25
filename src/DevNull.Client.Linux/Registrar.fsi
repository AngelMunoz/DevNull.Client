module DevNull.Client.Linux.Registrar

open DevNull.Client.Natives

/// The devnull:// scheme handler for Linux: a NoDisplay .desktop entry
/// in the user's applications directory, claimed for the scheme
/// through xdg-mime.
val registrar: ISchemeRegistrar
