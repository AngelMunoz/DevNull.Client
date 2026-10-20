module DevNull.Client.SchemeRegistration

/// One operating system's handler for the devnull:// scheme: it points
/// the OS at this executable.
type ISchemeRegistrar =
  abstract Register: unit -> Result<unit, string>

/// Registers the devnull:// scheme with the registrar of the operating
/// system the app runs on. macOS takes the handler from the app bundle,
/// so it fails here by design.
val register: unit -> Result<unit, string>
