module DevNull.Client.SshKeygen

open System
open System.IO
open System.Threading
open CliWrap
open CliWrap.Buffered
open IcedTasks

/// The ssh-keygen binary: the system one when it exists, the one on
/// PATH otherwise.
let executable =
  let system =
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.System),
      "OpenSSH",
      "ssh-keygen.exe"
    )

  if File.Exists system then system else "ssh-keygen"

/// One ssh-keygen run: its stdout when it exits clean, its stderr or
/// the timeout when it does not. Both streams drain while the process
/// runs, a run that outlives the 30-second bound is killed, and the
/// caller never blocks a thread waiting for it.
///
/// The exit code is checked here on purpose rather than through the
/// wrapper's own validation: a non-zero exit is an answer this
/// function reports, whose words ride stderr, not an exception the
/// caller has to catch.
let run(arguments: string) : Async<Result<string, string>> = asyncEx {
  try
    use bound = new CancellationTokenSource(TimeSpan.FromSeconds 30.0)

    let command =
      Cli
        .Wrap(executable)
        .WithArguments(arguments)
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync(bound.Token)


    let! finished = command.Task

    if finished.ExitCode = 0 then
      return Ok finished.StandardOutput
    else
      let message = finished.StandardError.Trim()

      return
        Error(
          if message = "" then
            $"ssh-keygen failed ({finished.ExitCode})."
          else
            message
        )
  with
  | :? OperationCanceledException ->
    return Error "ssh-keygen did not finish in 30 seconds."
  | ex -> return Error ex.Message
}
