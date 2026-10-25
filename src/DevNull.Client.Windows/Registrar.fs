module DevNull.Client.Windows.Registrar

open Microsoft.Win32

// The registry APIs return null when they cannot create the key; the
// BCL does not annotate the return as nullable.
#nowarn "3261"

open DevNull.Client.Natives

/// The devnull:// scheme handler for Windows: a per-user registry
/// entry under Software\Classes that points at this executable.
let registrar = {
  new ISchemeRegistrar with
    member _.Register() =
      try
        let command = "\"" + executablePath() + "\" \"%1\""

        match Registry.CurrentUser.CreateSubKey @"Software\Classes\devnull" with
        | null -> Error "Could not open the devnull registry key."
        | key ->
          use key = key
          key.SetValue("", "URL:devnull Protocol")
          key.SetValue("URL Protocol", "")

          match key.CreateSubKey @"shell\open\command" with
          | null -> Error "Could not open the devnull command key."
          | commandKey ->
            use commandKey = commandKey
            commandKey.SetValue("", command)
            Ok()
      with ex ->
        Error ex.Message
}
