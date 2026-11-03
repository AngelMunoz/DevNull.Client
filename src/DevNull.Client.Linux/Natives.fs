module DevNull.Client.Linux.Natives

open System.Runtime.InteropServices
open Microsoft.Extensions.Logging
open DevNull.Client.Linux.Audio
open DevNull.Client.Natives

// A library this machine carries: the probe that decides between a
// live service and its `ValueNone`.
let present(library: string) =
  let mutable handle = 0n
  NativeLibrary.TryLoad(library, &handle)

let services(logging: ILoggerFactory) : NativeServices =
  let log = logging.CreateLogger "DevNull.Client.Linux"

  let audio =
    if present "libpulse-simple.so.0" then
      log.LogDebug("The PulseAudio client library is present.")

      ValueSome(PulseEngine(log) :> IAudioEngine)
    else
      log.LogInformation(
        "No PulseAudio client library; voice notes stay hidden."
      )

      ValueNone

  let sounds =
    if present "libcanberra.so.0" then
      log.LogDebug("The libcanberra library is present.")

      ValueSome(SystemSounds(log) :> ISystemSounds)
    else
      log.LogInformation("No libcanberra; the client stays silent on alerts.")

      ValueNone

  {
    Registrar = Registrar.registrar logging
    Audio = audio
    Sounds = sounds
  }
