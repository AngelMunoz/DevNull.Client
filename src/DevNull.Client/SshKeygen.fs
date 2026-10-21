module DevNull.Client.SshKeygen

open System
open System.Diagnostics
open System.IO

/// The ssh-keygen binary: the system one when it exists, the one on
/// the PATH otherwise.
let executable =
  let system =
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.System),
      "OpenSSH",
      "ssh-keygen.exe"
    )

  if File.Exists system then system else "ssh-keygen"

/// One ssh-keygen run: its stdout when it exits clean, its stderr or
/// the timeout when it does not. A run that outlives the 30-second
/// bound is killed.
let run(arguments: string) : Result<string, string> =
  try
    let start =
      ProcessStartInfo(
        FileName = executable,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      )

    match Process.Start(start) with
    | null -> Error "ssh-keygen did not start."
    | started ->
      use signing = started

      // Both streams drain while the process runs, so the timeout
      // bounds the run itself and never a full pipe.
      let output = signing.StandardOutput.ReadToEndAsync()
      let error = signing.StandardError.ReadToEndAsync()

      if signing.WaitForExit(30_000) then
        let stdout = output.GetAwaiter().GetResult()
        let stderr = error.GetAwaiter().GetResult()

        if signing.ExitCode = 0 then
          Ok stdout
        else
          Error(
            if stderr.Trim() = "" then
              $"ssh-keygen failed ({signing.ExitCode})."
            else
              stderr.Trim()
          )
      else
        signing.Kill()
        signing.WaitForExit()
        Error "ssh-keygen did not finish in 30 seconds."
  with ex ->
    Error ex.Message
