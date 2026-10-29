module DevNull.Client.SshKeygen

/// One ssh-keygen run: its stdout when it exits clean, its stderr or
/// the timeout when it does not. Both streams drain while the process
/// runs, a run that outlives the 30-second bound is killed, and the
/// caller never blocks a thread waiting for it.
val run: arguments: string -> Async<Result<string, string>>
