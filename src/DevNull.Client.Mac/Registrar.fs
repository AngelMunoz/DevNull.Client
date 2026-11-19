module DevNull.Client.Mac.Registrar

open System
open System.Diagnostics
open System.IO
open Microsoft.Extensions.Logging
open DevNull.Client.Natives

// LaunchServices' own registration tool; the system always carries it.
let lsregister =
  "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister"

// The .app bundle the running executable lives in: the executable sits
// at <bundle>/Contents/MacOS/<name>, so three parents up is the bundle.
let bundleRoot() : string option =
  executablePath()
  |> Directory.GetParent
  |> Option.ofObj
  |> Option.bind(fun macos -> Option.ofObj macos.Parent)
  |> Option.bind(fun contents -> Option.ofObj contents.Parent)
  |> Option.filter(fun bundle -> bundle.Extension = ".app")
  |> Option.map(fun bundle -> bundle.FullName)

let registrar(logging: ILoggerFactory) = {
  new ISchemeRegistrar with
    member _.Register() =
      let log = logging.CreateLogger "DevNull.Client.Mac.Registrar"

      match bundleRoot() with
      | None ->
        log.LogWarning(
          "The executable is not inside an app bundle; the devnull scheme is not claimed."
        )

        Error "The executable is not inside an app bundle."
      | Some bundle ->
        try
          let start =
            ProcessStartInfo(
              FileName = lsregister,
              Arguments = $"-f \"{bundle}\"",
              RedirectStandardError = true,
              UseShellExecute = false,
              CreateNoWindow = true
            )

          match Process.Start start with
          | null ->
            log.LogWarning("lsregister did not start.")
            Error "lsregister did not start."
          | proc ->
            use proc = proc
            proc.WaitForExit()

            if proc.ExitCode = 0 then
              log.LogDebug(
                "LaunchServices re-read the bundle; the devnull scheme is claimed."
              )

              Ok()
            else
              log.LogWarning(
                "lsregister exited with {ExitCode}; the scheme is not claimed.",
                proc.ExitCode
              )

              Error $"lsregister failed ({proc.ExitCode})."
        with ex ->
          log.LogWarning(ex, "The scheme handler was not installed.")
          Error ex.Message
}
