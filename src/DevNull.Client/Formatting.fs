module DevNull.Client.Formatting

open System
open System.Globalization

let timeAgo(now: DateTimeOffset, at: DateTimeOffset) : string =
  let span = now - at

  if span.TotalMinutes < 1.0 then "now"
  elif span.TotalHours < 1.0 then $"{int span.TotalMinutes}m"
  elif span.TotalDays < 1.0 then $"{int span.TotalHours}h"
  elif span.TotalDays < 30.0 then $"{int span.TotalDays}d"
  else at.ToString("MMM d")

let shortCount(count: int) : string =
  if count < 1000 then
    string count
  else
    let thousands = float count / 1000.0

    thousands.ToString("F0", CultureInfo.InvariantCulture) + "k"
