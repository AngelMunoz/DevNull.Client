module DevNull.Client.SchemeRegistration

open System
open System.Diagnostics
open System.IO

/// One operating system's handler for the devnull:// scheme: it points
/// the OS at this executable.
type ISchemeRegistrar =
  abstract Register: unit -> Result<unit, string>

let executablePath() =
  match Environment.ProcessPath with
  | null ->
    let name =
      if OperatingSystem.IsWindows() then
        "DevNull.Client.exe"
      else
        "DevNull.Client"

    Path.Combine(AppContext.BaseDirectory, name)
  | path when
    path.EndsWith("DevNull.Client.exe", StringComparison.OrdinalIgnoreCase)
    ->
    path
  | path -> path

module Windows =

  open Microsoft.Win32

  // The registry APIs return null when they cannot create the key; the
  // BCL does not annotate the return as nullable.
  #nowarn "3261"

  let registrar = {
    new ISchemeRegistrar with
      member _.Register() =
        try
          let command = "\"" + executablePath() + "\" \"%1\""

          match
            Registry.CurrentUser.CreateSubKey @"Software\Classes\devnull"
          with
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

#warnon "3261"

module Linux =

  let registrar = {
    new ISchemeRegistrar with
      member _.Register() =
        try
          // xdg-mime resolves the entry id only against the XDG
          // applications directory, so the file cannot live in the
          // app's own data folder.
          let directory =
            Path.Combine(
              Environment.GetFolderPath
                Environment.SpecialFolder.LocalApplicationData,
              "applications"
            )

          Directory.CreateDirectory(directory) |> ignore

          let entry = Path.Combine(directory, "devnull.desktop")

          let content =
            "[Desktop Entry]\n"
            + "Type=Application\n"
            + "Name=/dev/null\n"
            + "NoDisplay=true\n"
            + $"Exec=\"{executablePath()}\" %%u\n"
            + "MimeType=x-scheme-handler/devnull;\n"

          File.WriteAllText(entry, content)

          let start =
            ProcessStartInfo(
              FileName = "xdg-mime",
              Arguments = "default devnull.desktop x-scheme-handler/devnull",
              RedirectStandardError = true,
              UseShellExecute = false,
              CreateNoWindow = true
            )

          match Process.Start start with
          | null -> Error "xdg-mime did not start."
          | xdg ->
            use xdg = xdg
            xdg.WaitForExit()

            if xdg.ExitCode = 0 then
              Ok()
            else
              Error $"xdg-mime failed ({xdg.ExitCode})."
        with ex ->
          Error ex.Message
  }

module MacOs =

  let registrar = {
    new ISchemeRegistrar with
      member _.Register() =
        Error
          "macOS takes the devnull:// handler from the app bundle (CFBundleURLTypes); runtime registration is not possible."
  }

let register() : Result<unit, string> =
  if OperatingSystem.IsWindows() then
    Windows.registrar.Register()
  elif OperatingSystem.IsMacOS() then
    MacOs.registrar.Register()
  elif OperatingSystem.IsLinux() then
    Linux.registrar.Register()
  else
    Error "This operating system has no devnull:// registrar."
