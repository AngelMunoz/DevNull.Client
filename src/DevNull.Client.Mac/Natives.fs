module DevNull.Client.Mac.Natives

open Microsoft.Extensions.Logging
open DevNull.Client.Natives

let services(logging: ILoggerFactory) : NativeServices =
  let log = logging.CreateLogger "DevNull.Client.Mac"

  log.LogInformation("No AVFoundation engine yet; voice notes stay hidden.")
  log.LogInformation("No system sounds yet; the client stays silent on alerts.")

  {
    Registrar = Registrar.registrar logging
    Audio = ValueNone
    Sounds = ValueNone
  }
