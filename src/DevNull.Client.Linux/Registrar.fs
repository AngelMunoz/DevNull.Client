module DevNull.Client.Linux.Registrar

open System
open System.Diagnostics
open System.IO
open Microsoft.Extensions.Logging
open DevNull.Client.Natives

/// The devnull:// scheme handler for Linux: a NoDisplay .desktop entry
/// in the user's applications directory, claimed for the scheme
/// through xdg-mime. The outcome returns to the caller, which logs the
/// refused registration; the steps land in the head's own log.
let registrar(logging: ILoggerFactory) = {
  new ISchemeRegistrar with
    member _.Register() =
      let log = logging.CreateLogger "DevNull.Client.Linux.Registrar"

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

        log.LogDebug("The devnull.desktop entry was written.")

        let start =
          ProcessStartInfo(
            FileName = "xdg-mime",
            Arguments = "default devnull.desktop x-scheme-handler/devnull",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
          )

        match Process.Start start with
        | null ->
          log.LogWarning("xdg-mime did not start.")
          Error "xdg-mime did not start."
        | xdg ->
          use xdg = xdg
          xdg.WaitForExit()

          if xdg.ExitCode = 0 then
            log.LogDebug("xdg-mime claimed the devnull scheme.")

            Ok()
          else
            log.LogWarning(
              "xdg-mime exited with {ExitCode}; the scheme is not claimed.",
              xdg.ExitCode
            )

            Error $"xdg-mime failed ({xdg.ExitCode})."
      with ex ->
        log.LogWarning(ex, "The scheme handler was not installed.")
        Error ex.Message
}
