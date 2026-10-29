module DevNull.Client.Formatting

open System

/// One past moment as "now", "5m", "3h", "2d", or the date, the way the
/// web client shows commit times.
val timeAgo: now: DateTimeOffset * at: DateTimeOffset -> string

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
