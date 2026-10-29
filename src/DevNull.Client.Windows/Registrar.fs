module DevNull.Client.Windows.Registrar

open Microsoft.Extensions.Logging
open Microsoft.Win32

// The registry APIs return null when they cannot create the key; the
// BCL does not annotate the return as nullable.
#nowarn "3261"

open DevNull.Client.Natives

/// The devnull:// scheme handler for Windows: a per-user registry
/// entry under Software\Classes that points at this executable. The
/// outcome returns to the caller, which logs the refused registration;
/// the steps land in the head's own log.
let registrar(logging: ILoggerFactory) = {
  new ISchemeRegistrar with
    member _.Register() =
      let log = logging.CreateLogger "DevNull.Client.Windows.Registrar"

      try
        let command = "\"" + executablePath() + "\" \"%1\""

        match Registry.CurrentUser.CreateSubKey @"Software\Classes\devnull" with
        | null ->
          log.LogWarning("The devnull registry key did not open.")
          Error "Could not open the devnull registry key."
        | key ->
          use key = key
          key.SetValue("", "URL:devnull Protocol")
          key.SetValue("URL Protocol", "")

          match key.CreateSubKey @"shell\open\command" with
          | null ->
            log.LogWarning("The devnull command key did not open.")
            Error "Could not open the devnull command key."
          | commandKey ->
            use commandKey = commandKey
            commandKey.SetValue("", command)

            log.LogDebug("The devnull scheme is claimed in the registry.")

            Ok()
      with ex ->
        log.LogWarning(ex, "The scheme handler was not installed.")
        Error ex.Message
}
