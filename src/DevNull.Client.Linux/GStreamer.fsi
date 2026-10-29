module DevNull.Client.Linux.GStreamer

open Microsoft.Extensions.Logging
open DevNull.Client.Natives

/// Starts the desktop's media framework once and says whether it can
/// play: one debug line with its own version word when it stands, one
/// warning line when the desktop carries no framework at all.
val initialize: log: ILogger -> bool

/// Stages one note's bytes in memory and hands them to the desktop's
/// own player, which demuxes, decodes, and sounds them at the given
/// speed and volume. A note the framework cannot take comes back as a
/// named failure.
val play:
  log: ILogger * media: byte array * speed: float * volume: float ->
    Result<IAudioPlayback, AudioFailure>
