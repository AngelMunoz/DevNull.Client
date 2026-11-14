module DevNull.Client.Formatting

open System
open System.Globalization
open System.Text.RegularExpressions
open DevNull.Client.Core.ApiTypes

/// One zone the person's times show in: the zone itself, and the name the
/// settings carry for it (an IANA id).
[<Struct>]
type Zone = { Info: TimeZoneInfo; Label: string }

/// The zone the person's own times show in: their chosen one, or the
/// device's, named by its IANA id when the machine knows one.
let zoneOf(settings: UserSettings option) : Zone =
  let local =
    if TimeZoneInfo.Local.Id.Contains '/' then
      TimeZoneInfo.Local.Id
    else
      match TimeZoneInfo.TryConvertWindowsIdToIanaId TimeZoneInfo.Local.Id with
      | true, iana ->
        Option.ofObj iana |> Option.defaultValue TimeZoneInfo.Local.Id
      | false, _ -> TimeZoneInfo.Local.Id

  let id =
    settings
    |> Option.bind(fun settings ->
      match settings.Timezone with
      | ZoneOf(id, _) -> Some id
      | FollowDevice -> None)
    |> Option.defaultValue local

  let info =
    try
      TimeZoneInfo.FindSystemTimeZoneById id
    with _ ->
      TimeZoneInfo.Local

  { Info = info; Label = id }

/// One past moment as the zone shows it: "Sat, Oct 31, 2026 11:00
/// (America/Denver)".
let absolute(zone: Zone, moment: DateTimeOffset) : string =
  let zoned = TimeZoneInfo.ConvertTime(moment, zone.Info)

  let stamp =
    zoned.ToString("ddd, MMM d, yyyy HH:mm", CultureInfo.InvariantCulture)

  $"{stamp} ({zone.Label})"


let timeAgo(now: DateTimeOffset, at: DateTimeOffset) : string =
  let span = now - at

  if span.TotalMinutes < 1.0 then "now"
  elif span.TotalHours < 1.0 then $"{int span.TotalMinutes}m"
  elif span.TotalDays < 1.0 then $"{int span.TotalHours}h"
  elif span.TotalDays < 30.0 then $"{int span.TotalDays}d"
  else at.ToString("MMM d")

/// The minute a wall-clock instant falls in, for gating time text
/// without formatting it: key a slice on this and the text rebuilds
/// at most once a minute. A label may trail a fresh minute by
/// seconds, where the span crosses a boundary mid-bucket.
let minuteBucket(moment: DateTimeOffset) : int64 =
  moment.UtcTicks / TimeSpan.TicksPerMinute

let shortCount(count: int) : string =
  if count < 1000 then
    string count
  else
    let thousands = float count / 1000.0

    thousands.ToString("F0", CultureInfo.InvariantCulture) + "k"

let dayLabel(now: DateTimeOffset, day: DateTimeOffset) : string =
  let today = now.Date
  let day' = day.Date

  if day' = today then
    "Today"
  elif day' = today.AddDays -1.0 then
    "Yesterday"
  else
    day'.ToString("ddd, MMM d", CultureInfo.InvariantCulture)

let plainText(html: string) : string =
  Regex.Replace(html, "<[^>]+>", "") |> System.Net.WebUtility.HtmlDecode

let blockTitle(language: string option, filename: string option) =
  filename |> Option.orElse language

/// The avatar URL sized for a full-size view: only a GitHub host
/// carries the size in its `s` query value, so only that host is
/// rebuilt; any other address — or one that does not parse — rides
/// through unchanged, the way the web's `:get` does.
let sizedAvatarUrl(url: string, pixels: int) : string =
  match Uri.TryCreate(url, UriKind.Absolute) with
  | true, uri ->
    match Option.ofObj uri with
    | Some uri when
      String.Equals(
        uri.Host,
        "avatars.githubusercontent.com",
        StringComparison.OrdinalIgnoreCase
      )
      ->
      let parts =
        uri.Query.TrimStart('?')
        |> fun query ->
          if query = "" then [] else query.Split '&' |> Array.toList

      let sized =
        if parts |> List.exists(fun part -> part.StartsWith "s=") then
          parts
          |> List.map(fun part ->
            if part.StartsWith "s=" then $"s={pixels}" else part)
        else
          parts @ [ $"s={pixels}" ]

      let builder = UriBuilder(uri)
      builder.Query <- String.concat "&" sized
      builder.Uri.AbsoluteUri
    | _ -> url
  | _ -> url
