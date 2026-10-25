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
