module DevNull.Client.Formatting

open System
open DevNull.Client.Core.ApiTypes

/// One zone the person's times show in: the zone itself, and the name the
/// settings carry for it (an IANA id).
[<Struct>]
type Zone = { Info: TimeZoneInfo; Label: string }

/// The zone the person's own times show in: their chosen one, or the
/// device's, named by its IANA id when the machine knows one.
val zoneOf: settings: UserSettings option -> Zone

/// One past moment as the zone shows it: "Sat, Oct 31, 2026 11:00
/// (America/Denver)".
val absolute: zone: Zone * moment: DateTimeOffset -> string

/// One past moment as "now", "5m", "3h", "2d", or the date, the way the
/// web client shows commit times.
val timeAgo: now: DateTimeOffset * at: DateTimeOffset -> string

/// The minute a wall-clock instant falls in, for gating time text
/// without formatting it: key a slice on this and the text rebuilds
/// at most once a minute.
val minuteBucket: moment: DateTimeOffset -> int64

/// A count as "1.2k", "345", the way the web client shows stars.
val shortCount: count: int -> string

/// One day boundary as "Today", "Yesterday", or "Fri, Apr 3", the way
/// the web client separates feed days.
val dayLabel: now: DateTimeOffset * day: DateTimeOffset -> string

/// The server renders code blocks as html; a plain surface shows the
/// text with the tags off and the entities the highlighter escaped put
/// back: tags go first, so an escaped `<` survives as the character it
/// is.
val plainText: html: string -> string

/// The one line over a code block: the filename the fence names, or
/// the language when no filename is there. None over an untitled
/// block, whose box keeps no header.
val blockTitle:
  language: string option * filename: string option -> string option

/// The avatar URL sized for a full-size view: only a GitHub host
/// carries the size in its `s` query value, so only that host is
/// rebuilt; any other address — or one that does not parse — rides
/// through unchanged, the way the web's `:get` does.
val sizedAvatarUrl: url: string * pixels: int -> string
