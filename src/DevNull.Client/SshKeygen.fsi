module DevNull.Client.SshKeygen

/// One ssh-keygen run: its stdout when it exits clean, its stderr or
/// the timeout when it does not. A run that outlives the 30-second
/// bound is killed.
val run: arguments: string -> Result<string, string>
